using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Calendar;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class CalendarCapabilityHandlerTests
{
	private static readonly DateTimeOffset _monday = new(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(2));

	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id_and_no_provider_declares_nothing()
	{
		var declared = new CalendarCapabilityHandler([new TestCalendarIntegration("Google", "alice")],
				TestMetadata.Default)
			.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared.Select(capability => (capability.Kind, capability.LocalId)),
				Is.EqualTo(new[] { (CapabilityKinds.Calendar, ProviderCapabilityId.LocalId) }));
			Assert.That(new CalendarCapabilityHandler([], TestMetadata.Default).DeclareCapabilities(), Is.Empty);
		});
	}

	[Test]
	public async Task Describe_merges_the_accounts_of_every_provider_and_falls_back_to_the_manifest_name()
	{
		var handler = new CalendarCapabilityHandler([
				new TestCalendarIntegration(string.Empty, "alice"), new TestCalendarIntegration(string.Empty, "bob")
			],
			TestMetadata.Default);

		var payload = await InvokeAsync<CalendarDescribePayload>(handler, CapabilityOperations.Calendar.Describe);

		Assert.Multiple(() =>
		{
			Assert.That(payload.ProviderName, Is.EqualTo(TestMetadata.Default.Name));
			Assert.That(payload.Accounts.Select(account => account.Id), Is.EqualTo(new[] { "alice", "bob" }));
		});
	}

	[Test]
	public async Task An_account_two_providers_announce_is_listed_once_and_served_by_the_first()
	{
		var first = new TestCalendarIntegration("Google", "alice");
		var second = new TestCalendarIntegration("Outlook", "alice", "bob");
		first.Calendars["alice"] = [new CalendarInfo { Id = "first", Name = "First" }];
		second.Calendars["alice"] = [new CalendarInfo { Id = "second", Name = "Second" }];
		var handler = new CalendarCapabilityHandler([first, second], TestMetadata.Default);

		var accounts = await InvokeAsync<CalendarAccountsResult>(handler, CapabilityOperations.Calendar.Accounts);
		var described = await InvokeAsync<CalendarDescribePayload>(handler, CapabilityOperations.Calendar.Describe);
		var calendars = await InvokeAsync<CalendarListResult>(handler,
			CapabilityOperations.Calendar.Calendars,
			new CalendarAccountArguments { AccountId = "alice" });

		Assert.Multiple(() =>
		{
			Assert.That(accounts.Accounts.Select(account => account.Id), Is.EqualTo(new[] { "alice", "bob" }));
			Assert.That(described.Accounts.Select(account => account.Id), Is.EqualTo(new[] { "alice", "bob" }));
			Assert.That(calendars.Calendars.Select(calendar => calendar.Id), Is.EqualTo(new[] { "first" }));
		});
	}

	[Test]
	public async Task Calendars_are_read_from_the_provider_that_owns_the_account()
	{
		var first = new TestCalendarIntegration("Google", "alice");
		var second = new TestCalendarIntegration("Outlook", "bob");
		second.Calendars["bob"] =
			[new CalendarInfo { Id = "work", Name = "Work", Color = "#336699", IsPrimary = true }];
		var handler = new CalendarCapabilityHandler([first, second], TestMetadata.Default);

		var result = await InvokeAsync<CalendarListResult>(handler,
			CapabilityOperations.Calendar.Calendars,
			new CalendarAccountArguments { AccountId = "bob" });

		var calendar = result.Calendars.Single();
		Assert.Multiple(() =>
		{
			Assert.That(calendar.Id, Is.EqualTo("work"));
			Assert.That(calendar.Color, Is.EqualTo("#336699"));
			Assert.That(calendar.IsPrimary, Is.True);
		});
	}

	[Test]
	public async Task Events_pass_the_query_on_and_answer_with_summaries_in_start_order()
	{
		var provider = new TestCalendarIntegration("Google", "alice");
		provider.Events["alice"] =
		[
			Event("late", _monday.AddHours(15)) with
			{
				Description = "Notes", Participants = [new CalendarParticipant { Name = "Bob" }]
			},
			Event("early", _monday.AddHours(9))
		];
		var handler = new CalendarCapabilityHandler([provider], TestMetadata.Default);

		var result = await InvokeAsync<CalendarEventsResult>(handler,
			CapabilityOperations.Calendar.Events,
			new CalendarEventsArguments
			{
				AccountId = "alice", From = _monday, To = _monday.AddDays(1), CalendarIds = ["primary"]
			});

		Assert.Multiple(() =>
		{
			Assert.That(provider.LastQuery!.From, Is.EqualTo(_monday));
			Assert.That(provider.LastQuery.To, Is.EqualTo(_monday.AddDays(1)));
			Assert.That(provider.LastQuery.CalendarIds, Is.EqualTo(new[] { "primary" }));
			Assert.That(result.Events.Select(e => e.Id), Is.EqualTo(new[] { "early", "late" }));
			Assert.That(result.Truncated, Is.False);
		});
	}

	[Test]
	public async Task An_events_reply_over_the_byte_budget_keeps_the_earliest_events_and_says_it_was_truncated()
	{
		var provider = new TestCalendarIntegration("Google", "alice");
		provider.Events["alice"] =
		[
			.. Enumerable.Range(0, 2000)
				.Reverse()
				.Select(index => Event($"evt-{index:D4}", _monday.AddMinutes(index)) with
				{
					Title = new string('t', 1000), Location = new string('l', 1000)
				})
		];
		var handler = new CalendarCapabilityHandler([provider], TestMetadata.Default);

		var result = await InvokeRawAsync(handler,
			CapabilityOperations.Calendar.Events,
			new CalendarEventsArguments { AccountId = "alice", From = _monday, To = _monday.AddDays(2) });
		var events = result.Deserialize<CalendarEventsResult>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(Encoding.UTF8.GetByteCount(result.GetRawText()), Is.LessThanOrEqualTo(ProtocolLimits.MaxCalendarReplyBytes));
			Assert.That(events.Truncated, Is.True);
			Assert.That(events.Events, Is.Not.Empty);
			Assert.That(events.Events.Select(e => e.Id),
				Is.EqualTo(Enumerable.Range(0, events.Events.Count).Select(index => $"evt-{index:D4}")));
			Assert.That(events.Events[0].Title, Has.Length.EqualTo(ProtocolLimits.MaxCalendarTitleLength));
			Assert.That(events.Events[0].Location, Has.Length.EqualTo(ProtocolLimits.MaxCalendarLocationLength));
		});
	}

	[Test]
	public async Task An_event_reply_carries_the_details_within_the_protocol_caps()
	{
		var provider = new TestCalendarIntegration("Google", "alice");
		provider.Events["alice"] =
		[
			Event("review", _monday.AddHours(10)) with
			{
				Description = new string('d', ProtocolLimits.MaxCalendarDescriptionLength + 500),
				MeetingUrl = "https://meet.example.com/" + new string('x', ProtocolLimits.MaxCalendarMeetingUrlLength),
				Participants =
				[
					new CalendarParticipant
					{
						Name = "Bob", IsOrganizer = true, Response = CalendarResponseStatus.Tentative
					},
					.. Enumerable.Range(0, ProtocolLimits.MaxCalendarParticipants + 20)
						.Select(index => new CalendarParticipant { Email = $"guest{index}@example.com" })
				]
			}
		];
		var handler = new CalendarCapabilityHandler([provider], TestMetadata.Default);

		var result = await InvokeAsync<CalendarEventResult>(handler,
			CapabilityOperations.Calendar.Event,
			new CalendarEventArguments { AccountId = "alice", CalendarId = "primary", EventId = "review" });

		var details = result.Event!;
		Assert.Multiple(() =>
		{
			Assert.That(details.Description, Has.Length.EqualTo(ProtocolLimits.MaxCalendarDescriptionLength));
			Assert.That(details.Participants, Has.Count.EqualTo(ProtocolLimits.MaxCalendarParticipants));
			Assert.That(details.Participants[0].Response, Is.EqualTo("Tentative"));
			Assert.That(details.Participants[0].IsOrganizer, Is.True);
			Assert.That(details.MeetingUrl, Is.Null);
		});
	}

	[Test]
	public async Task An_event_that_no_longer_exists_is_an_empty_answer_rather_than_a_failure()
	{
		var handler = new CalendarCapabilityHandler([new TestCalendarIntegration("Google", "alice")],
			TestMetadata.Default);

		var result = await InvokeAsync<CalendarEventResult>(handler,
			CapabilityOperations.Calendar.Event,
			new CalendarEventArguments { AccountId = "alice", CalendarId = "primary", EventId = "gone" });

		Assert.That(result.Event, Is.Null);
	}

	[Test]
	public async Task An_unknown_account_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		var handler = new CalendarCapabilityHandler([new TestCalendarIntegration("Google", "alice")],
			TestMetadata.Default);

		var calendars = await handler.InvokeAsync(Invocation(CapabilityOperations.Calendar.Calendars,
				new CalendarAccountArguments { AccountId = "mallory" }),
			CancellationToken.None);
		var events = await handler.InvokeAsync(Invocation(CapabilityOperations.Calendar.Events,
				new CalendarEventsArguments { AccountId = "mallory", From = _monday, To = _monday.AddDays(1) }),
			CancellationToken.None);
		var calendarEvent = await handler.InvokeAsync(Invocation(CapabilityOperations.Calendar.Event,
				new CalendarEventArguments { AccountId = "mallory", CalendarId = "primary", EventId = "x" }),
			CancellationToken.None);
		var unknownOperation = await handler.InvokeAsync(Invocation("sync"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(calendars.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(events.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(calendarEvent.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private static CalendarEvent Event(string id, DateTimeOffset start)
		=> new() { Id = id, CalendarId = "primary", Title = id, Start = start, End = start.AddMinutes(30) };

	private static CapabilityInvocation Invocation(string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.Calendar,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static async Task<JsonElement> InvokeRawAsync(
		CalendarCapabilityHandler handler,
		string operation,
		object? arguments = null)
	{
		var result = await handler.InvokeAsync(Invocation(operation, arguments), CancellationToken.None);

		Assert.That(result.IsFailure, Is.False, result.Error?.Message);
		return result.Data!.Value;
	}

	private static async Task<T> InvokeAsync<T>(
		CalendarCapabilityHandler handler,
		string operation,
		object? arguments = null)
		=> (await InvokeRawAsync(handler, operation, arguments)).Deserialize<T>(PluginProtocolJson.Options)!;
}
