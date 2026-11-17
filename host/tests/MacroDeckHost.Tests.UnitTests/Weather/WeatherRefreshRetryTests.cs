using MacroDeckHost.Integrations.Weather;
using MacroDeckHost.Integrations.Weather.Providers;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherRefreshRetryTests
{
	private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

	[Test]
	public async Task Weather_that_could_not_be_fetched_while_offline_appears_shortly_after_the_network_returns()
	{
		var context = new WeatherIntegrationTests.FakeContext();
		var entryId = context.ConfigStore.AddEntry("Berlin", "52.52", "13.41").ToString();
		var client = new FakeOpenMeteoClient { ForecastException = new HttpRequestException("offline") };
		var time = new FakeTimeProvider();

		using var integration = new WeatherIntegration(client, time: time);
		await integration.InitializeAsync(context);

		var retryIn = await NextFetchIn(time);
		client.ForecastException = null;
		client.Forecast = WeatherIntegrationTests.ForecastWith(18);
		time.Advance(retryIn);
		var station = (WeatherStation)integration.GetStation(entryId)!;
		var available = await Eventually(() => station.Current.IsAvailable);
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(retryIn, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(1)));
			Assert.That(available, Is.True);
		});
	}

	[Test]
	public async Task Retries_while_the_network_stays_down_slow_down_but_never_wait_longer_than_five_minutes()
	{
		var context = new WeatherIntegrationTests.FakeContext();
		context.ConfigStore.AddEntry("Berlin", "52.52", "13.41");
		var client = new FakeOpenMeteoClient { ForecastException = new HttpRequestException("offline") };
		var time = new FakeTimeProvider();

		using var integration = new WeatherIntegration(client, time: time);
		await integration.InitializeAsync(context);

		var delays = new List<TimeSpan>();
		for (var attempt = 0; attempt < 8; attempt++)
		{
			var delay = await NextFetchIn(time);
			delays.Add(delay);
			time.Advance(delay);
		}

		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(delays, Is.All.LessThanOrEqualTo(TimeSpan.FromMinutes(5)));
			Assert.That(delays, Is.Ordered.Ascending);
			Assert.That(delays[^1], Is.GreaterThan(delays[0]));
			Assert.That(client.ForecastRequests, Is.GreaterThanOrEqualTo(8));
		});
	}

	[Test]
	public async Task Weather_goes_back_to_the_regular_fifteen_minute_refresh_once_a_retry_succeeds()
	{
		var context = new WeatherIntegrationTests.FakeContext();
		context.ConfigStore.AddEntry("Berlin", "52.52", "13.41");
		var client = new FakeOpenMeteoClient { ForecastException = new HttpRequestException("offline") };
		var time = new FakeTimeProvider();

		using var integration = new WeatherIntegration(client, time: time);
		await integration.InitializeAsync(context);

		time.Advance(await NextFetchIn(time));
		time.Advance(await NextFetchIn(time));
		client.ForecastException = null;
		client.Forecast = WeatherIntegrationTests.ForecastWith(18);
		time.Advance(await NextFetchIn(time));
		await Eventually(() => client.ForecastRequests == 3);
		var afterSuccess = await NextFetchIn(time, TimeSpan.FromMinutes(5));

		await integration.ShutdownAsync();

		Assert.That(afterSuccess, Is.EqualTo(TimeSpan.FromMinutes(15)));
	}

	[Test]
	public async Task A_forecast_answer_without_current_conditions_is_retried_like_a_failed_request()
	{
		var context = new WeatherIntegrationTests.FakeContext();
		context.ConfigStore.AddEntry("Berlin", "52.52", "13.41");
		var client = new FakeOpenMeteoClient { Forecast = null };
		var time = new FakeTimeProvider();

		using var integration = new WeatherIntegration(client, time: time);
		await integration.InitializeAsync(context);

		var retryIn = await NextFetchIn(time);
		await integration.ShutdownAsync();

		Assert.That(retryIn, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(1)));
	}

	[Test]
	public async Task A_forecast_request_that_times_out_is_retried_instead_of_stopping_the_refresh()
	{
		var context = new WeatherIntegrationTests.FakeContext();
		context.ConfigStore.AddEntry("Berlin", "52.52", "13.41");
		var client = new FakeOpenMeteoClient
		{
			ForecastException = new TaskCanceledException("timeout", new TimeoutException())
		};
		var time = new FakeTimeProvider();

		using var integration = new WeatherIntegration(client, time: time);
		await integration.InitializeAsync(context);

		var retryIn = await NextFetchIn(time);
		await integration.ShutdownAsync();

		Assert.That(retryIn, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(1)));
	}

	private static async Task<TimeSpan> NextFetchIn(FakeTimeProvider time, TimeSpan? longerThan = null)
	{
		var deadline = DateTime.UtcNow + _wait;
		while (DateTime.UtcNow < deadline)
		{
			var due = time.ActiveDueTimes;
			if (due.Count == 1 && due[0] - time.Now > (longerThan ?? TimeSpan.Zero))
			{
				return due[0] - time.Now;
			}

			await Task.Delay(10);
		}

		throw new TimeoutException("The weather refresh loop never scheduled its next fetch.");
	}

	private static async Task<bool> Eventually(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + _wait;
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return true;
			}

			await Task.Delay(10);
		}

		return false;
	}
}
