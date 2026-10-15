using System.Collections.Concurrent;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Variables;
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
using MacroDeckHost.Integrations.Weather.Protocol;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherSurfacesFollowAFetchTests
{
	private static readonly TimeSpan _wellInsideThePoll = TimeSpan.FromSeconds(10);

	[Test]
	[CancelAfter(30_000)]
	public async Task The_weather_widget_and_variables_follow_every_fetch_instead_of_their_own_poll()
	{
		var client = new FakeOpenMeteoClient { Forecast = Forecast(0) };
		var refresh = new VariableRefreshSignal();
		using var trigger = new WeatherBroadcastTrigger();
		var context = new Context(new IntegrationVariableApi(WeatherIntegration.IntegrationId, null!, refresh),
			new IntegrationWeatherProviderApi(trigger));
		context.ConfigStore.AddEntry("Berlin", "52.52", "13.41");

		using var integration = new WeatherIntegration(client, TimeSpan.FromSeconds(1));
		var notifier = new WeatherStateNotifier();
		var published = new ConcurrentQueue<string>();
		notifier.StateChanged += (_, args) => published.Enqueue(args.State.Condition);

		using var service = new WeatherStateBroadcastBackgroundService(new StartedHostLifetime(),
			new IntegrationRegistry(integration),
			trigger,
			new RecordingUiTransport(),
			notifier,
			new LoggerConfiguration().CreateLogger());
		await service.StartAsync(CancellationToken.None);

		try
		{
			await integration.InitializeAsync(context);
			await WaitUntilAsync(() => published.Contains("clear"));
			var variablesAskedAfterFirstFetch = refresh.DrainEagerRefreshRequested(WeatherIntegration.IntegrationId);

			client.Forecast = Forecast(61);
			await WaitUntilAsync(() => published.Contains("rain"));
			await WaitUntilAsync(() => refresh.DrainEagerRefreshRequested(WeatherIntegration.IntegrationId));

			Assert.That(variablesAskedAfterFirstFetch, Is.True, "the variables were asked to follow the first fetch");
		}
		finally
		{
			await integration.ShutdownAsync();
			await service.StopAsync(CancellationToken.None);
		}
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + _wellInsideThePoll;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail(
					$"Not observed within {_wellInsideThePoll.TotalSeconds} s, the widget would have waited for its poll.");
			}

			await Task.Delay(20);
		}
	}

	private static OpenMeteoForecastResponse Forecast(int weatherCode)
	{
		return new OpenMeteoForecastResponse
		{
			Current = new OpenMeteoCurrent
				{ Temperature = 12, ApparentTemperature = 11, WeatherCode = weatherCode, IsDay = 1 }
		};
	}

	private sealed class IntegrationRegistry(WeatherIntegration integration) : IWeatherRegistry
	{
		public IReadOnlyList<WeatherStationDescriptor> GetInstances()
		{
			return integration.GetInstances()
				.Select(instance => new WeatherStationDescriptor($"{WeatherIntegration.IntegrationId}::{instance.Id}",
					WeatherIntegration.IntegrationId,
					"Open-Meteo",
					instance.DisplayName,
					false))
				.ToList();
		}

		public IWeatherStation? GetStation(string instanceId)
		{
			return integration.GetStation(instanceId[(instanceId.IndexOf("::", StringComparison.Ordinal) + 2)..]);
		}

		public IWeatherStation? DefaultStation => null;
	}

	private sealed class Context(IVariableApi variables, IWeatherProviderApi weatherProvider) : IIntegrationContext
	{
		public WeatherIntegrationTests.FakeConfig ConfigStore { get; } = new();

		public IIntegrationConfig Config => ConfigStore;

		public IVariableApi Variables { get; } = variables;

		public IWeatherProviderApi WeatherProvider { get; } = weatherProvider;

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
