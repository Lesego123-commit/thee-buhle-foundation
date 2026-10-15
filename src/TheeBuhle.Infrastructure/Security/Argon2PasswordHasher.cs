// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - OWASP (2024) Password Storage Cheat Sheet.
//    - Biryukov et al. (2021) RFC 9106: Argon2.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using TheeBuhle.Application.Abstractions;

namespace TheeBuhle.Infrastructure.Security;

/// <summary>
/// Password hashing with Argon2id.
/// </summary>
/// <remarks>
/// <para>
/// Argon2id rather than bcrypt or PBKDF2, and the reason is specific rather than
/// fashionable: bcrypt and PBKDF2 are CPU-hard but cheap in <em>memory</em>, so
/// a GPU with thousands of cores attacks them thousands of passwords at a time.
/// Argon2id is memory-hard — each guess needs the full 19 MiB below — which
/// collapses that parallelism, because a GPU has cores but not gigabytes of fast
/// memory per core.
/// </para>
/// <para>
/// The parameters are the OWASP minimum for Argon2id. They are deliberately not
/// tuned higher: this runs on an Azure App Service instance shared with the rest
/// of the API, and a hash that takes a second is a denial-of-service vector on
/// the login endpoint rather than extra security.
/// </para>
/// </remarks>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    /// <summary>19 MiB, the OWASP minimum for Argon2id (OWASP, 2024; RFC 9106).</summary>
    private const int MemoryKib = 19 * 1024;

    private const int Iterations = 2;

    private const int Parallelism = 1;

    private const int SaltBytes = 16;

    private const int HashBytes = 32;

    /// <summary>Identifies the scheme and its parameters, so they can change later.</summary>
    private const string Prefix = "argon2id";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt);

        // The parameters are stored with the hash rather than assumed from the
        // constants above. That is what makes raising the cost later possible:
        // an old hash still verifies with the parameters it was made under, and
        // is rewritten at the next successful sign-in.
        return string.Join('$',
            Prefix,
            MemoryKib.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Parallelism.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var parts = hash.Split('$');

        if (parts.Length != 6
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var memory)
            || !int.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || !int.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var parallelism))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[4]);
            expected = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            // A corrupted or hand-edited hash. False, not an exception: the
            // caller's job is to answer "was this the right password", and a
            // malformed stored hash means no password is right.
            return false;
        }

        var actual = Derive(password, salt, memory, iterations, parallelism, expected.Length);

        // Fixed-time comparison. A naive loop that returns on the first
        // differing byte leaks how much of the hash matched, one request at a
        // time.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Whether a stored hash was made with weaker parameters than the current
    /// configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the parameters-in-the-hash format is <em>for</em>. When
    /// OWASP raises its minimum — and it has, twice — every existing account is
    /// still on the old cost, and without this method the only options are to
    /// leave them there forever or to force a password reset on everybody.
    /// </para>
    /// <para>
    /// Instead <c>AuthService</c> asks this after a successful sign-in, when it
    /// has the plaintext in hand for the only moment it ever will, and rewrites
    /// the hash. Accounts upgrade themselves as people log in, and nobody is
    /// asked to do anything.
    /// </para>
    /// <para>
    /// An unrecognisable hash returns true. A hash this code cannot parse is one
    /// it cannot verify either, and treating it as needing replacement is the
    /// safe direction to be wrong in.
    /// </para>
    /// </remarks>
    public bool NeedsRehash(string hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return true;
        }

        var parts = hash.Split('$');

        if (parts.Length != 6
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var memory)
            || !int.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || !int.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var parallelism))
        {
            return true;
        }

        // Weaker on any axis, not all three. A hash with the right memory but
        // one pass is still weaker than the current configuration.
        return memory < MemoryKib || iterations < Iterations || parallelism < Parallelism;
    }

    /// <summary>
    /// Burns the same time as a real verification, against a hash that cannot
    /// match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by <c>AuthService</c> when the email does not exist. Without it,
    /// an unknown address returns in a millisecond and a known one takes the
    /// ~40 ms Argon2 needs — which turns the login endpoint into an account
    /// enumeration oracle no matter how carefully the error message is worded.
    /// </para>
    /// <para>
    /// The identical error message is the visible half of that defence; this is
    /// the half that matters, because a timing difference is measurable even
    /// when the response body is not.
    /// </para>
    /// </remarks>
    public void DummyVerify(string password)
    {
        // A real derivation over a throwaway salt. Anything cheaper — a sleep, a
        // fixed delay — is either measurably different or adds jitter that
        // averages out over a few hundred requests.
        _ = Derive(password ?? string.Empty, RandomNumberGenerator.GetBytes(SaltBytes));
    }

    private static byte[] Derive(
        string password,
        byte[] salt,
        int memoryKib = MemoryKib,
        int iterations = Iterations,
        int parallelism = Parallelism,
        int length = HashBytes)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon.GetBytes(length);
    }
}
