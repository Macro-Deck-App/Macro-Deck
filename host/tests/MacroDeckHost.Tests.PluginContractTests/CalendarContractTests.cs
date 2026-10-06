using MacroDeck.Plugin.Hosting.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class CalendarContractTests : CapabilityContractFixture
{
	private static readonly DateTimeOffset _monday = new(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(2));

	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.Calendar,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private async Task<ICalendarProvider> ConnectAsync(TestCalendarIntegration calendar)
		=> (ICalendarProvider)await ConnectAsync(
			[new CalendarCapabilityHandler([calendar], TestMetadata.Default)],
			[Provider()],
			[CapabilityKinds.Calendar]);

	[Test]
	public async Task A_plugin_calendar_provider_surfaces_its_accounts_through_the_host_integration()
	{
		var provider = await ConnectAsync(new TestCalendarIntegration("Google Calendar", "alice", "bob"));

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate((IIntegration)provider), Is.Empty);
			Assert.That(provider.ProviderName, Is.EqualTo("Google Calendar"));
			Assert.That(provider.GetAccounts().Select(account => account.Id), Is.EqualTo(new[] { "alice", "bob" }));
			Assert.That(ProvidedCapabilityCatalog.For((IIntegration)provider).Select(capability => capability.Kind),
				Does.Contain(CapabilityKinds.Calendar));
		});
	}

	[Test]
	public async Task Every_read_round_trips_to_the_sdk_types()
	{
		var start = _monday.AddHours(9);
		var calendar = new TestCalendarIntegration("Google Calendar", "alice");
		calendar.Calendars["alice"] = [new CalendarInfo { Id = "primary", Name = "Alice", IsPrimary = true }];
		calendar.Events["alice"] =
		[
			new CalendarEvent
			{
				Id = "standup",
				CalendarId = "primary",
				Title = "Stand-up",
				Start = start,
				End = start.AddMinutes(15),
				Location = "Room 4",
				Description = "<p>Daily</p>",
				MeetingUrl = "https://meet.example.com/abc",
				Participants =
				[
					new CalendarParticipant
					{
						Name = "Bob", IsOrganizer = true, Response = CalendarResponseStatus.Declined
					}
				]
			}
		];
		var provider = await ConnectAsync(calendar);

		var calendars = await provider.GetCalendarsAsync("alice", CancellationToken.None);
		var events = await provider.GetEventsAsync("alice",
			new CalendarEventQuery { From = _monday, To = _monday.AddDays(1) },
			CancellationToken.None);
		var details = await provider.GetEventAsync("alice", "primary", "standup", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(calendars.Single().IsPrimary, Is.True);
			Assert.That(events.Single().Start, Is.EqualTo(start));
			Assert.That(events.Single().MeetingUrl, Is.EqualTo("https://meet.example.com/abc"));
			Assert.That(details!.Description, Is.EqualTo("<p>Daily</p>"));
			Assert.That(details.Participants.Single().Response, Is.EqualTo(CalendarResponseStatus.Declined));
			Assert.That(details.Participants.Single().IsOrganizer, Is.True);
		});
	}

	[Test]
	public async Task An_event_that_no_longer_exists_reads_as_null()
	{
		var provider = await ConnectAsync(new TestCalendarIntegration("Google Calendar", "alice"));

		Assert.That(await provider.GetEventAsync("alice", "primary", "gone", CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task A_failed_read_reaches_the_host_as_a_failure_never_as_an_empty_calendar()
	{
		var calendar = new TestCalendarIntegration("Google Calendar", "alice")
		{
			ReadFailure = new HttpRequestException("token=abc123")
		};
		var provider = await ConnectAsync(calendar);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () => await provider.GetEventsAsync(
			"alice",
			new CalendarEventQuery { From = _monday, To = _monday.AddDays(1) },
			CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(exception.Message, Does.Not.Contain("token=abc123"));
		});
	}

	[Test]
	public async Task A_plugin_calendar_reaches_the_host_registry_and_event_cache()
	{
		var calendar = new TestCalendarIntegration("Google Calendar", "alice");
		calendar.Calendars["alice"] = [new CalendarInfo { Id = "primary", Name = "Alice" }];
		calendar.Events["alice"] =
		[
			new CalendarEvent
			{
				Id = "standup",
				CalendarId = "primary",
				Title = "Stand-up",
				Start = _monday.AddHours(9),
				End = _monday.AddHours(9).AddMinutes(15)
			}
		];
		await ConnectAsync(calendar);

		var registry = new CalendarRegistry(IntegrationRegistry, Serilog.Core.Logger.None);
		using var cache = new CalendarEventCache(registry,
			new FixedTime(_monday.AddHours(8)),
			Serilog.Core.Logger.None,
			TimeZoneInfo.Utc);
		await cache.SyncAsync(CancellationToken.None);

		var synced = cache.Snapshot.Events.Single();
		Assert.Multiple(() =>
		{
			Assert.That(registry.GetAccounts().Select(account => account.AccountId),
				Is.EqualTo(new[] { $"{PluginId}::alice" }));
			Assert.That(synced.AccountId, Is.EqualTo($"{PluginId}::alice"));
			Assert.That(synced.CalendarName, Is.EqualTo("Alice"));
			Assert.That(synced.Title, Is.EqualTo("Stand-up"));
			Assert.That(cache.Snapshot.Accounts.Single().Status, Is.EqualTo(CalendarAccountStatus.Ok));
		});
	}

	[Test]
	public async Task Notifying_a_changed_account_list_makes_the_host_report_the_new_accounts()
	{
		var calendar = new TestCalendarIntegration("Google Calendar", "alice");
		await ConnectAsync(calendar);

		calendar.AccountIds.Add("bob");
		await SendStateUpdateFromPluginAsync(CapabilityKinds.Calendar);

		await WaitForAsync(() => CurrentProvider().GetAccounts().Count == 2,
			"The host's snapshot was never refreshed after state.update.");

		Assert.That(CurrentProvider().GetAccounts().Select(account => account.Id),
			Is.EqualTo(new[] { "alice", "bob" }));

		ICalendarProvider CurrentProvider()
			=> (ICalendarProvider)IntegrationRegistry.Integrations
				.Single(candidate => string.Equals(candidate.Id, PluginId, StringComparison.Ordinal));
	}

	private sealed class FixedTime(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
