using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Protocol.Capabilities.Calendar;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class CalendarDtoSerializationTests
{
	[Test]
	public void Describe_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new CalendarDescribePayload
		{
			ProviderName = "Google Calendar",
			Accounts = [new CalendarAccountDto { Id = "account-1", DisplayName = "alice@example.com" }]
		};

		var actual = RoundTrip(payload);

		Assert.Multiple(() =>
		{
			Assert.That(actual.ProviderName, Is.EqualTo("Google Calendar"));
			Assert.That(actual.Accounts.Single().Id, Is.EqualTo("account-1"));
			Assert.That(actual.Accounts.Single().DisplayName, Is.EqualTo("alice@example.com"));
		});
	}

	[Test]
	public void An_events_reply_keeps_each_events_offset_and_its_truncation_flag()
	{
		var start = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.FromHours(2));
		var result = new CalendarEventsResult
		{
			Events =
			[
				new CalendarEventSummaryDto
				{
					Id = "evt-1_20261005T073000Z",
					CalendarId = "primary",
					Title = "Stand-up",
					Start = start,
					End = start.AddMinutes(15),
					Location = "Room 4",
					MeetingUrl = "https://meet.example.com/abc"
				}
			],
			Truncated = true
		};

		var actual = RoundTrip(result);
		var calendarEvent = actual.Events.Single();

		Assert.Multiple(() =>
		{
			Assert.That(actual.Truncated, Is.True);
			Assert.That(calendarEvent.Start, Is.EqualTo(start));
			Assert.That(calendarEvent.Start.Offset, Is.EqualTo(TimeSpan.FromHours(2)));
			Assert.That(calendarEvent.MeetingUrl, Is.EqualTo("https://meet.example.com/abc"));
		});
	}

	[Test]
	public void An_event_reply_carries_the_response_status_as_its_name()
	{
		var result = new CalendarEventResult
		{
			Event = new CalendarEventDto
			{
				Id = "evt-1",
				CalendarId = "primary",
				Title = "Review",
				Start = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
				End = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero),
				IsAllDay = true,
				Description = "<b>Agenda</b>",
				Participants =
				[
					new CalendarParticipantDto
					{
						Name = "Bob", Email = "bob@example.com", IsOrganizer = true, Response = "Accepted"
					}
				]
			}
		};

		var json = JsonSerializer.SerializeToNode(result, PluginProtocolJson.Options)!;
		var actual = RoundTrip(result);

		Assert.Multiple(() =>
		{
			Assert.That(json["event"]!["participants"]![0]!["response"]!.GetValue<string>(), Is.EqualTo("Accepted"));
			Assert.That(actual.Event!.IsAllDay, Is.True);
			Assert.That(actual.Event.Description, Is.EqualTo("<b>Agenda</b>"));
			Assert.That(actual.Event.Participants.Single().IsOrganizer, Is.True);
		});
	}

	[Test]
	public void An_event_reply_for_a_deleted_event_carries_no_event()
		=> Assert.That(RoundTrip(new CalendarEventResult()).Event, Is.Null);

	[Test]
	public void A_participant_without_a_response_reads_as_unknown()
	{
		var actual = JsonSerializer.Deserialize<CalendarParticipantDto>("""{"name":"Carol"}""",
			PluginProtocolJson.Options);

		Assert.That(actual!.Response, Is.EqualTo("Unknown"));
	}

	[Test]
	public void No_calendar_dto_exposes_an_enum_typed_property()
	{
		var types = typeof(CalendarEventDto).Assembly.GetTypes()
			.Where(type => type.IsPublic && type.Namespace == typeof(CalendarEventDto).Namespace);

		Assert.Multiple(() =>
		{
			foreach (var type in types)
			{
				foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				{
					Assert.That(property.PropertyType.IsEnum,
						Is.False,
						$"{type.FullName}.{property.Name} is an enum, which would serialize as an undocumented integer.");
				}
			}
		});
	}

	private static T RoundTrip<T>(T value)
	{
		var node = JsonSerializer.SerializeToNode(value, PluginProtocolJson.Options)!.AsObject();
		node["unknownField"] = JsonValue.Create("ignored");

		return JsonSerializer.Deserialize<T>(node.ToJsonString(), PluginProtocolJson.Options)!;
	}
}
