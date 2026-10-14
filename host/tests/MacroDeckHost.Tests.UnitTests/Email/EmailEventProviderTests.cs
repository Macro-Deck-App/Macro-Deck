using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using static MacroDeckHost.Tests.UnitTests.Email.EmailTesting;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailEventProviderTests
{
	private static readonly EmailInboxDescriptor _work
		= new("app.imap::work", "app.imap", "work", "IMAP", "Work", "me@example.com");

	private static readonly EmailInboxDescriptor _home
		= new("app.imap::home", "app.imap", "home", "IMAP", "Home", null);

	[Test]
	public void A_received_mail_is_published_with_its_inbox_sender_subject_and_time()
	{
		var bus = new RecordingBus();
		var provider = new EmailEventProvider(bus);

		provider.Publish([new EmailReceived(_work, Message("m1", 5, sender: "Alice", subject: "Invoice"))]);

		var occurrence = bus.Published.Single();
		Assert.Multiple(() =>
		{
			Assert.That(occurrence.EventId, Is.EqualTo("email::email-received"));
			Assert.That(occurrence.Parameters["inbox"], Is.EqualTo("app.imap::work"));
			Assert.That(occurrence.Parameters["inboxName"], Is.EqualTo("Work"));
			Assert.That(occurrence.Parameters["sender"], Is.EqualTo("Alice"));
			Assert.That(occurrence.Parameters["senderAddress"], Is.EqualTo("m1@example.com"));
			Assert.That(occurrence.Parameters["subject"], Is.EqualTo("Invoice"));
			Assert.That(occurrence.Parameters["messageId"], Is.EqualTo("m1"));
			Assert.That(occurrence.Parameters["receivedAt"], Is.EqualTo("2026-01-01T12:05:00.0000000Z"));
		});
	}

	[Test]
	public async Task Inbox_sender_and_subject_filters_select_matching_mail()
	{
		var provider = new EmailEventProvider(new RecordingBus());
		var received
			= provider.EventDefinitions.Single(definition => definition.Id == EmailEventProvider.ReceivedEventId);
		var mail = new EmailReceived(_work, Message("m1", 5, sender: "Alice", subject: "Invoice 42"));

		var results = new[]
		{
			await MatchesAsync(received, mail, "inbox", "app.imap::work", "=="),
			await MatchesAsync(received, mail, "inbox", "app.imap::home", "=="),
			await MatchesAsync(received, mail, "sender", "Alice", "=="),
			await MatchesAsync(received, mail, "sender", "Bob", "=="),
			await MatchesAsync(received, mail, "subject", "Invoice 42", "=="),
			await MatchesAsync(received, mail, "subject", "Receipt", "=="),
			await MatchesAsync(received, mail, "subject", "Receipt", "!=")
		};

		Assert.Multiple(() =>
		{
			foreach (var definition in provider.EventDefinitions)
			{
				Assert.That(definition.PayloadParameters.Select(parameter => parameter.Name),
					Is.SupersetOf(definition.ConfigurationParameters.Select(parameter => parameter.Name)),
					definition.Id);
			}

			Assert.That(results, Is.EqualTo(new[] { true, false, true, false, true, false, true }));
		});
	}

	[Test]
	public void Connection_and_count_changes_carry_their_inbox()
	{
		var bus = new RecordingBus();
		var provider = new EmailEventProvider(bus);

		provider.Publish([
			new EmailUnreadCountChanged(_home, 3, 1),
			new EmailInboxDisconnected(_home),
			new EmailInboxConnected(_home)
		]);

		Assert.Multiple(() =>
		{
			Assert.That(bus.Published.Select(occurrence => occurrence.EventId),
				Is.EqualTo(new[]
					{ "email::unread-count-changed", "email::inbox-disconnected", "email::inbox-connected" }));
			Assert.That(bus.Published.All(occurrence => Equals(occurrence.Parameters["inbox"], "app.imap::home")),
				Is.True);
			Assert.That(bus.Published[0].Parameters["unreadCount"], Is.EqualTo(3d));
			Assert.That(bus.Published[0].Parameters["previousUnreadCount"], Is.EqualTo(1d));
		});
	}

	private static async Task<bool> MatchesAsync(
		MacroDeck.Sdk.Events.EventDefinition definition,
		EmailInboxChange change,
		string parameter,
		string value,
		string @operator)
	{
		var eventId = EmailEventProvider.Qualify(definition.Id);
		var descriptor = new EventDefinitionDescriptor(QualifiedId.Parse(eventId),
			EmailEventProvider.ProviderIdValue,
			"Email",
			false,
			definition);
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			eventId,
			new Dictionary<string, EventConfigurationValue>(StringComparer.Ordinal)
			{
				[parameter] = new(JsonSerializer.SerializeToElement(value), @operator)
			},
			null);
		var context = await new VariableTemplateRenderer(new VariableRegistry())
			.CreateContextAsync(VariableScope.Global, null);

		return new EventSubscriptionMatcher(
				new ActionConditionEvaluator(new VariableTemplateRenderer(new VariableRegistry())))
			.Matches(subscription, descriptor, context.WithEvent(EmailEventProvider.Occurrence(change).Payload));
	}

	private sealed class RecordingBus : IEventBus
	{
		private readonly Channel<EventOccurrence> _channel = Channel.CreateUnbounded<EventOccurrence>();

		public List<EventOccurrence> Published { get; } = [];

		public ChannelReader<EventOccurrence> Reader => _channel.Reader;

		public void Publish(EventOccurrence occurrence)
		{
			Published.Add(occurrence);
		}
	}
}
