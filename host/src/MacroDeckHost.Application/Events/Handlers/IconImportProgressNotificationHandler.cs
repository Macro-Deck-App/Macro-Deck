using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconImportProgressNotificationHandler : INotificationHandler<IconImportProgressNotification>
{
	private readonly IUiTransport _uiTransport;

	public IconImportProgressNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(IconImportProgressNotification notification, CancellationToken cancellationToken)
	{
		var batch = notification.Batch;
		await _uiTransport.Send(new IconImportProgressEvent
			{
				BatchId = batch.Id.ToString(),
				PackId = batch.PackId.ToString(),
				State = batch.State.ToString(),
				SourceName = batch.SourceName,
				Total = notification.Total,
				Processed = notification.Processed,
				Failed = notification.Failed,
				Error = batch.Error
			},
			cancellationToken);
	}
}
