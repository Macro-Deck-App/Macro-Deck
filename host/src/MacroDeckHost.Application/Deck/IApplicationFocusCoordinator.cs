namespace MacroDeckHost.Application.Deck;

public interface IApplicationFocusCoordinator
{
	Task OnFocusChanged(FocusedApplication app, CancellationToken cancellationToken);

	Task OnFolderReported(Guid deviceId,
		Guid folderId,
		string? navigationToken,
		bool isResync,
		CancellationToken cancellationToken);

	Task OnDevicePresenceChanged(Guid deviceId, bool online, CancellationToken cancellationToken);

	Task OnRulesChanged(CancellationToken cancellationToken);
}
