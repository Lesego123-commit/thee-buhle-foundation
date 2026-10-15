// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Infrastructure.Persistence;

namespace TheeBuhle.Api;

/// <summary>
/// Loads the demonstration data into a hosted database, for a presentation.
/// </summary>
/// <remarks>
/// <para>
/// The same seeder Development runs on startup: programmes, learners, marks,
/// registers, news, donations, and one account per role (staff, safeguarding
/// lead, programme manager, mentors, parents, learners). What differs is the
/// password. It is typed at a hidden prompt by whoever runs this, because the
/// development password is printed in the README and a public site seeded with
/// it would have a staff console anybody could sign in to.
/// </para>
/// <code>
/// dotnet TheeBuhle.Api.dll seed-demo
/// </code>
/// <para>
/// <strong>It only runs on a database with no programmes yet</strong>, so it
/// cannot mix invented learners into a database the foundation has started
/// using for real.
/// </para>
/// </remarks>
public static class DemoDataCommand
{
    public const string Command = "seed-demo";

    private const int MinimumPasswordLength = 12;

    public static async Task<int> RunAsync()
    {
        await using var provider = AdminBootstrap.BuildServices();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TheeBuhleDbContext>();

        if (await context.Programmes.AnyAsync())
        {
            Console.Error.WriteLine(
                "This database already has programmes, so it is in use. Demo data is only loaded into a fresh one.");

            return 1;
        }

        Console.WriteLine("Every demo account will share the password you choose now.");

        var password = AdminBootstrap.ReadHidden("Demo password (at least 12 characters): ");

        if (password.Length < MinimumPasswordLength)
        {
            Console.Error.WriteLine("That password is too short. Nothing was loaded.");

            return 1;
        }

        if (AdminBootstrap.ReadHidden("Type it again: ") != password)
        {
            Console.Error.WriteLine("The two passwords did not match. Nothing was loaded.");

            return 1;
        }

        Console.WriteLine("Loading. This takes a minute or two against Azure SQL...");

        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(password);

        Console.WriteLine(
            $"Done: {await context.Learners.CountAsync()} learners, "
            + $"{await context.Attendance.CountAsync()} register entries, "
            + $"{await context.Users.CountAsync()} accounts. The demo accounts are listed in the README.");

        return 0;
    }
}
