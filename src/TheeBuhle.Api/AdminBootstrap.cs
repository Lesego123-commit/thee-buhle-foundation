// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Common;
using TheeBuhle.Infrastructure;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Api;

/// <summary>
/// Creates the first administrator of an empty production database.
/// </summary>
/// <remarks>
/// <para>
/// Production starts with no accounts, because the seeder is Development-only,
/// and every other account is created by an administrator. Something has to
/// create the first one. Run from a machine that can reach the database:
/// </para>
/// <code>
/// dotnet run --project src/TheeBuhle.Api -- create-admin --email director@... --first Buhle --surname Ndlovu
/// </code>
/// <para>
/// The password is typed at a hidden prompt, never passed as an argument, so it
/// does not land in shell history or a process listing. It is hashed with the
/// same Argon2id hasher the sign-in endpoint verifies against.
/// </para>
/// <para>
/// <strong>It refuses to run once any administrator exists.</strong> A command
/// that could add an administrator at any time would be a way round the staff
/// console's own access control for anyone who can reach the database.
/// </para>
/// </remarks>
public static class AdminBootstrap
{
    public const string Command = "create-admin";

    /// <summary>The same minimum the change-password endpoint enforces.</summary>
    private const int MinimumPasswordLength = 12;

    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseOptions(args);

        if (options is null)
        {
            Console.Error.WriteLine(
                "Usage: create-admin --email <address> --first <name> --surname <name> [--title <job title>]");

            return 2;
        }

        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TheeBuhleDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        if (await context.Users.AnyAsync(user => user.Role == Role.Admin))
        {
            Console.Error.WriteLine(
                "An administrator already exists. Further accounts are created from the staff console.");

            return 1;
        }

        if (await context.Users.AnyAsync(user => user.Email == options.Email))
        {
            Console.Error.WriteLine($"An account for {options.Email} already exists.");

            return 1;
        }

        var password = ReadHidden("Password (at least 12 characters): ");

        if (password.Length < MinimumPasswordLength)
        {
            Console.Error.WriteLine("That password is too short. Nothing was created.");

            return 1;
        }

        if (ReadHidden("Type it again: ") != password)
        {
            Console.Error.WriteLine("The two passwords did not match. Nothing was created.");

            return 1;
        }

        var user = new User
        {
            Email = options.Email,
            PasswordHash = hasher.Hash(password),
            Role = Role.Admin,
            Status = UserStatus.Active,
        };

        context.Users.Add(user);

        context.Staff.Add(new StaffMember
        {
            User = user,
            FirstName = options.FirstName,
            Surname = options.Surname,
            JobTitle = options.JobTitle,
            IsSafeguardingLead = false,
        });

        await context.SaveChangesAsync();

        Console.WriteLine($"Created administrator {options.Email}. Sign in at /admin.");

        return 0;
    }

    /// <summary>
    /// The data layer and nothing else, configured the way the API is: the
    /// settings files beside the executable, then environment variables.
    /// </summary>
    internal static ServiceProvider BuildServices()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }

    private static BootstrapOptions? ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                return null;
            }

            values[args[i][2..]] = args[i + 1].Trim();
        }

        if (!values.TryGetValue("email", out var email) || !email.Contains('@', StringComparison.Ordinal)
            || !values.TryGetValue("first", out var first) || first.Length == 0
            || !values.TryGetValue("surname", out var surname) || surname.Length == 0)
        {
            return null;
        }

        return new BootstrapOptions(
            email.ToLowerInvariant(),
            first,
            surname,
            values.GetValueOrDefault("title") is { Length: > 0 } title ? title : "Director");
    }

    /// <summary>Reads a line without echoing it.</summary>
    internal static string ReadHidden(string prompt)
    {
        Console.Write(prompt);

        var buffer = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }

        Console.WriteLine();

        return buffer.ToString();
    }

    private sealed record BootstrapOptions(string Email, string FirstName, string Surname, string JobTitle);
}
