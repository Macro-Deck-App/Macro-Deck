using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FoldersReorderedNotificationHandler : INotificationHandler<FoldersReorderedNotification>
{
	private readonly IUiTransport _uiTransport;

	public FoldersReorderedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(FoldersReorderedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new FoldersReorderedEvent
		{
			ProfileId = notification.ProfileId.ToString(),
			Folders = notification.Folders.Select(FolderDtoMapper.MapToPlacement).ToList()
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
