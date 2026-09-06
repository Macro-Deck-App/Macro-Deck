using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Common.Security;

/// <summary>PKCE (RFC 7636) S256 challenge/verifier handling.</summary>
public static partial class PkceCodeChallenge
{
	// RFC 7636's unreserved character set, base64url without padding, over a 32-byte SHA-256 digest -
	// always exactly 43 characters.
	private const int ChallengeLength = 43;

	// RFC 7636 allows 43-128 characters of unreserved characters for the verifier itself; 32 random
	// bytes base64url-encoded (43 characters) is the minimum entropy the spec recommends.
	private const int VerifierRandomBytes = 32;

	public static string NewVerifier()
	{
		var bytes = RandomNumberGenerator.GetBytes(VerifierRandomBytes);
		return Base64Url(bytes);
	}

	public static bool IsWellFormed(string? codeChallenge)
		=> codeChallenge is { Length: ChallengeLength } && ChallengeCharactersRegex().IsMatch(codeChallenge);

	public static bool Verify(string codeVerifier, string codeChallenge)
	{
		var computed = Compute(codeVerifier);
		var computedBytes = Encoding.ASCII.GetBytes(computed);
		var storedBytes = Encoding.ASCII.GetBytes(codeChallenge);

		// The length check must happen before FixedTimeEquals: that primitive requires equal-length
		// inputs and throws otherwise, and a length mismatch is not itself secret, so short-circuiting
		// on it leaks nothing that a constant-time comparison was protecting.
		return computedBytes.Length == storedBytes.Length &&
			CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
	}

	public static string Compute(string codeVerifier)
	{
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
		return Base64Url(digest);
	}

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	[GeneratedRegex("^[A-Za-z0-9_-]+$")]
	private static partial Regex ChallengeCharactersRegex();
}
