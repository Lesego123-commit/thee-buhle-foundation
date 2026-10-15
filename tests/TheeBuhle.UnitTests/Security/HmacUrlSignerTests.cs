// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
// =============================================================================

using TheeBuhle.Domain.Common;
using TheeBuhle.Infrastructure.Security;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Security;

/// <summary>
/// Tests for the HMAC signer — consent signatures and download links.
/// </summary>
public sealed class HmacUrlSignerTests
{
    private static readonly Guid Resource = Guid.Parse("0195a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");

    private static readonly Guid Learner = Guid.Parse("0195a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c");

    private static readonly Guid Form = Guid.Parse("0195a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5d");

    private static readonly Guid Parent = Guid.Parse("0195a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5e");

    private readonly FakeClock clock = new(new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.FromHours(2)));

    private readonly HmacUrlSigner signer;

    public HmacUrlSignerTests() =>
        signer = new HmacUrlSigner(
            new SigningOptions { SigningKey = "a-test-key-of-at-least-thirty-two-bytes" },
            clock);

    // ------------------------------------------------------------- downloads

    [Fact]
    public void A_signature_it_produced_verifies()
    {
        var expires = clock.Now.AddMinutes(15);
        var signature = signer.SignDownload(Resource, Learner, expires);

        Assert.True(signer.VerifyDownload(Resource, Learner, expires, signature));
    }

    /// <summary>
    /// The attack the user id in the payload exists to stop.
    /// </summary>
    /// <remarks>
    /// A learner who copies their download link and sends it to somebody else
    /// must not have handed over a working link. Signing the user into the URL
    /// is what makes the link personal rather than a bearer token for the file.
    /// </remarks>
    [Fact]
    public void A_link_signed_for_one_user_does_not_verify_for_another()
    {
        var expires = clock.Now.AddMinutes(15);
        var signature = signer.SignDownload(Resource, Learner, expires);

        Assert.False(signer.VerifyDownload(Resource, Guid.CreateVersion7(), expires, signature));
    }

    /// <summary>
    /// The attack the resource id in the payload exists to stop.
    /// </summary>
    /// <remarks>
    /// Without the resource in the signature, a signed link to one PDF is a
    /// signed link to every file in storage — change the id in the URL and keep
    /// the signature.
    /// </remarks>
    [Fact]
    public void A_link_signed_for_one_resource_does_not_verify_for_another()
    {
        var expires = clock.Now.AddMinutes(15);
        var signature = signer.SignDownload(Resource, Learner, expires);

        Assert.False(signer.VerifyDownload(Guid.CreateVersion7(), Learner, expires, signature));
    }

    /// <summary>
    /// The attack that makes it worth signing the expiry rather than merely
    /// sending it.
    /// </summary>
    /// <remarks>
    /// The expiry travels as its own URL parameter, so the obvious attack is to
    /// edit it. Because it is inside the signed payload, editing it changes what
    /// the signature would have to be — so the extended link fails the signature
    /// check, not the expiry check.
    /// </remarks>
    [Fact]
    public void Extending_the_expiry_in_the_url_invalidates_the_signature()
    {
        var expires = clock.Now.AddMinutes(15);
        var signature = signer.SignDownload(Resource, Learner, expires);

        Assert.False(signer.VerifyDownload(Resource, Learner, expires.AddYears(1), signature));
    }

    [Fact]
    public void An_expired_link_does_not_verify()
    {
        var expires = clock.Now.AddMinutes(15);
        var signature = signer.SignDownload(Resource, Learner, expires);

        // The signature is still perfectly valid; the link is simply old.
        clock.Advance(TimeSpan.FromMinutes(16));

        Assert.False(signer.VerifyDownload(Resource, Learner, expires, signature));
    }

    [Fact]
    public void A_link_still_inside_its_window_verifies()
    {
        var expires = clock.Now.AddMinutes(15);
        var signature = signer.SignDownload(Resource, Learner, expires);

        clock.Advance(TimeSpan.FromMinutes(14));

        Assert.True(signer.VerifyDownload(Resource, Learner, expires, signature));
    }

    [Theory]
    [InlineData("")]
    [InlineData("deadbeef")]
    [InlineData("not hex at all")]
    public void A_garbage_signature_does_not_verify(string garbage)
    {
        Assert.False(signer.VerifyDownload(Resource, Learner, clock.Now.AddMinutes(15), garbage));
    }

    /// <summary>
    /// Without the server key, a signature cannot be produced.
    /// </summary>
    /// <remarks>
    /// The property that makes the whole scheme worth anything: an attacker who
    /// can see a hundred valid links still cannot mint the hundred-and-first,
    /// because they do not have the key.
    /// </remarks>
    [Fact]
    public void A_signature_made_with_a_different_key_does_not_verify()
    {
        var expires = clock.Now.AddMinutes(15);

        var attacker = new HmacUrlSigner(
            new SigningOptions { SigningKey = "a-different-key-of-at-least-32-bytes!" },
            clock);

        var forged = attacker.SignDownload(Resource, Learner, expires);

        Assert.False(signer.VerifyDownload(Resource, Learner, expires, forged));
    }

    // -------------------------------------------------------------- consents

    [Fact]
    public void A_consent_signature_is_reproducible_from_the_stored_facts()
    {
        var signedAt = clock.Now;

        var first = signer.SignConsent(Form, Learner, Parent, ConsentDecision.Granted, signedAt, "102.65.1.4");
        var second = signer.SignConsent(Form, Learner, Parent, ConsentDecision.Granted, signedAt, "102.65.1.4");

        // Determinism is the point: the signature can be recomputed from the row
        // years later and checked. A random component would make the record
        // unverifiable, which would defeat its only purpose.
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Every fact must be covered, because each one is a way of altering what
    /// the record says.
    /// </summary>
    /// <remarks>
    /// Changing the decision turns a refusal into a permission. Changing the
    /// learner moves a permission to another child. Changing the parent
    /// attributes it to somebody who never gave it. Changing the time moves it
    /// either side of a deadline. All four have to break the signature.
    /// </remarks>
    [Fact]
    public void Altering_any_recorded_fact_changes_the_consent_signature()
    {
        var signedAt = clock.Now;
        var original = signer.SignConsent(Form, Learner, Parent, ConsentDecision.Granted, signedAt, "102.65.1.4");

        var variants = new[]
        {
            signer.SignConsent(Guid.CreateVersion7(), Learner, Parent, ConsentDecision.Granted, signedAt, "102.65.1.4"),
            signer.SignConsent(Form, Guid.CreateVersion7(), Parent, ConsentDecision.Granted, signedAt, "102.65.1.4"),
            signer.SignConsent(Form, Learner, Guid.CreateVersion7(), ConsentDecision.Granted, signedAt, "102.65.1.4"),
            signer.SignConsent(Form, Learner, Parent, ConsentDecision.Declined, signedAt, "102.65.1.4"),
            signer.SignConsent(Form, Learner, Parent, ConsentDecision.Withdrawn, signedAt, "102.65.1.4"),
            signer.SignConsent(Form, Learner, Parent, ConsentDecision.Granted, signedAt.AddSeconds(1), "102.65.1.4"),
            signer.SignConsent(Form, Learner, Parent, ConsentDecision.Granted, signedAt, "102.65.1.5"),
        };

        Assert.All(variants, variant => Assert.NotEqual(original, variant));
    }

    /// <summary>
    /// A consent signature and a download signature over related values must not
    /// collide.
    /// </summary>
    /// <remarks>
    /// This is what the <c>consent.v1</c> and <c>download.v1</c> prefixes are
    /// for. Domain separation costs a dozen bytes and removes a whole class of
    /// bug where a signature minted for one purpose is accepted for another.
    /// </remarks>
    [Fact]
    public void The_two_signature_kinds_are_domain_separated()
    {
        var moment = clock.Now;

        var consent = signer.SignConsent(Form, Learner, Parent, ConsentDecision.Granted, moment, "102.65.1.4");
        var download = signer.SignDownload(Form, Learner, moment);

        Assert.NotEqual(consent, download);
    }
}
