// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;

namespace TheeBuhle.Infrastructure.Persistence;

/// <summary>
/// Demonstration data that the core seed did not cover.
/// </summary>
/// <remarks>
/// <para>
/// Three gaps, each of which left a feature that works but could never be seen:
/// </para>
/// <list type="bullet">
/// <item>Two news articles, so the News page never had a second page and its
/// pagination never rendered.</item>
/// <item>No completed donations, so every published donation total read R0 and
/// the donor-facing empty state was the only thing a demonstration showed.</item>
/// <item>No learning resources, so every subject in the app read "0 of 0 done"
/// and the Subject screen could only show its empty state.</item>
/// </list>
/// <para>
/// <strong>Additive and idempotent.</strong> Each item is keyed — a news post by
/// slug, a donation by gateway reference, a resource by subject and title — and
/// inserted only if it is not already there. That is why this runs on every
/// start rather than only on an empty database: an existing developer database
/// picks it up without being dropped, and running it twice changes nothing.
/// </para>
/// <para>
/// Like everything the seeder writes, none of this is real. The site's own
/// footer says the data is illustrative.
/// </para>
/// </remarks>
public sealed partial class DatabaseSeeder
{
    private async Task SeedDemonstrationExtrasAsync(CancellationToken cancellationToken)
    {
        var added = 0;

        await unitOfWork.TransactionAsync(async () =>
        {
            added += await SeedMoreNewsAsync(cancellationToken);
            added += await SeedDonationsAsync(cancellationToken);
            added += await SeedResourcesAsync(cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        if (added > 0)
        {
            SeedLog.ExtrasSeeded(logger, added);
        }
    }

    // ------------------------------------------------------------------ news

    private async Task<int> SeedMoreNewsAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;

        // Seven more, across every category, so the index has a second page
        // (six to a page) and each category filter has something in it.
        var posts = new (string Slug, NewsCategory Category, string Title, string Excerpt, string Body, string Author, int Minutes, int DaysAgo)[]
        {
            ("thandi-42-to-71", NewsCategory.SuccessStory,
                "From 42% to 71% in one year",
                "A Grade 10 learner who nearly dropped Mathematics finished the year with a distinction in sight.",
                "<p>In February her first test came back at 42%. She stayed for every Saturday session, worked "
                + "through the past papers with her tutor and met her mentor every week.</p><p>Her June mark was 58%. "
                + "Her September mark was 71%. She is now choosing between engineering and actuarial science.</p>",
                "Lerato Khumalo", 4, 5),

            ("first-bursary-2026", NewsCategory.SuccessStory,
                "Our first learner to win a full bursary",
                "A matriculant from Sebokeng has been awarded a four-year engineering bursary.",
                "<p>He joined the programme in Grade 9. Four years of Saturday tutoring and a mentor who reviewed "
                + "every draft of his application later, he has been awarded a full engineering bursary.</p>",
                "Buhle Ndlovu", 3, 12),

            ("holiday-camp-registrations", NewsCategory.Announcement,
                "June holiday camp registrations are open",
                "Four days of catch-up work, sport and mentoring. Free, with transport and a hot lunch.",
                "<p>Registration for the June holiday camp is now open. The camp is free and includes "
                + "transport from three pick-up points and a hot lunch every day.</p><p>Places are limited "
                + "to 120 learners. Parents need to complete the permission form in the app.</p>",
                "Lerato Khumalo", 2, 15),

            ("vaal-weekly-feature", NewsCategory.Press,
                "Vaal Weekly: the Saturday classroom",
                "The local paper spent a Saturday morning with our Grade 11 tutoring group.",
                "<p>Vaal Weekly spent a Saturday morning in Hall A with our Grade 11 group and wrote about what "
                + "they found: a quiet room, a whiteboard full of trigonometry, and twelve learners who chose "
                + "to be there.</p>",
                "Buhle Ndlovu", 3, 30),

            ("attendance-term-2", NewsCategory.ProgrammeUpdate,
                "Term 2 in numbers",
                "Attendance held at 83% through a term of taxi fare increases.",
                "<p>Attendance for Term 2 held at 83%, despite two taxi fare increases. Transport subsidies "
                + "remain our single biggest funding gap.</p><p>We publish the full figures, including where we "
                + "fell short, on the Impact page.</p>",
                "Sipho Maseko", 3, 40),

            ("fika-cafe-partnership", NewsCategory.Community,
                "Fika Café is feeding our Saturday sessions",
                "A local café now provides lunch for every learner at the Vanderbijlpark venue.",
                "<p>Fika Café has offered to provide lunch for every learner at our Vanderbijlpark venue for the "
                + "rest of the year. Learners who used to leave at noon to eat now stay for the afternoon session.</p>",
                "Buhle Ndlovu", 2, 48),

            ("safeguarding-induction-dates", NewsCategory.Announcement,
                "New safeguarding induction dates for volunteers",
                "Two induction sessions this term for newly cleared mentors and tutors.",
                "<p>All volunteers who work with learners complete a two-hour safeguarding induction before their "
                + "first session. This term's inductions run on two Wednesday evenings at our offices.</p>",
                "Nomsa Dube", 2, 60),
        };

        var existing = await context.NewsPosts
            .Select(post => post.Slug)
            .ToListAsync(cancellationToken);

        var added = 0;

        foreach (var post in posts)
        {
            if (existing.Contains(post.Slug))
            {
                continue;
            }

            context.NewsPosts.Add(new NewsPost
            {
                Slug = post.Slug,
                Category = post.Category,
                Title = post.Title,
                Excerpt = post.Excerpt,
                BodyHtml = post.Body,
                AuthorName = post.Author,
                ReadMinutes = post.Minutes,
                PublishedAt = now.AddDays(-post.DaysAgo),
            });

            added++;
        }

        return added;
    }

    // ------------------------------------------------------------- donations

    /// <summary>
    /// Completed gifts, plus one pending.
    /// </summary>
    /// <remarks>
    /// The pending one is deliberate. The published totals count completed
    /// donations only, and the staff console lists both — so a demonstration
    /// can show a gift that has not cleared sitting in the list and <em>not</em>
    /// in the total, which is the rule the donations page states publicly.
    /// </remarks>
    private async Task<int> SeedDonationsAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;

        var gifts = new (string Reference, string Name, string Email, bool Organisation, decimal Amount, DonationFrequency Frequency, DonationStatus Status, int DaysAgo, bool Certificate)[]
        {
            ("TB-SEED-0001", "Palesa Motaung", "palesa.motaung@example.co.za", false, 250m, DonationFrequency.Monthly, DonationStatus.Complete, 58, true),
            ("TB-SEED-0002", "Palesa Motaung", "palesa.motaung@example.co.za", false, 250m, DonationFrequency.Monthly, DonationStatus.Complete, 28, true),
            ("TB-SEED-0003", "Johan van der Merwe", "johan.vdm@example.co.za", false, 600m, DonationFrequency.Once, DonationStatus.Complete, 44, true),
            ("TB-SEED-0004", "Vaal Logistics (Pty) Ltd", "csi@vaallogistics.example.co.za", true, 25_000m, DonationFrequency.Once, DonationStatus.Complete, 35, true),
            ("TB-SEED-0005", "Ayanda Zulu", "ayanda.zulu@example.co.za", false, 150m, DonationFrequency.Monthly, DonationStatus.Complete, 20, true),
            ("TB-SEED-0006", "Ayanda Zulu", "ayanda.zulu@example.co.za", false, 150m, DonationFrequency.Monthly, DonationStatus.Complete, 1, false),
            ("TB-SEED-0007", "Run Studio Members", "hello@runstudio.example.co.za", true, 1_800m, DonationFrequency.Once, DonationStatus.Complete, 9, true),
            ("TB-SEED-0008", "Karin Botha", "karin.botha@example.co.za", false, 500m, DonationFrequency.Once, DonationStatus.Pending, 0, false),
        };

        var existing = await context.Donations
            .Where(donation => donation.GatewayReference.StartsWith("TB-SEED-"))
            .Select(donation => donation.GatewayReference)
            .ToListAsync(cancellationToken);

        var donors = new Dictionary<string, Donor>(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var gift in gifts)
        {
            if (existing.Contains(gift.Reference))
            {
                continue;
            }

            // One donor per address, as the real service does — a monthly giver
            // with a donor row per month would break the annual 18A total.
            if (!donors.TryGetValue(gift.Email, out var donor))
            {
                donor = await context.Donors.FirstOrDefaultAsync(d => d.Email == gift.Email, cancellationToken)
                    ?? new Donor
                    {
                        DisplayName = gift.Name,
                        Email = gift.Email,
                        IsOrganisation = gift.Organisation,
                    };

                if (context.Entry(donor).State == EntityState.Detached)
                {
                    context.Donors.Add(donor);
                }

                donors[gift.Email] = donor;
            }

            var createdAt = now.AddDays(-gift.DaysAgo);
            var complete = gift.Status == DonationStatus.Complete;

            context.Donations.Add(new Donation
            {
                Donor = donor,
                Amount = gift.Amount,
                Frequency = gift.Frequency,
                Status = gift.Status,
                GatewayReference = gift.Reference,
                WantsSection18A = true,
                ConsentPopia = true,
                ReceivedAt = complete ? createdAt.AddMinutes(4) : null,

                // The newest completed gift has no certificate yet, so the
                // console's "S18A pending" state has something to show.
                Section18AIssuedAt = complete && gift.Certificate ? createdAt.AddHours(2) : null,
                CreatedAt = createdAt,
            });

            added++;
        }

        return added;
    }

    // ------------------------------------------------------------- resources

    /// <summary>
    /// A handful of resources per core subject, for the grades the seeded
    /// learners are in.
    /// </summary>
    /// <remarks>
    /// The storage paths name files that do not exist. The download endpoint
    /// signs a URL to the path, so a demonstration shows the signed link being
    /// issued; the file behind it is the client's to supply.
    /// </remarks>
    private async Task<int> SeedResourcesAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;

        var library = new (string Subject, string Title, string Topic, ResourceType Type, int? Minutes, long? Bytes)[]
        {
            ("Mathematics", "Trigonometric ratios, part one", "Trigonometry", ResourceType.Video, 18, null),
            ("Mathematics", "Trigonometric ratios, part two", "Trigonometry", ResourceType.Video, 22, null),
            ("Mathematics", "Euclidean geometry worksheet", "Euclidean geometry", ResourceType.Worksheet, null, 412_000),
            ("Mathematics", "November past paper, Paper 1", "Exam preparation", ResourceType.PastPaper, null, 1_240_000),
            ("Physical Science", "Balancing chemical equations", "Chemical change", ResourceType.Video, 15, null),
            ("Physical Science", "Newton's laws worksheet", "Mechanics", ResourceType.Worksheet, null, 356_000),
            ("Physical Science", "June past paper, Paper 2", "Exam preparation", ResourceType.PastPaper, null, 980_000),
            ("Accounting", "Ledger accounts, step by step", "Ledgers", ResourceType.Video, 20, null),
            ("Accounting", "Trial balance worksheet", "Ledgers", ResourceType.Worksheet, null, 288_000),
            ("English HL", "Essay structure that examiners reward", "Writing", ResourceType.Video, 14, null),
            ("English HL", "Set-work reading list", "Literature", ResourceType.ReadingList, null, 96_000),
            ("Life Sciences", "Cell division, explained", "Cells", ResourceType.Video, 16, null),
        };

        var subjects = await context.Subjects
            .Where(subject => subject.Grade >= 10 && subject.Grade <= 12)
            .ToListAsync(cancellationToken);

        var existing = await context.Resources
            .Select(resource => new { resource.SubjectId, resource.Title })
            .ToListAsync(cancellationToken);

        var added = 0;

        foreach (var subject in subjects)
        {
            foreach (var item in library.Where(entry => entry.Subject == subject.Name))
            {
                if (existing.Any(e => e.SubjectId == subject.Id && e.Title == item.Title))
                {
                    continue;
                }

                var slug = new string(item.Title.ToLowerInvariant()
                    .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                    .ToArray());

                context.Resources.Add(new LearningResource
                {
                    SubjectId = subject.Id,
                    Title = item.Title,
                    Topic = item.Topic,
                    Type = item.Type,
                    Grade = subject.Grade,
                    StoragePath = $"resources/grade-{subject.Grade}/{slug}",
                    DurationMinutes = item.Minutes,
                    SizeBytes = item.Bytes,
                    PublishedAt = now.AddDays(-7 - added),
                });

                added++;
            }
        }

        return added;
    }
}

internal static partial class SeedLog
{
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Added {Count} demonstration extras (news, donations, learning resources)")]
    public static partial void ExtrasSeeded(ILogger logger, int count);
}
