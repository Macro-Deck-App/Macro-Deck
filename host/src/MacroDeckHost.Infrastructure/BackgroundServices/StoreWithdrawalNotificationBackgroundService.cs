using System.Threading.Channels;
using MacroDeckHost.Application.Store.Updates;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class StoreWithdrawalNotificationBackgroundService : BackgroundService
{
	private readonly IStoreWithdrawalState _state;
	private readonly StoreWithdrawalNotifier _notifier;
	private readonly ILogger _logger;
	private readonly Channel<IReadOnlyList<StoreInstalledWithdrawal>> _channel =
		Channel.CreateUnbounded<IReadOnlyList<StoreInstalledWithdrawal>>();

	public StoreWithdrawalNotificationBackgroundService(IStoreWithdrawalState state,
		StoreWithdrawalNotifier notifier,
		ILogger logger)
	{
		_state = state;
		_notifier = notifier;
		_logger = logger.ForContext<StoreWithdrawalNotificationBackgroundService>();
		_state.Changed += OnChanged;
		OnChanged();
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
			await foreach (var withdrawals in _channel.Reader.ReadAllAsync(stoppingToken))
			{
				try
				{
					await _notifier.Notify(withdrawals, stoppingToken);
				}
				catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
				{
					_logger.Warning(ex, "Raising the store withdrawal warnings failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void OnChanged() => _channel.Writer.TryWrite(_state.Current);
}
