using System.Text.Json;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class WeatherStateBroadcastBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _interval = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan _emptyConfirmInterval = TimeSpan.FromSeconds(5);

	private readonly IWeatherRegistry _registry;
	private readonly IWeatherBroadcastTrigger _trigger;
	private readonly IUiTransport _transport;
	private readonly IWeatherStateNotifier _notifier;
	private readonly ILogger _logger;

	private readonly Dictionary<string, string> _lastByInstance = new(StringComparer.Ordinal);
	private string _lastInstancesJson = "[]";
	private bool _instancesEmptyPending;

	public WeatherStateBroadcastBackgroundService(
		IHostApplicationLifetime lifetime,
		IWeatherRegistry registry,
		IWeatherBroadcastTrigger trigger,
		IUiTransport transport,
		IWeatherStateNotifier notifier,
		ILogger logger)
		: base(lifetime)
	{
		_registry = registry;
		_trigger = trigger;
		_transport = transport;
		_notifier = notifier;
		_logger = logger.ForContext<WeatherStateBroadcastBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				var wait = _interval;
				try
				{
					wait = await Tick(stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Weather broadcast tick failed");
				}

				await _trigger.WaitAsync(wait, stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	internal async Task<TimeSpan> Tick(CancellationToken ct)
	{
		var instances = _registry.GetInstances();

		// An enumeration that momentarily drops to zero while an integration is (re)initializing must not
		// blank an already-populated widget to "No location". Hold last-known-good (both the station list
		// and the per-instance states) and recheck soon; a real change is confirmed on the follow-up tick.
		if (instances.Count == 0 && _lastInstancesJson != "[]" && !_instancesEmptyPending)
		{
			_instancesEmptyPending = true;
			return _emptyConfirmInterval;
		}

		// A held-back empty that turned out to be transient must be re-announced even though the list is
		// unchanged: the pull path (GetWeatherInstances) reads the registry raw, so every client that
		// pulled during the hold window received that empty enumeration, and the change-diff below would
		// never correct them (issue #132).
		var repairAfterHold = _instancesEmptyPending && instances.Count > 0;
		_instancesEmptyPending = false;

		await BroadcastInstances(instances, ct, repairAfterHold);

		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var instance in instances)
		{
			seen.Add(instance.InstanceId);
			var station = _registry.GetStation(instance.InstanceId);
			var payload = station is null
				? WeatherStatePayload.Unavailable(instance.InstanceId, instance.DisplayName)
				: WeatherStatePayload.From(await station.GetSnapshotAsync(ct), instance.InstanceId);

			await BroadcastIfChanged(instance.InstanceId, payload, ct);
		}

		foreach (var goneId in _lastByInstance.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			await BroadcastIfChanged(goneId, WeatherStatePayload.Unavailable(goneId), ct);
			_lastByInstance.Remove(goneId);
		}

		return _interval;
	}

	private async Task BroadcastInstances(
		IReadOnlyList<WeatherStationDescriptor> instances,
		CancellationToken ct,
		bool force = false)
	{
		var dtos = instances.Select(WeatherInstanceDto.From).ToList();
		var json = JsonSerializer.Serialize(dtos);
		if (json == _lastInstancesJson && !force)
		{
			return;
		}

		_lastInstancesJson = json;
		await _transport.Send(new WeatherInstancesChangedNotification { Instances = dtos }, ct);
	}

	private async Task BroadcastIfChanged(string instanceId, WeatherStatePayload payload, CancellationToken ct)
	{
		var json = JsonSerializer.Serialize(payload);
		if (_lastByInstance.TryGetValue(instanceId, out var previous) && previous == json)
		{
			return;
		}

		_lastByInstance[instanceId] = json;

		_notifier.Publish(instanceId, payload);
		await _transport.Send(new WeatherStateChangedNotification { State = payload }, ct);
	}
}
