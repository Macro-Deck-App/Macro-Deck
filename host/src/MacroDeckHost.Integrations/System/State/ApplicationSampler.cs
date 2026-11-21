using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Focus;

namespace MacroDeckHost.Integrations.System.State;

internal sealed record WatchedApplication(Guid EntryId, string Title, string Key, string Application);

internal sealed record ApplicationSample(int Instances, int Windows, ApplicationWindowState? WindowState);

internal sealed record ApplicationSamples(
	IReadOnlyDictionary<Guid, ApplicationSample> Entries,
	ApplicationSample? Focused)
{
	public static ApplicationSamples Empty { get; } = new(new Dictionary<Guid, ApplicationSample>(), null);
}

internal sealed class ApplicationSampler : IDisposable
{
	private readonly IProcessSnapshotSource _processes;
	private readonly IApplicationWindowReader _windows;

	public ApplicationSampler(IProcessSnapshotSource processes, IApplicationWindowReader windows)
	{
		_processes = processes;
		_windows = windows;
	}

	public ApplicationWindowAvailability Availability => _windows.Availability;

	public ApplicationSamples Sample(IReadOnlyList<WatchedApplication> watched, FocusedAppInfo? focused)
	{
		if (watched.Count == 0 && focused is null)
		{
			return ApplicationSamples.Empty;
		}

		var snapshot = _processes.Capture();
		var entryProcesses = watched.ToDictionary(entry => entry.EntryId,
			entry => ApplicationMatch.FindProcessIds(snapshot, entry.Application));
		var focusedProcesses = focused is null
			? null
			: ApplicationMatch.FindSameApplication(snapshot,
				focused.ProcessId,
				focused.ExecutablePath,
				focused.ProcessName);

		var windowsAvailable = _windows.Availability == ApplicationWindowAvailability.Available;
		var allProcesses = entryProcesses.Values.SelectMany(ids => ids)
			.Concat(focusedProcesses ?? Enumerable.Empty<int>())
			.ToHashSet();
		var windows = windowsAvailable && allProcesses.Count > 0
			? _windows.Read(allProcesses)
			: new Dictionary<int, IReadOnlyList<ApplicationWindowState>>();

		var entries = entryProcesses.ToDictionary(entry => entry.Key,
			entry => Summarize(entry.Value, windows, windowsAvailable));
		var focusedSample = focusedProcesses is null ? null : Summarize(focusedProcesses, windows, windowsAvailable);
		return new ApplicationSamples(entries, focusedSample);
	}

	public void Dispose()
	{
		_windows.Dispose();
	}

	private static ApplicationSample Summarize(
		IReadOnlySet<int> processIds,
		IReadOnlyDictionary<int, IReadOnlyList<ApplicationWindowState>> windows,
		bool windowsAvailable)
	{
		var states = processIds.SelectMany(id => windows.TryGetValue(id, out var list) ? list : []).ToList();
		return new ApplicationSample(processIds.Count,
			states.Count,
			windowsAvailable ? ApplicationWindowStates.Aggregate(processIds.Count, states) : null);
	}
}
