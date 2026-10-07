using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Mediator;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FolderCreatedNotificationHandler : INotificationHandler<FolderCreatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IColorReferenceResolver _colors;

	public FolderCreatedNotificationHandler(IUiTransport uiTransport, IColorReferenceResolver colors)
	{
		_colors = colors;
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(FolderCreatedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new FolderCreatedEvent { Folder = FolderDtoMapper.MapToDto(notification.Folder, _colors) };
		await _uiTransport.Send(evt, cancellationToken);
	}
}
