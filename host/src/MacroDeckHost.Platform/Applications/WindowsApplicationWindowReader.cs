using System.Runtime.Versioning;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Platform.Applications;

[SupportedOSPlatform("windows")]
internal sealed class WindowsApplicationWindowReader : IApplicationWindowReader
{
	public ApplicationWindowAvailability Availability => ApplicationWindowAvailability.Available;

	public IReadOnlyDictionary<int, IReadOnlyList<ApplicationWindowState>> Read(IReadOnlySet<int> processIds)
	{
		var result = new Dictionary<int, List<ApplicationWindowState>>();
		if (processIds.Count == 0)
		{
			return new Dictionary<int, IReadOnlyList<ApplicationWindowState>>();
		}

		Win32ApplicationWindows.EnumerateTopLevel(window =>
		{
			_ = Win32Windows.GetWindowThreadProcessId(window, out var processId);
			if (!processIds.Contains((int)processId) || !Win32ApplicationWindows.IsUserWindow(window))
			{
				return;
			}

			if (!result.TryGetValue((int)processId, out var states))
			{
				states = [];
				result[(int)processId] = states;
			}

			states.Add(Win32ApplicationWindows.ReadState(window));
		});

		return result.ToDictionary(entry => entry.Key, IReadOnlyList<ApplicationWindowState> (entry) => entry.Value);
	}

	public void Dispose()
	{
	}
}
