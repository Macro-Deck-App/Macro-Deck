using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class ApplicationFocusBackgroundServiceTests
{
	[Test]
	public async Task Startup_element_is_forwarded_with_every_field_intact()
	{
		var expected = new FocusedApplication(100, "/usr/bin/foo", "foo", "com.example.foo");
		var watcher = new ChannelApplicationFocusWatcher();
		watcher.Enqueue(expected);
		watcher.Complete();
		var coordinator = new RecordingApplicationFocusCoordinator();
		var service = CreateService(watcher, coordinator);

		await RunToCompletion(service);

		Assert.That(coordinator.FocusChanges, Has.Count.EqualTo(1));
		var actual = coordinator.FocusChanges[0];
		Assert.Multiple(() =>
		{
			Assert.That(actual.ProcessId, Is.EqualTo(expected.ProcessId));
			Assert.That(actual.ExecutablePath, Is.EqualTo(expected.ExecutablePath));
			Assert.That(actual.ProcessName, Is.EqualTo(expected.ProcessName));
			Assert.That(actual.BundleId, Is.EqualTo(expected.BundleId));
		});
	}

	[Test]
	public async Task Dedup_is_by_pid_and_returning_to_a_prior_pid_is_a_change()
	{
		var watcher = new ChannelApplicationFocusWatcher();
		foreach (var pid in new[] { 100, 100, 200, 200, 100 })
		{
			watcher.Enqueue(App(pid));
		}

		watcher.Complete();
		var coordinator = new RecordingApplicationFocusCoordinator();
		var service = CreateService(watcher, coordinator);

		await RunToCompletion(service);

		Assert.That(coordinator.FocusChanges.Select(app => app.ProcessId), Is.EqualTo(new[] { 100, 200, 100 }));
	}

	[Test]
	public async Task Same_pid_with_different_metadata_is_still_a_duplicate()
	{
		var watcher = new ChannelApplicationFocusWatcher();
		watcher.Enqueue(new FocusedApplication(100, "/usr/bin/foo", "foo", "com.example.foo"));
		watcher.Enqueue(new FocusedApplication(100, null, "foo", null));
		watcher.Complete();
		var coordinator = new RecordingApplicationFocusCoordinator();
		var service = CreateService(watcher, coordinator);

		await RunToCompletion(service);

		Assert.That(coordinator.FocusChanges, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Unsupported_watcher_never_starts_a_subscription()
	{
		var watcher = new UnsupportedWatcher();
		var coordinator = new RecordingApplicationFocusCoordinator();
		var service = CreateService(watcher, coordinator);

		await RunToCompletion(service);

		Assert.Multiple(() =>
		{
			Assert.That(watcher.WatchAsyncCalled, Is.False);
			Assert.That(coordinator.FocusChanges, Is.Empty);
		});
	}

	[Test]
	public async Task Cancellation_tears_down_the_subscription_without_faulting()
	{
		var watcher = new CancellationTrackingWatcher();
		var coordinator = new SignalingCoordinator();
		var service = CreateService(watcher, coordinator);

		await service.StartAsync(CancellationToken.None);
		await coordinator.FirstCall;
		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(watcher.Disposed, Is.True);
			Assert.That(service.ExecuteTask?.IsFaulted, Is.False);
			Assert.That(coordinator.FocusChanges, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_stream_that_faults_before_any_element_does_not_kill_the_host()
	{
		var watcher = new FaultingWatcher(itemsBeforeThrow: 0, items: []);
		var coordinator = new RecordingApplicationFocusCoordinator();
		var service = CreateService(watcher, coordinator, TimeSpan.FromMilliseconds(10));

		await service.StartAsync(CancellationToken.None);

		// The stream faults every time it is subscribed, so the execute task never completes on its
		// own with retry in place. Waiting for a second subscription attempt proves the fault was
		// retried rather than merely not having crashed yet.
		await watcher.Resubscribed.WaitAsync(TimeSpan.FromSeconds(5));

		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(service.ExecuteTask?.IsFaulted, Is.False);
			Assert.That(coordinator.FocusChanges, Is.Empty);
		});
	}

	[Test]
	public async Task A_stream_that_faults_after_good_elements_keeps_what_was_already_forwarded()
	{
		var watcher = new FaultingWatcher(itemsBeforeThrow: 2, items: [App(100), App(200)]);
		var coordinator = new CountdownApplicationFocusCoordinator(expectedCount: 2);
		var service = CreateService(watcher, coordinator, TimeSpan.FromMilliseconds(10));

		await service.StartAsync(CancellationToken.None);
		await coordinator.Reached.WaitAsync(TimeSpan.FromSeconds(5));
		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(service.ExecuteTask?.IsFaulted, Is.False);
			Assert.That(coordinator.FocusChanges.Select(app => app.ProcessId), Is.EqualTo(new[] { 100, 200 }));
		});
	}

	[Test]
	public async Task A_faulted_stream_is_retried_and_focus_detection_resumes()
	{
		var expected = App(300);
		var watcher = new FaultOnceThenYieldWatcher(expected);
		var coordinator = new SignalingCoordinator();
		var service = CreateService(watcher, coordinator, TimeSpan.FromMilliseconds(10));

		await service.StartAsync(CancellationToken.None);

		// Only the watcher's second subscription ever produces an element, so the coordinator observing
		// one proves the fault from the first subscription was retried rather than ending the loop.
		await coordinator.FirstCall.WaitAsync(TimeSpan.FromSeconds(5));

		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(service.ExecuteTask?.IsFaulted, Is.False);
			Assert.That(coordinator.FocusChanges.Select(app => app.ProcessId),
				Is.EqualTo(new[] { expected.ProcessId }));
		});
	}

	[Test]
	public async Task A_coordinator_throw_does_not_stop_detection_of_later_changes()
	{
		var watcher = new ChannelApplicationFocusWatcher();
		watcher.Enqueue(App(100));
		watcher.Enqueue(App(200));
		watcher.Complete();
		var coordinator = new ThrowOnceApplicationFocusCoordinator();
		var service = CreateService(watcher, coordinator);

		await RunToCompletion(service);

		Assert.That(coordinator.FocusChanges.Select(app => app.ProcessId), Is.EqualTo(new[] { 200 }));
	}

	private static ApplicationFocusBackgroundService CreateService(
		IApplicationFocusWatcher watcher,
		IApplicationFocusCoordinator coordinator)
		=> new(new StartedHostLifetime(), watcher, coordinator, Log.Logger);

	private static ApplicationFocusBackgroundService CreateService(
		IApplicationFocusWatcher watcher,
		IApplicationFocusCoordinator coordinator,
		TimeSpan retryDelay)
		=> new(new StartedHostLifetime(), watcher, coordinator, Log.Logger, retryDelay);

	private static async Task RunToCompletion(ApplicationFocusBackgroundService service)
	{
		await service.StartAsync(CancellationToken.None);
		if (service.ExecuteTask is { } executeTask)
		{
			await executeTask;
		}

		await service.StopAsync(CancellationToken.None);
	}

	private static FocusedApplication App(int pid) => new(pid, null, "app" + pid, null);

	private sealed class ChannelApplicationFocusWatcher : IApplicationFocusWatcher
	{
		private readonly Channel<FocusedApplication> _channel = Channel.CreateUnbounded<FocusedApplication>();

		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public void Enqueue(FocusedApplication app) => _channel.Writer.TryWrite(app);

		public void Complete() => _channel.Writer.TryComplete();

		public IAsyncEnumerable<FocusedApplication> WatchAsync(CancellationToken cancellationToken)
			=> _channel.Reader.ReadAllAsync(cancellationToken);
	}

	private sealed class UnsupportedWatcher : IApplicationFocusWatcher
	{
		public bool IsSupported => false;

		public string? UnsupportedReason => "not supported here";

		public bool WatchAsyncCalled { get; private set; }

		public IAsyncEnumerable<FocusedApplication> WatchAsync(CancellationToken cancellationToken)
		{
			WatchAsyncCalled = true;
			throw new InvalidOperationException("WatchAsync must not be called when unsupported");
		}
	}

	private sealed class CancellationTrackingWatcher : IApplicationFocusWatcher
	{
		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public bool Disposed { get; private set; }

		public async IAsyncEnumerable<FocusedApplication> WatchAsync(
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			try
			{
				yield return new FocusedApplication(100, null, "app100", null);
				await Task.Delay(Timeout.Infinite, cancellationToken);
			}
			finally
			{
				Disposed = true;
			}
		}
	}

	// Gives per-scenario control over exactly when the stream faults, which an iterator method with a
	// throw statement cannot express without leaving unreachable code behind.
	private sealed class FaultingWatcher(int itemsBeforeThrow, IReadOnlyList<FocusedApplication> items)
		: IApplicationFocusWatcher
	{
		private readonly TaskCompletionSource _resubscribed = new();
		private int _subscriptionCount;

		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		// Completes once WatchAsync is called a second time, proving the fault from the first
		// subscription was retried rather than left the loop.
		public Task Resubscribed => _resubscribed.Task;

		public IAsyncEnumerable<FocusedApplication> WatchAsync(CancellationToken cancellationToken)
		{
			if (Interlocked.Increment(ref _subscriptionCount) > 1)
			{
				_resubscribed.TrySetResult();
			}

			return new ThrowingAsyncEnumerable(itemsBeforeThrow, items);
		}
	}

	private sealed class ThrowingAsyncEnumerable(int itemsBeforeThrow, IReadOnlyList<FocusedApplication> items)
		: IAsyncEnumerable<FocusedApplication>
	{
		public IAsyncEnumerator<FocusedApplication> GetAsyncEnumerator(CancellationToken cancellationToken = default)
			=> new Enumerator(itemsBeforeThrow, items);

		private sealed class Enumerator(int itemsBeforeThrow, IReadOnlyList<FocusedApplication> items)
			: IAsyncEnumerator<FocusedApplication>
		{
			private int _index = -1;

			public FocusedApplication Current => items[_index];

			public ValueTask<bool> MoveNextAsync()
			{
				_index++;
				if (_index >= itemsBeforeThrow)
				{
					throw new InvalidOperationException("simulated watch failure");
				}

				return ValueTask.FromResult(true);
			}

			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}

	// Faults its first subscription immediately and never yields from it; its second subscription
	// yields one element and then stays open. Used to prove that a fault triggers a re-subscription
	// rather than ending detection for good.
	private sealed class FaultOnceThenYieldWatcher(FocusedApplication itemOnRetry) : IApplicationFocusWatcher
	{
		private int _subscriptionCount;

		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public IAsyncEnumerable<FocusedApplication> WatchAsync(CancellationToken cancellationToken)
			=> Interlocked.Increment(ref _subscriptionCount) == 1
				? new ThrowingAsyncEnumerable(itemsBeforeThrow: 0, items: [])
				: YieldThenWait(cancellationToken);

		private async IAsyncEnumerable<FocusedApplication> YieldThenWait(
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			yield return itemOnRetry;
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}
	}

	private sealed class ThrowOnceApplicationFocusCoordinator : IApplicationFocusCoordinator
	{
		private readonly RecordingApplicationFocusCoordinator _inner = new();
		private bool _threw;

		public IReadOnlyList<FocusedApplication> FocusChanges => _inner.FocusChanges;

		public Task OnFocusChanged(FocusedApplication app, CancellationToken cancellationToken)
		{
			if (!_threw)
			{
				_threw = true;
				throw new InvalidOperationException("simulated coordinator failure");
			}

			return _inner.OnFocusChanged(app, cancellationToken);
		}

		public Task OnFolderReported(Guid deviceId,
			Guid folderId,
			string? navigationToken,
			bool isResync,
			CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task OnDevicePresenceChanged(Guid deviceId, bool online, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task OnRulesChanged(CancellationToken cancellationToken) => throw new NotSupportedException();
	}

	private sealed class SignalingCoordinator : IApplicationFocusCoordinator
	{
		private readonly RecordingApplicationFocusCoordinator _inner = new();
		private readonly TaskCompletionSource _firstCall = new();

		public IReadOnlyList<FocusedApplication> FocusChanges => _inner.FocusChanges;

		public Task FirstCall => _firstCall.Task;

		public async Task OnFocusChanged(FocusedApplication app, CancellationToken cancellationToken)
		{
			await _inner.OnFocusChanged(app, cancellationToken);
			_firstCall.TrySetResult();
		}

		public Task OnFolderReported(Guid deviceId,
			Guid folderId,
			string? navigationToken,
			bool isResync,
			CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task OnDevicePresenceChanged(Guid deviceId, bool online, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task OnRulesChanged(CancellationToken cancellationToken) => throw new NotSupportedException();
	}

	// Signals once a target number of elements has been forwarded, so a test can wait for "all the
	// expected elements arrived" instead of racing an arbitrary delay against delivery.
	private sealed class CountdownApplicationFocusCoordinator(int expectedCount) : IApplicationFocusCoordinator
	{
		private readonly RecordingApplicationFocusCoordinator _inner = new();
		private readonly TaskCompletionSource _reached = new();
		private int _count;

		public IReadOnlyList<FocusedApplication> FocusChanges => _inner.FocusChanges;

		public Task Reached => _reached.Task;

		public async Task OnFocusChanged(FocusedApplication app, CancellationToken cancellationToken)
		{
			await _inner.OnFocusChanged(app, cancellationToken);
			if (Interlocked.Increment(ref _count) >= expectedCount)
			{
				_reached.TrySetResult();
			}
		}

		public Task OnFolderReported(Guid deviceId,
			Guid folderId,
			string? navigationToken,
			bool isResync,
			CancellationToken cancellationToken) => throw new NotSupportedException();

		public Task OnDevicePresenceChanged(Guid deviceId, bool online, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task OnRulesChanged(CancellationToken cancellationToken) => throw new NotSupportedException();
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
