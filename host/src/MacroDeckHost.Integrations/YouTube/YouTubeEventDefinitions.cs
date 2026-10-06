using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.YouTube.Events;

namespace MacroDeckHost.Integrations.YouTube;

internal static class YouTubeEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		Event(YouTubeEventIds.StreamOnline,
			Strings.StreamOnlineName(),
			Strings.StreamOnlineDescription(),
			Strings.StreamCategory(),
			payload:
			[
				Text("title", Strings.Title()),
				Text("startedAt", Strings.StartedAt()),
				Text("broadcastId", Strings.BroadcastId())
			]),

		Event(YouTubeEventIds.StreamOffline,
			Strings.StreamOfflineName(),
			Strings.StreamOfflineDescription(),
			Strings.StreamCategory()),

		Event(YouTubeEventIds.SuperChat,
			Strings.SuperChatName(),
			Strings.SuperChatDescription(),
			Strings.SuperChatCategory(),
			payload:
			[
				Text("messageId", Strings.MessageId()),
				.. Author(),
				Text("amount", Strings.Amount()),
				Number("amountMicros", Strings.AmountMicros()),
				Text("currency", Strings.Currency()),
				Number("tier", Strings.Tier()),
				Text("comment", Strings.Comment())
			]),

		Event(YouTubeEventIds.SuperSticker,
			Strings.SuperStickerName(),
			Strings.SuperStickerDescription(),
			Strings.SuperChatCategory(),
			payload:
			[
				Text("messageId", Strings.MessageId()),
				.. Author(),
				Text("amount", Strings.Amount()),
				Number("amountMicros", Strings.AmountMicros()),
				Text("currency", Strings.Currency()),
				Number("tier", Strings.Tier()),
				Text("sticker", Strings.Sticker())
			]),

		Event(YouTubeEventIds.NewMember,
			Strings.NewMemberName(),
			Strings.NewMemberDescription(),
			Strings.MembershipsCategory(),
			payload:
			[
				.. Author(),
				Text("levelName", Strings.LevelName()),
				Bool("isUpgrade", Strings.IsUpgrade())
			]),

		Event(YouTubeEventIds.MemberMilestone,
			Strings.MemberMilestoneName(),
			Strings.MemberMilestoneDescription(),
			Strings.MembershipsCategory(),
			payload:
			[
				.. Author(),
				Text("levelName", Strings.LevelName()),
				Number("months", Strings.Months()),
				Text("comment", Strings.Comment())
			]),

		Event(YouTubeEventIds.MembershipGift,
			Strings.MembershipGiftName(),
			Strings.MembershipGiftDescription(),
			Strings.MembershipsCategory(),
			payload:
			[
				.. Author(),
				Text("levelName", Strings.LevelName()),
				Number("giftCount", Strings.GiftCount())
			]),

		Event(YouTubeEventIds.Any,
			Strings.AnyName(),
			Strings.AnyDescription(),
			Strings.AdvancedCategory(),
			configuration:
			[
				ActionParameter.DynamicChoice("type",
					label: Strings.EventType(),
					description: Strings.EventTypeDescription(),
					placeholder: Strings.AnyEventPlaceholder())
			],
			payload:
			[
				Selectable("type", Strings.EventType()),
				.. Author(),
				Text("message", Strings.Message())
			])
	];

	public static IReadOnlyDictionary<string, IReadOnlyList<string>> PayloadNames { get; } =
		All.ToDictionary(definition => definition.Id,
			IReadOnlyList<string> (definition) => [.. definition.PayloadParameters.Select(parameter => parameter.Name)],
			StringComparer.Ordinal);

	private static EventDefinition Event(
		string id,
		LocalizedText name,
		LocalizedText description,
		LocalizedText category,
		IReadOnlyList<ActionParameter>? configuration = null,
		IReadOnlyList<ActionParameter>? payload = null)
		=> new()
		{
			Id = id,
			Name = name,
			Description = description,
			Category = category,
			ConfigurationParameters = [Account(), .. configuration ?? []],
			PayloadParameters = [.. AccountPayload(), .. payload ?? []]
		};

	private static ActionParameter Account()
		=> ActionParameter.DynamicChoice("account",
			label: Strings.Channel(),
			description: Strings.ChannelDescription(),
			placeholder: Strings.FirstChannelPlaceholder());

	private static ActionParameter[] AccountPayload()
		=> [Selectable("account", Strings.ChannelId()), Text("accountName", Strings.Channel())];

	private static ActionParameter[] Author()
		=> [Text("authorChannelId", Strings.AuthorChannelId()), Text("authorName", Strings.AuthorName())];

	private static ActionParameter Text(string name, LocalizedText label) => ActionParameter.Text(name, label: label);

	// Payload values the integration can list: the same options its matching filter offers, so a
	// condition on one is authored by picking a name while the stored value stays the raw id.
	private static ActionParameter Selectable(string name, LocalizedText label)
		=> ActionParameter.DynamicChoice(name, label: label);

	private static ActionParameter Number(string name, LocalizedText label)
		=> ActionParameter.Number(name, label: label);

	private static ActionParameter Bool(string name, LocalizedText label) => ActionParameter.Toggle(name, label: label);
}
