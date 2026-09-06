using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Twitch;

internal static class TwitchEventDefinitions
{
	private static readonly LocalizedText StreamCategory = AppStrings.Integrations.Twitch.Events.StreamCategory();
	private static readonly LocalizedText CommunityCategory = AppStrings.Integrations.Twitch.Events.CommunityCategory();

	private static readonly LocalizedText SubscriptionCategory
		= AppStrings.Integrations.Twitch.Events.SubscriptionsCategory();

	private static readonly LocalizedText BitsCategory = AppStrings.Integrations.Twitch.Events.BitsCategory();

	private static readonly LocalizedText ChannelPointsCategory
		= AppStrings.Integrations.Twitch.Events.ChannelPointsCategory();

	private static readonly LocalizedText InteractiveCategory
		= AppStrings.Integrations.Twitch.Events.PollsAndPredictionsCategory();

	private static readonly LocalizedText HypeTrainCategory = AppStrings.Integrations.Twitch.Events.HypeTrainCategory();
	private static readonly LocalizedText GoalsCategory = AppStrings.Integrations.Twitch.Events.GoalsCategory();
	private static readonly LocalizedText ChatCategory = AppStrings.Integrations.Twitch.Events.ChatCategory();

	private static readonly LocalizedText ModerationCategory
		= AppStrings.Integrations.Twitch.Events.ModerationCategory();

	private static readonly LocalizedText ConnectionCategory
		= AppStrings.Integrations.Twitch.Events.ConnectionCategory();

	private static readonly LocalizedText AdvancedCategory = AppStrings.Integrations.Twitch.Events.AdvancedCategory();

	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		Event(TwitchEventIds.StreamOnline,
			Fields.StreamOnlineName(),
			Fields.StreamOnlineDescription(),
			StreamCategory,
			payload: [Text("streamType", Fields.Type()), Text("startedAt", Fields.StartedAt())]),

		Event(TwitchEventIds.StreamOffline,
			Fields.StreamOfflineName(),
			Fields.StreamOfflineDescription(),
			StreamCategory),

		Event(TwitchEventIds.ChannelUpdate,
			Fields.ChannelUpdateName(),
			Fields.ChannelUpdateDescription(),
			StreamCategory,
			payload:
			[
				Text("title", Fields.Title()),
				Text("categoryId", Fields.CategoryId()),
				Text("categoryName", Fields.Category()),
				Text("language", Fields.Language()),
				Text("contentLabels", Fields.ContentLabels())
			]),

		Event(TwitchEventIds.AdBreakBegin,
			Fields.AdBreakBeginName(),
			Fields.AdBreakBeginDescription(),
			StreamCategory,
			payload:
			[
				Number("durationSeconds", Fields.DurationSeconds()),
				Bool("isAutomatic", Fields.Automatic()),
				Text("startedAt", Fields.StartedAt()),
				Text("requesterName", Fields.StartedBy())
			]),

		Event(TwitchEventIds.Follow,
			Fields.FollowName(),
			Fields.FollowDescription(),
			CommunityCategory,
			payload: [.. User(), Text("followedAt", Fields.FollowedAt())]),

		Event(TwitchEventIds.RaidIncoming,
			Fields.RaidIncomingName(),
			Fields.RaidIncomingDescription(),
			CommunityCategory,
			payload:
			[
				Text("fromUserId", Fields.FromUserId()),
				Text("fromUserLogin", Fields.FromLogin()),
				Text("fromUserName", Fields.FromName()),
				Number("viewers", Fields.Viewers())
			]),

		Event(TwitchEventIds.RaidOutgoing,
			Fields.RaidOutgoingName(),
			Fields.RaidOutgoingDescription(),
			CommunityCategory,
			payload:
			[
				Text("toUserId", Fields.ToUserId()),
				Text("toUserLogin", Fields.ToLogin()),
				Text("toUserName", Fields.ToName()),
				Number("viewers", Fields.Viewers())
			]),

		Event(TwitchEventIds.ShoutoutCreated,
			Fields.ShoutoutCreatedName(),
			Fields.ShoutoutCreatedDescription(),
			CommunityCategory,
			payload:
			[
				Text("toUserLogin", Fields.ToLogin()),
				Text("toUserName", Fields.ToName()),
				Number("viewerCount", Fields.Viewers())
			]),

		Event(TwitchEventIds.ShoutoutReceived,
			Fields.ShoutoutReceivedName(),
			Fields.ShoutoutReceivedDescription(),
			CommunityCategory,
			payload:
			[
				Text("fromUserLogin", Fields.FromLogin()),
				Text("fromUserName", Fields.FromName()),
				Number("viewerCount", Fields.Viewers())
			]),

		Event(TwitchEventIds.VipAdded,
			Fields.VipAddedName(),
			Fields.VipAddedDescription(),
			CommunityCategory,
			payload: [.. User()]),

		Event(TwitchEventIds.VipRemoved,
			Fields.VipRemovedName(),
			Fields.VipRemovedDescription(),
			CommunityCategory,
			payload: [.. User()]),

		Event(TwitchEventIds.Subscribe,
			Fields.SubscribeName(),
			Fields.SubscribeDescription(),
			SubscriptionCategory,
			configuration: [Tier()],
			payload: [.. User(), Text("tier", Fields.Tier()), Bool("isGift", Fields.Gifted())]),

		Event(TwitchEventIds.SubscriptionMessage,
			Fields.SubscriptionMessageName(),
			Fields.SubscriptionMessageDescription(),
			SubscriptionCategory,
			configuration: [Tier()],
			payload:
			[
				.. User(),
				Text("tier", Fields.Tier()),
				Number("cumulativeMonths", Fields.MonthsTotal()),
				Number("streakMonths", Fields.MonthStreak()),
				Number("durationMonths", Fields.MonthsBought()),
				Text("message", Fields.Message())
			]),

		Event(TwitchEventIds.SubscriptionGift,
			Fields.SubscriptionGiftName(),
			Fields.SubscriptionGiftDescription(),
			SubscriptionCategory,
			configuration: [Tier()],
			payload:
			[
				.. User(),
				Text("tier", Fields.Tier()),
				Number("total", Fields.GiftedNow()),
				Number("cumulativeTotal", Fields.GiftedInTotal()),
				Bool("isAnonymous", Fields.Anonymous())
			]),

		Event(TwitchEventIds.SubscriptionEnd,
			Fields.SubscriptionEndName(),
			Fields.SubscriptionEndDescription(),
			SubscriptionCategory,
			configuration: [Tier()],
			payload: [.. User(), Text("tier", Fields.Tier()), Bool("isGift", Fields.WasGifted())]),

		Event(TwitchEventIds.Cheer,
			Fields.CheerName(),
			Fields.CheerDescription(),
			BitsCategory,
			payload:
			[
				.. User(),
				Number("bits", Fields.Bits()),
				Text("message", Fields.Message()),
				Bool("isAnonymous", Fields.Anonymous())
			]),

		Event(TwitchEventIds.RewardRedeemed,
			Fields.RewardRedeemedName(),
			Fields.RewardRedeemedDescription(),
			ChannelPointsCategory,
			configuration: [RewardFilter()],
			payload:
			[
				.. User(),
				Text("rewardId", Fields.RewardId()),
				Selectable("rewardTitle", Fields.Reward()),
				Number("rewardCost", Fields.Cost()),
				Text("redemptionId", Fields.RedemptionId()),
				Text("userInput", Fields.ViewerInput()),
				Text("status", Fields.Status())
			]),

		Event(TwitchEventIds.RewardRedemptionUpdated,
			Fields.RewardRedemptionUpdatedName(),
			Fields.RewardRedemptionUpdatedDescription(),
			ChannelPointsCategory,
			configuration: [RewardFilter()],
			payload:
			[
				.. User(),
				Text("rewardId", Fields.RewardId()),
				Selectable("rewardTitle", Fields.Reward()),
				Text("redemptionId", Fields.RedemptionId()),
				Text("userInput", Fields.ViewerInput()),
				Text("status", Fields.Status())
			]),

		Event(TwitchEventIds.AutomaticRewardRedeemed,
			Fields.AutomaticRewardRedeemedName(),
			Fields.AutomaticRewardRedeemedDescription(),
			ChannelPointsCategory,
			payload: [.. User(), Text("rewardType", Fields.Reward()), Text("userInput", Fields.ViewerInput())]),

		Event(TwitchEventIds.PollBegin,
			Fields.PollBeginName(),
			Fields.PollBeginDescription(),
			InteractiveCategory,
			payload:
			[
				Text("pollId", Fields.PollId()),
				Text("title", Fields.Title()),
				Text("choices", Fields.Choices()),
				Text("endsAt", Fields.EndsAt())
			]),

		Event(TwitchEventIds.PollEnd,
			Fields.PollEndName(),
			Fields.PollEndDescription(),
			InteractiveCategory,
			payload:
			[
				Text("pollId", Fields.PollId()),
				Text("title", Fields.Title()),
				Text("status", Fields.Status()),
				Text("winningChoice", Fields.WinningChoice()),
				Number("winningVotes", Fields.WinningVotes())
			]),

		Event(TwitchEventIds.PredictionBegin,
			Fields.PredictionBeginName(),
			Fields.PredictionBeginDescription(),
			InteractiveCategory,
			payload:
			[
				Text("predictionId", Fields.PredictionId()),
				Text("title", Fields.Title()),
				Text("outcomes", Fields.Outcomes()),
				Text("locksAt", Fields.LocksAt())
			]),

		Event(TwitchEventIds.PredictionLock,
			Fields.PredictionLockName(),
			Fields.PredictionLockDescription(),
			InteractiveCategory,
			payload: [Text("predictionId", Fields.PredictionId()), Text("title", Fields.Title())]),

		Event(TwitchEventIds.PredictionEnd,
			Fields.PredictionEndName(),
			Fields.PredictionEndDescription(),
			InteractiveCategory,
			payload:
			[
				Text("predictionId", Fields.PredictionId()),
				Text("title", Fields.Title()),
				Text("status", Fields.Status()),
				Text("winningOutcome", Fields.WinningOutcome())
			]),

		Event(TwitchEventIds.HypeTrainBegin,
			Fields.HypeTrainBeginName(),
			Fields.HypeTrainBeginDescription(),
			HypeTrainCategory,
			payload:
			[
				Number("level", Fields.Level()),
				Number("total", Fields.Total()),
				Number("goal", Fields.Goal()),
				Text("startedAt", Fields.StartedAt()),
				Text("expiresAt", Fields.ExpiresAt())
			]),

		Event(TwitchEventIds.HypeTrainProgress,
			Fields.HypeTrainProgressName(),
			Fields.HypeTrainProgressDescription(),
			HypeTrainCategory,
			payload:
			[
				Number("level", Fields.Level()),
				Number("total", Fields.Total()),
				Number("progress", Fields.Progress()),
				Number("goal", Fields.Goal())
			]),

		Event(TwitchEventIds.HypeTrainEnd,
			Fields.HypeTrainEndName(),
			Fields.HypeTrainEndDescription(),
			HypeTrainCategory,
			payload:
			[
				Number("level", Fields.Level()), Number("total", Fields.Total()), Text("endedAt", Fields.EndedAt())
			]),

		Event(TwitchEventIds.GoalBegin,
			Fields.GoalBeginName(),
			Fields.GoalBeginDescription(),
			GoalsCategory,
			payload: [.. Goal()]),
		Event(TwitchEventIds.GoalProgress,
			Fields.GoalProgressName(),
			Fields.GoalProgressDescription(),
			GoalsCategory,
			payload: [.. Goal()]),
		Event(TwitchEventIds.GoalEnd,
			Fields.GoalEndName(),
			Fields.GoalEndDescription(),
			GoalsCategory,
			payload: [.. Goal(), Bool("isAchieved", Fields.Achieved())]),

		Event(TwitchEventIds.ChatNotification,
			Fields.ChatNotificationName(),
			Fields.ChatNotificationDescription(),
			ChatCategory,
			configuration: [NoticeType()],
			payload:
			[
				.. User(),
				Text("noticeType", Fields.Notice()),
				Text("message", Fields.Message()),
				Text("systemMessage", Fields.SystemMessage())
			]),

		Event(TwitchEventIds.ChatCleared,
			Fields.ChatClearedName(),
			Fields.ChatClearedDescription(),
			ChatCategory),

		Event(TwitchEventIds.ChatMessageDeleted,
			Fields.ChatMessageDeletedName(),
			Fields.ChatMessageDeletedDescription(),
			ChatCategory,
			payload: [.. User(), Text("messageId", Fields.MessageId())]),

		Event(TwitchEventIds.ChatSettingsUpdated,
			Fields.ChatSettingsUpdatedName(),
			Fields.ChatSettingsUpdatedDescription(),
			ChatCategory,
			payload:
			[
				Bool("emoteMode", Fields.EmoteOnly()),
				Bool("followerMode", Fields.FollowersOnly()),
				Number("followerModeDurationMinutes", Fields.FollowersOnlyMinutes()),
				Bool("slowMode", Fields.SlowMode()),
				Number("slowModeWaitTimeSeconds", Fields.SlowModeSeconds()),
				Bool("subscriberMode", Fields.SubscribersOnly()),
				Bool("uniqueChatMode", Fields.UniqueChat())
			]),

		Event(TwitchEventIds.Ban,
			Fields.BanName(),
			Fields.BanDescription(),
			ModerationCategory,
			payload:
			[
				.. User(),
				Text("moderatorLogin", Fields.ModeratorLogin()),
				Text("moderatorName", Fields.Moderator()),
				Text("reason", Fields.Reason()),
				Bool("isPermanent", Fields.Permanent()),
				Text("endsAt", Fields.EndsAt())
			]),

		Event(TwitchEventIds.Unban,
			Fields.UnbanName(),
			Fields.UnbanDescription(),
			ModerationCategory,
			payload:
			[
				.. User(), Text("moderatorLogin", Fields.ModeratorLogin()), Text("moderatorName", Fields.Moderator())
			]),

		Event(TwitchEventIds.Connected,
			MacroDeckStrings.Connection.Connected(),
			Fields.ConnectedDescription(),
			ConnectionCategory),

		Event(TwitchEventIds.Disconnected,
			MacroDeckStrings.Connection.Disconnected(),
			Fields.DisconnectedDescription(),
			ConnectionCategory),

		Event(TwitchEventIds.Any,
			Fields.AnyName(),
			Fields.AnyDescription(),
			AdvancedCategory,
			configuration:
			[
				ActionParameter.DynamicChoice("type",
					label: Fields.EventType(),
					description: Fields.EventTypeDescription(),
					placeholder: Fields.AnyEventPlaceholder())
			],
			payload:
			[
				Selectable("type", Fields.EventType()),
				Text("version", Fields.Version()),
				Text("messageId", Fields.MessageId()),
				Text("userLogin", Fields.UserLogin()),
				Text("userName", Fields.User()),
				Text("message", Fields.Message()),
				Text("data", Fields.RawData())
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
			label: Fields.Account(),
			description: Fields.AccountDescription(),
			placeholder: Fields.FirstAccountPlaceholder());

	private static ActionParameter[] AccountPayload()
		=>
		[
			Selectable("account", Fields.AccountId()),
			Text("accountLogin", Fields.AccountLogin()),
			Text("accountName", Fields.Account())
		];

	private static ActionParameter[] User()
		=> [Text("userId", Fields.UserId()), Text("userLogin", Fields.UserLogin()), Text("userName", Fields.User())];

	private static ActionParameter[] Goal()
		=>
		[
			Text("goalId", Fields.GoalId()),
			Text("goalType", Fields.Type()),
			Text("description", Fields.Description()),
			Number("currentAmount", Fields.Current()),
			Number("targetAmount", Fields.Target())
		];

	private static ActionParameter Tier()
		=> ActionParameter.Choice("tier",
			[
				new ActionParameterOption { Value = "1000", Label = Fields.TierOption1() },
				new ActionParameterOption { Value = "2000", Label = Fields.TierOption2() },
				new ActionParameterOption { Value = "3000", Label = Fields.TierOption3() },
				new ActionParameterOption { Value = "Prime", Label = Fields.TierOptionPrime() }
			],
			label: Fields.Tier(),
			description: Fields.TierDescription());

	private static ActionParameter RewardFilter()
		=> ActionParameter.DynamicChoice("rewardTitle",
			label: Fields.Reward(),
			description: Fields.RewardDescription(),
			placeholder: Fields.AnyRewardPlaceholder());

	private static ActionParameter NoticeType()
		=> ActionParameter.Choice("noticeType",
			[
				new ActionParameterOption { Value = "sub", Label = Fields.OptionSubscription() },
				new ActionParameterOption { Value = "resub", Label = Fields.OptionResubscription() },
				new ActionParameterOption { Value = "sub_gift", Label = Fields.OptionGiftedSubscription() },
				new ActionParameterOption { Value = "community_sub_gift", Label = Fields.OptionCommunityGift() },
				new ActionParameterOption { Value = "gift_paid_upgrade", Label = Fields.OptionGiftUpgrade() },
				new ActionParameterOption { Value = "prime_paid_upgrade", Label = Fields.OptionPrimeUpgrade() },
				new ActionParameterOption { Value = "raid", Label = Fields.OptionRaid() },
				new ActionParameterOption { Value = "unraid", Label = Fields.OptionRaidCancelled() },
				new ActionParameterOption { Value = "pay_it_forward", Label = Fields.OptionPayItForward() },
				new ActionParameterOption { Value = "announcement", Label = Fields.OptionAnnouncement() },
				new ActionParameterOption { Value = "bits_badge_tier", Label = Fields.OptionBitsBadge() },
				new ActionParameterOption { Value = "charity_donation", Label = Fields.OptionCharityDonation() }
			],
			label: Fields.Notice(),
			description: Fields.NoticeDescription());

	private static ActionParameter Text(string name, LocalizedText label) => ActionParameter.Text(name, label: label);

	// Payload values the integration can list: the same options its matching filter offers, so a
	// condition on one is authored by picking a name while the stored value stays the raw id.
	private static ActionParameter Selectable(string name, LocalizedText label)
		=> ActionParameter.DynamicChoice(name, label: label);

	private static ActionParameter Number(string name, LocalizedText label) =>
		ActionParameter.Number(name, label: label);

	private static ActionParameter Bool(string name, LocalizedText label) => ActionParameter.Toggle(name, label: label);

	private static class Fields
	{
		public static LocalizedText StreamOnlineName() => AppStrings.Integrations.Twitch.Events.StreamOnlineName();

		public static LocalizedText StreamOnlineDescription() =>
			AppStrings.Integrations.Twitch.Events.StreamOnlineDescription();

		public static LocalizedText StreamOfflineName() => AppStrings.Integrations.Twitch.Events.StreamOfflineName();

		public static LocalizedText StreamOfflineDescription() =>
			AppStrings.Integrations.Twitch.Events.StreamOfflineDescription();

		public static LocalizedText ChannelUpdateName() => AppStrings.Integrations.Twitch.Events.ChannelUpdateName();

		public static LocalizedText ChannelUpdateDescription() =>
			AppStrings.Integrations.Twitch.Events.ChannelUpdateDescription();

		public static LocalizedText AdBreakBeginName() => AppStrings.Integrations.Twitch.Events.AdBreakBeginName();

		public static LocalizedText AdBreakBeginDescription() =>
			AppStrings.Integrations.Twitch.Events.AdBreakBeginDescription();

		public static LocalizedText FollowName() => AppStrings.Integrations.Twitch.Events.FollowName();
		public static LocalizedText FollowDescription() => AppStrings.Integrations.Twitch.Events.FollowDescription();
		public static LocalizedText RaidIncomingName() => AppStrings.Integrations.Twitch.Events.RaidIncomingName();

		public static LocalizedText RaidIncomingDescription() =>
			AppStrings.Integrations.Twitch.Events.RaidIncomingDescription();

		public static LocalizedText RaidOutgoingName() => AppStrings.Integrations.Twitch.Events.RaidOutgoingName();

		public static LocalizedText RaidOutgoingDescription() =>
			AppStrings.Integrations.Twitch.Events.RaidOutgoingDescription();

		public static LocalizedText ShoutoutCreatedName() =>
			AppStrings.Integrations.Twitch.Events.ShoutoutCreatedName();

		public static LocalizedText ShoutoutCreatedDescription() =>
			AppStrings.Integrations.Twitch.Events.ShoutoutCreatedDescription();

		public static LocalizedText ShoutoutReceivedName() =>
			AppStrings.Integrations.Twitch.Events.ShoutoutReceivedName();

		public static LocalizedText ShoutoutReceivedDescription() =>
			AppStrings.Integrations.Twitch.Events.ShoutoutReceivedDescription();

		public static LocalizedText VipAddedName() => AppStrings.Integrations.Twitch.Events.VipAddedName();

		public static LocalizedText VipAddedDescription() =>
			AppStrings.Integrations.Twitch.Events.VipAddedDescription();

		public static LocalizedText VipRemovedName() => AppStrings.Integrations.Twitch.Events.VipRemovedName();

		public static LocalizedText VipRemovedDescription() =>
			AppStrings.Integrations.Twitch.Events.VipRemovedDescription();

		public static LocalizedText SubscribeName() => AppStrings.Integrations.Twitch.Events.SubscribeName();

		public static LocalizedText SubscribeDescription() =>
			AppStrings.Integrations.Twitch.Events.SubscribeDescription();

		public static LocalizedText SubscriptionMessageName() =>
			AppStrings.Integrations.Twitch.Events.SubscriptionMessageName();

		public static LocalizedText SubscriptionMessageDescription() =>
			AppStrings.Integrations.Twitch.Events.SubscriptionMessageDescription();

		public static LocalizedText SubscriptionGiftName() =>
			AppStrings.Integrations.Twitch.Events.SubscriptionGiftName();

		public static LocalizedText SubscriptionGiftDescription() =>
			AppStrings.Integrations.Twitch.Events.SubscriptionGiftDescription();

		public static LocalizedText SubscriptionEndName() =>
			AppStrings.Integrations.Twitch.Events.SubscriptionEndName();

		public static LocalizedText SubscriptionEndDescription() =>
			AppStrings.Integrations.Twitch.Events.SubscriptionEndDescription();

		public static LocalizedText CheerName() => AppStrings.Integrations.Twitch.Events.CheerName();
		public static LocalizedText CheerDescription() => AppStrings.Integrations.Twitch.Events.CheerDescription();
		public static LocalizedText RewardRedeemedName() => AppStrings.Integrations.Twitch.Events.RewardRedeemedName();

		public static LocalizedText RewardRedeemedDescription() =>
			AppStrings.Integrations.Twitch.Events.RewardRedeemedDescription();

		public static LocalizedText RewardRedemptionUpdatedName() =>
			AppStrings.Integrations.Twitch.Events.RewardRedemptionUpdatedName();

		public static LocalizedText RewardRedemptionUpdatedDescription() =>
			AppStrings.Integrations.Twitch.Events.RewardRedemptionUpdatedDescription();

		public static LocalizedText AutomaticRewardRedeemedName() =>
			AppStrings.Integrations.Twitch.Events.AutomaticRewardRedeemedName();

		public static LocalizedText AutomaticRewardRedeemedDescription() =>
			AppStrings.Integrations.Twitch.Events.AutomaticRewardRedeemedDescription();

		public static LocalizedText PollBeginName() => AppStrings.Integrations.Twitch.Events.PollBeginName();

		public static LocalizedText PollBeginDescription() =>
			AppStrings.Integrations.Twitch.Events.PollBeginDescription();

		public static LocalizedText PollEndName() => AppStrings.Integrations.Twitch.Events.PollEndName();
		public static LocalizedText PollEndDescription() => AppStrings.Integrations.Twitch.Events.PollEndDescription();

		public static LocalizedText PredictionBeginName() =>
			AppStrings.Integrations.Twitch.Events.PredictionBeginName();

		public static LocalizedText PredictionBeginDescription() =>
			AppStrings.Integrations.Twitch.Events.PredictionBeginDescription();

		public static LocalizedText PredictionLockName() => AppStrings.Integrations.Twitch.Events.PredictionLockName();

		public static LocalizedText PredictionLockDescription() =>
			AppStrings.Integrations.Twitch.Events.PredictionLockDescription();

		public static LocalizedText PredictionEndName() => AppStrings.Integrations.Twitch.Events.PredictionEndName();

		public static LocalizedText PredictionEndDescription() =>
			AppStrings.Integrations.Twitch.Events.PredictionEndDescription();

		public static LocalizedText HypeTrainBeginName() => AppStrings.Integrations.Twitch.Events.HypeTrainBeginName();

		public static LocalizedText HypeTrainBeginDescription() =>
			AppStrings.Integrations.Twitch.Events.HypeTrainBeginDescription();

		public static LocalizedText HypeTrainProgressName() =>
			AppStrings.Integrations.Twitch.Events.HypeTrainProgressName();

		public static LocalizedText HypeTrainProgressDescription() =>
			AppStrings.Integrations.Twitch.Events.HypeTrainProgressDescription();

		public static LocalizedText HypeTrainEndName() => AppStrings.Integrations.Twitch.Events.HypeTrainEndName();

		public static LocalizedText HypeTrainEndDescription() =>
			AppStrings.Integrations.Twitch.Events.HypeTrainEndDescription();

		public static LocalizedText GoalBeginName() => AppStrings.Integrations.Twitch.Events.GoalBeginName();

		public static LocalizedText GoalBeginDescription() =>
			AppStrings.Integrations.Twitch.Events.GoalBeginDescription();

		public static LocalizedText GoalProgressName() => AppStrings.Integrations.Twitch.Events.GoalProgressName();

		public static LocalizedText GoalProgressDescription() =>
			AppStrings.Integrations.Twitch.Events.GoalProgressDescription();

		public static LocalizedText GoalEndName() => AppStrings.Integrations.Twitch.Events.GoalEndName();
		public static LocalizedText GoalEndDescription() => AppStrings.Integrations.Twitch.Events.GoalEndDescription();

		public static LocalizedText ChatNotificationName() =>
			AppStrings.Integrations.Twitch.Events.ChatNotificationName();

		public static LocalizedText ChatNotificationDescription() =>
			AppStrings.Integrations.Twitch.Events.ChatNotificationDescription();

		public static LocalizedText ChatClearedName() => AppStrings.Integrations.Twitch.Events.ChatClearedName();

		public static LocalizedText ChatClearedDescription() =>
			AppStrings.Integrations.Twitch.Events.ChatClearedDescription();

		public static LocalizedText ChatMessageDeletedName() =>
			AppStrings.Integrations.Twitch.Events.ChatMessageDeletedName();

		public static LocalizedText ChatMessageDeletedDescription() =>
			AppStrings.Integrations.Twitch.Events.ChatMessageDeletedDescription();

		public static LocalizedText ChatSettingsUpdatedName() =>
			AppStrings.Integrations.Twitch.Events.ChatSettingsUpdatedName();

		public static LocalizedText ChatSettingsUpdatedDescription() =>
			AppStrings.Integrations.Twitch.Events.ChatSettingsUpdatedDescription();

		public static LocalizedText BanName() => AppStrings.Integrations.Twitch.Events.BanName();
		public static LocalizedText BanDescription() => AppStrings.Integrations.Twitch.Events.BanDescription();
		public static LocalizedText UnbanName() => AppStrings.Integrations.Twitch.Events.UnbanName();
		public static LocalizedText UnbanDescription() => AppStrings.Integrations.Twitch.Events.UnbanDescription();
		public static LocalizedText AnyName() => AppStrings.Integrations.Twitch.Events.AnyName();
		public static LocalizedText AnyDescription() => AppStrings.Integrations.Twitch.Events.AnyDescription();

		public static LocalizedText ConnectedDescription() =>
			AppStrings.Integrations.Twitch.Events.ConnectedDescription();

		public static LocalizedText DisconnectedDescription() =>
			AppStrings.Integrations.Twitch.Events.DisconnectedDescription();

		public static LocalizedText Type() => AppStrings.Integrations.Twitch.Events.Type();
		public static LocalizedText StartedAt() => AppStrings.Integrations.Twitch.Events.StartedAt();
		public static LocalizedText Title() => AppStrings.Integrations.Twitch.Events.Title();
		public static LocalizedText CategoryId() => AppStrings.Integrations.Twitch.Events.CategoryId();
		public static LocalizedText Category() => AppStrings.Integrations.Twitch.Events.Category();
		public static LocalizedText Language() => AppStrings.Integrations.Twitch.Events.Language();
		public static LocalizedText ContentLabels() => AppStrings.Integrations.Twitch.Events.ContentLabels();
		public static LocalizedText DurationSeconds() => AppStrings.Integrations.Twitch.Events.DurationSeconds();
		public static LocalizedText Automatic() => AppStrings.Integrations.Twitch.Events.Automatic();
		public static LocalizedText StartedBy() => AppStrings.Integrations.Twitch.Events.StartedBy();
		public static LocalizedText FollowedAt() => AppStrings.Integrations.Twitch.Events.FollowedAt();
		public static LocalizedText FromUserId() => AppStrings.Integrations.Twitch.Events.FromUserId();
		public static LocalizedText FromLogin() => AppStrings.Integrations.Twitch.Events.FromLogin();
		public static LocalizedText FromName() => AppStrings.Integrations.Twitch.Events.FromName();
		public static LocalizedText Viewers() => AppStrings.Integrations.Twitch.Events.Viewers();
		public static LocalizedText ToUserId() => AppStrings.Integrations.Twitch.Events.ToUserId();
		public static LocalizedText ToLogin() => AppStrings.Integrations.Twitch.Events.ToLogin();
		public static LocalizedText ToName() => AppStrings.Integrations.Twitch.Events.ToName();
		public static LocalizedText UserId() => AppStrings.Integrations.Twitch.Events.UserId();
		public static LocalizedText UserLogin() => AppStrings.Integrations.Twitch.Events.UserLogin();
		public static LocalizedText User() => AppStrings.Integrations.Twitch.Events.User();
		public static LocalizedText Account() => AppStrings.Integrations.Twitch.Events.Account();
		public static LocalizedText AccountDescription() => AppStrings.Integrations.Twitch.Events.AccountDescription();

		public static LocalizedText FirstAccountPlaceholder() =>
			AppStrings.Integrations.Twitch.Events.FirstAccountPlaceholder();

		public static LocalizedText AccountId() => AppStrings.Integrations.Twitch.Events.AccountId();
		public static LocalizedText AccountLogin() => AppStrings.Integrations.Twitch.Events.AccountLogin();
		public static LocalizedText Tier() => AppStrings.Integrations.Twitch.Events.Tier();
		public static LocalizedText TierDescription() => AppStrings.Integrations.Twitch.Events.TierDescription();
		public static LocalizedText TierOption1() => AppStrings.Integrations.Twitch.Events.TierOption1();
		public static LocalizedText TierOption2() => AppStrings.Integrations.Twitch.Events.TierOption2();
		public static LocalizedText TierOption3() => AppStrings.Integrations.Twitch.Events.TierOption3();
		public static LocalizedText TierOptionPrime() => AppStrings.Integrations.Twitch.Events.TierOptionPrime();
		public static LocalizedText Gifted() => AppStrings.Integrations.Twitch.Events.Gifted();
		public static LocalizedText MonthsTotal() => AppStrings.Integrations.Twitch.Events.MonthsTotal();
		public static LocalizedText MonthStreak() => AppStrings.Integrations.Twitch.Events.MonthStreak();
		public static LocalizedText MonthsBought() => AppStrings.Integrations.Twitch.Events.MonthsBought();
		public static LocalizedText Message() => AppStrings.Integrations.Twitch.Events.Message();
		public static LocalizedText GiftedNow() => AppStrings.Integrations.Twitch.Events.GiftedNow();
		public static LocalizedText GiftedInTotal() => AppStrings.Integrations.Twitch.Events.GiftedInTotal();
		public static LocalizedText Anonymous() => AppStrings.Integrations.Twitch.Events.Anonymous();
		public static LocalizedText WasGifted() => AppStrings.Integrations.Twitch.Events.WasGifted();
		public static LocalizedText Bits() => AppStrings.Integrations.Twitch.Events.Bits();
		public static LocalizedText RewardId() => AppStrings.Integrations.Twitch.Events.RewardId();
		public static LocalizedText Reward() => AppStrings.Integrations.Twitch.Events.Reward();
		public static LocalizedText Cost() => AppStrings.Integrations.Twitch.Events.Cost();
		public static LocalizedText RedemptionId() => AppStrings.Integrations.Twitch.Events.RedemptionId();
		public static LocalizedText ViewerInput() => AppStrings.Integrations.Twitch.Events.ViewerInput();
		public static LocalizedText Status() => AppStrings.Integrations.Twitch.Events.Status();
		public static LocalizedText RewardDescription() => AppStrings.Integrations.Twitch.Events.RewardDescription();

		public static LocalizedText AnyRewardPlaceholder() =>
			AppStrings.Integrations.Twitch.Events.AnyRewardPlaceholder();

		public static LocalizedText PollId() => AppStrings.Integrations.Twitch.Events.PollId();
		public static LocalizedText Choices() => AppStrings.Integrations.Twitch.Events.Choices();
		public static LocalizedText EndsAt() => AppStrings.Integrations.Twitch.Events.EndsAt();
		public static LocalizedText WinningChoice() => AppStrings.Integrations.Twitch.Events.WinningChoice();
		public static LocalizedText WinningVotes() => AppStrings.Integrations.Twitch.Events.WinningVotes();
		public static LocalizedText PredictionId() => AppStrings.Integrations.Twitch.Events.PredictionId();
		public static LocalizedText Outcomes() => AppStrings.Integrations.Twitch.Events.Outcomes();
		public static LocalizedText LocksAt() => AppStrings.Integrations.Twitch.Events.LocksAt();
		public static LocalizedText WinningOutcome() => AppStrings.Integrations.Twitch.Events.WinningOutcome();
		public static LocalizedText Level() => AppStrings.Integrations.Twitch.Events.Level();
		public static LocalizedText Total() => AppStrings.Integrations.Twitch.Events.Total();
		public static LocalizedText Goal() => AppStrings.Integrations.Twitch.Events.Goal();
		public static LocalizedText ExpiresAt() => AppStrings.Integrations.Twitch.Events.ExpiresAt();
		public static LocalizedText Progress() => AppStrings.Integrations.Twitch.Events.Progress();
		public static LocalizedText EndedAt() => AppStrings.Integrations.Twitch.Events.EndedAt();
		public static LocalizedText GoalId() => AppStrings.Integrations.Twitch.Events.GoalId();
		public static LocalizedText Description() => AppStrings.Integrations.Twitch.Events.Description();
		public static LocalizedText Current() => AppStrings.Integrations.Twitch.Events.Current();
		public static LocalizedText Target() => AppStrings.Integrations.Twitch.Events.Target();
		public static LocalizedText Achieved() => AppStrings.Integrations.Twitch.Events.Achieved();
		public static LocalizedText Notice() => AppStrings.Integrations.Twitch.Events.Notice();
		public static LocalizedText NoticeDescription() => AppStrings.Integrations.Twitch.Events.NoticeDescription();
		public static LocalizedText OptionSubscription() => AppStrings.Integrations.Twitch.Events.OptionSubscription();

		public static LocalizedText OptionResubscription() =>
			AppStrings.Integrations.Twitch.Events.OptionResubscription();

		public static LocalizedText OptionGiftedSubscription() =>
			AppStrings.Integrations.Twitch.Events.OptionGiftedSubscription();

		public static LocalizedText OptionCommunityGift() =>
			AppStrings.Integrations.Twitch.Events.OptionCommunityGift();

		public static LocalizedText OptionGiftUpgrade() => AppStrings.Integrations.Twitch.Events.OptionGiftUpgrade();
		public static LocalizedText OptionPrimeUpgrade() => AppStrings.Integrations.Twitch.Events.OptionPrimeUpgrade();
		public static LocalizedText OptionRaid() => AppStrings.Integrations.Twitch.Events.OptionRaid();

		public static LocalizedText OptionRaidCancelled() =>
			AppStrings.Integrations.Twitch.Events.OptionRaidCancelled();

		public static LocalizedText OptionPayItForward() => AppStrings.Integrations.Twitch.Events.OptionPayItForward();
		public static LocalizedText OptionAnnouncement() => AppStrings.Integrations.Twitch.Events.OptionAnnouncement();
		public static LocalizedText OptionBitsBadge() => AppStrings.Integrations.Twitch.Events.OptionBitsBadge();

		public static LocalizedText OptionCharityDonation() =>
			AppStrings.Integrations.Twitch.Events.OptionCharityDonation();

		public static LocalizedText SystemMessage() => AppStrings.Integrations.Twitch.Events.SystemMessage();
		public static LocalizedText MessageId() => AppStrings.Integrations.Twitch.Events.MessageId();
		public static LocalizedText EmoteOnly() => AppStrings.Integrations.Twitch.Events.EmoteOnly();
		public static LocalizedText FollowersOnly() => AppStrings.Integrations.Twitch.Events.FollowersOnly();

		public static LocalizedText FollowersOnlyMinutes() =>
			AppStrings.Integrations.Twitch.Events.FollowersOnlyMinutes();

		public static LocalizedText SlowMode() => AppStrings.Integrations.Twitch.Events.SlowMode();
		public static LocalizedText SlowModeSeconds() => AppStrings.Integrations.Twitch.Events.SlowModeSeconds();
		public static LocalizedText SubscribersOnly() => AppStrings.Integrations.Twitch.Events.SubscribersOnly();
		public static LocalizedText UniqueChat() => AppStrings.Integrations.Twitch.Events.UniqueChat();
		public static LocalizedText ModeratorLogin() => AppStrings.Integrations.Twitch.Events.ModeratorLogin();
		public static LocalizedText Moderator() => AppStrings.Integrations.Twitch.Events.Moderator();
		public static LocalizedText Reason() => AppStrings.Integrations.Twitch.Events.Reason();
		public static LocalizedText Permanent() => AppStrings.Integrations.Twitch.Events.Permanent();
		public static LocalizedText Version() => AppStrings.Integrations.Twitch.Events.Version();
		public static LocalizedText RawData() => AppStrings.Integrations.Twitch.Events.RawData();
		public static LocalizedText EventType() => AppStrings.Integrations.Twitch.Events.EventType();

		public static LocalizedText EventTypeDescription() =>
			AppStrings.Integrations.Twitch.Events.EventTypeDescription();

		public static LocalizedText AnyEventPlaceholder() =>
			AppStrings.Integrations.Twitch.Events.AnyEventPlaceholder();
	}
}
