using System.Threading.RateLimiting;
using MacroDeckHost.Auth;

namespace MacroDeckHost.Licensing;

public static class LegacyLicenseTransferRateLimit
{
	public const int PerAddressLimit = 5;
	public const int HostLimit = 20;
	public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
	private static readonly PathString TransferPath = "/api/legacy/md2-app/license-transfer";

	// Every accepted request can reach the Platform, and a LAN caller can switch addresses freely, so a
	// host-wide ceiling applies on top of the per-address one.
	public static PartitionedRateLimiter<HttpContext> Create()
		=> PartitionedRateLimiter.CreateChained(
			PartitionedRateLimiter.Create<HttpContext, string>(context =>
				Partition(context, HostIdentityRateLimit.AddressKey(context), PerAddressLimit)),
			PartitionedRateLimiter.Create<HttpContext, string>(context => Partition(context, "host", HostLimit)));

	private static RateLimitPartition<string> Partition(HttpContext context, string key, int permits)
		=> !context.Request.Path.StartsWithSegments(TransferPath)
			? RateLimitPartition.GetNoLimiter(string.Empty)
			: RateLimitPartition.GetFixedWindowLimiter(key,
				_ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = Window, QueueLimit = 0 });
}
