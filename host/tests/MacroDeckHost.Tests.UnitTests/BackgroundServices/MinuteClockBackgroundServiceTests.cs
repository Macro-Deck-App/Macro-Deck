using MacroDeckHost.Application.Events;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
public class MinuteClockBackgroundServiceTests
{
	private static readonly TimeZoneInfo _nepal
		= TimeZoneInfo.CreateCustomTimeZone("Test Nepal", new TimeSpan(5, 45, 0), "Test Nepal", "Test Nepal");

	private ManualTimeProvider _clock = null!;
	private RecordingMediator _mediator = null!;
	private MinuteClockBackgroundService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_clock = new ManualTimeProvider
		{
			Now = new DateTimeOffset(2026, 3, 1, 12, 0, 30, TimeSpan.Zero),
			Zone = _nepal
		};
		_mediator = new RecordingMediator();
		_service = new MinuteClockBackgroundService(_mediator, _clock, Serilog.Log.Logger);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _service.StopAsync(CancellationToken.None);
		_service.Dispose();
	}

	[Test]
	public async Task The_minute_is_announced_at_the_start_of_each_minute_and_not_before()
	{
		await StartAsync();

		await AdvanceAsync(TimeSpan.FromSeconds(29));
		var beforeBoundary = Announcements();
		await AdvanceAsync(TimeSpan.FromSeconds(1));
		var atFirstBoundary = Announcements();
		await AdvanceAsync(TimeSpan.FromSeconds(60));

		Assert.Multiple(() =>
		{
			Assert.That(beforeBoundary, Is.Zero);
			Assert.That(atFirstBoundary, Is.EqualTo(1));
			Assert.That(Announcements(), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task A_late_wake_still_announces_its_minute_once_and_keeps_to_the_minute_after()
	{
		await StartAsync();

		await AdvanceAsync(TimeSpan.FromSeconds(75));
		var afterLateWake = Announcements();
		await AdvanceAsync(TimeSpan.FromSeconds(15));

		Assert.Multiple(() =>
		{
			Assert.That(afterLateWake, Is.EqualTo(1));
			Assert.That(Announcements(), Is.EqualTo(2));
		});
	}

	private async Task StartAsync()
	{
		var scheduled = _clock.ScheduledCount;
		await _service.StartAsync(CancellationToken.None);
		await _clock.WaitForScheduleAsync(scheduled);
	}

	private async Task AdvanceAsync(TimeSpan delta)
	{
		var scheduled = _clock.ScheduledCount;
		if (_clock.Advance(delta))
		{
			await _clock.WaitForScheduleAsync(scheduled);
		}
	}

	private int Announcements()
	{
		lock (_mediator.Published)
		{
			return _mediator.Published.OfType<MinuteElapsedNotification>().Count();
		}
	}
}
