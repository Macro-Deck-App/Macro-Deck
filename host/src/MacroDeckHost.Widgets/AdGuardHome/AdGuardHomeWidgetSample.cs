using MacroDeckHost.Application.AdGuardHome;

namespace MacroDeckHost.Widgets.AdGuardHome;

internal static class AdGuardHomeWidgetSample
{
	public static AdGuardHomeViewState Build(AdGuardHomeWidgetOptions options, DateTimeOffset now)
	{
		var snapshot = new AdGuardHomeSnapshot("sample", "AdGuard Home", "sample", AdGuardHomeConnection.Connected)
		{
			ProtectionEnabled = true,
			DnsRunning = true,
			Version = "v0.107.57",
			Statistics = new AdGuardHomeStatistics(48213, 7934, 12, 31, 4, 8.4),
		};

		return AdGuardHomeViewStateResolver.Resolve(options, snapshot, now);
	}
}
