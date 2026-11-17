using System.Security.Claims;
using CrestApps.OrchardCore.AI.Agent.Contents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchardCore.ContentManagement;
using OrchardCore.Contents.Services;
using OrchardCore.Contents.ViewModels;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.Navigation;
using YesSql;
using YesSql.Filters.Nodes;
using YesSql.Filters.Query;

namespace CrestApps.OrchardCore.Tests.Agent.Contents;

/// <summary>
/// The content search tool must hand its term to Orchard Core's Contents admin list query as a parsed filter, because
/// that query only runs the filter and ignores the search text.
/// </summary>
public sealed class SearchForContentsToolTests
{
    [Fact]
    public async Task InvokeAsync_WithATerm_QueriesWithTheParsedTerm()
    {
        // Arrange
        var queryService = new CapturingQueryService();
        var arguments = CreateArguments("handbook", queryService, new DefaultHttpContext { User = CreateUser() });

        // Act
        var result = await new SearchForContentsTool().InvokeAsync(arguments, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("\"contentItemsCount\": 0", Assert.IsType<string>(result), StringComparison.Ordinal);

        var options = Assert.Single(queryService.Options);
        var term = Assert.Single(options.FilterResult.OfType<DefaultTermNode>());

        Assert.Equal("handbook", term.ToString());
    }

    [Fact]
    public async Task InvokeAsync_WithAContentTypeFilter_QueriesThatContentType()
    {
        // Arrange
        var queryService = new CapturingQueryService();
        var arguments = CreateArguments("type:Article handbook", queryService, new DefaultHttpContext { User = CreateUser() });

        // Act
        await new SearchForContentsTool().InvokeAsync(arguments, TestContext.Current.CancellationToken);

        // Assert
        var options = Assert.Single(queryService.Options);

        Assert.Equal("Article", options.SelectedContentType);
        Assert.Equal("handbook", Assert.Single(options.FilterResult.OfType<DefaultTermNode>()).ToString());
    }

    [Fact]
    public async Task InvokeAsync_WithoutARequest_RefusesAndNeverQueries()
    {
        // Arrange
        var queryService = new CapturingQueryService();
        var arguments = CreateArguments("handbook", queryService, httpContext: null);

        // Act
        var result = await new SearchForContentsTool().InvokeAsync(arguments, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("background", Assert.IsType<string>(result), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(queryService.Options);
    }

    private static ClaimsPrincipal CreateUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")], "Test"));
    }

    private static AIFunctionArguments CreateArguments(string term, CapturingQueryService queryService, HttpContext httpContext)
    {
        var contentManager = new Mock<IContentManager>();
        contentManager
            .Setup(manager => manager.LoadAsync(It.IsAny<ContentItem>()))
            .Returns<ContentItem>(Task.FromResult);

        var services = new ServiceCollection()
            .AddOptions()
            .Configure<PagerOptions>(options => options.PageSize = 10)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(contentManager.Object)
            .AddSingleton(Mock.Of<IUpdateModelAccessor>())
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext })
            .AddSingleton<IContentsAdminListQueryService>(queryService)
            .AddSingleton<IContentsAdminListFilterParser>(_ =>
            {
                var builder = new QueryEngineBuilder<ContentItem>();
                new DefaultContentsAdminListFilterProvider().Build(builder);

                return new DefaultContentsAdminListFilterParser(builder.Build());
            })
            .BuildServiceProvider();

        return new AIFunctionArguments(new Dictionary<string, object> { ["term"] = term })
        {
            Services = services,
        };
    }

    private sealed class CapturingQueryService : IContentsAdminListQueryService
    {
        public List<ContentOptionsViewModel> Options { get; } = [];

        public Task<IQuery<ContentItem>> QueryAsync(ContentOptionsViewModel model, IUpdateModel updater)
        {
            Options.Add(model);

            var query = new Mock<IQuery<ContentItem>>();
            query.Setup(q => q.Skip(It.IsAny<int>())).Returns(query.Object);
            query.Setup(q => q.Take(It.IsAny<int>())).Returns(query.Object);
            query.Setup(q => q.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
            query.Setup(q => q.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

            return Task.FromResult(query.Object);
        }
    }
}
