using MacroDeckHost.Application.Weather;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
public class WeatherBroadcastTriggerTests
{
	[Test]
	public async Task WaitAsync_ReturnsTrue_WhenRefreshWasRequested()
	{
		var trigger = new WeatherBroadcastTrigger();

		trigger.RequestRefresh();

		Assert.That(await trigger.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
	}

	[Test]
	public async Task WaitAsync_ReturnsFalse_OnTimeout_WithoutRequest()
	{
		var trigger = new WeatherBroadcastTrigger();

		Assert.That(await trigger.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None), Is.False);
	}

	[Test]
	public async Task MultipleRequests_CoalesceIntoOneWakeup()
	{
		var trigger = new WeatherBroadcastTrigger();

		trigger.RequestRefresh();
		trigger.RequestRefresh();
		trigger.RequestRefresh();

		Assert.That(await trigger.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
		Assert.That(await trigger.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None), Is.False);
	}
}
