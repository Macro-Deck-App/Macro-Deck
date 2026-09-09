using MacroDeckHost.Integrations.Weather;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherIntegrationTests
{
	[Test]
	public async Task The_widget_shows_the_configuration_name_rather_than_the_configured_location_value()
	{
		var context = new FakeContext();
		var entryId = context.ConfigStore.AddEntry("Office", latitude: "52.52", longitude: "13.41");
		context.ConfigStore.SetDisplayName(entryId, "52.52, 13.41");

		using var integration = new WeatherIntegration(new FakeOpenMeteoClient());
		await integration.InitializeAsync(context);

		var snapshot = await integration.GetStation(entryId.ToString())!.GetSnapshotAsync(CancellationToken.None);
		await integration.ShutdownAsync();

		Assert.That(snapshot.LocationName, Is.EqualTo("Office"));
	}

	[Test]
	public async Task A_configuration_with_no_title_of_its_own_falls_back_to_the_stored_location_value()
	{
		var context = new FakeContext();
		var entryId = context.ConfigStore.AddEntry("   ", latitude: "52.52", longitude: "13.41");
		context.ConfigStore.SetDisplayName(entryId, "52.52, 13.41");

		using var integration = new WeatherIntegration(new FakeOpenMeteoClient());
		await integration.InitializeAsync(context);

		var snapshot = await integration.GetStation(entryId.ToString())!.GetSnapshotAsync(CancellationToken.None);
		await integration.ShutdownAsync();

		Assert.That(snapshot.LocationName, Is.EqualTo("52.52, 13.41"));
	}

	[Test]
	public async Task Renaming_a_configuration_keeps_the_weather_it_already_had()
	{
		var context = new FakeContext();
		var entryId = context.ConfigStore.AddEntry("Berlin", latitude: "52.52", longitude: "13.41");
		var client = new FakeOpenMeteoClient { Forecast = ForecastWith(21) };

		using var integration = new WeatherIntegration(client);
		await integration.InitializeAsync(context);
		await ((WeatherStation)integration.GetStation(entryId.ToString())!).RefreshAsync(CancellationToken.None);

		context.ConfigStore.Rename(entryId, "Office");
		await integration.ShutdownAsync();
		await integration.InitializeAsync(context);

		var snapshot = await integration.GetStation(entryId.ToString())!.GetSnapshotAsync(CancellationToken.None);
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(snapshot.IsAvailable, Is.True);
			Assert.That(snapshot.Temperature, Is.EqualTo(21.0));
			Assert.That(snapshot.LocationName, Is.EqualTo("Office"));
		});
	}

	[Test]
	public async Task Moving_a_configuration_to_new_coordinates_does_not_keep_the_old_location_weather()
	{
		var context = new FakeContext();
		var entryId = context.ConfigStore.AddEntry("Berlin", latitude: "52.52", longitude: "13.41");
		var client = new FakeOpenMeteoClient { Forecast = ForecastWith(21) };

		using var integration = new WeatherIntegration(client);
		await integration.InitializeAsync(context);
		await ((WeatherStation)integration.GetStation(entryId.ToString())!).RefreshAsync(CancellationToken.None);

		context.ConfigStore.Move(entryId, latitude: "35.68", longitude: "139.69");
		client.Forecast = ForecastWith(30);
		await integration.ShutdownAsync();
		await integration.InitializeAsync(context);

		var snapshot = await integration.GetStation(entryId.ToString())!.GetSnapshotAsync(CancellationToken.None);
		await integration.ShutdownAsync();

		Assert.That(snapshot.Temperature, Is.Not.EqualTo(21.0));
	}

	private static OpenMeteoForecastResponse ForecastWith(double temperature)
		=> new()
		{
			Current = new OpenMeteoCurrent
			{
				Temperature = temperature,
				ApparentTemperature = temperature,
				WeatherCode = 0,
				IsDay = 1
			}
		};

	private sealed class FakeConfig : IIntegrationConfig
	{
		private readonly List<ConfigEntrySnapshot> _entries = [];
		private readonly Dictionary<(Guid, string), string?> _values = [];

		public Guid AddEntry(string title, string latitude, string longitude)
		{
			var id = Guid.NewGuid();
			_entries.Add(new ConfigEntrySnapshot(id, title));
			_values[(id, WeatherConfigKeys.Latitude)] = latitude;
			_values[(id, WeatherConfigKeys.Longitude)] = longitude;
			_values[(id, WeatherConfigKeys.Unit)] = "celsius";

			return id;
		}

		public void SetDisplayName(Guid entryId, string displayName)
			=> _values[(entryId, WeatherConfigKeys.DisplayName)] = displayName;

		public void Rename(Guid entryId, string title)
		{
			var index = _entries.FindIndex(entry => entry.Id == entryId);
			_entries[index] = new ConfigEntrySnapshot(entryId, title);
		}

		public void Move(Guid entryId, string latitude, string longitude)
		{
			_values[(entryId, WeatherConfigKeys.Latitude)] = latitude;
			_values[(entryId, WeatherConfigKeys.Longitude)] = longitude;
		}

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> Task.FromResult(_values.GetValueOrDefault((entryId, key)));

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task SetStringAsync(Guid entryId,
			string key,
			string? value,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public Task SetSecretAsync(Guid entryId,
			string key,
			string value,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}

	private sealed class FakeContext : IIntegrationContext
	{
		public FakeConfig ConfigStore { get; } = new();

		public IIntegrationConfig Config => ConfigStore;

		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
