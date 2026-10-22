using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

public class IntegrationCapabilityDataBroadcastTests
{
	private BatteryRegistry _batteries = null!;
	private RecordingUiTransport _transport = null!;
	private IntegrationCapabilityDataBroadcastBackgroundService _service = null!;

	[SetUp]
	public async Task SetUp()
	{
		_batteries = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_transport = new RecordingUiTransport();
		_service = new IntegrationCapabilityDataBroadcastBackgroundService(new StartedHostLifetime(),
			_batteries,
			new EmptyCalendarCache(),
			new EmptyInboxCache(),
			_transport,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			TimeSpan.FromMilliseconds(20));
		await _service.StartAsync(CancellationToken.None);

		var level = 79;
		while (Events().Count == 0 && level > 0)
		{
			_batteries.Publish("first.plugin", null, [Battery("pad", level--)]);
			await Task.Delay(30);
		}

		Assert.That(Events(), Is.Not.Empty, "The broadcaster never started listening.");

		int seen;
		do
		{
			seen = Events().Count;
			await Task.Delay(150);
		} while (Events().Count != seen);

		_transport.GroupMessages.Clear();
	}

	[TearDown]
	public async Task TearDown()
	{
		await _service.StopAsync(CancellationToken.None);
		_service.Dispose();
		_batteries.Dispose();
	}

	[Test]
	public async Task A_battery_change_is_announced_once_for_the_integration_that_owns_it()
	{
		_batteries.Publish("first.plugin", null, [Battery("pad", 10)]);
		_batteries.Publish("first.plugin", null, [Battery("pad", 5)]);

		await WaitForAsync(() => Events().Count > 0);
		await Task.Delay(100);

		var events = Events();
		Assert.Multiple(() =>
		{
			Assert.That(events, Has.Count.EqualTo(1));
			Assert.That(events[0].Kind, Is.EqualTo(CapabilityKinds.BatteryProvider));
			Assert.That(events[0].IntegrationIds, Is.EqualTo(new[] { "first.plugin" }));
		});
	}

	[Test]
	public async Task Another_integration_publishing_batteries_does_not_report_the_unchanged_one()
	{
		_batteries.Publish("second.plugin", null, [Battery("mouse", 40)]);

		await WaitForAsync(() => Events().Count > 0);

		Assert.That(Events().Single().IntegrationIds, Is.EqualTo(new[] { "second.plugin" }));
	}

	[Test]
	public async Task Removing_the_last_battery_of_an_integration_is_announced_too()
	{
		_batteries.RemoveAll("first.plugin");

		await WaitForAsync(() => Events().Count > 0);

		Assert.That(Events().Single().IntegrationIds, Is.EqualTo(new[] { "first.plugin" }));
	}

	private List<IntegrationCapabilityDataChangedEvent> Events()
	{
		return
		[
			.. _transport.GroupMessages.Select(entry => entry.Message).OfType<IntegrationCapabilityDataChangedEvent>()
		];
	}

	private static BatteryDevice Battery(string id, int level)
	{
		return new BatteryDevice { Id = id, Name = id, LevelPercent = level };
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was never met.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);

		public CancellationToken ApplicationStopping => CancellationToken.None;

		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}

	private sealed class EmptyCalendarCache : ICalendarEventCache
	{
		public CalendarSnapshot Snapshot => CalendarSnapshot.Empty;

		public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public Task SyncAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public Task<CalendarEventDetails?> GetEventDetailsAsync(
			string calendarKey,
			string eventId,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<CalendarEventDetails?>(null);
		}
	}

	private sealed class EmptyInboxCache : IEmailInboxCache
	{
		public EmailInboxesSnapshot Snapshot => EmailInboxesSnapshot.Empty;

		public DateTimeOffset? NextRetryAt => null;

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public Task SyncAsync(EmailSyncRequest request, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public EmailInboxAggregate Aggregate(IReadOnlyCollection<string> inboxRefs)
		{
			return EmailInboxesSnapshot.Empty.Aggregate(inboxRefs);
		}

		public void RequestSync()
		{
		}
	}
}
