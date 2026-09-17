using MacroDeckHost.Application.Store;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class StoreRegistryRefreshBackgroundServiceTests
{
	[Test]
	public async Task A_refresh_cancelled_by_the_stopping_host_is_not_reported_as_an_error()
	{
		var time = new ManualTimeProvider();
		var refresher = new CancelledOnRefreshRefresher();
		var sink = new CapturingSink();
		var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
		using var lifetime = new StubHostApplicationLifetime(started: true);
		using var service = new StoreRegistryRefreshBackgroundService(lifetime,
			refresher,
			StoreRegistryOptions.Default,
			time,
			logger);

		var armed = time.ScheduledCount;
		await service.StartAsync(CancellationToken.None);
		await time.WaitForScheduleAsync(armed);
		time.Advance(TimeSpan.FromMinutes(3));
		await refresher.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(refresher.Trigger, Is.EqualTo(StoreRegistryRefreshTrigger.Scheduled));
			Assert.That(sink.Events.Where(logEvent => logEvent.Level >= LogEventLevel.Warning), Is.Empty);
		});
	}

	[Test]
	public async Task The_registry_is_refreshed_within_seconds_of_starting_and_then_every_hour()
	{
		var time = new ManualTimeProvider();
		var refresher = new CountingRefresher();
		using var lifetime = new StubHostApplicationLifetime(started: true);
		using var service = new StoreRegistryRefreshBackgroundService(lifetime,
			refresher,
			StoreRegistryOptions.Default,
			time,
			Serilog.Core.Logger.None);

		var armed = time.ScheduledCount;
		await service.StartAsync(CancellationToken.None);
		await time.WaitForScheduleAsync(armed);
		var armedHourly = time.ScheduledCount;
		time.Advance(TimeSpan.FromSeconds(5));
		await refresher.WaitForCount(1);
		await time.WaitForScheduleAsync(armedHourly);
		time.Advance(TimeSpan.FromMinutes(60));
		await refresher.WaitForCount(2);
		await service.StopAsync(CancellationToken.None);

		Assert.That(refresher.Count, Is.EqualTo(2));
	}

	private sealed class CountingRefresher : IStoreRegistryRefresher
	{
		private int _count;

		public int Count => _count;

		public StoreRegistryStatus Status => StoreRegistryStatus.Unavailable;

		public async Task WaitForCount(int expected)
		{
			for (var attempt = 0; attempt < 400 && _count < expected; attempt++)
			{
				await Task.Delay(10);
			}

			Assert.That(_count, Is.GreaterThanOrEqualTo(expected));
		}

		public Task<Result<RegistryRefreshError>> Refresh(CancellationToken cancellationToken = default) =>
			Refresh(StoreRegistryRefreshTrigger.Manual, cancellationToken);

		public Task<Result<RegistryRefreshError>> Refresh(StoreRegistryRefreshTrigger trigger,
			CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _count);
			return Task.FromResult(Result.Ok<RegistryRefreshError>());
		}

		public Task LoadCachedRegistry(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class CancelledOnRefreshRefresher : IStoreRegistryRefresher
	{
		public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public StoreRegistryRefreshTrigger? Trigger { get; private set; }

		public StoreRegistryStatus Status => StoreRegistryStatus.Unavailable;

		public Task<Result<RegistryRefreshError>> Refresh(CancellationToken cancellationToken = default) =>
			Refresh(StoreRegistryRefreshTrigger.Manual, cancellationToken);

		public Task<Result<RegistryRefreshError>> Refresh(StoreRegistryRefreshTrigger trigger,
			CancellationToken cancellationToken = default)
		{
			Trigger = trigger;
			Called.TrySetResult();
			return Task.FromException<Result<RegistryRefreshError>>(new OperationCanceledException());
		}

		public Task LoadCachedRegistry(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class CapturingSink : Serilog.Core.ILogEventSink
	{
		private readonly Lock _lock = new();
		private readonly List<LogEvent> _events = [];

		public IReadOnlyList<LogEvent> Events
		{
			get
			{
				lock (_lock)
				{
					return _events.ToList();
				}
			}
		}

		public void Emit(LogEvent logEvent)
		{
			lock (_lock)
			{
				_events.Add(logEvent);
			}
		}
	}
}
