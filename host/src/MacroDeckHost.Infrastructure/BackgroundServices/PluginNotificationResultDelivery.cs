using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class PluginNotificationResultDelivery : BackgroundService
{
	private const int MaxPendingPerPlugin = 100;

	private readonly INotificationManager _manager;
	private readonly IPluginSessionRegistry _sessions;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly TimeProvider _time;
	private readonly TimeSpan _retryInterval;
	private readonly TimeSpan _giveUpAfter = ProtocolTimeouts.SessionResumeWindow + TimeSpan.FromSeconds(30);
	private readonly ILogger _logger;
	private readonly object _lock = new();
	private readonly Dictionary<string, List<PendingResult>> _pending = new(StringComparer.Ordinal);

	private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
		{ FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

	public PluginNotificationResultDelivery(
		INotificationManager manager,
		IPluginSessionRegistry sessions,
		IPluginCapabilityInvoker invoker,
		TimeProvider time,
		ILogger logger,
		TimeSpan? retryInterval = null)
	{
		_retryInterval = retryInterval ?? TimeSpan.FromSeconds(2);
		_manager = manager;
		_sessions = sessions;
		_invoker = invoker;
		_time = time;
		_logger = logger.ForContext<PluginNotificationResultDelivery>();

		_manager.Published += OnPublished;
		_sessions.SessionAttached += OnAttached;
		_sessions.SessionEnded += OnEnded;
	}

	public override void Dispose()
	{
		_manager.Published -= OnPublished;
		_sessions.SessionAttached -= OnAttached;
		_sessions.SessionEnded -= OnEnded;
		base.Dispose();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				await WaitAsync(stoppingToken);
				await DeliverAllAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task WaitAsync(CancellationToken cancellationToken)
	{
		bool hasPending;
		lock (_lock)
		{
			hasPending = _pending.Count > 0;
		}

		if (!hasPending)
		{
			await _wake.Reader.WaitToReadAsync(cancellationToken);
		}
		else
		{
			var wait = _wake.Reader.WaitToReadAsync(cancellationToken).AsTask();
			await Task.WhenAny(wait, Task.Delay(_retryInterval, _time, cancellationToken));
			cancellationToken.ThrowIfCancellationRequested();
		}

		while (_wake.Reader.TryRead(out _))
		{
		}
	}

	private async Task DeliverAllAsync(CancellationToken cancellationToken)
	{
		string[] plugins;
		lock (_lock)
		{
			plugins = [.. _pending.Keys];
		}

		foreach (var pluginId in plugins)
		{
			await DeliverAsync(pluginId, cancellationToken);
		}
	}

	private async Task DeliverAsync(string pluginId, CancellationToken cancellationToken)
	{
		while (true)
		{
			PendingResult next;
			lock (_lock)
			{
				if (!_pending.TryGetValue(pluginId, out var queue) || queue.Count == 0)
				{
					_pending.Remove(pluginId);
					return;
				}

				next = queue[0];
			}

			var delivered = false;
			try
			{
				await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Ui,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Ui.NotificationResult,
						Arguments = JsonSerializer.SerializeToElement(next.Arguments, PluginProtocolJson.Options)
					},
					cancellationToken);
				delivered = true;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				var permanent = exception is RemoteCapabilityException { Retryable: false }
					|| _time.GetUtcNow() - next.Since > _giveUpAfter;

				if (permanent)
				{
					_logger.Warning(exception,
						"A notification result could not be delivered to plugin '{PluginId}'",
						pluginId);
					delivered = true;
				}
			}

			lock (_lock)
			{
				if (delivered && _pending.TryGetValue(pluginId, out var queue))
				{
					queue.Remove(next);
					if (queue.Count == 0)
					{
						_pending.Remove(pluginId);
					}
				}
			}

			if (!delivered)
			{
				return;
			}
		}
	}

	private void OnPublished(NotificationEvent notification)
	{
		if (notification.Kind != NotificationEventKind.Closed
			|| notification.Notification.Owner.Category != NotificationSourceCategory.Plugin
			|| notification.Result is not { } result
			|| result.CloseReason == NotificationCloseReason.OwnerGone)
		{
			return;
		}

		var pluginId = notification.Notification.Owner.Id;
		var arguments = new UiNotificationResultArguments
		{
			NotificationId = notification.Notification.Id,
			CloseReason = NotificationWireMapper.ReasonToWire(result.CloseReason),
			ActionId = result.ActionId,
			ClientId = result.ClientId,
			Values = result.Values.Count == 0 ? null : result.Values
		};

		lock (_lock)
		{
			if (!_pending.TryGetValue(pluginId, out var queue))
			{
				queue = [];
				_pending[pluginId] = queue;
			}

			queue.Add(new PendingResult(arguments, _time.GetUtcNow()));
			if (queue.Count > MaxPendingPerPlugin)
			{
				queue.RemoveAt(0);
			}
		}

		_wake.Writer.TryWrite(1);
	}

	private void OnAttached(object? sender, PluginSessionAttachedEventArgs e)
	{
		_wake.Writer.TryWrite(1);
	}

	private void OnEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		if (e.Reason != PluginSessionEndReason.Pruned)
		{
			return;
		}

		lock (_lock)
		{
			_pending.Remove(e.PluginId);
		}
	}

	private sealed class PendingResult(UiNotificationResultArguments arguments, DateTimeOffset since)
	{
		public UiNotificationResultArguments Arguments { get; } = arguments;

		public DateTimeOffset Since { get; } = since;
	}
}
