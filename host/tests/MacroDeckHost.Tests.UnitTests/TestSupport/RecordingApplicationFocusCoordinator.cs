using MacroDeckHost.Application.Deck;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class RecordingApplicationFocusCoordinator : IApplicationFocusCoordinator
{
	public List<FocusedApplication> FocusChanges { get; } = [];

	public List<(Guid DeviceId, Guid FolderId, string? NavigationToken, bool IsResync)> FolderReports { get; } = [];

	public List<(Guid DeviceId, bool Online)> PresenceChanges { get; } = [];

	public int RulesChangedCount { get; private set; }

	public Task OnFocusChanged(FocusedApplication app, CancellationToken cancellationToken)
	{
		FocusChanges.Add(app);
		return Task.CompletedTask;
	}

	public Task OnFolderReported(Guid deviceId,
		Guid folderId,
		string? navigationToken,
		bool isResync,
		CancellationToken cancellationToken)
	{
		FolderReports.Add((deviceId, folderId, navigationToken, isResync));
		return Task.CompletedTask;
	}

	public Task OnDevicePresenceChanged(Guid deviceId, bool online, CancellationToken cancellationToken)
	{
		PresenceChanges.Add((deviceId, online));
		return Task.CompletedTask;
	}

	public Task OnRulesChanged(CancellationToken cancellationToken)
	{
		RulesChangedCount++;
		return Task.CompletedTask;
	}
}
