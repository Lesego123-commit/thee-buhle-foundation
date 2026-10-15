// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
// =============================================================================

namespace TheeBuhle.Domain.Common;

/// <summary>
/// The closed vocabulary a mentor tags a check-in with, and what each tag means
/// for routing.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One definition, because there used to be three and they
/// disagreed.</strong> The vocabulary was duplicated as an allow-list in
/// <c>MentorshipService</c>, an urgent-areas list on <c>ConcernFlagged</c>, and
/// a programme-areas list in <c>MentorLeadHandler</c> — and
/// <c>"abuse"</c> appeared in the urgent list but not in the allow-list.
/// </para>
/// <para>
/// The consequence was as bad as it sounds. A mentor tagging a check-in
/// <c>abuse</c> had the tag silently dropped by the allow-list before the event
/// was built, so <c>IsUrgent</c> never saw it and the concern was
/// <strong>not escalated</strong>. The single most serious tag in the
/// vocabulary was the one that did not work, and nothing failed or logged.
/// </para>
/// <para>
/// A unit test asserting "an urgent support area escalates at a calm rating"
/// found it. The fix is this file: the vocabulary is declared once, each area
/// carries its own classification, and the three consumers ask rather than
/// repeat. A tag that exists but is unroutable is now impossible to create,
/// because adding one to <see cref="Urgent"/> or <see cref="Programme"/> adds it
/// to <see cref="All"/> by construction.
/// </para>
/// </remarks>
public static class SupportAreas
{
    /// <summary>
    /// Areas that must never be triaged by programme staff.
    /// </summary>
    /// <remarks>
    /// A concern about any of these may be <em>about</em> an adult in the child's
    /// life — possibly one of the staff the earlier links in the escalation chain
    /// represent — so it goes straight to the designated safeguarding lead.
    /// </remarks>
    public static IReadOnlySet<string> Urgent { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "home_situation",
        "safety",
        "wellbeing",
        "abuse",
        "neglect",
        "bullying",
    };

    /// <summary>
    /// Areas a pastoral lead cannot action, whoever they are.
    /// </summary>
    /// <remarks>
    /// Each of these needs a programme or logistics decision — a venue, a
    /// timetable, a transport subsidy. The mentor lead declines them not because
    /// they are too serious but because they are not theirs to fix, which is the
    /// distinction that makes the programme-manager link reachable at all.
    /// </remarks>
    public static IReadOnlySet<string> Programme { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "attendance",
        "transport",
        "venue",
        "sessions",
        "fees",
    };

    /// <summary>
    /// Ordinary pastoral and academic areas, which the mentor lead handles.
    /// </summary>
    public static IReadOnlySet<string> Pastoral { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "euclidean_geometry",
        "exam_technique",
        "confidence",
        "motivation",
        "study_habits",
        "subject_choice",
        "career_guidance",
    };

    /// <summary>
    /// Every recognised area.
    /// </summary>
    /// <remarks>
    /// <strong>Derived from the three sets above, never listed separately.</strong>
    /// That is the whole point of this file: an area cannot be urgent and
    /// simultaneously unrecognised, because the allow-list is computed from the
    /// classifications rather than maintained beside them.
    /// </remarks>
    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(
            Urgent.Concat(Programme).Concat(Pastoral),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether an area is one the system recognises.</summary>
    public static bool IsRecognised(string area) => All.Contains(area);

    /// <summary>Whether any of these areas requires going straight to the safeguarding lead.</summary>
    public static bool AnyUrgent(this IEnumerable<string> areas) => areas.Any(Urgent.Contains);

    /// <summary>Whether any of these areas is a programme matter rather than a pastoral one.</summary>
    public static bool AnyProgramme(this IEnumerable<string> areas) => areas.Any(Programme.Contains);

    /// <summary>
    /// Keeps only the recognised areas, without duplicates.
    /// </summary>
    /// <remarks>
    /// Unrecognised tags are dropped rather than rejected, so a client sending a
    /// stale or invented area does not fail a mentor's check-in — but they are
    /// dropped <em>after</em> this vocabulary has been consulted, so nothing that
    /// affects routing can be silently discarded.
    /// </remarks>
    public static List<string> Clean(IEnumerable<string> areas) =>
        [.. areas.Where(IsRecognised).Distinct(StringComparer.OrdinalIgnoreCase)];
}
