using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace MacroDeckHost.Auth;

// The wire contract the companion app verifies by hand; the app mirrors it in docs/integration/macro-deck-host.md.
public static class HostIdentityMessage
{
	public const string DomainLine = "macrodeck-host-identity/v1";
	public const int MinNonceBytes = 16;
	public const int MaxNonceBytes = 64;

	public static byte[] Build(string publicKeyBase64, string endpoint, string nonceBase64)
		=> Encoding.UTF8.GetBytes($"{DomainLine}\n{publicKeyBase64}\n{endpoint}\n{nonceBase64}");

	// Lower-case hex, IPv4-mapped addresses as IPv4, IPv6 bracketed without a zone id, then the port.
	public static string CanonicalAuthority(IPAddress address, int port)
	{
		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			return $"[{new IPAddress(address.GetAddressBytes())}]:{port}";
		}

		return $"{address}:{port}";
	}

	// What a person compares by eye: the first 12 bytes of SHA-256 over the raw point, as six groups of four.
	public static string Fingerprint(byte[] publicKey)
	{
		var hex = Convert.ToHexString(SHA256.HashData(publicKey), 0, 12);
		return string.Join(' ', Enumerable.Range(0, 6).Select(group => hex.Substring(group * 4, 4)));
	}

	// Canonical means standard alphabet, padded, and exactly what re-encoding the bytes produces.
	public static bool TryParseNonce(string? value, out byte[] nonce)
	{
		nonce = [];
		if (string.IsNullOrEmpty(value) || value.Length > 4 * ((MaxNonceBytes + 2) / 3))
		{
			return false;
		}

		var buffer = new byte[value.Length];
		if (!Convert.TryFromBase64String(value, buffer, out var written) ||
			written is < MinNonceBytes or > MaxNonceBytes ||
			Convert.ToBase64String(buffer, 0, written) != value)
		{
			return false;
		}

		nonce = buffer[..written];
		return true;
	}
}

public static class HostIdentityRateLimit
{
	public const int PerAddressLimit = 30;
	public const int PerNetworkLimit = 300;
	public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
	private static readonly PathString ChallengePath = "/api/auth/identity";

	// Per address, plus a ceiling per IPv6 /64 against a caller rotating through its addresses; either refuses.
	public static PartitionedRateLimiter<HttpContext> Create()
		=> PartitionedRateLimiter.CreateChained(
			PartitionedRateLimiter.Create<HttpContext, string>(context =>
				Partition(context, AddressKey(context.Connection.RemoteIpAddress), PerAddressLimit)),
			PartitionedRateLimiter.Create<HttpContext, string>(context =>
				Partition(context, NetworkKey(context.Connection.RemoteIpAddress), PerNetworkLimit)));

	public static string AddressKey(IPAddress? address)
	{
		if (address is null)
		{
			return "unknown";
		}

		if (address.IsIPv4MappedToIPv6)
		{
			return address.MapToIPv4().ToString();
		}

		return new IPAddress(address.GetAddressBytes()).ToString();
	}

	public static string? NetworkKey(IPAddress? address)
		=> address is { AddressFamily: AddressFamily.InterNetworkV6, IsIPv4MappedToIPv6: false }
			? $"{Convert.ToHexString(address.GetAddressBytes(), 0, 8)}/64"
			: null;

	private static RateLimitPartition<string> Partition(HttpContext context, string? key, int permits)
		=> key is null || !context.Request.Path.StartsWithSegments(ChallengePath)
			? RateLimitPartition.GetNoLimiter(string.Empty)
			: RateLimitPartition.GetFixedWindowLimiter(key,
				_ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = Window, QueueLimit = 0 });
}
