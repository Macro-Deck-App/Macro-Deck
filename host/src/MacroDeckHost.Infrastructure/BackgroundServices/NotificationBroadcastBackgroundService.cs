using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class NotificationBroadcastBackgroundService : BackgroundService
{
	private static readonly TimeSpan _updateWindow = TimeSpan.FromMilliseconds(250);

	private readonly INotificationManager _manager;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _time;

	private readonly Channel<NotificationEvent> _events
		= Channel.CreateUnbounded<NotificationEvent>(new UnboundedChannelOptions { SingleReader = true });

	private readonly Dictionary<Guid, NotificationEvent> _pendingUpdates = [];

	public NotificationBroadcastBackgroundService(
		INotificationManager manager,
		IUiTransport transport,
		TimeProvider time)
	{
		_manager = manager;
		_transport = transport;
		_time = time;
		_manager.Published += OnPublished;
	}

	public override void Dispose()
	{
		_manager.Published -= OnPublished;
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		Task<bool>? arrival = null;
		Task? window = null;

		try
		{
			while (true)
			{
				arrival ??= _events.Reader.WaitToReadAsync(stoppingToken).AsTask();

				if (_pendingUpdates.Count == 0)
				{
					window = null;
					if (!await arrival)
					{
						return;
					}

					arrival = null;
					await DrainAsync(stoppingToken);
					continue;
				}

				window ??= Task.Delay(_updateWindow, _time, stoppingToken);
				if (await Task.WhenAny(arrival, window) == arrival)
				{
					if (!await arrival)
					{
						return;
					}

					arrival = null;
					await DrainAsync(stoppingToken);
				}
				else
				{
					window = null;
					await FlushUpdatesAsync(stoppingToken);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void OnPublished(NotificationEvent notification)
	{
		_events.Writer.TryWrite(notification);
	}

	private async Task DrainAsync(CancellationToken cancellationToken)
	{
		while (_events.Reader.TryRead(out var notification))
		{
			if (notification.Notification.Popup == NotificationPopup.None)
			{
				continue;
			}

			switch (notification.Kind)
			{
				case NotificationEventKind.Updated:
					_pendingUpdates[notification.Notification.Id] = notification;
					break;

				case NotificationEventKind.Closed:
					_pendingUpdates.Remove(notification.Notification.Id);
					await SendAsync(notification.Notification.Target,
						new NotificationClosedEvent
						{
							Id = notification.Notification.Id,
							Revision = notification.Notification.Revision,
							Reason = ReasonToWire(notification.Result?.CloseReason),
							ActionId = notification.Result?.ActionId
						},
						cancellationToken);
					break;

				default:
					await SendAsync(notification.Notification.Target,
						new NotificationShownEvent
							{ Notification = ActiveNotificationDto.From(notification.Notification, _time.GetUtcNow()) },
						cancellationToken);
					break;
			}
		}
	}

	private async Task FlushUpdatesAsync(CancellationToken cancellationToken)
	{
		var updates = _pendingUpdates.Values.ToList();
		_pendingUpdates.Clear();

		foreach (var update in updates)
		{
			await SendAsync(update.Notification.Target,
				new NotificationUpdatedEvent
					{ Notification = ActiveNotificationDto.From(update.Notification, _time.GetUtcNow()) },
				cancellationToken);
		}
	}

	private async Task SendAsync<T>(NotificationTarget target, T message, CancellationToken cancellationToken)
		where T : class
	{
		try
		{
			switch (target.Kind)
			{
				case NotificationTargetKind.AdminUi:
					await _transport.SendToGroup(UiAdminGroups.Admin, message, cancellationToken);
					break;

				case NotificationTargetKind.Devices:
					foreach (var deviceId in target.DeviceIds)
					{
						await _transport.SendToGroup(UiDeviceGroups.For(deviceId), message, cancellationToken);
					}

					break;

				default:
					await _transport.Send(message, cancellationToken);
					break;
			}
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
		}
	}

	internal static string ReasonToWire(NotificationCloseReason? reason)
	{
		return reason switch
		{
			NotificationCloseReason.Action => NotificationsWire.CloseReason.Action,
			NotificationCloseReason.Timeout => NotificationsWire.CloseReason.Timeout,
			NotificationCloseReason.Programmatic => NotificationsWire.CloseReason.Programmatic,
			NotificationCloseReason.OwnerGone => NotificationsWire.CloseReason.OwnerGone,
			NotificationCloseReason.HostUnavailable => NotificationsWire.CloseReason.HostUnavailable,
			_ => NotificationsWire.CloseReason.Dismissed
		};
	}
}
