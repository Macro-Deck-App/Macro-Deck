using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Actions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A3 - a deadline produces <c>TIMEOUT</c> and the handler genuinely observed cancellation, rather than
/// the test host abandoning the call and synthesising a local timeout while the handler keeps running.
/// </summary>
[TestFixture]
public class A3_TimeoutTests
{
	[Test]
	public async Task A_short_deadline_times_out_and_the_handler_observes_cancellation()
	{
		var integration = new TestIntegration("test.a3");
		ILogger logger = null!;

		integration.WithAction(new DelegateAction("stall",
			async ctx =>
			{
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

		await using var harness = PluginTestHarness.Create(builder => builder
			.RegisterIntegration(_ => integration));

		logger = harness.Services.GetRequiredService<ILogger>()
			.ForContext(Constants.SourceContextPropertyName, "A3");

		var timeout = TimeSpan.FromMilliseconds(200);
		var outcome = await harness.Actions.ExecuteAsync("stall",
			options: new CapabilityInvokeOptions { Timeout = timeout });

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Succeeded, Is.False);
			Assert.That(outcome.Error!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
			Assert.That(outcome.Elapsed, Is.GreaterThanOrEqualTo(timeout));
		});

		// Proves the handler itself observed the cancellation - not that the test host merely gave up
		// waiting while the handler kept running unnoticed.
		await harness.Logs.WaitForAsync(logEvent => logEvent.Message.Contains("cancelled", StringComparison.Ordinal),
			TimeSpan.FromSeconds(5));
	}
}
