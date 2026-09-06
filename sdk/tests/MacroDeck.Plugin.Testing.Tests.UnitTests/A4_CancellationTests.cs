using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Serilog;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A4 - cancelling an in-flight invocation yields exactly one cancelled reply; cancelling an
/// already-answered correlation is a documented no-op, not an error.
/// </summary>
[TestFixture]
public class A4_CancellationTests
{
	[Test]
	public async Task Cancelling_an_in_flight_invocation_produces_exactly_one_cancelled_reply()
	{
		var integration = new TestIntegration("test.a4");
		ILogger logger = null!;

		integration.WithAction(new DelegateAction("stall",
			async ctx =>
			{
				logger.Information("stall-started");

				try
				{
					await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
				}
				catch (OperationCanceledException)
				{
					logger.Information("cancelled");
					throw;
				}

				return ActionResult.Success();
			}));

		integration.WithAction(new DelegateAction("quick", _ => Task.FromResult(ActionResult.Success())));

		await using var host = await MacroDeckTestHost.StartAsync();

		// UseMacroDeckLogging is what actually ships an ILogger call to the host over log.publish -
		// without it, host.Logs (which only observes wire traffic) would never see "stall-started" or
		// "cancelled" no matter how long WaitForAsync waited, even though the plugin's own console
		// shows both lines. This test needs the real MacroDeckTestHost (not PluginTestHarness) because
		// it is exercising capability.cancel over the actual wire, so the fix is to make this plugin
		// ship its logs that way too, not to switch away from MacroDeckTestHost.
		var builder = MacroDeckPlugin.CreatePlugin()
			.UseMacroDeckLogging()
			.RegisterIntegration(_ => integration);

		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		logger = plugin.Application.Services.GetRequiredService<ILogger>()
			.ForContext(Constants.SourceContextPropertyName, "A4");

		var invokeTask = session.InvokeAsync(CapabilityKinds.Actions,
			"stall",
			CapabilityOperations.Actions.Execute,
			options: new CapabilityInvokeOptions { Timeout = TimeSpan.FromSeconds(30) });

		await host.Logs.WaitForAsync(logEvent => logEvent.Message.Contains("stall-started", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5));

		var invokeEnvelope = host.Messages.All.Last(message
			=> message.Direction == ProtocolMessageDirection.ToPlugin &&
			message.Envelope.Type == MessageTypes.CapabilityInvoke);

		await session.CancelAsync(invokeEnvelope.Envelope.Id);

		var outcome = await invokeTask;

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Succeeded, Is.False);
			Assert.That(outcome.Error!.Code, Is.EqualTo(ProtocolErrorCodes.Cancelled));
		});

		await host.Logs.WaitForAsync(logEvent => logEvent.Message.Contains("cancelled", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5));

		var repliesForThatCorrelation = CountReplies(host, invokeEnvelope.Envelope.Id);
		Assert.That(repliesForThatCorrelation, Is.EqualTo(1));
	}

	[Test]
	public async Task Cancelling_an_already_answered_correlation_is_a_silent_no_op()
	{
		var integration = new TestIntegration("test.a4b")
			.WithAction(new DelegateAction("quick", _ => Task.FromResult(ActionResult.Success())));

		await using var host = await MacroDeckTestHost.StartAsync();

		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => integration);

		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		var outcome = await session.Actions.ExecuteAsync("quick");
		var repliesBefore = CountReplies(host, outcome.CorrelationId);

		// Pins the baseline: without this, repliesAfter == repliesBefore would pass even if both were 0,
		// which proves nothing about the reply this invocation actually got.
		Assert.That(repliesBefore, Is.EqualTo(1));

		Assert.DoesNotThrowAsync(async () => await session.CancelAsync(outcome.CorrelationId));

		// No cancel reply is sent for an unknown/already-answered correlation - give a well-behaving
		// plugin a moment to (incorrectly) reply if it were going to, then confirm it did not.
		await Task.Delay(TimeSpan.FromMilliseconds(200));
		var repliesAfter = CountReplies(host, outcome.CorrelationId);

		Assert.Multiple(() =>
		{
			Assert.That(repliesAfter, Is.EqualTo(repliesBefore));
			Assert.That(outcome.Succeeded, Is.True);
		});
	}

	private static int CountReplies(MacroDeckTestHost host, string correlationId)
		=> host.Messages.All.Count(message
			=> message.Direction == ProtocolMessageDirection.FromPlugin &&
			message.Envelope.Type == MessageTypes.CapabilityResult &&
			string.Equals(message.Envelope.CorrelationId, correlationId, StringComparison.Ordinal));
}
