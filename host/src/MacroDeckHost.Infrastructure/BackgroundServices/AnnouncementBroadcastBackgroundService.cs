using System.Threading.Channels;
using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Announcements;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class AnnouncementBroadcastBackgroundService : BackgroundService
{
	private static readonly TimeSpan _coalesceWindow = TimeSpan.FromMilliseconds(250);

	private readonly IAnnouncementService _announcements;
	private readonly IUiTransport _transport;

	private readonly Channel<byte> _signal = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
	{
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true
	});

	public AnnouncementBroadcastBackgroundService(IAnnouncementService announcements, IUiTransport transport)
	{
		_announcements = announcements;
		_transport = transport;

		_announcements.Changed += OnChanged;
	}

	public override void Dispose()
	{
		_announcements.Changed -= OnChanged;
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

		try
		{
			await _transport.SendToGroup(UiAdminGroups.Admin,
				new AnnouncementChangedEvent { Announcement = _announcements.Pending },
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
