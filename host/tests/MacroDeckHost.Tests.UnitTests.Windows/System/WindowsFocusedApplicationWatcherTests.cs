using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsFocusedApplicationWatcherTests
{
	[Test]
	public void Constructs_with_native_support_and_no_unsupported_reason()
	{
		using var watcher = new WindowsFocusedApplicationWatcher();

		Assert.Multiple(() =>
		{
			Assert.That(watcher.IsSupported, Is.True);
			Assert.That(watcher.UnsupportedReason, Is.Null);
		});
	}

	[Test]
	public async Task WatchAsync_can_be_started_and_cancelled_cleanly_and_the_pump_thread_terminates()
	{
		var watcher = new WindowsFocusedApplicationWatcher();
		using var cts = new CancellationTokenSource();

		var items = new List<FocusedAppInfo>();
		var enumeration = Task.Run(async () =>
		{
			await foreach (var info in watcher.WatchAsync(cts.Token))
			{
				items.Add(info);
			}
		});

		// Give the subscription-time seed (and any immediate foreground change) a chance to arrive
		// before cancelling. CI's Windows runner may have no interactive foreground window, so no
		// item arriving here is expected and fine.
		await Task.Delay(TimeSpan.FromMilliseconds(200));
		cts.Cancel();

		Exception? unexpected = null;
		try
		{
			await enumeration.WaitAsync(TimeSpan.FromSeconds(5));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			unexpected = ex;
		}

		Assert.That(unexpected, Is.Null, "Cancelling the enumeration should not fault it.");

		Assert.DoesNotThrow(watcher.Dispose);

		// The most valuable assertion here: that the pump thread actually terminated, not merely that
		// Dispose returned quickly - Dispose returning fast is also what happens when the join times out
		// and the thread is left running.
		Assert.That(watcher.PumpThreadIsAlive, Is.False);

		if (items.Count > 0)
		{
			var info = items[0];
			Assert.Multiple(() =>
			{
				Assert.That(info.ProcessId, Is.GreaterThan(0));
				Assert.That(info.BundleId, Is.Null);
				Assert.That(info.ExecutablePath is not null || info.ProcessName is not null, Is.True);
			});
		}

		TestContext.Out.WriteLine($"Focus events observed before cancellation: {items.Count}");
	}

	[Test]
	public void Constructing_disposing_and_constructing_a_second_instance_does_not_throw()
	{
		Assert.DoesNotThrow(() =>
		{
			var first = new WindowsFocusedApplicationWatcher();
			first.Dispose();

			using var second = new WindowsFocusedApplicationWatcher();
		});
	}
}
