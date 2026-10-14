using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Weather;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Widgets.Preview;
using MacroDeckHost.Widgets.Weather;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// A widget session is long-lived and shared, so it outlives the state it was opened against. The
/// case that matters here is the one a reader meets on a fresh install: the deck is on screen before
/// the weather integration has been configured, so the card opens against no station at all and has to
/// find one when it appears - without being reopened, which nothing would do on its own.
/// </summary>
[TestFixture]
public class WeatherWidgetSessionRefreshTests
{
	[Test]
	public async Task ACardOpenedBeforeAnyStationExistedFindsTheOneThatAppears()
	{
		var registry = new GrowingRegistry();
		var notifier = new WeatherStateNotifier();
		var provider = new WeatherWidgetUiProvider(registry,
			new NullResourceStore(),
			notifier,
			new PassThroughSampleText(),
			new LoggerConfiguration().CreateLogger());

		await using var session = await provider.CreateSessionAsync(WidgetSurface(), CancellationToken.None);
		Assert.That(session, Is.Not.Null);

		Assert.That(TextOf(session!.BuildTree()),
			Does.Not.Contain("Sunnyside"),
			"nothing is configured yet, so there is no location to name");

		// The integration is set up: a station exists, and the broadcast that notices it goes out.
		registry.Add("station-1", "Sunnyside");
		notifier.Publish("station-1",
			WeatherStatePayload.From(await registry.GetStation("station-1")!
					.GetSnapshotAsync(CancellationToken.None),
				"station-1"));

		await WaitUntil(() => TextOf(session.BuildTree()).Contains("Sunnyside", StringComparison.Ordinal));

		Assert.That(TextOf(session.BuildTree()),
			Does.Contain("Sunnyside"),
			"the card never picked up the station that appeared after it opened");
	}

	[Test]
	public async Task ACardSavedWithFirstAvailableShowsTheFirstStation()
	{
		var registry = new GrowingRegistry();
		registry.Add("station-1", "Sunnyside");
		var provider = new WeatherWidgetUiProvider(registry,
			new NullResourceStore(),
			new WeatherStateNotifier(),
			new PassThroughSampleText(),
			new LoggerConfiguration().CreateLogger());

		await using var session = await provider.CreateSessionAsync(WidgetSurface("""{"instanceId":""}"""),
			CancellationToken.None);

		Assert.That(TextOf(session!.BuildTree()), Does.Contain("Sunnyside"));
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (DateTime.UtcNow < deadline && !condition())
		{
			await Task.Delay(20);
		}
	}

	private static string TextOf(UiTree tree)
	{
		return JsonSerializer.Serialize(tree);
	}

	private static UiSessionRequest WidgetSurface(string data = "{}")
	{
		return new UiSessionRequest
		{
			UiModelVersion = 1,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					// No instance stated: the widget takes whichever station the host has, which on a
					// fresh install is none.
					[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse(data).RootElement
				}
			}
		};
	}

	private sealed class GrowingRegistry : IWeatherRegistry
	{
		private readonly List<WeatherStationDescriptor> _instances = [];
		private readonly Dictionary<string, IWeatherStation> _stations = new(StringComparer.Ordinal);

		public void Add(string instanceId, string location)
		{
			_instances.Add(new WeatherStationDescriptor(instanceId, "app.weather", "Open-Meteo", location, false));
			_stations[instanceId] = new StaticStation(location);
		}

		public IReadOnlyList<WeatherStationDescriptor> GetInstances()
		{
			return _instances;
		}

		public IWeatherStation? GetStation(string instanceId)
		{
			return _stations.GetValueOrDefault(instanceId);
		}

		public IWeatherStation? DefaultStation
			=> _instances.Count > 0 ? GetStation(_instances[0].InstanceId) : null;
	}

	private sealed class StaticStation : IWeatherStation
	{
		private readonly string _location;

		public StaticStation(string location)
		{
			_location = location;
		}

		public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct)
		{
			return Task.FromResult(new WeatherSnapshot
			{
				IsAvailable = true, LocationName = _location, Temperature = 21, IsDay = true
			});
		}
	}

	private sealed class NullResourceStore : IUiResourceStore
	{
		public UiResource Register(UiResourceRegistration registration)
		{
			return new UiResource { ResourceId = $"{registration.OwnerId}.{registration.Name}", ContentHash = "hash" };
		}

		public bool TryGet(string resourceId, out UiResourceContent content)
		{
			content = null!;
			return false;
		}

		public bool Remove(string resourceId)
		{
			return false;
		}
	}

	private sealed class PassThroughSampleText : IWidgetSampleTextResolver
	{
		public ValueTask<string> ResolveAsync(LocalizedString value)
		{
			return ValueTask.FromResult(value.ToString() ?? string.Empty);
		}
	}
}
