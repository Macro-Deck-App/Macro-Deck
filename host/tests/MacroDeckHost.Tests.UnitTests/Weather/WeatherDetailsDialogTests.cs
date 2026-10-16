using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.Weather;
using MacroDeckHost.Integrations.Weather.Providers;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherDetailsDialogTests
{
	[TestCase(true)]
	[TestCase(false)]
	public async Task The_dialog_shows_the_weather_of_the_location_it_was_opened_for(bool qualified)
	{
		await using var harness = await Harness.StartAsync("Berlin", "Paris");
		var paris = harness.EntryIds[1];

		var requested = qualified ? $"{WeatherIntegration.IntegrationId}::{paris}" : paris;

		await using var session = await harness.OpenAsync(requested);

		Assert.That(Tree(session), Does.Contain("Paris").And.Not.Contain("Berlin"));
	}

	[Test]
	public async Task Without_a_location_the_dialog_shows_the_first_location()
	{
		await using var harness = await Harness.StartAsync("Berlin", "Paris");

		await using var session = await harness.OpenAsync(null);

		Assert.That(Tree(session), Does.Contain("Berlin"));
	}

	[Test]
	public async Task A_location_of_another_weather_provider_is_not_shown_as_one_of_its_own()
	{
		await using var harness = await Harness.StartAsync("Berlin");

		await using var session = await harness.OpenAsync("com.example.rooftop::roof");

		Assert.That(Tree(session), Does.Not.Contain("Berlin"));
	}

	[Test]
	public async Task A_dialog_opened_before_initialization_fills_in_its_location_and_icons_once_initialized()
	{
		await using var harness = Harness.Create("Berlin");
		await using var session = await harness.OpenAsync(null);
		var before = Tree(session);

		await harness.InitializeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(before, Does.Not.Contain("Berlin"));
			Assert.That(Tree(session), Does.Contain("Berlin"));
			Assert.That(Tree(session), Does.Contain($"{WeatherIntegration.IntegrationId}.icon.animated.clear-day"));
		});
	}

	[Test]
	public async Task The_dialog_is_served_for_its_own_view_only()
	{
		await using var harness = await Harness.StartAsync("Berlin");

		var session = await harness.Integration.CreateSessionAsync(Request("another-view", null),
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	private static string Tree(IUiSession session)
	{
		return UiCanonicalJson.Serialize(session.BuildTree());
	}

	private static UiSessionRequest Request(string viewId, string? instanceId)
	{
		var data = instanceId is null
			? new Dictionary<string, string>()
			: new Dictionary<string, string> { ["instanceId"] = instanceId };

		return new UiSessionRequest
		{
			UiModelVersion = 1,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Dialog,
				SessionMode = UiSessionModes.Exclusive,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiDialogSurfaceAttributes.ModalId] = JsonSerializer.SerializeToElement("modal"),
					[UiDialogSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(viewId),
					[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data)
				}
			}
		};
	}

	private sealed class Harness : IAsyncDisposable
	{
		private readonly WeatherIntegrationTests.FakeContext _context;

		private Harness(params string[] locations)
		{
			_context = new WeatherIntegrationTests.FakeContext
			{
				UiResources = new BuiltInUiResources(WeatherIntegration.IntegrationId,
					new UiResourceStore(),
					typeof(WeatherIntegration))
			};
			EntryIds =
			[
				.. locations.Select(location =>
					_context.ConfigStore.AddEntry(location, "52.52", "13.41").ToString())
			];
			Integration = new WeatherIntegration(new FakeOpenMeteoClient
			{
				Forecast = WeatherIntegrationTests.ForecastWith(21)
			});
		}

		public WeatherIntegration Integration { get; }

		public IReadOnlyList<string> EntryIds { get; }

		public static Harness Create(params string[] locations)
		{
			return new Harness(locations);
		}

		public static async Task<Harness> StartAsync(params string[] locations)
		{
			var harness = new Harness(locations);
			await harness.InitializeAsync();
			return harness;
		}

		public async Task InitializeAsync()
		{
			await Integration.InitializeAsync(_context);
			foreach (var entryId in EntryIds)
			{
				await ((WeatherStation)Integration.GetStation(entryId)!).RefreshAsync(CancellationToken.None);
			}

			await Integration.ShutdownAsync();
			await Integration.InitializeAsync(_context);
		}

		public async Task<IUiSession> OpenAsync(string? instanceId)
		{
			return (await Integration.CreateSessionAsync(Request("weather-details", instanceId),
				CancellationToken.None))!;
		}

		public async ValueTask DisposeAsync()
		{
			await Integration.ShutdownAsync();
			Integration.Dispose();
		}
	}
}
