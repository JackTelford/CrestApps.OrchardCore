using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.ContentManagement;
using OrchardCore.Contents.Services;
using OrchardCore.Contents.ViewModels;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.Json;
using OrchardCore.Navigation;

namespace CrestApps.OrchardCore.AI.Agent.Contents;

/// <summary>
/// Represents the search for contents tool.
/// </summary>
public sealed class SearchForContentsTool : AIFunction
{
    public const string TheName = "searchForContentItems";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "term": {
          "type": "string",
          "description": "The text to look for in content item titles. The search syntax of the Content Items admin list is supported, for example 'type:Article' or 'status:Draft'."
        },
        "pageNumber": {
          "type": "integer",
          "description": "The page number of results to return.",
          "default": 1
        }
      },
      "required": [
        "term"
      ],
      "additionalProperties": false
    }
    """);

    public override string Name => TheName;

    public override string Description => "Search for content items whose title contains the given term, along with a way to paginate the results.";

    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>()
    {
        ["Strict"] = false,
    };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ArgumentNullException.ThrowIfNull(arguments.Services);

        var logger = arguments.Services.GetRequiredService<ILogger<SearchForContentsTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", TheName);
        }

        var contentManager = arguments.Services.GetRequiredService<IContentManager>();
        var contentsAdminListQueryService = arguments.Services.GetRequiredService<IContentsAdminListQueryService>();
        var contentsAdminListFilterParser = arguments.Services.GetRequiredService<IContentsAdminListFilterParser>();
        var updateModelAccessor = arguments.Services.GetRequiredService<IUpdateModelAccessor>();
        var httpContextAccessor = arguments.Services.GetRequiredService<IHttpContextAccessor>();

        var options = arguments.Services.GetRequiredService<IOptions<DocumentJsonSerializerOptions>>().Value;
        var pagerOptions = arguments.Services.GetRequiredService<IOptions<PagerOptions>>().Value;

        if (!arguments.TryGetFirstString("term", out var term))
        {
            logger.LogWarning("AI tool '{ToolName}': Unable to find a term argument in the function arguments.", TheName);

            return "Unable to find a term argument in the function arguments.";
        }

        // The filters of the Content Items admin list read the user of the current request to keep what that user may view.
        if (httpContextAccessor.HttpContext is null)
        {
            logger.LogWarning("AI tool '{ToolName}': HttpContext is null (likely running in a background task). The content items a user may view cannot be resolved.", TheName);

            return "Content items can only be searched during a user's request. This function is not available in a background task.";
        }

        var page = arguments.GetFirstValueOrDefault("pageNumber", 1);

        var startingIndex = (Math.Max(1, page) - 1) * pagerOptions.PageSize;

        // The Content Items admin list query runs the parsed filter only, so the term is parsed the way that list parses its search box.
        var filterResult = contentsAdminListFilterParser.Parse(term);

        var contentOptions = new ContentOptionsViewModel()
        {
            SearchText = term,
            OriginalSearchText = term,
            StartIndex = startingIndex,
            FilterResult = filterResult,
        };

        filterResult.MapTo(contentOptions);

        var query = await contentsAdminListQueryService.QueryAsync(contentOptions, updateModelAccessor.ModelUpdater);

        var contentItemsCount = await query.CountAsync(cancellationToken);

        var contentItems = await query.Skip(startingIndex)
            .Take(pagerOptions.PageSize)
            .ListAsync(contentManager);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' completed.", TheName);
        }

        return
        $$"""
{
"contentItems": {{JsonSerializer.Serialize(contentItems, options.SerializerOptions)}},
"contentItemsCount": {{contentItemsCount}},
"totalPages": {{Math.Ceiling((double)contentItemsCount / pagerOptions.PageSize)}},
"pageSize": {{pagerOptions.PageSize}}
}
""";
    }
}
