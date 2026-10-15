// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model and, when a connection string is
/// available, apply a migration.
/// </summary>
/// <remarks>
/// <para>
/// EF prefers a factory like this over the startup project's host when one
/// exists, so it has to be able to find the real connection string — otherwise
/// <c>database update</c> silently tries to reach a server that does not exist.
/// It therefore reads the same configuration sources the API does, in the same
/// order: <c>appsettings.json</c>, the environment-specific file, user-secrets
/// and finally environment variables.
/// </para>
/// <para>
/// <strong>Adding a migration needs no connection at all</strong> — only the
/// model. That is what makes <c>migrations add</c> safe to run in CI, where
/// there is no Azure SQL to reach and no credential to hold, and it is why the
/// fallback below is a placeholder rather than a failure: a missing connection
/// string should not stop somebody generating a migration.
/// </para>
/// <para>
/// No connection string is ever in source. In production it is an Azure App
/// Service connection setting; locally it is user-secrets, which lives outside
/// the repository.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TheeBuhleDbContext>
{
    /// <summary>
    /// Used when nothing is configured.
    /// </summary>
    /// <remarks>
    /// Deliberately unreachable rather than a plausible default like
    /// <c>localhost</c>. A syntactically valid connection string is enough to
    /// build the model, and one that cannot connect makes a
    /// <c>database update</c> run without configuration fail immediately and
    /// obviously instead of doing something unexpected to whatever database
    /// happens to be on the machine.
    /// </remarks>
    private const string ModelOnlyPlaceholder =
        "Server=(design-time-no-connection);Database=TheeBuhle;Trusted_Connection=True;";

    public TheeBuhleDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        // Walks up to find the API project, because `dotnet ef` runs with the
        // Infrastructure project as the working directory and the settings files
        // live with the host.
        var apiDirectory = LocateApiProject() ?? Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("TheeBuhle") ?? ModelOnlyPlaceholder;

        var options = new DbContextOptionsBuilder<TheeBuhleDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TheeBuhleDbContext(options, new DesignTimeClock());
    }

    /// <summary>Finds the API project directory by walking up from here.</summary>
    private static string? LocateApiProject()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "TheeBuhle.Api");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            // Also handles being run from inside the API project itself.
            if (File.Exists(Path.Combine(directory.FullName, "TheeBuhle.Api.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// A clock for the tool.
    /// </summary>
    /// <remarks>
    /// Never used to stamp a real row — a migration does not write entities. The
    /// Unix epoch rather than "now", so that if it ever did appear in data it
    /// would be unmistakably wrong rather than plausibly recent.
    /// </remarks>
    private sealed class DesignTimeClock : IClock
    {
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch;

        public DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UnixEpoch.Date);
    }
}
