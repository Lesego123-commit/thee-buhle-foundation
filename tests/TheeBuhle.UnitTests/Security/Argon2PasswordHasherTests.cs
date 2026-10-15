// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - OWASP (2024) Password Storage Cheat Sheet.
// =============================================================================

using System.Diagnostics;
using TheeBuhle.Infrastructure.Security;

namespace TheeBuhle.UnitTests.Security;

/// <summary>
/// Tests for the password hasher.
/// </summary>
/// <remarks>
/// Worth testing properly rather than trusting, because every failure mode here
/// is silent. A hasher that always returns true for <c>Verify</c> passes a
/// smoke test, logs nothing and lets anybody into any account.
/// </remarks>
public sealed class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher hasher = new();

    [Fact]
    public void Verify_accepts_the_correct_password()
    {
        var hash = hasher.Hash("correct horse battery staple");

        Assert.True(hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_rejects_the_wrong_password()
    {
        var hash = hasher.Hash("correct horse battery staple");

        Assert.False(hasher.Verify("correct horse battery stapler", hash));
    }

    [Fact]
    public void Verify_is_case_sensitive()
    {
        var hash = hasher.Hash("Sebokeng2026");

        Assert.False(hasher.Verify("sebokeng2026", hash));
    }

    /// <summary>
    /// The same password must produce a different hash every time.
    /// </summary>
    /// <remarks>
    /// This is the salt working. Without it, two people who choose the same
    /// password get the same hash — so a leaked database immediately reveals
    /// which accounts share a password, and one cracked hash cracks all of them.
    /// </remarks>
    [Fact]
    public void Hash_is_salted_so_two_hashes_of_one_password_differ()
    {
        var first = hasher.Hash("Vanderbijlpark!1");
        var second = hasher.Hash("Vanderbijlpark!1");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("Vanderbijlpark!1", first));
        Assert.True(hasher.Verify("Vanderbijlpark!1", second));
    }

    [Fact]
    public void Hash_records_the_parameters_it_used()
    {
        var parts = hasher.Hash("anything").Split('$');

        Assert.Equal(6, parts.Length);
        Assert.Equal("argon2id", parts[0]);

        // 19 MiB, 2 passes, 1 lane — the OWASP minimum for Argon2id.
        Assert.Equal("19456", parts[1]);
        Assert.Equal("2", parts[2]);
        Assert.Equal("1", parts[3]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("argon2id$19456$2$1$only-five-parts")]
    [InlineData("bcrypt$19456$2$1$c2FsdA==$aGFzaA==")]
    [InlineData("argon2id$notanumber$2$1$c2FsdA==$aGFzaA==")]
    [InlineData("argon2id$19456$2$1$!!!not-base64!!!$aGFzaA==")]
    public void Verify_returns_false_for_a_malformed_hash_rather_than_throwing(string malformed)
    {
        // False, not an exception. The caller is asking "was this the right
        // password", and a corrupted stored hash means no password is right —
        // whereas an exception here would surface as a 500 on the login page.
        Assert.False(hasher.Verify("anything", malformed));
    }

    [Fact]
    public void NeedsRehash_is_false_for_a_hash_this_version_produced()
    {
        Assert.False(hasher.NeedsRehash(hasher.Hash("current")));
    }

    [Theory]
    // Weaker on memory only.
    [InlineData("argon2id$4096$2$1$c2FsdA==$aGFzaA==")]
    // Weaker on iterations only — the axis a naive check forgets.
    [InlineData("argon2id$19456$1$1$c2FsdA==$aGFzaA==")]
    // Unparseable, so it cannot be verified either.
    [InlineData("$2y$10$abcdefghijklmnopqrstuv")]
    [InlineData("")]
    public void NeedsRehash_is_true_for_anything_weaker_or_unrecognisable(string stored)
    {
        Assert.True(hasher.NeedsRehash(stored));
    }

    /// <summary>
    /// The timing defence against account enumeration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AuthService</c> returns one identical message for a wrong password, an
    /// unknown email and a locked account. That closes the <em>visible</em>
    /// channel. This closes the measurable one: without
    /// <c>DummyVerify</c> the unknown-email path skips Argon2 entirely and
    /// returns in microseconds, so the response time answers the question the
    /// error message refuses to.
    /// </para>
    /// <para>
    /// The assertion is loose on purpose — a factor of five, not a percentage.
    /// A CI runner is a noisy, shared machine and a tight bound would fail
    /// intermittently for reasons that have nothing to do with the code. What is
    /// being asserted is that the dummy path costs roughly what the real one
    /// does, not that it matches to the millisecond; the failure this catches is
    /// somebody deleting the <c>DummyVerify</c> call, which is a factor of a
    /// thousand, not five.
    /// </para>
    /// </remarks>
    [Fact]
    public void DummyVerify_costs_about_as_much_as_a_real_verify()
    {
        var hash = hasher.Hash("baseline");

        // Warm up, so JIT and the first large allocation are not attributed to
        // whichever of the two runs happens to go first.
        _ = hasher.Verify("baseline", hash);
        hasher.DummyVerify("baseline");

        var real = Time(() => hasher.Verify("baseline", hash));
        var dummy = Time(() => hasher.DummyVerify("baseline"));

        Assert.InRange(dummy.TotalMilliseconds, real.TotalMilliseconds / 5, real.TotalMilliseconds * 5);
    }

    private static TimeSpan Time(Action action)
    {
        const int Runs = 5;

        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < Runs; i++)
        {
            action();
        }

        return stopwatch.Elapsed / Runs;
    }
}
