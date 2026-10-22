using System.Runtime.Versioning;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Integrations.System.Providers;

[SupportedOSPlatform("macos")]
internal sealed class MacOsHostBatteryReader : IHostBatteryReader
{
	public async Task<IReadOnlyList<HostBatteryReading>?> ReadAsync(CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunWithResultAsync("pmset", ["-g", "batt"], cancellationToken);
		return result.Succeeded ? MacOsPmsetParser.Parse(result.StandardOutput) : null;
	}
}
