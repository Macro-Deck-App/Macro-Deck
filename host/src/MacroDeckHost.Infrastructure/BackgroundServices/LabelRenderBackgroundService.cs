using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class LabelRenderBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(200);

	private readonly LabelRenderChannel _queue;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUiTransport _uiTransport;
	private readonly LabelSubscriptionTracker _subscriptions;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IDeviceSurfaceRenderSink? _deviceSurfaces;
	private readonly ILogger _logger;

	public LabelRenderBackgroundService(
		IHostApplicationLifetime lifetime,
		LabelRenderChannel queue,
		IServiceScopeFactory scopeFactory,
		IUiTransport uiTransport,
		LabelSubscriptionTracker subscriptions,
		IWidgetRenderSignals renderSignals,
		ILogger logger,
		IDeviceSurfaceRenderSink? deviceSurfaces = null)
		: base(lifetime)
	{
		_deviceSurfaces = deviceSurfaces;
		_queue = queue;
		_scopeFactory = scopeFactory;
		_uiTransport = uiTransport;
		_subscriptions = subscriptions;
		_renderSignals = renderSignals;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		var reader = _queue.Reader;
		while (await reader.WaitToReadAsync(stoppingToken))
		{
			var batch = new HashSet<Guid>();
			while (reader.TryRead(out var id))
			{
				batch.Add(id);
			}

			await Task.Delay(_debounce, stoppingToken);
			while (reader.TryRead(out var id))
			{
				batch.Add(id);
			}

			foreach (var widgetId in batch)
			{
				try
				{
					await Process(widgetId, stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Failed to push label text for widget {WidgetId}", widgetId);
				}
			}
		}
	}

	private async Task Process(Guid widgetId, CancellationToken cancellationToken)
	{
		var id = widgetId.ToString();

		if (!_subscriptions.HasAnySubscribers(id))
		{
			return;
		}

		using var scope = _scopeFactory.CreateScope();
		var labelText = scope.ServiceProvider.GetRequiredService<ILabelTextService>();

		foreach (var state in _subscriptions.SubscribedStates(id))
		{
			var text = await labelText.ResolveText(widgetId, state, cancellationToken);
			var evt = new LabelTextUpdatedEvent { WidgetId = id, State = state, Text = text };

			// Raised beside the client push, carrying the exact resolved text it computed, so an open
			// session for this widget's state can never disagree with what a legacy client is pushed.
			_renderSignals.RaiseLabelChanged(evt);

			await _uiTransport.SendToGroup(LabelGroups.For(id, state), evt, cancellationToken);

			if (_deviceSurfaces is not null)
			{
				await _deviceSurfaces.OnWidgetLabelChanged(widgetId, state, cancellationToken);
			}
		}
	}
}
