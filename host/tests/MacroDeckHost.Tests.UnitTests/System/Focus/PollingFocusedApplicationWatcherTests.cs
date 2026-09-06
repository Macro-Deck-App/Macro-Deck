using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Deck;
using MacroDeckHost.Integrations.System.Focus;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.System.Focus;

[TestFixture]
public class PollingFocusedApplicationWatcherTests
{
	[Test]
	public async Task WatchAsync_FirstElement_DoesNotWaitForThePollInterval()
	{
		var reader = new FixedFocusedWindowReader(new FocusedAppInfo(100, "/usr/bin/foo", "foo", "com.example.foo"));
		var watcher = new PollingFocusedApplicationWatcher(reader, TimeSpan.FromMinutes(10));

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		FocusedAppInfo? first = null;
		await foreach (var info in watcher.WatchAsync(cts.Token))
		{
			first = info;
			break;
		}

		Assert.That(first, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(first!.ProcessId, Is.EqualTo(100));
			Assert.That(first.ExecutablePath, Is.EqualTo("/usr/bin/foo"));
			Assert.That(first.ProcessName, Is.EqualTo("foo"));
			Assert.That(first.BundleId, Is.EqualTo("com.example.foo"));
		});
	}

	[Test]
	public async Task EndToEnd_NullReads_YieldNothingAndDoNotDisturbDedup()
	{
		var reader = new ScriptedFocusedWindowReader([
			() => null,
			() => null,
			() => new FocusedAppInfo(100, null, "app100", null),
			() => null,
			() => new FocusedAppInfo(100, null, "app100", null),
			() => new FocusedAppInfo(200, null, "app200", null)
		]);

		var coordinator = await RunEndToEnd(reader, TimeSpan.FromMilliseconds(10), expectedCoordinatorCalls: 2);

		Assert.That(coordinator.FocusChanges.Select(app => app.ProcessId), Is.EqualTo(new[] { 100, 200 }));
	}

	[Test]
	public async Task EndToEnd_TransientReadExceptions_DoNotEndTheStream()
	{
		var reader = new ScriptedFocusedWindowReader([
			() => new FocusedAppInfo(100, null, "app100", null),
			() => throw new InvalidOperationException("transient read failure"),
			() => throw new InvalidOperationException("transient read failure"),
			() => new FocusedAppInfo(200, null, "app200", null)
		]);

		var coordinator = await RunEndToEnd(reader, TimeSpan.FromMilliseconds(10), expectedCoordinatorCalls: 2);

		Assert.That(coordinator.FocusChanges.Select(app => app.ProcessId), Is.EqualTo(new[] { 100, 200 }));
	}

	[Test]
	public async Task WatchAsync_Cancellation_StopsPollingWithoutDisposingTheReader()
	{
		var interval = TimeSpan.FromMilliseconds(20);
		var reader = new FixedFocusedWindowReader(new FocusedAppInfo(100, null, "app100", null));
		var watcher = new PollingFocusedApplicationWatcher(reader, interval);

		using var cts = new CancellationTokenSource();
		var firstElementSeen = new TaskCompletionSource();
		var consumeTask = Task.Run(async () =>
		{
			await foreach (var _ in watcher.WatchAsync(cts.Token))
			{
				firstElementSeen.TrySetResult();
			}
		});

		await firstElementSeen.Task;
		await cts.CancelAsync();

		try
		{
			await consumeTask;
		}
		catch (OperationCanceledException)
		{
		}

		var countAfterCancel = reader.ReadCount;
		await Task.Delay(TimeSpan.FromMilliseconds(interval.TotalMilliseconds * 10));

		Assert.Multiple(() =>
		{
			Assert.That(reader.ReadCount, Is.EqualTo(countAfterCancel));
			Assert.That(reader.Disposed, Is.False);
		});
	}

	[Test]
	public void Dispose_ReaderIsDisposable_DisposesTheReader()
	{
		var reader = new FixedFocusedWindowReader(null);
		var watcher = new PollingFocusedApplicationWatcher(reader);

		watcher.Dispose();

		Assert.That(reader.Disposed, Is.True);
	}

	[Test]
	public void Dispose_ReaderIsNotDisposable_DoesNotThrow()
	{
		var watcher = new PollingFocusedApplicationWatcher(new NonDisposableFocusedWindowReader());

		Assert.DoesNotThrow(() => watcher.Dispose());
	}

	private static async Task<RecordingApplicationFocusCoordinator> RunEndToEnd(
		IFocusedWindowReader reader,
		TimeSpan interval,
		int expectedCoordinatorCalls)
	{
		var watcher = new FocusedApplicationWatcher(new PollingFocusedApplicationWatcher(reader, interval));
		var coordinator = new RecordingApplicationFocusCoordinator();
		var service = new ApplicationFocusBackgroundService(new StartedHostLifetime(),
			watcher,
			coordinator,
			Log.Logger);

		await service.StartAsync(CancellationToken.None);

		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (coordinator.FocusChanges.Count < expectedCoordinatorCalls && DateTime.UtcNow < deadline)
		{
			await Task.Delay(5);
		}

		// Give any further (unwanted) coordinator calls a chance to show up before we assert on the count.
		await Task.Delay(interval * 5);

		await service.StopAsync(CancellationToken.None);
		return coordinator;
	}

	private sealed class FixedFocusedWindowReader(FocusedAppInfo? value) : IFocusedWindowReader, IDisposable
	{
		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public int ReadCount { get; private set; }

		public bool Disposed { get; private set; }

		public FocusedAppInfo? Read()
		{
			ReadCount++;
			return value;
		}

		public void Dispose() => Disposed = true;
	}

	private sealed class NonDisposableFocusedWindowReader : IFocusedWindowReader
	{
		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public FocusedAppInfo? Read() => null;
	}

	private sealed class ScriptedFocusedWindowReader : IFocusedWindowReader
	{
		private readonly List<Func<FocusedAppInfo?>> _script;
		private int _index;

		public ScriptedFocusedWindowReader(IEnumerable<Func<FocusedAppInfo?>> script) => _script = [.. script];

		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public FocusedAppInfo? Read()
		{
			var step = _script[_index];
			if (_index < _script.Count - 1)
			{
				_index++;
			}

			return step();
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = new(true);
		public CancellationToken ApplicationStopped { get; } = new(true);

		public void StopApplication()
		{
		}
	}
}
