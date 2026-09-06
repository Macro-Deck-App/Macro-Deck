using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Reconnection;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A19 - <see cref="ManualTimeProvider" /> actually drives reconnect scheduling, and
/// <see cref="Wait.UntilAsync" /> never lies about what happened.
/// </summary>
[TestFixture]
public class A19_ManualClockTests
{
	[Test]
	public async Task ManualTimeProvider_drives_reconnect_scheduling_for_an_in_process_plugin()
	{
		// A registration that always fails, non-fatally, so the plugin keeps retrying with backoff
		// instead of giving up for good.
		await using var host = await MacroDeckTestHost.StartAsync(new MacroDeckTestHostOptions
		{
			Registration = PluginRegistrationPolicy.Reject(ProtocolErrorCodes.InternalError)
		});

		var clock = new ManualTimeProvider();

		var builder = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration("test.a19"))
			.ConfigureServices((_, services) => services.AddSingleton<TimeProvider>(clock));

		await using var plugin = await host.HostAsync(builder, PluginTestCredentials.SelfRegistering);

		var state = plugin.Application.Services.GetRequiredService<PluginConnectionState>();

		await Wait.UntilAsync(() => state.ReconnectAttempt > 0,
			TimeSpan.FromSeconds(10),
			because: "the plugin never reached its first reconnect attempt");

		var frozenAttempt = state.ReconnectAttempt;

		// Real time passing must not move the schedule - only the injected clock does. Derived from the
		// policy itself (full jitter's own upper bound for the *next* attempt), not a fixed guess: a flat
		// 1s wait only "usually" outlasts a near-maximum-jitter delay for the very first attempt, which
		// is exactly the flake this scenario calls out.
		var wellBeyondBackoff = ReconnectPolicy.DelayFor(frozenAttempt + 1, 1.0) + TimeSpan.FromMilliseconds(250);
		await Task.Delay(wellBeyondBackoff);
		Assert.That(state.ReconnectAttempt, Is.EqualTo(frozenAttempt));

		for (var i = 0; i < 3; i++)
		{
			var before = state.ReconnectAttempt;
			clock.Advance(ReconnectPolicy.MaxDelay);

			// Waited for rather than slept over: a fixed sleep that a loaded machine outruns lets one
			// Advance's attempt land inside the next iteration's window and read as two.
			await Wait.UntilAsync(() => state.ReconnectAttempt > before,
				TimeSpan.FromSeconds(10),
				because: "advancing past the whole backoff never produced the next reconnect attempt");

			var afterAdvance = state.ReconnectAttempt;
			await Task.Delay(TimeSpan.FromMilliseconds(300));

			Assert.That(state.ReconnectAttempt,
				Is.EqualTo(afterAdvance),
				"the next attempt was scheduled on real time rather than on the injected clock");
		}
	}

	[Test]
	public void
		Wait_UntilAsync_throws_PluginTestTimeoutException_naming_the_reason_when_the_condition_never_becomes_true()
	{
		var exception = Assert.ThrowsAsync<PluginTestTimeoutException>(async ()
			=> await Wait.UntilAsync(() => false, TimeSpan.FromMilliseconds(150), because: "xyz"));

		Assert.That(exception!.Message, Does.Contain("xyz"));
	}

	[Test]
	public void Wait_UntilAsync_returns_without_throwing_once_the_condition_becomes_true()
	{
		var flag = false;
		_ = Task.Run(async () =>
		{
			await Task.Delay(50);
			flag = true;
		});

		Assert.DoesNotThrowAsync(async () => await Wait.UntilAsync(() => flag, TimeSpan.FromSeconds(5)));
	}
}
