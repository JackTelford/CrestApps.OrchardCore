using System.Security.Claims;
using CrestApps.OrchardCore.AI.Agent.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchardCore.Data;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Models;

namespace CrestApps.OrchardCore.Tests.Agent.Tenants;

/// <summary>
/// A tenant the AI Agent creates must be saved the way the Tenants admin saves one: uninitialized and with a setup
/// secret, which is what makes Orchard Core's setup screen ask for a setup token.
/// </summary>
public sealed class CreateTenantToolTests
{
    [Fact]
    public async Task InvokeAsync_WhenTheTenantIsCreated_SavesItWithASetupSecret()
    {
        // Arrange
        var saved = new List<SavedTenant>();
        var arguments = CreateArguments("tenant1", saved);

        // Act
        var result = await new CreateTenantTool().InvokeAsync(arguments, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("created successfully", Assert.IsType<string>(result), StringComparison.Ordinal);

        var tenant = Assert.Single(saved);

        Assert.Equal("tenant1", tenant.Name);
        Assert.Equal(TenantState.Uninitialized, tenant.State);
        Assert.True(Guid.TryParse(tenant.Secret, out var secret), "The tenant was saved without a setup secret.");
        Assert.NotEqual(Guid.Empty, secret);
    }

    [Fact]
    public async Task InvokeAsync_WhenTwoTenantsAreCreated_GivesEachItsOwnSecret()
    {
        // Arrange
        var saved = new List<SavedTenant>();
        var tool = new CreateTenantTool();

        // Act
        await tool.InvokeAsync(CreateArguments("tenant1", saved), TestContext.Current.CancellationToken);
        await tool.InvokeAsync(CreateArguments("tenant2", saved), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, saved.Count);
        Assert.All(saved, tenant => Assert.False(string.IsNullOrEmpty(tenant.Secret)));
        Assert.NotEqual(saved[0].Secret, saved[1].Secret);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheResultIsReturned_NeverRevealsTheSecret()
    {
        // Arrange
        var saved = new List<SavedTenant>();
        var arguments = CreateArguments("tenant1", saved);

        // Act
        var result = await new CreateTenantTool().InvokeAsync(arguments, TestContext.Current.CancellationToken);

        // Assert
        var tenant = Assert.Single(saved);

        Assert.False(string.IsNullOrEmpty(tenant.Secret));
        Assert.DoesNotContain(tenant.Secret, Assert.IsType<string>(result), StringComparison.OrdinalIgnoreCase);
    }

    private static AIFunctionArguments CreateArguments(string tenantName, List<SavedTenant> saved)
    {
        var shellHost = new Mock<IShellHost>();

        // The tool disposes the settings once they are saved, so their values are read while they are being saved.
        shellHost
            .Setup(host => host.UpdateShellSettingsAsync(It.IsAny<ShellSettings>()))
            .Callback<ShellSettings>(settings => saved.Add(new SavedTenant(settings.Name, settings.State, settings["Secret"])))
            .Returns(Task.CompletedTask);

        var shellSettingsManager = new Mock<IShellSettingsManager>();
        shellSettingsManager
            .Setup(manager => manager.CreateDefaultSettings())
            .Returns(() => new ShellSettings());

        // A signed-in caller who holds every permission, so the tests cover what the tool does once it may run.
        var authorization = new Mock<IAuthorizationService>();
        authorization
            .Setup(service => service.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")], "Test"));

        var services = new ServiceCollection()
            .AddSingleton(new ShellSettings { Name = ShellSettings.DefaultShellName })
            .AddSingleton(shellHost.Object)
            .AddSingleton(shellSettingsManager.Object)
            .AddSingleton(new DatabaseProvider { Name = "Sqlite", Value = "Sqlite", IsDefault = true })
            .AddSingleton(authorization.Object)
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } })
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

        return new AIFunctionArguments(new Dictionary<string, object>
        {
            ["name"] = tenantName,
            ["recipeName"] = "Blank",
            ["requestUrlPrefix"] = tenantName,
        })
        {
            Services = services,
        };
    }

    private sealed record SavedTenant(string Name, TenantState State, string Secret);
}
