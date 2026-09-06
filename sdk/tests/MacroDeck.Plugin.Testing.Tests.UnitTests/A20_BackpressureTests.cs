using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A20 - pausing the host's drain withholds exactly the non-exempt traffic; <c>capability.result</c> is
/// exempt, so an in-flight invocation still completes.
/// </summary>
[TestFixture]
public class A20_BackpressureTests
{
	[Test]
	public async Task Pausing_the_drain_withholds_exactly_the_non_exempt_traffic()
	{
		var integration = new TestIntegration("test.a20")
			.WithEvent(new EventDefinition { Id = "pinged", Name = "Pinged" });

		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ =>
			{
				integration.WithAction(new DelegateAction("ping",
					_ =>
					{
						integration.Context!.Events.Publish("pinged");
						return Task.FromResult(ActionResult.Success());
					}));

				return integration;
			});

		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		await host.PauseDrainingAsync();

		// Everything from here is "during the pause"; PauseDrainingAsync's own flow.pause envelope is
		// already recorded by the time it returns (PluginConnection.SendAsync records before completing).
		var pauseMarker = host.Messages.All.Count;

		var outcome = await session.Actions.ExecuteAsync("ping",
			options: new CapabilityInvokeOptions { Timeout = TimeSpan.FromSeconds(10) });

		Assert.That(outcome.Succeeded, Is.True, "capability.result is exempt and must complete even while paused");

		// Give any (incorrectly) non-exempt traffic a moment to arrive, if it were going to.
		await Task.Delay(TimeSpan.FromMilliseconds(300));
		Assert.That(host.Events.Published, Is.Empty, "event.publish is not exempt and must stay withheld while paused");

		// The real traffic recorded while paused, not a restatement of which types the rule already says
		// are exempt. Restricted to what the plugin itself sent: flow.pause/flow.resume only throttle the
		// plugin's own outbound queue (see PluginSessionConnection.SendLoopAsync) - a host-sent message
		// such as this test's own capability.invoke is never withheld and would otherwise wrongly land in
		// the "non-exempt" partition below.
		var duringPause = host.Messages.All
			.Skip(pauseMarker)
			.Where(message => message.Direction == ProtocolMessageDirection.FromPlugin)
			.ToList();

		await host.ResumeDrainingAsync();
		await host.Events.WaitForAsync("pinged", TimeSpan.FromSeconds(5));

		Assert.That(host.Events.Published, Has.Count.EqualTo(1));

		var exempt = duringPause.Where(message => ProtocolBackpressure.IsExemptWhilePaused(message.Envelope.Type))
			.ToList();
		var nonExempt = duringPause.Where(message => !ProtocolBackpressure.IsExemptWhilePaused(message.Envelope.Type))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(exempt,
				Is.Not.Empty,
				"no exempt traffic from the plugin (e.g. capability.result) was observed while paused");
			Assert.That(nonExempt, Is.Empty, "the plugin sent non-exempt traffic while paused");
		});
	}
}
