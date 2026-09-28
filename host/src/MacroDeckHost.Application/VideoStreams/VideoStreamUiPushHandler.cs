using MacroDeck.Localization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.VideoStreams;
using Mediator;

namespace MacroDeckHost.Application.VideoStreams;

public sealed class VideoStreamUiPushHandler :
	INotificationHandler<VideoStreamCatalogChangedNotification>,
	INotificationHandler<VideoStreamSessionChangedNotification>,
	INotificationHandler<VideoStreamSignalNotification>,
	INotificationHandler<VideoStreamSessionClosedNotification>
{
	private readonly IUiTransport _transport;

	public VideoStreamUiPushHandler(IUiTransport transport) => _transport = transport;

	public async ValueTask Handle(VideoStreamCatalogChangedNotification notification,
		CancellationToken cancellationToken)
		=> await _transport.Send(new VideoStreamCatalogChangedEvent(), cancellationToken);

	public async ValueTask Handle(VideoStreamSessionChangedNotification notification,
		CancellationToken cancellationToken)
		=> await _transport.SendToConnection(notification.ConnectionId,
			new VideoStreamSessionChangedEvent
			{
				SessionId = notification.SessionId,
				Revision = notification.Revision,
				State = VideoStreamUiMapping.WireName(notification.State),
				Description = VideoStreamUiMapping.ToMessage(notification.Description),
				Reason = VideoStreamUiMapping.WireName(notification.Reason),
				Message = notification.Message
			},
			cancellationToken);

	public async ValueTask Handle(VideoStreamSignalNotification notification, CancellationToken cancellationToken)
		=> await _transport.SendToConnection(notification.ConnectionId,
			new VideoStreamSignalEvent
			{
				SessionId = notification.SessionId, Signal = VideoStreamUiMapping.ToMessage(notification.Signal)
			},
			cancellationToken);

	public async ValueTask Handle(VideoStreamSessionClosedNotification notification,
		CancellationToken cancellationToken)
		=> await _transport.SendToConnection(notification.ConnectionId,
			new VideoStreamSessionClosedEvent
			{
				SessionId = notification.SessionId,
				Reason = VideoStreamUiMapping.WireName(notification.Reason),
				Error = notification.Error is { } error ? VideoStreamUiMapping.WireName(error) : null,
				Message = notification.Message ??
					(notification.Error is { } failure ? VideoStreamUiMapping.ErrorText(failure) : (LocalizedText?)null)
			},
			cancellationToken);
}
