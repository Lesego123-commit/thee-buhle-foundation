// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;

namespace TheeBuhle.UnitTests.Domain;

/// <summary>
/// The support-area vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// These tests exist because of a real bug that nothing else would have caught.
/// The vocabulary was duplicated in three places — an allow-list in the
/// mentorship service, an urgent-areas list on the domain event, and a
/// programme-areas list in the escalation chain — and <c>"abuse"</c> was in the
/// urgent list but not in the allow-list.
/// </para>
/// <para>
/// So a mentor tagging a check-in <c>abuse</c> had the tag silently dropped
/// before the event was built, <c>IsUrgent</c> never saw it, and the concern was
/// not escalated. The most serious tag in the vocabulary did nothing, and
/// nothing failed or logged.
/// </para>
/// <para>
/// The structural tests below are the guard. They assert the <em>relationships
/// between the sets</em> rather than their contents, so they keep holding as
/// areas are added — which is exactly when the divergence would otherwise creep
/// back.
/// </para>
/// </remarks>
public sealed class SupportAreaVocabularyTests
{
    /// <summary>
    /// Every classified area is a recognised area.
    /// </summary>
    /// <remarks>
    /// The invariant that was violated. An area the routing rules act on but the
    /// allow-list rejects is unreachable — the tag is discarded before the rule
    /// ever runs.
    /// </remarks>
    [Fact]
    public void Every_urgent_area_is_a_recognised_area()
    {
        var unreachable = SupportAreas.Urgent.Where(area => !SupportAreas.IsRecognised(area)).ToList();

        Assert.Empty(unreachable);
    }

    [Fact]
    public void Every_programme_area_is_a_recognised_area()
    {
        var unreachable = SupportAreas.Programme.Where(area => !SupportAreas.IsRecognised(area)).ToList();

        Assert.Empty(unreachable);
    }

    [Fact]
    public void Every_pastoral_area_is_a_recognised_area()
    {
        var unreachable = SupportAreas.Pastoral.Where(area => !SupportAreas.IsRecognised(area)).ToList();

        Assert.Empty(unreachable);
    }

    /// <summary>
    /// Every recognised area has exactly one classification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An area in two sets would route ambiguously. An area in none would be
    /// accepted and stored and then fall through every link of the chain to the
    /// safeguarding lead — which is safe, but means an academic tag lands in the
    /// child-protection queue.
    /// </para>
    /// <para>
    /// This is the assertion that makes adding an area safe: put it in one set
    /// and everything follows, put it in two or none and this fails.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_recognised_area_has_exactly_one_classification()
    {
        var miscategorised = SupportAreas.All
            .Select(area => new
            {
                Area = area,
                Count =
                    (SupportAreas.Urgent.Contains(area) ? 1 : 0)
                    + (SupportAreas.Programme.Contains(area) ? 1 : 0)
                    + (SupportAreas.Pastoral.Contains(area) ? 1 : 0),
            })
            .Where(row => row.Count != 1)
            .Select(row => $"{row.Area} is in {row.Count} sets")
            .ToList();

        Assert.Empty(miscategorised);
    }

    /// <summary>
    /// The three classification sets are disjoint.
    /// </summary>
    /// <remarks>
    /// The same invariant from the other direction, and worth asserting
    /// separately: a set-level failure names the overlap, where the per-area one
    /// names the area.
    /// </remarks>
    [Fact]
    public void The_three_classifications_do_not_overlap()
    {
        Assert.Empty(SupportAreas.Urgent.Intersect(SupportAreas.Programme, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(SupportAreas.Urgent.Intersect(SupportAreas.Pastoral, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(SupportAreas.Programme.Intersect(SupportAreas.Pastoral, StringComparer.OrdinalIgnoreCase));
    }

    // -------------------------------------------------- the routing behaviour

    /// <summary>
    /// Every urgent area, on its own, makes a concern urgent.
    /// </summary>
    /// <remarks>
    /// Driven from the vocabulary rather than from a hard-coded list, so a newly
    /// added urgent area is covered the moment it is declared — which is the only
    /// way this test keeps being true rather than becoming a snapshot of what was
    /// urgent when it was written.
    /// </remarks>
    [Fact]
    public void Every_urgent_area_makes_a_concern_urgent_at_a_calm_rating()
    {
        var notUrgent = SupportAreas.Urgent
            .Where(area => !Concern(WellbeingRating.Steady, area).IsUrgent)
            .ToList();

        Assert.Empty(notUrgent);
    }

    [Fact]
    public void No_pastoral_area_alone_makes_a_concern_urgent()
    {
        var wronglyUrgent = SupportAreas.Pastoral
            .Where(area => Concern(WellbeingRating.Steady, area).IsUrgent)
            .ToList();

        Assert.Empty(wronglyUrgent);
    }

    [Fact]
    public void No_programme_area_alone_makes_a_concern_urgent()
    {
        var wronglyUrgent = SupportAreas.Programme
            .Where(area => Concern(WellbeingRating.Steady, area).IsUrgent)
            .ToList();

        Assert.Empty(wronglyUrgent);
    }

    /// <summary>
    /// A "concerned" rating is urgent regardless of the area.
    /// </summary>
    /// <remarks>
    /// Driven across every recognised area, so no combination of rating and tag
    /// can produce a concerned learner whose concern is not treated as urgent.
    /// </remarks>
    [Fact]
    public void A_concerned_rating_is_urgent_whatever_the_area()
    {
        var notUrgent = SupportAreas.All
            .Where(area => !Concern(WellbeingRating.Concerned, area).IsUrgent)
            .ToList();

        Assert.Empty(notUrgent);
        Assert.True(Concern(WellbeingRating.Concerned).IsUrgent, "with no areas at all, the rating must still be enough");
    }

    // ------------------------------------------------------------- cleaning

    [Fact]
    public void Cleaning_keeps_recognised_areas_and_drops_the_rest()
    {
        var cleaned = SupportAreas.Clean(["exam_technique", "not_a_real_area", "safety"]);

        Assert.Equal(2, cleaned.Count);
        Assert.Contains("exam_technique", cleaned);
        Assert.Contains("safety", cleaned);
    }

    [Fact]
    public void Cleaning_removes_duplicates_regardless_of_case()
    {
        var cleaned = SupportAreas.Clean(["safety", "SAFETY", "Safety"]);

        Assert.Single(cleaned);
    }

    /// <summary>
    /// Cleaning never drops an area that affects routing.
    /// </summary>
    /// <remarks>
    /// The property whose absence was the original bug, asserted directly: pass
    /// every urgent area through the cleaner and get all of them back.
    /// </remarks>
    [Fact]
    public void Cleaning_never_drops_an_urgent_area()
    {
        var cleaned = SupportAreas.Clean(SupportAreas.Urgent);

        Assert.Equal(SupportAreas.Urgent.Count, cleaned.Count);
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("Home_Situation")]
    [InlineData("aBuSe")]
    public void Area_matching_ignores_case(string area)
    {
        Assert.True(SupportAreas.IsRecognised(area));
        Assert.True(Concern(WellbeingRating.Steady, area).IsUrgent);
    }

    private static ConcernFlagged Concern(WellbeingRating wellbeing, params string[] areas) =>
        new(
            CheckInId: Guid.CreateVersion7(),
            AssignmentId: Guid.CreateVersion7(),
            LearnerId: Guid.CreateVersion7(),
            LearnerReference: "A-214",
            MentorId: Guid.CreateVersion7(),
            Wellbeing: wellbeing,
            SupportAreas: areas);
}
