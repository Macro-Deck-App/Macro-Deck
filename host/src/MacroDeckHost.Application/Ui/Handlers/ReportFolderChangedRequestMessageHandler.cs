using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class ReportFolderChangedRequestMessageHandler
	: IUiTransportMessageHandler<ReportFolderChangedRequest, ReportFolderChangedResponse>
{
	private readonly IFolderCache _folderCache;
	private readonly IEventBus _bus;
	private readonly IApplicationFocusCoordinator _coordinator;
	private readonly IHostLockState _lockState;

	public ReportFolderChangedRequestMessageHandler(
		IFolderCache folderCache,
		IEventBus bus,
		IApplicationFocusCoordinator coordinator,
		IHostLockState lockState)
	{
		_folderCache = folderCache;
		_bus = bus;
		_coordinator = coordinator;
		_lockState = lockState;
	}

	public async ValueTask<ReportFolderChangedResponse> Handle(
		ReportFolderChangedRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId))
		{
			return new ReportFolderChangedResponse { Success = true };
		}

		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return new ReportFolderChangedResponse { Success = true };
		}

		if (_lockState.IsLocked)
		{
			return new ReportFolderChangedResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
			};
		}

		// A resync (the coordinator driving a device back to a folder itself) must not re-fire
		// "Folder Changed" as if the user had navigated there.
		if (!request.IsResync)
		{
			_bus.Publish(new EventOccurrence(EventIds.Qualify(EventIds.FolderChanged),
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["folderId"] = folder.Id.ToString(),
					["folderName"] = folder.Name,
					["profileId"] = folder.ProfileId.ToString(),
					["deviceId"] = request.DeviceId?.ToString(),
					["clientId"] = request.ClientId ?? string.Empty
				}));
		}

		if (request.DeviceId is { } deviceId)
		{
			await _coordinator.OnFolderReported(deviceId,
				folder.Id,
				request.NavigationToken,
				request.IsResync,
				cancellationToken);
		}

		return new ReportFolderChangedResponse { Success = true };
	}
}
