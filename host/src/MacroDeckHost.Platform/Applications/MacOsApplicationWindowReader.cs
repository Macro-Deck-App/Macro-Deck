using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Applications;

[SupportedOSPlatform("macos")]
internal sealed class MacOsApplicationWindowReader : IApplicationWindowReader
{
	public ApplicationWindowAvailability Availability
		=> MacOsAccessibility.IsTrusted()
			? ApplicationWindowAvailability.Available
			: ApplicationWindowAvailability.AccessibilityNotGranted;

	public IReadOnlyDictionary<int, IReadOnlyList<ApplicationWindowState>> Read(IReadOnlySet<int> processIds)
	{
		var result = new Dictionary<int, IReadOnlyList<ApplicationWindowState>>();
		if (processIds.Count == 0 || !MacOsAccessibility.IsTrusted())
		{
			return result;
		}

		foreach (var processId in processIds.Where(MacOsRunningApplication.CanOwnWindows))
		{
			var windows = MacOsAccessibilityWindows.ReadWindows(processId);
			if (windows.Count > 0)
			{
				result[processId] = windows;
			}
		}

		return result;
	}

	public void Dispose()
	{
	}
}
