using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Host;

/// <summary>
/// Regression coverage for the bound on waiting. A loader that never answers otherwise hangs the run forever,
/// and this repository already has a known hang of exactly that shape, so the settle budget is the host's
/// obligation rather than the caller's: the runtime owns no clock, and a poll loop would be the timing-brittle
/// version of the same thing. Traces to acceptance scenario 36 for issue #540.
/// </summary>
[TestFixture]
public class UiTestHostSettleTests
{
	[Test]
	public void SettleAsync_times_out_rather_than_hanging_when_a_load_never_completes()
	{
		// Never completed, and never cancelled either: the load is simply stuck, which is what a plugin talking to
		// an unresponsive service looks like.
		var never = new TaskCompletionSource<string>();
		var state = new UiAsyncState<string>(_ => never.Task, string.Empty);

		state.Reload();

		var host = UiTestHost.Render(new UiFlow
		{
			Key = "setup",
			Children =
			[
				new UiStringInput { Key = "loaded", Binding = Bind.ReadOnly(UiValue.From(() => state.Value)) },
			],
		});

		// The type is load-bearing: a bare TimeoutException is what the code under test might have thrown itself,
		// and an OperationCanceledException is what a test-runner token produces. Neither would tell a reader that
		// the view failed to settle.
		Assert.That(async () => await host.SettleAsync(TimeSpan.FromMilliseconds(50)),
			Throws.TypeOf<UiTestTimeoutException>().And.Message.Contains("50"));

		// And the default overload is bounded too, so a test that forgot to name a budget still terminates.
		Assert.That(async () => await host.SettleAsync(), Throws.TypeOf<UiTestTimeoutException>());

		Assert.That(state.IsLoading, Is.True, "the load is still stuck, which is why settling could not finish");
	}

	[Test]
	public void SettleAsync_reports_a_handler_that_faulted_while_it_was_settling()
	{
		// An asynchronous handler's fault has nowhere to surface but HandlerFaulted, so a settle that ignored it
		// would leave a test green with the work it was waiting for never having happened. A gate the test owns
		// decides when the handler faults, so the fault lands during the settle rather than whenever the scheduler
		// got round to it.
		var gate = new TaskCompletionSource();
		var host = UiTestHost.Render(new UiFlow
		{
			Key = "setup",
			Children =
			[
				new UiStep
				{
					Key = "credentials",
					Events =
					[
						UiEventHandler.OnAsync(UiConfigEvents.Submit,
							async _ =>
							{
								await gate.Task.ConfigureAwait(false);

								throw new InvalidOperationException("the remote step failed");
							}),
					],
				},
			],
		});

		Assert.That(host.ById("setup.credentials").Submit().IsAccepted,
			Is.True,
			"dispatch answers before the asynchronous work it started can finish");

		var settling = host.SettleAsync();

		gate.SetResult();

		Assert.That(async () => await settling,
			Throws.TypeOf<UiTestAssertionException>().And.Message.Contains("the remote step failed"));

		// The surface is still usable afterwards - one faulting handler is not a dead session.
		Assert.That(host.ById("setup").Id, Is.EqualTo("setup"));
	}
}
