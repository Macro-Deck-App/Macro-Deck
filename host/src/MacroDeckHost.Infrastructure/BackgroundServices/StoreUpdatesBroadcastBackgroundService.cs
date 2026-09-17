using System.Threading.Channels;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Store.Updates;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StoreUpdatesBroadcastBackgroundService : BackgroundService
{
	private readonly IStoreUpdateState _state;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger _logger;
	private readonly Channel<IReadOnlyList<StoreAvailableUpdate>> _channel =
		Channel.CreateUnbounded<IReadOnlyList<StoreAvailableUpdate>>();

	public StoreUpdatesBroadcastBackgroundService(IStoreUpdateState state,
		IServiceScopeFactory scopeFactory,
		ILogger logger)
	{
		_state = state;
		_scopeFactory = scopeFactory;
		_logger = logger.ForContext<StoreUpdatesBroadcastBackgroundService>();
		_state.Changed += OnChanged;
	}

	public override void Dispose()
	{
		_state.Changed -= OnChanged;
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var updates in _channel.Reader.ReadAllAsync(stoppingToken))
			{
				try
				{
					await using var scope = _scopeFactory.CreateAsyncScope();
					await scope.ServiceProvider.GetRequiredService<IMediator>()
						.Publish(new StoreUpdatesChangedNotification(updates), stoppingToken);
				}
				catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
				{
					_logger.Warning(ex, "Publishing the store update list failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void OnChanged() => _channel.Writer.TryWrite(_state.Current);
}
