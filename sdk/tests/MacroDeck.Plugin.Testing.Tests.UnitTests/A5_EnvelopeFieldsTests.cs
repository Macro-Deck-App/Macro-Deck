using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A5 - <see cref="CapabilityInvokeOptions" /> travels on the envelope, never duplicated inside the payload.
/// </summary>
[TestFixture]
public class A5_EnvelopeFieldsTests
{
	[Test]
	public async Task Deadline_and_idempotency_key_live_on_the_envelope_not_the_payload()
	{
		var integration = new TestIntegration("test.a5")
			.WithAction(new DelegateAction("quick", _ => Task.FromResult(ActionResult.Success())));

		await using var host = await MacroDeckTestHost.StartAsync();

		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => integration);

		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		var outcome = await session.Actions.ExecuteAsync("quick",
			options: new CapabilityInvokeOptions { Timeout = TimeSpan.FromSeconds(5), IdempotencyKey = "key-a5" });

		var invokeEnvelope = host.Messages.All
			.Single(message => message.Direction == ProtocolMessageDirection.ToPlugin &&
				message.Envelope.Id == outcome.CorrelationId)
			.Envelope;

		Assert.Multiple(() =>
		{
			Assert.That(invokeEnvelope.DeadlineMs, Is.Not.Null);
			Assert.That(invokeEnvelope.IdempotencyKey, Is.EqualTo("key-a5"));

			var payload = invokeEnvelope.Payload!.Value;
			Assert.That(payload.TryGetProperty("deadlineMs", out _), Is.False);
			Assert.That(payload.TryGetProperty("deadline", out _), Is.False);
			Assert.That(payload.TryGetProperty("idempotencyKey", out _), Is.False);
			Assert.That(payload.TryGetProperty("correlationId", out _), Is.False);
		});
	}

	[Test]
	public void An_idempotency_key_longer_than_the_limit_is_rejected_by_argument_validation()
	{
		var integration = new TestIntegration("test.a5b")
			.WithAction(new DelegateAction("quick", _ => Task.FromResult(ActionResult.Success())));

		var harness = PluginTestHarness.Create(builder => builder
			.RegisterIntegration(_ => integration));

		try
		{
			var overlongKey = new string('k', ProtocolLimits.MaxIdempotencyKeyLength + 1);

			Assert.ThrowsAsync<ArgumentException>(async () => await harness.Actions.ExecuteAsync("quick",
				options: new CapabilityInvokeOptions { IdempotencyKey = overlongKey }));
		}
		finally
		{
			harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
	}
}
