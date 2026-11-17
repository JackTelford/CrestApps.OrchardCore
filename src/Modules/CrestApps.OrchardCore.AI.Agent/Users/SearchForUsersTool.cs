using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.Navigation;
using OrchardCore.Users.Services;
using OrchardCore.Users.ViewModels;

namespace CrestApps.OrchardCore.AI.Agent.Users;

/// <summary>
/// Represents the search for users tool.
/// </summary>
public sealed class SearchForUsersTool : AIFunction
{
    public const string TheName = "searchForUsers";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "term": {
          "type": "string",
          "description": "The text to look for in user names. The search syntax of the Users admin list is supported, for example 'email:jane@example.com' or 'role:Editor'."
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

    public override string Description => "Search for users whose user name contains the given term, along with a way to paginate the results.";

    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>()
    {
        ["Strict"] = false,
    };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(arguments.Services);

        var logger = arguments.Services.GetRequiredService<ILogger<SearchForUsersTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        var usersAdminListQueryService = arguments.Services.GetRequiredService<IUsersAdminListQueryService>();
        var usersAdminListFilterParser = arguments.Services.GetRequiredService<IUsersAdminListFilterParser>();
        var updateModelAccessor = arguments.Services.GetRequiredService<IUpdateModelAccessor>();

        var pagerOptions = arguments.Services.GetRequiredService<IOptions<PagerOptions>>().Value;

        if (!arguments.TryGetFirstString("term", out var term))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument '{ArgumentName}'.", Name, "term");

            return "Unable to find a term argument in the function arguments.";
        }

        var page = arguments.GetFirstValueOrDefault("pageNumber", 1);

        if (page < 1)
        {
            page = 1;
        }

        var startingIndex = (page - 1) * pagerOptions.PageSize;

        // The Users admin list query runs the parsed filter only, so the term is parsed the way that list parses its search box.
        var filterResult = usersAdminListFilterParser.Parse(term);

        var options = new UserIndexOptions()
        {
            SearchText = term,
            OriginalSearchText = term,
            StartIndex = startingIndex,
            FilterResult = filterResult,
        };

        filterResult.MapTo(options);

        var query = await usersAdminListQueryService.QueryAsync(options, updateModelAccessor.ModelUpdater);

        var usersCount = await query.CountAsync(cancellationToken);

        var users = await query.Skip(startingIndex)
            .Take(pagerOptions.PageSize)
            .ListAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' completed.", Name);
        }

        return
        $$"""
{
"users": {{JsonSerializer.Serialize(users.Select(user => user.AsAIObject()))}},
"usersCount": {{usersCount}},
"totalPages": {{Math.Ceiling((double)usersCount / pagerOptions.PageSize)}},
"pageSize": {{pagerOptions.PageSize}}
}
""";
    }
}
