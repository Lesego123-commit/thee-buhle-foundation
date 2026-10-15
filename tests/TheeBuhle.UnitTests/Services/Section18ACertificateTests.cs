// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - xUnit.net (2024) xUnit.net documentation.
//    - Republic of South Africa (1962) Income Tax Act 58 of 1962, section 18A.
// =============================================================================

using Microsoft.Extensions.Logging.Abstractions;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Listeners;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.UnitTests.Fakes;

namespace TheeBuhle.UnitTests.Services;

/// <summary>
/// Tests for when a Section 18A certificate is, and is not, emailed.
/// </summary>
/// <remarks>
/// The case that matters is the unconfigured one. A freshly deployed site has
/// no registration numbers, and a "certificate" sent then would carry blank
/// PBO and NPO numbers. That is a document SARS rejects, and the donor finds
/// out at tax time.
/// </remarks>
public sealed class Section18ACertificateTests
{
    private readonly RecordingEmailSender email = new();

    private IssueSection18ACertificate Listener(string? pbo, string? npo) =>
        new(
            new FakeDonationRepository(),
            email,
            new Section18AOptions { PboNumber = pbo!, NpoNumber = npo! },
            new FakeClock(),
            NullLogger<IssueSection18ACertificate>.Instance);

    private static DonationCompleted Donation(decimal amount = 500m, bool wantsCertificate = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "donor@example.co.za", "A Donor", amount, DonationFrequency.Once, wantsCertificate);

    [Fact]
    public async Task No_certificate_is_sent_while_the_registration_numbers_are_missing()
    {
        await Listener(pbo: null, npo: null).HandleAsync(Donation(), CancellationToken.None);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task No_certificate_is_sent_with_only_one_of_the_two_numbers()
    {
        await Listener(pbo: "930012345", npo: "  ").HandleAsync(Donation(), CancellationToken.None);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_certificate_carrying_both_numbers_is_sent_once_they_are_configured()
    {
        await Listener(pbo: "930012345", npo: "123-456 NPO").HandleAsync(Donation(), CancellationToken.None);

        var message = Assert.Single(email.Sent);
        Assert.Contains("930012345", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("123-456 NPO", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_donor_who_did_not_ask_for_one_gets_none()
    {
        await Listener(pbo: "930012345", npo: "123-456 NPO")
            .HandleAsync(Donation(wantsCertificate: false), CancellationToken.None);

        Assert.Empty(email.Sent);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("930012345", "", false)]
    [InlineData("", "123-456 NPO", false)]
    [InlineData("930012345", "123-456 NPO", true)]
    public void Certificates_need_both_numbers(string? pbo, string? npo, bool expected)
    {
        var options = new Section18AOptions { PboNumber = pbo!, NpoNumber = npo! };

        Assert.Equal(expected, options.CanIssueCertificates);
    }

    [Theory]
    [InlineData("123-456 NPO", "123-456 NPO")]
    [InlineData("123-456", "NPO 123-456")]
    public void The_NPO_label_is_not_doubled(string configured, string shown)
    {
        var options = new Section18AOptions { PboNumber = "930012345", NpoNumber = configured };

        Assert.Equal(shown, options.NpoDisplay);
        Assert.Equal("PBO 930012345", options.PboDisplay);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool IsEnabled => true;

        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);

            return Task.FromResult(true);
        }
    }
}
