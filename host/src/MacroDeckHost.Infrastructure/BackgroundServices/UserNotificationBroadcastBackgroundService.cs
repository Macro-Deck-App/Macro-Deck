using System.Threading.Channels;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class UserNotificationBroadcastBackgroundService : BackgroundService
{
	private static readonly TimeSpan _coalesceWindow = TimeSpan.FromMilliseconds(250);

	private readonly IUserNotificationStore _store;
	private readonly IUiTransport _transport;

	private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
	{
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true
	});

	public UserNotificationBroadcastBackgroundService(IUserNotificationStore store, IUiTransport transport)
	{
		_store = store;
		_transport = transport;

		_store.Changed += OnChanged;
	}

	public override void Dispose()
	{
		_store.Changed -= OnChanged;
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (await _signal.Reader.WaitToReadAsync(stoppingToken))
			{
				await Task.Delay(_coalesceWindow, stoppingToken);
				await Flush(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task Flush(CancellationToken ct)
	{
		while (_signal.Reader.TryRead(out _))
		{
		}

		var notifications = _store.Snapshot().ToList();

		try
		{
			await _transport.SendToGroup(UiAdminGroups.Admin,
				new UserNotificationsChangedEvent { Notifications = notifications },
				ct);
		}
		catch (Exception) when (!ct.IsCancellationRequested)
		{
		}
	}

	private void OnChanged()
	{
		_signal.Writer.TryWrite(1);
	}
}
