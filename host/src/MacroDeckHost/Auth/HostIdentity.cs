using System.Net;
using System.Net.Sockets;
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
	public const string PolicyName = "host-identity";

	// An IPv6 host usually owns a whole /64 and can pick any address in it, so the /64 is one caller.
	public static string PartitionKey(IPAddress? address)
	{
		if (address is null)
		{
			return "unknown";
		}

		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}

		return address.AddressFamily == AddressFamily.InterNetworkV6
			? $"{Convert.ToHexString(address.GetAddressBytes(), 0, 8)}/64"
			: address.ToString();
	}

	public static RateLimitPartition<string> Partition(HttpContext context)
		=> RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context.Connection.RemoteIpAddress),
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = 30,
				Window = TimeSpan.FromSeconds(10),
				QueueLimit = 0
			});
}
