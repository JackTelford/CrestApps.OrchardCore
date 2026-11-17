using System.Text.Json;
using CrestApps.OrchardCore.AI.Agent.Users;
using CrestApps.OrchardCore.Tests.Utilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.Navigation;
using OrchardCore.Users;
using OrchardCore.Users.Indexes;
using OrchardCore.Users.Models;
using OrchardCore.Users.Services;
using YesSql;
using YesSql.Filters.Query;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.OrchardCore.Tests.Agent.Users;

/// <summary>
/// Runs the user search tool through Orchard Core's own Users admin list query on a real YesSql store, because a fake
/// query would hide both things these tests pin: what the tool hands to the model for each user, and whether the term
/// reaches the query at all.
/// </summary>
public sealed class SearchForUsersToolTests
{
    private const string AlicePasswordHash = "hash-of-alice";
    private const string AliceSecurityStamp = "stamp-of-alice";
    private const string AliceResetToken = "reset-token-of-alice";
    private const string AliceTokenValue = "authenticator-key-of-alice";

    [Fact]
    public async Task InvokeAsync_WhenUsersHoldCredentials_ReturnsTheProjectionOnly()
    {
        // Arrange
        var path = CreateDatabasePath();
        var store = await CreateStoreAsync(path);

        try
        {
            await using var services = CreateServices(store);
            await using var scope = services.CreateAsyncScope();

            // Act
            var result = await InvokeAsync(scope.ServiceProvider, "staff");

            // Assert
            Assert.Contains("alice.staff", result, StringComparison.Ordinal);

            foreach (var secret in new[] { AlicePasswordHash, AliceSecurityStamp, AliceResetToken, AliceTokenValue })
            {
                Assert.DoesNotContain(secret, result, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var property in new[] { nameof(User.PasswordHash), nameof(User.SecurityStamp), nameof(User.ResetToken), nameof(User.UserTokens), nameof(User.LoginInfos) })
            {
                Assert.DoesNotContain(property, result, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            TemporarySqliteDatabase.DisposeAndDelete(store, path);
        }
    }

    [Fact]
    public async Task InvokeAsync_WithATerm_ReturnsOnlyTheUsersWhoseNameContainsIt()
    {
        // Arrange
        var path = CreateDatabasePath();
        var store = await CreateStoreAsync(path);

        try
        {
            await using var services = CreateServices(store);
            await using var scope = services.CreateAsyncScope();

            // Act
            var result = await InvokeAsync(scope.ServiceProvider, "ali");

            // Assert
            using var document = JsonDocument.Parse(result);

            Assert.Equal(["alice.staff"], GetUserNames(document));
            Assert.Equal(1, document.RootElement.GetProperty("usersCount").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("totalPages").GetInt32());
        }
        finally
        {
            TemporarySqliteDatabase.DisposeAndDelete(store, path);
        }
    }

    [Fact]
    public async Task InvokeAsync_WithATermThatMatchesSeveralUsers_ReturnsThemByName()
    {
        // Arrange
        var path = CreateDatabasePath();
        var store = await CreateStoreAsync(path);

        try
        {
            await using var services = CreateServices(store);
            await using var scope = services.CreateAsyncScope();

            // Act
            var result = await InvokeAsync(scope.ServiceProvider, "staff");

            // Assert
            using var document = JsonDocument.Parse(result);

            Assert.Equal(["alice.staff", "bob.staff"], GetUserNames(document));
            Assert.Equal(2, document.RootElement.GetProperty("usersCount").GetInt32());
        }
        finally
        {
            TemporarySqliteDatabase.DisposeAndDelete(store, path);
        }
    }

    [Fact]
    public async Task InvokeAsync_WithAnEmailFilter_ReturnsOnlyTheUsersWithThatEmail()
    {
        // Arrange
        var path = CreateDatabasePath();
        var store = await CreateStoreAsync(path);

        try
        {
            await using var services = CreateServices(store);
            await using var scope = services.CreateAsyncScope();

            // Act
            var result = await InvokeAsync(scope.ServiceProvider, "email:carol@example.com");

            // Assert
            using var document = JsonDocument.Parse(result);

            Assert.Equal(["carol"], GetUserNames(document));
        }
        finally
        {
            TemporarySqliteDatabase.DisposeAndDelete(store, path);
        }
    }

    private static async Task<string> InvokeAsync(IServiceProvider services, string term)
    {
        var arguments = new AIFunctionArguments(new Dictionary<string, object> { ["term"] = term })
        {
            Services = services,
        };

        var result = await new SearchForUsersTool().InvokeAsync(arguments, TestContext.Current.CancellationToken);

        return Assert.IsType<string>(result);
    }

    private static string[] GetUserNames(JsonDocument document)
    {
        return document.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Select(user => user.GetProperty(nameof(User.UserName)).GetString())
            .ToArray();
    }

    private static ServiceProvider CreateServices(IStore store)
    {
        var userManager = new UserManager<IUser>(
            Mock.Of<IUserStore<IUser>>(),
            null,
            null,
            null,
            null,
            new UpperInvariantLookupNormalizer(),
            null,
            null,
            null);

        return new ServiceCollection()
            .AddOptions()
            .Configure<PagerOptions>(options => options.PageSize = 10)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(userManager)
            .AddSingleton(Mock.Of<IUpdateModelAccessor>())
            .AddScoped(_ => store.CreateSession())
            .AddScoped<IUsersAdminListQueryService, DefaultUsersAdminListQueryService>()
            .AddSingleton<IUsersAdminListFilterParser>(_ =>
            {
                var builder = new QueryEngineBuilder<User>();
                new DefaultUsersAdminListFilterProvider().Build(builder);

                return new DefaultUsersAdminListFilterParser(builder.Build());
            })
            .BuildServiceProvider();
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"crestapps-agent-users-{Guid.NewGuid():N}.db");
    }

    private static async Task<IStore> CreateStoreAsync(string databasePath)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreFactory.Create(configuration => configuration.UseSqLite($"Data Source={databasePath};Pooling=False"));
        store.RegisterIndexes([new UserIndexProvider()]);
        await store.InitializeAsync(cancellationToken);

        await using (var session = store.CreateSession())
        {
            var transaction = await session.BeginTransactionAsync(cancellationToken);
            var schemaBuilder = new SchemaBuilder(store.Configuration, transaction);

            await schemaBuilder.CreateMapIndexTableAsync<UserIndex>(table => table
                .Column<string>(nameof(UserIndex.NormalizedUserName))
                .Column<string>(nameof(UserIndex.NormalizedEmail))
                .Column<bool>(nameof(UserIndex.IsEnabled), column => column.NotNull().WithDefault(true))
                .Column<bool>(nameof(UserIndex.IsLockoutEnabled), column => column.NotNull().WithDefault(false))
                .Column<DateTime?>(nameof(UserIndex.LockoutEndUtc), column => column.Nullable())
                .Column<int>(nameof(UserIndex.AccessFailedCount), column => column.NotNull().WithDefault(0))
                .Column<string>(nameof(UserIndex.UserId)));

            await transaction.CommitAsync(cancellationToken);
        }

        await using (var session = store.CreateSession())
        {
            var alice = CreateUser("alice.staff", "alice@example.com");
            alice.PasswordHash = AlicePasswordHash;
            alice.SecurityStamp = AliceSecurityStamp;
            alice.ResetToken = AliceResetToken;
            alice.UserTokens.Add(new UserToken { LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = AliceTokenValue });

            await session.SaveAsync(alice, cancellationToken: cancellationToken);
            await session.SaveAsync(CreateUser("bob.staff", "bob@example.com"), cancellationToken: cancellationToken);
            await session.SaveAsync(CreateUser("carol", "carol@example.com"), cancellationToken: cancellationToken);
            await session.SaveChangesAsync(cancellationToken);
        }

        return store;
    }

    private static User CreateUser(string userName, string email)
    {
        return new User
        {
            UserId = Guid.NewGuid().ToString("N"),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            IsEnabled = true,
        };
    }
}
