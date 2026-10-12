using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class EmailInboxDtoSerializationTests
{
	[Test]
	public void Describe_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new EmailInboxDescribePayload
		{
			ProviderName = "IMAP",
			Inboxes = [new EmailInboxDto { Id = "i0123", DisplayName = "Work", Address = "alice@example.com" }]
		};

		var actual = RoundTrip(payload);

		Assert.Multiple(() =>
		{
			Assert.That(actual.ProviderName, Is.EqualTo("IMAP"));
			Assert.That(actual.Inboxes.Single(), Is.EqualTo(payload.Inboxes[0]));
		});
	}

	[Test]
	public void Snapshot_arguments_default_to_ten_messages_of_any_read_state()
	{
		var actual = JsonSerializer.Deserialize<EmailInboxSnapshotArguments>("""{"inboxId":"i0123"}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(actual.InboxId, Is.EqualTo("i0123"));
			Assert.That(actual.MaxMessages, Is.EqualTo(10));
			Assert.That(actual.UnreadOnly, Is.False);
		});
	}

	[Test]
	public void A_snapshot_reply_carries_its_status_as_a_name_and_keeps_each_messages_offset()
	{
		var receivedAt = new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.FromHours(2));
		var result = new EmailInboxSnapshotResult
		{
			Status = "Error",
			StatusMessage = "Sign-in failed.",
			UnreadCount = 3,
			TotalCount = 120,
			Messages =
			[
				new EmailMessageDto
				{
					Id = "42-1001",
					SenderName = "Bob",
					SenderAddress = "bob@example.com",
					Subject = "Lunch?",
					Preview = "Are you free at noon",
					ReceivedAt = receivedAt
				}
			],
			SyncedAt = receivedAt.AddMinutes(1),
			Truncated = true
		};

		var json = JsonSerializer.SerializeToNode(result, PluginProtocolJson.Options)!;
		var actual = RoundTrip(result);

		Assert.Multiple(() =>
		{
			Assert.That(json["status"]!.GetValue<string>(), Is.EqualTo("Error"));
			Assert.That(actual.StatusMessage, Is.EqualTo("Sign-in failed."));
			Assert.That(actual.UnreadCount, Is.EqualTo(3));
			Assert.That(actual.TotalCount, Is.EqualTo(120));
			Assert.That(actual.Truncated, Is.True);
			Assert.That(actual.Messages.Single(), Is.EqualTo(result.Messages[0]));
			Assert.That(actual.Messages.Single().ReceivedAt.Offset, Is.EqualTo(TimeSpan.FromHours(2)));
		});
	}

	[Test]
	public void No_email_inbox_dto_exposes_an_enum_typed_property()
	{
		var types = typeof(EmailInboxSnapshotResult).Assembly.GetTypes()
			.Where(type => type.IsPublic && type.Namespace == typeof(EmailInboxSnapshotResult).Namespace);

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
