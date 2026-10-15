// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using TheeBuhle.Application.Services;

namespace TheeBuhle.UnitTests.Security;

/// <summary>
/// Tests for the one-time codes that invitations and password resets use.
/// </summary>
/// <remarks>
/// These codes get read out over the phone and copied from SMS messages, so the
/// format rules are about people as much as about security.
/// </remarks>
public sealed class AccountCodesTests
{
    [Fact]
    public void A_code_is_two_groups_of_five_from_the_unambiguous_alphabet()
    {
        var code = AccountCodes.Generate();

        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", code);
        Assert.All(code.Replace("-", string.Empty, StringComparison.Ordinal), character =>
            Assert.Contains(character, AccountCodes.Alphabet));
    }

    [Fact]
    public void The_alphabet_leaves_out_characters_people_confuse()
    {
        foreach (var confusable in "0O1IL")
        {
            Assert.DoesNotContain(confusable, AccountCodes.Alphabet);
        }
    }

    [Fact]
    public void Codes_do_not_repeat()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => AccountCodes.Generate()).ToHashSet();

        Assert.Equal(2000, codes.Count);
    }

    [Theory]
    [InlineData("K7MPQ-2XRT9")]
    [InlineData("k7mpq-2xrt9")]
    [InlineData("K7MPQ 2XRT9")]
    [InlineData(" k7mpq2xrt9 ")]
    public void Case_spaces_and_the_dash_do_not_matter(string typed)
    {
        Assert.True(AccountCodes.Matches(typed, AccountCodes.Hash("K7MPQ-2XRT9")));
    }

    [Theory]
    [InlineData("K7MPQ-2XRT8")]
    [InlineData("")]
    [InlineData(null)]
    public void A_different_code_does_not_match(string? typed)
    {
        Assert.False(AccountCodes.Matches(typed, AccountCodes.Hash("K7MPQ-2XRT9")));
    }

    [Fact]
    public void Only_a_hash_is_stored()
    {
        var hash = AccountCodes.Hash("K7MPQ-2XRT9");

        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain("K7MPQ", hash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 1, 15, "2026-1")]
    [InlineData(2026, 4, 1, "2026-2")]
    [InlineData(2026, 9, 22, "2026-3")]
    [InlineData(2026, 12, 31, "2026-4")]
    public void Enrolments_use_the_school_term(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, AccountService.CurrentTerm(new DateOnly(year, month, day)));
    }
}
