using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Configuration;

public static class LoopbackSecret
{
	public const string EnvironmentVariable = "MACRODECK_LOOPBACK_SECRET";
	public const string HeaderName = "X-MacroDeck-Loopback-Secret";
	public const string SecretFileName = "loopback-secret";
	public const int SecretLength = 32;
	public const int NonceHexLength = 32;
	public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

	// Every MAC input starts with its own fixed label, so no attacker-chosen proof nonce can
	// reproduce the session cookie or a session code.
	private const string SessionLabel = "macro-deck-loopback-session";
	private const string CodeLabel = "macro-deck-loopback-code:";
	private const string ProofLabel = "macro-deck-loopback-proof:";

	private static byte[]? _secret;
	private static readonly Dictionary<string, DateTimeOffset> RedeemedCodes = new(StringComparer.Ordinal);
	private static readonly object RedeemedGate = new();

	public static void Set(string hex)
	{
		if (!TryParse(hex, out var bytes))
		{
			throw new ArgumentException("The loopback secret must be at least 32 bytes of hex.", nameof(hex));
		}

		var previous = Interlocked.CompareExchange(ref _secret, bytes, null);
		if (previous is not null && !CryptographicOperations.FixedTimeEquals(previous, bytes))
		{
			throw new InvalidOperationException("The loopback secret was already set.");
		}
	}

	public static bool TryParse(string? hex, out byte[] bytes)
	{
		bytes = [];
		if (string.IsNullOrWhiteSpace(hex) || hex.Length < SecretLength * 2 || hex.Length % 2 != 0)
		{
			return false;
		}

		try
		{
			bytes = Convert.FromHexString(hex.Trim());
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	public static string Generate() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretLength));

	public static bool MatchesHeader(string? value)
	{
		var secret = Volatile.Read(ref _secret);
		if (secret is null || string.IsNullOrEmpty(value) || !TryParse(value, out var presented))
		{
			return false;
		}

		return CryptographicOperations.FixedTimeEquals(secret, presented);
	}

	public static string? SessionCookieValue()
	{
		var secret = Volatile.Read(ref _secret);
		return secret is null ? null : Mac(secret, SessionLabel);
	}

	public static bool MatchesSessionCookie(string? value)
	{
		var expected = SessionCookieValue();
		return expected is not null && !string.IsNullOrEmpty(value) && FixedTimeEquals(expected, value);
	}

	public static string? Proof(string nonce)
	{
		var secret = Volatile.Read(ref _secret);
		return secret is null || !IsNonce(nonce) ? null : Mac(secret, ProofLabel + nonce);
	}

	public static bool TryRedeemCode(string? code, DateTimeOffset now)
	{
		var secret = Volatile.Read(ref _secret);
		var parts = code?.Split('.');
		if (secret is null || parts is not { Length: 3 } ||
			!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expirySeconds) ||
			!IsNonce(parts[1]))
		{
			return false;
		}

		var expiry = DateTimeOffset.FromUnixTimeSeconds(Math.Min(expirySeconds, DateTimeOffset.MaxValue.ToUnixTimeSeconds()));
		if (expiry <= now || expiry > now + CodeLifetime + TimeSpan.FromSeconds(5) ||
			!FixedTimeEquals(Mac(secret, $"{CodeLabel}{parts[0]}.{parts[1]}"), parts[2]))
		{
			return false;
		}

		lock (RedeemedGate)
		{
			foreach (var spent in RedeemedCodes.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
			{
				RedeemedCodes.Remove(spent);
			}

			return RedeemedCodes.TryAdd(parts[1], expiry);
		}
	}

	public static bool IsNonce(string? value)
		=> value is { Length: NonceHexLength } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

	private static string Mac(byte[] secret, string message)
		=> Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(message)));

	private static bool FixedTimeEquals(string expected, string presented)
		=> CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(presented));
}
