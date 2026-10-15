// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Microsoft (2026) ASP.NET Core documentation, including WebApplicationFactory integration testing.
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.IntegrationTests;

/// <summary>
/// Starts the real API against a real SQL Server database.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No in-memory provider and no mocked database.</strong> That is the
/// whole point of this project: almost every rule worth testing at this level is
/// enforced by something the in-memory provider does not have — filtered unique
/// indexes, check constraints, an identity column, a locking read, and the SQL
/// translation of the visibility filters.
/// </para>
/// <para>
/// A suite that passed against the in-memory provider would prove that the C#
/// compiles, which the unit tests already establish. These tests exist to prove
/// the <em>schema and the queries</em> behave, so they need the real thing.
/// </para>
/// <para>
/// Each test class gets its own database, dropped afterwards. Sharing one would
/// make the tests order-dependent, and the safeguarding tests in particular
/// depend on there being exactly one designated lead — which another test
/// removing the lead would quietly break.
/// </para>
/// </remarks>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// A unique database per fixture instance.
    /// </summary>
    /// <remarks>
    /// Named from a GUID rather than from the test class, so two classes running
    /// in parallel cannot collide and a crashed run leaves an obviously
    /// disposable database rather than one somebody has to identify.
    /// </remarks>
    private readonly string databaseName = $"TheeBuhleTest_{Guid.NewGuid():N}";

    /// <summary>
    /// Where to find SQL Server.
    /// </summary>
    /// <remarks>
    /// From the environment when CI sets it, otherwise a local default. CI runs
    /// SQL Server as a service container and passes its connection details
    /// through <c>TEST_SQL_SERVER</c>; a developer with SQL Server installed
    /// needs no configuration at all.
    /// </remarks>
    private static string ServerConnection =>
        Environment.GetEnvironmentVariable("TEST_SQL_SERVER")
        ?? "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

    public string ConnectionString =>
        $"{ServerConnection.TrimEnd(';')};Database={databaseName}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // ----------------------------------------------------------------
            //  The DbContext registration is REPLACED, rather than the
            //  connection string being injected through configuration.
            //
            //  Two earlier attempts did not work, and both failed in ways worth
            //  recording:
            //
            //   1. An in-memory configuration source. Overridden by the
            //      user-secrets provider Program.cs adds for Development, so
            //      the whole suite silently ran against a developer's own local
            //      database instead of a disposable one — and passed, against
            //      data left over from manual testing.
            //
            //   2. An environment variable. Environment variables are
            //      PROCESS-GLOBAL, and xUnit runs test classes in parallel — so
            //      two fixtures wrote to the same variable, both hosts ended up
            //      pointed at one database, and both tried to create it.
            //
            //  Replacing the registration is per-host and deterministic, and it
            //  is the last word because it runs after everything Program.cs did.
            // ----------------------------------------------------------------
            services.RemoveAll<DbContextOptions<TheeBuhleDbContext>>();
            services.RemoveAll<DbContextOptions>();

            services.AddDbContext<TheeBuhleDbContext>(options =>
                options.UseSqlServer(ConnectionString, sql =>
                {
                    // The retry strategy is kept, because UnitOfWork wraps its
                    // transactions in an execution strategy specifically to be
                    // compatible with it — and a test suite that ran without
                    // retries would not exercise that.
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(2),
                        errorNumbersToAdd: null);

                    sql.MigrationsAssembly(typeof(TheeBuhleDbContext).Assembly.FullName);
                }));

            // The host's own startup path migrates and seeds in Development, so
            // there is nothing else to replace — and that is deliberate. These
            // tests exercise the same migration and the same seeder a developer
            // and a deployment use, rather than a test-only schema that could
            // drift from it.
            services.RemoveAll<IHostedService>();
        });
    }

    public async Task InitializeAsync()
    {
        // Touching the client is what starts the host, which runs the migration
        // and the seeder.
        using var client = CreateClient();

        using var probe = await client.GetAsync(new Uri("/health", UriKind.Relative));

        probe.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Drops the test database.
    /// </summary>
    /// <remarks>
    /// Named explicitly rather than overriding <c>DisposeAsync</c>, because
    /// xUnit's <see cref="IAsyncLifetime"/> and
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> both declare a
    /// <c>DisposeAsync</c> with different return types — so one class cannot
    /// satisfy both with a single override.
    /// </remarks>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await DropDatabaseAsync();

        Dispose();
    }

    /// <summary>Opens a context against the test database, for direct assertions.</summary>
    /// <remarks>
    /// Some properties are only observable in the database — that a note exists
    /// in exactly one table, that a rolled-back check-in left no row. Asserting
    /// those through the API would be asserting the API's own filtering rather
    /// than the state.
    /// </remarks>
    public TheeBuhleDbContext CreateDbContext() =>
        Services.GetRequiredService<IServiceScopeFactory>()
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<TheeBuhleDbContext>();

    private async Task DropDatabaseAsync()
    {
        try
        {
            await using var scope = Services.CreateAsyncScope();

            var context = scope.ServiceProvider.GetRequiredService<TheeBuhleDbContext>();

            await context.Database.EnsureDeletedAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A failed drop must not fail the test run. The database is named
            // from a GUID and is obviously disposable, so the worst case is a
            // stray database on a developer's machine rather than a red build
            // for a reason unrelated to the code.
        }
    }
}

/// <summary>Removes every registration of a service type.</summary>
internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
