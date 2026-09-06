using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FolderCreatedNotificationHandler : INotificationHandler<FolderCreatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public FolderCreatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(FolderCreatedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new FolderCreatedEvent { Folder = FolderDtoMapper.MapToDto(notification.Folder) };
		await _uiTransport.Send(evt, cancellationToken);
	}
}
