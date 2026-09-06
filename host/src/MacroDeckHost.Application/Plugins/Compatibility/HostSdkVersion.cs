using MacroDeck.Sdk.Deprecation;

namespace MacroDeckHost.Application.Plugins.Compatibility;

public static class HostSdkVersion
{
	public static Version Current { get; } =
		typeof(MacroDeckSdkUsageAttribute).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
}
