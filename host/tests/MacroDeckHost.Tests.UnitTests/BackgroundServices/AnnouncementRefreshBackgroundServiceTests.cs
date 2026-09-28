using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.Store;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class AnnouncementRefreshBackgroundServiceTests
{
	[Test]
	public async Task Announcements_are_checked_within_seconds_of_starting_and_then_about_every_six_hours_even_after_a_failure()
	{
		var time = new ManualTimeProvider();
		var announcements = new FailingOnceAnnouncements();
		using var lifetime = new StubHostApplicationLifetime(started: true);
		using var service = new AnnouncementRefreshBackgroundService(lifetime,
			announcements,
			time,
			Serilog.Core.Logger.None);

		var armed = time.ScheduledCount;
		await service.StartAsync(CancellationToken.None);
		await time.WaitForScheduleAsync(armed);
		var armedInterval = time.ScheduledCount;
		time.Advance(TimeSpan.FromSeconds(30));
		await announcements.WaitForCount(1);

		await time.WaitForScheduleAsync(armedInterval);
		time.Advance(TimeSpan.FromHours(5));
		await Task.Delay(50);
		Assert.That(announcements.Count, Is.EqualTo(1), "not before the interval");

		time.Advance(TimeSpan.FromMinutes(90));
		await announcements.WaitForCount(2);
		await service.StopAsync(CancellationToken.None);

		Assert.That(announcements.Count, Is.EqualTo(2));
	}

	private sealed class FailingOnceAnnouncements : IAnnouncementService
	{
		private int _count;

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public int Count => _count;

		public Announcement? Pending => null;

		public Task Refresh(CancellationToken cancellationToken)
			=> Interlocked.Increment(ref _count) == 1
				? throw new InvalidOperationException("platform down")
				: Task.CompletedTask;

		public Task MarkSeen(int number, CancellationToken cancellationToken) => Task.CompletedTask;

		public async Task WaitForCount(int expected)
		{
			for (var attempt = 0; attempt < 400 && _count < expected; attempt++)
			{
				await Task.Delay(25);
			}

			Assert.That(_count, Is.GreaterThanOrEqualTo(expected));
		}
	}
}
