using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A7 - <see cref="PluginEventCollector" /> records only real <c>event.publish</c> messages, never the
/// <c>state.update</c> or <c>capability.result</c> traffic the same action also generates.
/// </summary>
[TestFixture]
public class A7_EventCollectionTests
{
	[Test]
	public async Task PluginEventCollector_records_only_real_event_publish_messages()
	{
		var integration = new TestIntegration("test.a7")
			.WithEvent(new EventDefinition { Id = "weather-refreshed", Name = "Weather refreshed" });

		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(provider =>
			{
				var notifier = provider.GetRequiredService<IPluginCatalogNotifier>();

				integration.WithAction(new DelegateAction("publish",
					_ =>
					{
						integration.Context!.Events.Publish("weather-refreshed",
							new Dictionary<string, object?> { ["temperatureCelsius"] = 21.5 });

						// Also produces state.update on the same wire, so Events isolating itself from that
						// traffic is actually exercised rather than trivially true.
						notifier.CatalogChanged(CapabilityKinds.Variables, reason: "test");

						return Task.FromResult(ActionResult.Success());
					}));

				return integration;
			});

		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		Assert.That(host.Events.Published, Is.Empty);

		Assert.ThrowsAsync<PluginTestTimeoutException>(async ()
			=> await host.Events.WaitForAsync("weather-refreshed", TimeSpan.FromMilliseconds(150)));

		var outcome = await session.Actions.ExecuteAsync("publish");
		Assert.That(outcome.Succeeded, Is.True);

		await host.Events.WaitForAsync("weather-refreshed", TimeSpan.FromSeconds(5));

		Assert.That(host.Events.Published, Has.Count.EqualTo(1));

		var published = host.Events.Published[0];

		Assert.Multiple(() =>
		{
			Assert.That(published.EventId, Is.EqualTo("weather-refreshed"));
			Assert.That(published.Parameters, Is.Not.Null);
			Assert.That(published.Parameters!.Value.GetProperty("temperatureCelsius").GetDouble(), Is.EqualTo(21.5));
		});
	}
}
