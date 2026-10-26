using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Focus;
using MacroDeckHost.Platform.Native;

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
		await cts.CancelAsync();

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

		await TestContext.Out.WriteLineAsync($"Focus events observed before cancellation: {items.Count}");
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

	[Test]
	public async Task A_late_foreground_event_for_a_window_no_longer_in_front_reports_the_window_in_front()
	{
		var target = new FocusedAppInfo(43508, @"C:\Jellyfin\JellyfinMediaPlayer.exe", "JellyfinMediaPlayer", null);
		using var watcher = new WindowsFocusedApplicationWatcher(new ForegroundReader(target));
		using var cts = new CancellationTokenSource();

		var items = new ConcurrentQueue<FocusedAppInfo>();
		var enumeration = Task.Run(async () =>
		{
			await foreach (var info in watcher.WatchAsync(cts.Token))
			{
				items.Enqueue(info);
			}
		});

		await WaitForCount(items, 1);

		RaiseForeground(watcher, new IntPtr(0x1001));
		RaiseForeground(watcher, GetDesktopWindow());

		await WaitForCount(items, 3);
		await cts.CancelAsync();
		try
		{
			await enumeration.WaitAsync(TimeSpan.FromSeconds(5));
		}
		catch (OperationCanceledException)
		{
		}

		Assert.Multiple(() =>
		{
			Assert.That(items, Has.Count.GreaterThanOrEqualTo(3));
			Assert.That(items, Has.All.EqualTo(target));
		});
	}

	private static void RaiseForeground(WindowsFocusedApplicationWatcher watcher, IntPtr window)
	{
		watcher.OnForegroundChanged(IntPtr.Zero,
			Win32WinEvent.EventSystemForeground,
			window,
			Win32WinEvent.ObjidWindow,
			Win32WinEvent.ChildidSelf,
			0,
			0);
	}

	private static async Task WaitForCount(ConcurrentQueue<FocusedAppInfo> items, int count)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
		while (items.Count < count && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}

	[DllImport("user32.dll")]
	private static extern IntPtr GetDesktopWindow();

	private sealed class ForegroundReader(FocusedAppInfo foreground) : IFocusedWindowReader
	{
		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public FocusedAppInfo? Read()
		{
			return foreground;
		}
	}
}
