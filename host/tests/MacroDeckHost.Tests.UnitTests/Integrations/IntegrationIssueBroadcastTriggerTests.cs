using MacroDeckHost.Application.Integrations;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationIssueBroadcastTriggerTests
{
	[Test]
	public async Task WaitAsync_ReturnsTrue_WhenRefreshWasRequested()
	{
		var trigger = new IntegrationIssueBroadcastTrigger();

		trigger.RequestRefresh();

		Assert.That(await trigger.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
	}

	[Test]
	public async Task WaitAsync_ReturnsFalse_OnTimeout_WithoutRequest()
	{
		var trigger = new IntegrationIssueBroadcastTrigger();

		Assert.That(await trigger.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None), Is.False);
	}

	[Test]
	public async Task MultipleRequests_CoalesceIntoOneWakeup()
	{
		var trigger = new IntegrationIssueBroadcastTrigger();

		trigger.RequestRefresh();
		trigger.RequestRefresh();
		trigger.RequestRefresh();

		Assert.That(await trigger.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None), Is.True);
		Assert.That(await trigger.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None), Is.False);
	}
}
