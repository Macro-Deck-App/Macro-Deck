using System.Threading.Channels;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StoreRegistryRefreshBroadcastBackgroundService : BackgroundService
{
	private readonly IStoreRegistryRefreshTracker _tracker;
	private readonly IUiTransport _transport;
	private readonly Channel<StoreRegistryRefreshRun> _channel = Channel.CreateUnbounded<StoreRegistryRefreshRun>();

	public StoreRegistryRefreshBroadcastBackgroundService(IStoreRegistryRefreshTracker tracker, IUiTransport transport)
	{
		_tracker = tracker;
		_transport = transport;
		_tracker.Changed += OnChanged;
	}

	public override void Dispose()
	{
		_tracker.Changed -= OnChanged;
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var run in _channel.Reader.ReadAllAsync(stoppingToken))
			{
				await Broadcast(run, stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task Broadcast(StoreRegistryRefreshRun run, CancellationToken cancellationToken)
	{
		try
		{
			await _transport.SendToGroup(UiAdminGroups.Admin,
				new StoreRegistryRefreshChangedEvent { Run = StoreRegistryRefreshRunBodyFactory.Create(run)! },
				cancellationToken);

			if (run is { IsTerminal: true, Status: { } status })
			{
				await _transport.SendToGroup(UiAdminGroups.Admin,
					new StoreRegistryStatusChangedEvent { Registry = StoreRegistryStatusBodyFactory.Create(status) },
					cancellationToken);
			}
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
		}
	}

	private void OnChanged(StoreRegistryRefreshRun run) => _channel.Writer.TryWrite(run);
}
