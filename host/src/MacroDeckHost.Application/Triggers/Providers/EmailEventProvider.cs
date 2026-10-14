using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Triggers.Providers;

public sealed class EmailEventProvider : IHostEventProvider, IEmailEventPublisher
{
	public const string ProviderIdValue = "email";

	public const string ReceivedEventId = "email-received";
	public const string UnreadCountChangedEventId = "unread-count-changed";
	public const string ReadStateChangedEventId = "read-state-changed";
	public const string InboxConnectedEventId = "inbox-connected";
	public const string InboxDisconnectedEventId = "inbox-disconnected";

	public const string InboxParameter = "inbox";
	public const string InboxNameParameter = "inboxName";
	public const string SenderParameter = "sender";
	public const string SenderAddressParameter = "senderAddress";
	public const string SubjectParameter = "subject";
	public const string PreviewParameter = "preview";
	public const string MessageIdParameter = "messageId";
	public const string ReceivedAtParameter = "receivedAt";
	public const string UnreadCountParameter = "unreadCount";
	public const string PreviousUnreadCountParameter = "previousUnreadCount";
	public const string IsReadParameter = "isRead";

	private readonly IEventBus _bus;

	public EmailEventProvider(IEventBus bus)
	{
		_bus = bus;
	}

	public string ProviderId => ProviderIdValue;

	public LocalizedText ProviderName => AppStrings.Events.Email.ProviderName();

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new()
		{
			Id = ReceivedEventId,
			Name = AppStrings.Events.Email.ReceivedName(),
			Description = AppStrings.Events.Email.ReceivedDescription(),
			Category = AppStrings.Events.Email.Category(),
			ConfigurationParameters =
			[
				InboxFilter(),
				ActionParameter.Text(SenderParameter,
					AppStrings.Events.Email.SenderLabel(),
					AppStrings.Events.Email.SenderFilterDescription()),
				ActionParameter.Text(SubjectParameter,
					AppStrings.Events.Email.SubjectLabel(),
					AppStrings.Events.Email.SubjectFilterDescription())
			],
			PayloadParameters =
			[
				Inbox(),
				InboxName(),
				ActionParameter.Text(SenderParameter, AppStrings.Events.Email.SenderLabel()),
				ActionParameter.Text(SenderAddressParameter, AppStrings.Events.Email.SenderAddressLabel()),
				ActionParameter.Text(SubjectParameter, AppStrings.Events.Email.SubjectLabel()),
				ActionParameter.Text(PreviewParameter, AppStrings.Events.Email.PreviewLabel()),
				ActionParameter.Text(MessageIdParameter, AppStrings.Events.Email.MessageIdLabel()),
				ActionParameter.Text(ReceivedAtParameter, AppStrings.Events.Email.ReceivedAtLabel())
			]
		},
		new()
		{
			Id = UnreadCountChangedEventId,
			Name = AppStrings.Events.Email.UnreadCountChangedName(),
			Description = AppStrings.Events.Email.UnreadCountChangedDescription(),
			Category = AppStrings.Events.Email.Category(),
			ConfigurationParameters = [InboxFilter()],
			PayloadParameters =
			[
				Inbox(),
				InboxName(),
				ActionParameter.Number(UnreadCountParameter, AppStrings.Events.Email.UnreadCountLabel()),
				ActionParameter.Number(PreviousUnreadCountParameter,
					AppStrings.Events.Email.PreviousUnreadCountLabel())
			]
		},
		new()
		{
			Id = ReadStateChangedEventId,
			Name = AppStrings.Events.Email.ReadStateChangedName(),
			Description = AppStrings.Events.Email.ReadStateChangedDescription(),
			Category = AppStrings.Events.Email.Category(),
			ConfigurationParameters = [InboxFilter()],
			PayloadParameters =
			[
				Inbox(),
				InboxName(),
				ActionParameter.Text(MessageIdParameter, AppStrings.Events.Email.MessageIdLabel()),
				ActionParameter.Text(SubjectParameter, AppStrings.Events.Email.SubjectLabel()),
				ActionParameter.Text(SenderParameter, AppStrings.Events.Email.SenderLabel()),
				ActionParameter.Toggle(IsReadParameter, AppStrings.Events.Email.IsReadLabel())
			]
		},
		Connection(InboxConnectedEventId,
			AppStrings.Events.Email.InboxConnectedName(),
			AppStrings.Events.Email.InboxConnectedDescription()),
		Connection(InboxDisconnectedEventId,
			AppStrings.Events.Email.InboxDisconnectedName(),
			AppStrings.Events.Email.InboxDisconnectedDescription())
	];

	public static string Qualify(string eventId)
	{
		return QualifiedId.Create(ProviderIdValue, eventId, OwnerIdKind.HostProvider, LocalIdKind.Declared, "Event")
			.ToString();
	}

	public void Publish(IReadOnlyList<EmailInboxChange> changes)
	{
		foreach (var change in changes)
		{
			var (eventId, values) = Occurrence(change);
			_bus.Publish(new EventOccurrence(Qualify(eventId), values));
		}
	}

	public static (string EventId, IReadOnlyDictionary<string, object?> Payload) Occurrence(EmailInboxChange change)
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[InboxParameter] = change.Inbox.Ref,
			[InboxNameParameter] = change.Inbox.DisplayName
		};

		switch (change)
		{
			case EmailReceived received:
				values[SenderParameter] = SenderOf(received.Message);
				values[SenderAddressParameter] = received.Message.SenderAddress ?? string.Empty;
				values[SubjectParameter] = received.Message.Subject;
				values[PreviewParameter] = received.Message.Preview ?? string.Empty;
				values[MessageIdParameter] = received.Message.Id;
				values[ReceivedAtParameter]
					= received.Message.ReceivedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
				return (ReceivedEventId, values);

			case EmailUnreadCountChanged unread:
				values[UnreadCountParameter] = (double)unread.UnreadCount;
				values[PreviousUnreadCountParameter] = (double)unread.PreviousUnreadCount;
				return (UnreadCountChangedEventId, values);

			case EmailReadStateChanged readState:
				values[MessageIdParameter] = readState.Message.Id;
				values[SubjectParameter] = readState.Message.Subject;
				values[SenderParameter] = SenderOf(readState.Message);
				values[IsReadParameter] = readState.Message.IsRead;
				return (ReadStateChangedEventId, values);

			case EmailInboxConnected:
				return (InboxConnectedEventId, values);

			case EmailInboxDisconnected:
				return (InboxDisconnectedEventId, values);

			default:
				throw new ArgumentOutOfRangeException(nameof(change), change.GetType().Name, null);
		}
	}

	private static string SenderOf(MacroDeck.Sdk.Email.EmailMessage message)
	{
		return message.SenderName ?? message.SenderAddress ?? string.Empty;
	}

	private static EventDefinition Connection(string id, LocalizedText name, LocalizedText description)
	{
		return new EventDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = AppStrings.Events.Email.Category(),
			ConfigurationParameters = [InboxFilter()],
			PayloadParameters = [Inbox(), InboxName()]
		};
	}

	private static ActionParameter Inbox()
	{
		return ActionParameter.DynamicChoice(InboxParameter,
			AppStrings.Events.Email.InboxLabel(),
			optionsSourceId: EmailOptionsSourceIds.Inboxes);
	}

	private static ActionParameter InboxName()
	{
		return ActionParameter.Text(InboxNameParameter, AppStrings.Events.Email.InboxNameLabel());
	}

	private static ActionParameter InboxFilter()
	{
		return ActionParameter.DynamicChoice(InboxParameter,
			AppStrings.Events.Email.InboxLabel(),
			AppStrings.Events.Email.InboxFilterDescription(),
			EmailOptionsSourceIds.Inboxes,
			AppStrings.Events.Email.AnyInboxPlaceholder());
	}
}
