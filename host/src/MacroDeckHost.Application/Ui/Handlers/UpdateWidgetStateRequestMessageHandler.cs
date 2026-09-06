using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateWidgetStateRequestMessageHandler
	: IUiTransportMessageHandler<UpdateWidgetStateRequest, UpdateWidgetStateResponse>
{
	private readonly IFolderCache _folderCache;
	private readonly IUiTransport _uiTransport;

	public UpdateWidgetStateRequestMessageHandler(
		IFolderCache folderCache,
		IUiTransport uiTransport)
	{
		_folderCache = folderCache;
		_uiTransport = uiTransport;
	}

	public async ValueTask<UpdateWidgetStateResponse> Handle(
		UpdateWidgetStateRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.WidgetId, out var widgetId) || !Guid.TryParse(request.FolderId, out var folderId))
		{
			return new UpdateWidgetStateResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "VALIDATION_ERROR", Message = AppStrings.Errors.Widgets.InvalidWidgetOrFolderId() }
			};
		}

		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return new UpdateWidgetStateResponse
			{
				Success = false,
				Error = new TransportError { Code = "NOT_FOUND", Message = AppStrings.Errors.Folders.NotFound() }
			};
		}

		var notification = new WidgetStateChangedNotification
		{
			WidgetId = request.WidgetId,
			FolderId = request.FolderId,
			Data = request.Data
		};
		await _uiTransport.Send(notification, cancellationToken);

		return new UpdateWidgetStateResponse { Success = true };
	}
}
