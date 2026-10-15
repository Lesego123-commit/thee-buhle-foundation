// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Microsoft (2026) ASP.NET Core documentation, including WebApplicationFactory integration testing.
//    - Microsoft (2026) Entity Framework Core documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;

namespace TheeBuhle.IntegrationTests;

/// <summary>
/// The safeguarding flow, end to end over HTTP against a real database.
/// </summary>
/// <remarks>
/// <para>
/// The most important tests in the project. Everything here is asserted through
/// the real API, the real Observer chain, the real Chain of Responsibility and
/// the real SQL Server — so what passes is the system a mentor would actually
/// use rather than a unit under laboratory conditions.
/// </para>
/// <para>
/// Three properties are established:
/// </para>
/// <list type="number">
/// <item>A flagged concern reaches the designated lead and nobody else — not
/// the parent, not programme staff, and <strong>not the mentor who wrote
/// it</strong>.</item>
/// <item>The note's text exists in exactly one table.</item>
/// <item>A concern that cannot be routed <strong>is not saved</strong>, and the
/// mentor is told to phone.</item>
/// </list>
/// </remarks>
[Collection(nameof(SafeguardingFlowTests))]
public sealed class SafeguardingFlowTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Disclosure =
        "Told me there is nobody at home in the evenings and she is looking after her brother.";

    [Fact]
    public async Task A_flagged_concern_reaches_the_lead_and_nobody_else()
    {
        var mentor = await fixture.SignInAsync("thandi.mokoena@example.co.za");
        var assignment = await mentor.AssignmentForAsync("A-214");
        var learnerId = await fixture.LearnerIdAsync("A-214");

        // -- the mentor logs a concern ------------------------------------
        var logged = await mentor.PostAsJsonAsync("/api/mentor/check-ins", new
        {
            assignmentId = assignment,
            occurredOn = "2026-09-20",
            mode = "Phone",
            wellbeing = "Concerned",
            notes = Disclosure,
            supportAreas = new[] { "home_situation" },
            concernFlagged = false,
        });

        logged.EnsureSuccessStatusCode();

        var result = await logged.Content.ReadFromJsonAsync<CheckInResponse>();

        // A "concerned" rating escalates on its own, with the box unticked.
        Assert.NotNull(result);
        Assert.True(result.Escalated);

        // -- the lead sees it ---------------------------------------------
        var lead = await fixture.SignInAsync("safeguarding@theebuhlefoundation.co.za");

        var queue = await lead.GetFromJsonAsync<List<ConcernRow>>("/api/safeguarding/concerns");

        Assert.NotNull(queue);

        var concern = Assert.Single(queue, row => row.ReferenceCode == "A-214");

        Assert.Equal("Concerned", concern.Wellbeing);
        Assert.Contains("nobody at home", concern.Notes ?? string.Empty, StringComparison.Ordinal);

        // It was routed to somebody, and the routing is recorded.
        Assert.NotNull(concern.EscalatedTo);
        Assert.NotNull(concern.EscalatedAt);

        // -- and nobody else does ----------------------------------------
        await AssertCannotSeeTheNoteAsync("nomvula.mahlangu@example.co.za", learnerId, "the parent");
        await AssertCannotSeeTheNoteAsync("thandi.mokoena@example.co.za", learnerId, "the mentor who wrote it");
        await AssertCannotSeeTheNoteAsync("programmes@theebuhlefoundation.co.za", learnerId, "programme staff");
    }

    /// <summary>
    /// The note's text exists in exactly one table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted against the database rather than through the API, because the
    /// point is not what a response contains — it is that a disclosure about a
    /// child was not <em>copied</em> anywhere. The audit trail and the
    /// notification both describe the concern; neither quotes it.
    /// </para>
    /// <para>
    /// That matters because the audit trail is append-only and the notification
    /// text appears on a lock screen.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_notes_text_is_never_copied_out_of_the_check_in()
    {
        var mentor = await fixture.SignInAsync("pieter.venter@example.co.za");
        var assignment = await mentor.AssignmentForAsync("A-244");

        const string Note = "UNIQUE-MARKER-9f3a a disclosure that must not be copied";

        var logged = await mentor.PostAsJsonAsync("/api/mentor/check-ins", new
        {
            assignmentId = assignment,
            occurredOn = "2026-09-20",
            mode = "InPerson",
            wellbeing = "Concerned",
            notes = Note,
            supportAreas = new[] { "safety" },
            concernFlagged = true,
        });

        logged.EnsureSuccessStatusCode();

        await using var context = fixture.CreateDbContext();

        var inCheckIns = await context.CheckIns
            .CountAsync(checkIn => checkIn.Notes != null && checkIn.Notes.Contains("UNIQUE-MARKER-9f3a"));

        var inAudit = await context.AuditLog.CountAsync(entry =>
            (entry.AfterJson != null && entry.AfterJson.Contains("UNIQUE-MARKER-9f3a"))
            || (entry.BeforeJson != null && entry.BeforeJson.Contains("UNIQUE-MARKER-9f3a")));

        var inNotifications = await context.Notifications.CountAsync(notification =>
            notification.Body.Contains("UNIQUE-MARKER-9f3a")
            || notification.Title.Contains("UNIQUE-MARKER-9f3a"));

        Assert.Equal(1, inCheckIns);
        Assert.Equal(0, inAudit);
        Assert.Equal(0, inNotifications);
    }

    /// <summary>
    /// The notification to the lead carries no detail.
    /// </summary>
    /// <remarks>
    /// Push notification text appears on a lock screen, and the phone may be on
    /// a kitchen table or shared. The lead is told that a flagged concern is
    /// waiting and where to read it, identified by pseudonym — and nothing else.
    /// </remarks>
    [Fact]
    public async Task The_lead_is_notified_by_pseudonym_with_no_detail()
    {
        var mentor = await fixture.SignInAsync("thandi.mokoena@example.co.za");
        var assignment = await mentor.AssignmentForAsync("A-227");

        var logged = await mentor.PostAsJsonAsync("/api/mentor/check-ins", new
        {
            assignmentId = assignment,
            occurredOn = "2026-09-20",
            mode = "Phone",
            wellbeing = "Concerned",
            notes = "Something specific and private about a child's family.",
            supportAreas = new[] { "home_situation" },
            concernFlagged = true,
        });

        logged.EnsureSuccessStatusCode();

        await using var context = fixture.CreateDbContext();

        var notification = await context.Notifications
            .Where(n => n.Title.Contains("flagged concern"))
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(notification);

        // The pseudonym is present.
        Assert.Contains("A-227", notification.Body, StringComparison.Ordinal);

        // The learner's real name is not.
        Assert.DoesNotContain("Karabo", notification.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nkosi", notification.Body, StringComparison.OrdinalIgnoreCase);

        // Nor is anything the mentor wrote.
        Assert.DoesNotContain("family", notification.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A concern that cannot be routed is not saved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The single most important behaviour in the system.</strong> A
    /// mentor who is told "check-in saved" for a concern that reached nobody is
    /// the worst outcome this system can produce — worse than an error, because
    /// the mentor stops worrying.
    /// </para>
    /// <para>
    /// The designated lead is removed, a concern is flagged, and the assertion is
    /// that the check-in count is <em>unchanged</em>. This exercises the real
    /// transaction rollback through the real non-advisory listener, which is why
    /// it belongs here rather than in the unit suite.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_unroutable_concern_is_refused_and_the_check_in_is_not_saved()
    {
        var mentor = await fixture.SignInAsync("pieter.venter@example.co.za");
        var assignment = await mentor.AssignmentForAsync("A-231");

        int before;

        // Remove the only designated safeguarding lead.
        await using (var setup = fixture.CreateDbContext())
        {
            before = await setup.CheckIns.CountAsync();

            await setup.Staff
                .Where(staff => staff.IsSafeguardingLead)
                .ExecuteUpdateAsync(set => set.SetProperty(staff => staff.IsSafeguardingLead, false));
        }

        try
        {
            var response = await mentor.PostAsJsonAsync("/api/mentor/check-ins", new
            {
                assignmentId = assignment,
                occurredOn = "2026-09-20",
                mode = "InPerson",
                wellbeing = "Concerned",
                notes = "THIS-MUST-NOT-BE-SAVED",
                supportAreas = new[] { "safety" },
                concernFlagged = true,
            });

            // 503, not 500: genuinely temporary and retryable, and the message
            // has to tell the mentor what to do instead.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("NOT been saved", body, StringComparison.Ordinal);
            Assert.Contains("phone", body, StringComparison.OrdinalIgnoreCase);

            await using var check = fixture.CreateDbContext();

            // Nothing was written.
            Assert.Equal(before, await check.CheckIns.CountAsync());

            Assert.Equal(0, await check.CheckIns
                .CountAsync(c => c.Notes != null && c.Notes.Contains("THIS-MUST-NOT-BE-SAVED")));
        }
        finally
        {
            // Restore the lead, so a failure here does not cascade into every
            // later test in this class.
            await using var restore = fixture.CreateDbContext();

            await restore.Staff
                .Where(staff => staff.JobTitle == "Safeguarding Lead")
                .ExecuteUpdateAsync(set => set.SetProperty(staff => staff.IsSafeguardingLead, true));
        }
    }

    /// <summary>
    /// An ordinary check-in is saved and is visible to the parent.
    /// </summary>
    /// <remarks>
    /// The control case. Without it, a bug that hid <em>every</em> check-in from
    /// a parent would pass all the tests above — the visibility assertions would
    /// still hold, for the wrong reason.
    /// </remarks>
    [Fact]
    public async Task An_ordinary_check_in_is_visible_to_the_parent()
    {
        var mentor = await fixture.SignInAsync("thandi.mokoena@example.co.za");
        var assignment = await mentor.AssignmentForAsync("A-214");
        var learnerId = await fixture.LearnerIdAsync("A-214");

        var logged = await mentor.PostAsJsonAsync("/api/mentor/check-ins", new
        {
            assignmentId = assignment,
            occurredOn = "2026-09-13",
            mode = "InPerson",
            wellbeing = "Thriving",
            notes = "VISIBLE-NOTE working through trigonometry, confident about the test",
            supportAreas = new[] { "exam_technique" },
            concernFlagged = false,
        });

        logged.EnsureSuccessStatusCode();

        var parent = await fixture.SignInAsync("nomvula.mahlangu@example.co.za");

        var progress = await parent.GetFromJsonAsync<LearnerProgressRow>(
            $"/api/learners/{learnerId}/progress");

        Assert.NotNull(progress);

        Assert.Contains(
            progress.CheckIns,
            checkIn => (checkIn.Notes ?? string.Empty).Contains("VISIBLE-NOTE", StringComparison.Ordinal));
    }

    private async Task AssertCannotSeeTheNoteAsync(string email, Guid learnerId, string who)
    {
        var client = await fixture.SignInAsync(email);

        var progress = await client.GetFromJsonAsync<LearnerProgressRow>(
            $"/api/learners/{learnerId}/progress");

        Assert.NotNull(progress);

        var leaked = progress.CheckIns
            .Where(checkIn => (checkIn.Notes ?? string.Empty).Contains("nobody at home", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(leaked);

        Assert.DoesNotContain(progress.CheckIns, checkIn => checkIn.ConcernFlagged);
    }

    private sealed record CheckInResponse(Guid CheckInId, bool Escalated, string Message);

    private sealed record ConcernRow(
        Guid CheckInId,
        string ReferenceCode,
        string Wellbeing,
        string? Notes,
        Guid? EscalatedTo,
        DateTimeOffset? EscalatedAt);

    private sealed record LearnerProgressRow(
        Guid LearnerId,
        string ReferenceCode,
        List<CheckInRow> CheckIns);

    private sealed record CheckInRow(Guid CheckInId, string? Notes, bool ConcernFlagged);
}
