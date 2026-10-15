// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using Microsoft.Extensions.Logging.Abstractions;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Domain.Security;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Services;

/// <summary>
/// Capturing a Saturday register.
/// </summary>
/// <remarks>
/// <para>
/// Two properties matter here. The register is captured <strong>in one
/// transaction</strong>, because a half-saved register looks complete while the
/// missing learners are silently absent from every report. And each mark raises
/// <strong>exactly one</strong> domain event, which is what makes the Observer
/// chain — badges, the parent notice, the cached summary — fire once per learner
/// rather than once per retry.
/// </para>
/// </remarks>
public sealed class AttendanceServiceTests
{
    private readonly FakeAttendanceRepository attendance = new();

    private readonly FakeSessionRepository sessions = new();

    private readonly FakeLearnerRepository learners = new();

    private readonly FakeAuditRepository audit = new();

    private readonly FakeEventDispatcher events = new();

    private readonly FakeUnitOfWork unitOfWork = new();

    private readonly FakeClock clock = new();

    private readonly AttendanceService service;

    private readonly TutoringSession session;

    private readonly Learner learner;

    public AttendanceServiceTests()
    {
        learner = learners.Add("A-214", firstName: "Lebo", surname: "Mahlangu");

        session = sessions.AddSession(
            new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 9, 19, 12, 30, 0, TimeSpan.FromHours(2)));

        sessions.Enrol(learner.Id, session.Id);

        service = new AttendanceService(
            attendance,
            learners,
            sessions,
            audit,
            events,
            unitOfWork,
            new FakeRequestContext(),
            clock,
            NullLogger<AttendanceService>.Instance);
    }

    // -------------------------------------------------------------- one mark

    [Fact]
    public async Task Capturing_a_mark_creates_it_and_raises_one_event()
    {
        var result = await service.CaptureAsync(Staff, session.Id, learner.Id, AttendanceStatus.Present, null, null);

        Assert.True(result.Created);
        Assert.Single(attendance.Added);
        Assert.Single(events.Of<AttendanceRecorded>());
    }

    /// <summary>
    /// The event carries the pseudonym, never the name.
    /// </summary>
    /// <remarks>
    /// The event's context is written straight into the log by the dispatcher, so
    /// a name on it would put an identified child in a log file. This asserts the
    /// serialised context as well as the property, because the property being
    /// right does not guarantee the context is.
    /// </remarks>
    [Fact]
    public async Task The_event_identifies_the_learner_by_reference_code_only()
    {
        await service.CaptureAsync(Staff, session.Id, learner.Id, AttendanceStatus.Present, null, null);

        var raised = Assert.Single(events.Of<AttendanceRecorded>());

        Assert.Equal("A-214", raised.LearnerReference);

        var context = System.Text.Json.JsonSerializer.Serialize(raised.Context);

        Assert.Contains("A-214", context, StringComparison.Ordinal);
        Assert.DoesNotContain("Mahlangu", context, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Lebo", context, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Capturing the same learner twice amends rather than duplicating.
    /// </summary>
    /// <remarks>
    /// A staff member correcting a mistyped mark is the normal case, so this has
    /// to be safe. The response says which it was, so the console can show
    /// "amended" rather than implying a second learner was added.
    /// </remarks>
    [Fact]
    public async Task Re_capturing_a_learner_amends_the_existing_mark()
    {
        await service.CaptureAsync(Staff, session.Id, learner.Id, AttendanceStatus.Absent, null, null);

        // The repository now finds the existing row, as the real one would.
        attendance.Existing = attendance.Added[0];

        var second = await service.CaptureAsync(Staff, session.Id, learner.Id, AttendanceStatus.Present, null, null);

        Assert.False(second.Created);
        Assert.Single(attendance.Added);
        Assert.Equal(AttendanceStatus.Present, attendance.Added[0].Status);
    }

    [Fact]
    public async Task A_learner_not_enrolled_in_the_sessions_programme_is_refused()
    {
        var stranger = learners.Add("A-999");

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CaptureAsync(Staff, session.Id, stranger.Id, AttendanceStatus.Present, null, null));

        Assert.Empty(attendance.Added);
    }

    [Fact]
    public async Task An_unknown_session_is_refused()
    {
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CaptureAsync(Staff, Guid.CreateVersion7(), learner.Id, AttendanceStatus.Present, null, null));
    }

    // --------------------------------------------------------- arrival times

    /// <summary>
    /// An arrival time before the session started is clamped to the start.
    /// </summary>
    /// <remarks>
    /// A phone with a wrong clock, or a staff member typing 09:00 as 08:00, must
    /// not record a learner arriving three hours before the session — which would
    /// then appear in a report as a negative lateness.
    /// </remarks>
    [Fact]
    public async Task An_arrival_before_the_session_started_is_clamped_to_the_start()
    {
        await service.CaptureAsync(
            Staff,
            session.Id,
            learner.Id,
            AttendanceStatus.Late,
            session.StartsAt.AddHours(-3),
            null);

        Assert.Equal(session.StartsAt, attendance.Added[0].ArrivedAt);
    }

    [Fact]
    public async Task An_arrival_after_the_session_ended_is_clamped_to_the_end()
    {
        await service.CaptureAsync(
            Staff,
            session.Id,
            learner.Id,
            AttendanceStatus.Late,
            session.EndsAt.AddHours(4),
            null);

        Assert.Equal(session.EndsAt, attendance.Added[0].ArrivedAt);
    }

    [Fact]
    public async Task An_absence_records_no_arrival_time()
    {
        await service.CaptureAsync(Staff, session.Id, learner.Id, AttendanceStatus.Absent, null, null);

        Assert.Null(attendance.Added[0].ArrivedAt);
    }

    // ------------------------------------------------------------ the bulk path

    [Fact]
    public async Task A_whole_register_is_captured_in_one_transaction()
    {
        var second = learners.Add("A-227");
        var third = learners.Add("A-231");

        sessions.Enrol(second.Id, session.Id);
        sessions.Enrol(third.Id, session.Id);

        var result = await service.CaptureBulkAsync(Staff, session.Id, [
            new RegisterEntry(learner.Id, AttendanceStatus.Present),
            new RegisterEntry(second.Id, AttendanceStatus.Absent),
            new RegisterEntry(third.Id, AttendanceStatus.Late, session.StartsAt.AddMinutes(20)),
        ]);

        Assert.Equal(3, result.Recorded);
        Assert.Equal(3, attendance.Added.Count);

        // One transaction, one commit — not three.
        Assert.Equal(1, unitOfWork.Commits);
    }

    [Fact]
    public async Task One_event_is_raised_per_learner_in_a_bulk_capture()
    {
        var second = learners.Add("A-227");

        sessions.Enrol(second.Id, session.Id);

        await service.CaptureBulkAsync(Staff, session.Id, [
            new RegisterEntry(learner.Id, AttendanceStatus.Present),
            new RegisterEntry(second.Id, AttendanceStatus.Absent),
        ]);

        Assert.Equal(2, events.Of<AttendanceRecorded>().Count());
    }

    /// <summary>
    /// The same learner twice in one payload is refused.
    /// </summary>
    /// <remarks>
    /// Two different marks for one learner in one request means the client has a
    /// bug. Last-write-wins would hide it, and guessing which mark was meant is
    /// not the server's decision to make.
    /// </remarks>
    [Fact]
    public async Task A_duplicate_learner_in_one_register_is_refused()
    {
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CaptureBulkAsync(Staff, session.Id, [
                new RegisterEntry(learner.Id, AttendanceStatus.Present),
                new RegisterEntry(learner.Id, AttendanceStatus.Absent),
            ]));

        Assert.Contains("same learner more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(attendance.Added);
    }

    [Fact]
    public async Task An_empty_register_is_refused()
    {
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CaptureBulkAsync(Staff, session.Id, []));
    }

    /// <summary>
    /// An absurdly large batch is refused rather than attempted.
    /// </summary>
    /// <remarks>
    /// 400 is far larger than the biggest real session, so a payload above it is
    /// a runaway client rather than a busy Saturday — and a million-row
    /// transaction would hold locks long enough to take the API down.
    /// </remarks>
    [Fact]
    public async Task A_batch_larger_than_the_cap_is_refused()
    {
        var entries = Enumerable.Range(0, 401)
            .Select(_ => new RegisterEntry(Guid.CreateVersion7(), AttendanceStatus.Present))
            .ToList();

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CaptureBulkAsync(Staff, session.Id, entries));

        Assert.Empty(attendance.Added);
    }

    /// <summary>
    /// One unenrolled learner fails the whole register.
    /// </summary>
    /// <remarks>
    /// All or nothing, deliberately. A register that saved nineteen of twenty
    /// marks is worse than one that saved none, because the nineteen look
    /// complete and nobody goes looking for the twentieth.
    /// </remarks>
    [Fact]
    public async Task One_bad_entry_rolls_the_whole_register_back()
    {
        var stranger = learners.Add("A-999");

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CaptureBulkAsync(Staff, session.Id, [
                new RegisterEntry(learner.Id, AttendanceStatus.Present),
                new RegisterEntry(stranger.Id, AttendanceStatus.Present),
            ]));

        Assert.Equal(1, unitOfWork.Rollbacks);
        Assert.Equal(0, unitOfWork.Commits);
    }

    // -------------------------------------------------------------- the audit

    [Fact]
    public async Task Capturing_a_mark_writes_an_audit_entry_without_the_learners_name()
    {
        await service.CaptureAsync(Staff, session.Id, learner.Id, AttendanceStatus.Absent, null, "Taxi fare");

        Assert.NotEmpty(audit.Entries);
        Assert.False(
            audit.AnySnapshotContains("Mahlangu"),
            "the audit trail must identify the learner by reference code, not by name");
        Assert.True(audit.AnySnapshotContains("A-214"));
    }

    private static AuthenticatedUser Staff => new(
        Guid.CreateVersion7(),
        "staff@test",
        Role.Staff,
        StaffId: Guid.CreateVersion7(),
        DisplayName: "A Staff Member");
}

/// <summary>A session repository with an in-memory enrolment set.</summary>
internal sealed class FakeSessionRepository : ISessionRepository
{
    private readonly Dictionary<Guid, TutoringSession> sessions = [];

    private readonly HashSet<(Guid LearnerId, Guid SessionId)> enrolments = [];

    public TutoringSession AddSession(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var session = new TutoringSession
        {
            ProgrammeId = Guid.CreateVersion7(),
            VenueId = Guid.CreateVersion7(),
            StartsAt = startsAt,
            EndsAt = endsAt,
            Capacity = 40,
        };

        sessions[session.Id] = session;

        return session;
    }

    public void Enrol(Guid learnerId, Guid sessionId) => enrolments.Add((learnerId, sessionId));

    public Task<TutoringSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(sessions.GetValueOrDefault(sessionId));

    public Task<bool> IsEnrolledInSessionProgrammeAsync(Guid learnerId, Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(enrolments.Contains((learnerId, sessionId)));

    public Task<SessionCard?> NextForLearnerAsync(Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<SessionCard?>(null);

    public Task<IReadOnlyList<SessionCard>> UpcomingAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SessionCard>>([]);

    public Task<IReadOnlyList<RegisterState>> RecentWithRegisterStateAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegisterState>>([]);

    public void Add(TutoringSession session) => sessions[session.Id] = session;
}
