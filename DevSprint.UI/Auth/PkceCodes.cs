using System.Security.Cryptography;
using System.Text;

namespace DevSprint.UI.Auth;

/// <summary>
/// PKCE (Proof Key for Code Exchange, RFC 7636) helper. Generates a random
/// <c>code_verifier</c> and the matching SHA-256 <c>code_challenge</c> used by
/// OAuth 2.0 Authorization Code flows on public clients (desktop, mobile, SPA).
/// </summary>
/// <remarks>
/// Atlassian's OAuth 2.0 (3LO) supports PKCE in addition to the client secret;
/// we use both. PKCE protects against authorization-code interception in case
/// the loopback redirect leaks (e.g., another local process sniffing the URL).
/// </remarks>
public static class PkceCodes
{
    /// <summary>Generates a fresh (verifier, challenge) pair for one sign-in attempt.</summary>
    public static (string Verifier, string Challenge) Generate()
    {
        // RFC 7636 §4.1: verifier is 43–128 chars from the unreserved URI set.
        // 32 random bytes encoded in base64url yields 43 chars — the minimum.
        var entropy = new byte[32];
        RandomNumberGenerator.Fill(entropy);
        var verifier = Base64UrlEncode(entropy);

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(challengeBytes);

        return (verifier, challenge);
    }

    /// <summary>Generates a short opaque token suitable for the OAuth <c>state</c> parameter.</summary>
    public static string GenerateState()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
