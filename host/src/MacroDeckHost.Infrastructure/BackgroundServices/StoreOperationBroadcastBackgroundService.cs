using System.Threading.Channels;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

/// <summary>Forwards every <see cref="IStoreOperationTracker" /> change to the admin UI group. Queued
/// rather than sent inline from the tracker's event, since the tracker fires synchronously from whichever
/// thread mutated it and must not block on a realtime transport send. A completed operation also raises
/// <see cref="StoreCatalogChangedEvent" /> - the item's install state just changed, so the catalog listing
/// needs to reload too.</summary>
public sealed class StoreOperationBroadcastBackgroundService : BackgroundService
{
	private readonly IStoreOperationTracker _tracker;
	private readonly IUiTransport _transport;
	private readonly Channel<StoreOperation> _channel = Channel.CreateUnbounded<StoreOperation>();

	public StoreOperationBroadcastBackgroundService(IStoreOperationTracker tracker, IUiTransport transport)
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
			await foreach (var operation in _channel.Reader.ReadAllAsync(stoppingToken))
			{
				await Broadcast(operation, stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task Broadcast(StoreOperation operation, CancellationToken cancellationToken)
	{
		try
		{
			await _transport.SendToGroup(UiAdminGroups.Admin,
				new StoreOperationChangedEvent { Operation = StoreOperationBodyFactory.Create(operation) },
				cancellationToken);

			if (operation.State is StoreOperationState.Completed)
			{
				await _transport.SendToGroup(UiAdminGroups.Admin, new StoreCatalogChangedEvent(), cancellationToken);
			}
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
		}
	}

	private void OnChanged(StoreOperation operation) => _channel.Writer.TryWrite(operation);
}
