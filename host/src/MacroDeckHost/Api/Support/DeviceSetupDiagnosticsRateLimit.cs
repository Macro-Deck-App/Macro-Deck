using System.Threading.RateLimiting;
using MacroDeckHost.Auth;

namespace MacroDeckHost.Api.Support;

public static class DeviceSetupDiagnosticsRateLimit
{
	public const string Path = "/api/device-setup/diagnostics";
	public const int PerAddressLimit = 5;
	public const int HostLimit = 30;
	public static readonly TimeSpan Window = TimeSpan.FromHours(1);

	public static PartitionedRateLimiter<HttpContext> Create()
	{
		return PartitionedRateLimiter.CreateChained(PartitionedRateLimiter.Create<HttpContext, string>(context =>
				Partition(context, HostIdentityRateLimit.AddressKey(context), PerAddressLimit)),
			PartitionedRateLimiter.Create<HttpContext, string>(context => Partition(context, "host", HostLimit)));
	}

	private static RateLimitPartition<string> Partition(HttpContext context, string key, int permits)
	{
		return !context.Request.Path.StartsWithSegments(Path)
			? RateLimitPartition.GetNoLimiter(string.Empty)
			: RateLimitPartition.GetFixedWindowLimiter(key,
				_ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = Window, QueueLimit = 0 });
	}
}
