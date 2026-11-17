using System.Threading.Channels;
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
			var batch = new Dictionary<Guid, bool>();
			Collect(reader, batch);

			await Task.Delay(_debounce, stoppingToken);
			Collect(reader, batch);

			foreach (var (widgetId, onlyIfChanged) in batch)
			{
				try
				{
					await Process(widgetId, onlyIfChanged, stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Failed to push label text for widget {WidgetId}", widgetId);
				}
			}
		}
	}

	private static void Collect(ChannelReader<LabelRender> reader, Dictionary<Guid, bool> batch)
	{
		while (reader.TryRead(out var render))
		{
			batch[render.WidgetId] = render.OnlyIfChanged && batch.GetValueOrDefault(render.WidgetId, true);
		}
	}

	private async Task Process(Guid widgetId, bool onlyIfChanged, CancellationToken cancellationToken)
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
			var texts = await labelText.ResolveTexts(widgetId, state, cancellationToken);
			if (onlyIfChanged && _subscriptions.WasPushed(id, state, texts))
			{
				continue;
			}

			_subscriptions.RecordPushed(id, state, texts);
			var evt = LabelTextUpdatedEvent.From(id, state, texts);

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
