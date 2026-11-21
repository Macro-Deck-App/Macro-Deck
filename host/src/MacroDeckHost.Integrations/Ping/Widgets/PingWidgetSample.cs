using MacroDeckHost.Integrations.Ping.State;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal static class PingWidgetSample
{
	public static PingViewState Build(PingWidgetOptions options)
	{
		var snapshot = new PingSnapshot(Guid.Empty, "NAS", "sample", "192.168.1.10")
		{
			Reachability = PingReachability.Online,
			LatencyMilliseconds = 4,
			PacketLossPercent = 0
		};

		return PingViewStateResolver.Resolve(options, snapshot, [3, 4, 3, 5, 4, 6, 4, 3, 7, 4, 5, 4]);
	}
}
