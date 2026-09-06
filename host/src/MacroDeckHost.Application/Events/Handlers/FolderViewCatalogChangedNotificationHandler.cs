using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.FolderViews;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FolderViewCatalogChangedNotificationHandler
	: INotificationHandler<FolderViewCatalogChangedNotification>
{
	private readonly IFolderViewRegistry _registry;
	private readonly IUiTransport _uiTransport;

	public FolderViewCatalogChangedNotificationHandler(IFolderViewRegistry registry, IUiTransport uiTransport)
	{
		_registry = registry;
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(
		FolderViewCatalogChangedNotification notification,
		CancellationToken cancellationToken)
	{
		var evt = new FolderViewCatalogChangedEvent
		{
			FolderViews = FolderViewDtoMapper.MapToDto(_registry.GetAll())
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
