using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Twitch.Events;

internal static class TwitchEventDefinitions
{
	private static readonly LocalizedText _streamCategory = AppStrings.Integrations.Twitch.Events.StreamCategory();

	private static readonly LocalizedText
		_communityCategory = AppStrings.Integrations.Twitch.Events.CommunityCategory();

	private static readonly LocalizedText _subscriptionCategory
		= AppStrings.Integrations.Twitch.Events.SubscriptionsCategory();

	private static readonly LocalizedText _bitsCategory = AppStrings.Integrations.Twitch.Events.BitsCategory();

	private static readonly LocalizedText _channelPointsCategory
		= AppStrings.Integrations.Twitch.Events.ChannelPointsCategory();

	private static readonly LocalizedText _interactiveCategory
		= AppStrings.Integrations.Twitch.Events.PollsAndPredictionsCategory();

	private static readonly LocalizedText
		_hypeTrainCategory = AppStrings.Integrations.Twitch.Events.HypeTrainCategory();

	private static readonly LocalizedText _goalsCategory = AppStrings.Integrations.Twitch.Events.GoalsCategory();
	private static readonly LocalizedText _chatCategory = AppStrings.Integrations.Twitch.Events.ChatCategory();

	private static readonly LocalizedText _moderationCategory
		= AppStrings.Integrations.Twitch.Events.ModerationCategory();

	private static readonly LocalizedText _connectionCategory
		= AppStrings.Integrations.Twitch.Events.ConnectionCategory();

	private static readonly LocalizedText _advancedCategory = AppStrings.Integrations.Twitch.Events.AdvancedCategory();

	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		Event(TwitchEventIds.StreamOnline,
			Fields.StreamOnlineName(),
			Fields.StreamOnlineDescription(),
			_streamCategory,
			payload: [Text("streamType", Fields.Type()), Text("startedAt", Fields.StartedAt())]),

		Event(TwitchEventIds.StreamOffline,
			Fields.StreamOfflineName(),
			Fields.StreamOfflineDescription(),
			_streamCategory),

		Event(TwitchEventIds.ChannelUpdate,
			Fields.ChannelUpdateName(),
			Fields.ChannelUpdateDescription(),
			_streamCategory,
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
			_streamCategory,
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
			_communityCategory,
			payload: [.. User(), Text("followedAt", Fields.FollowedAt())]),

		Event(TwitchEventIds.RaidIncoming,
			Fields.RaidIncomingName(),
			Fields.RaidIncomingDescription(),
			_communityCategory,
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
			_communityCategory,
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
			_communityCategory,
			payload:
			[
				Text("toUserLogin", Fields.ToLogin()),
				Text("toUserName", Fields.ToName()),
				Number("viewerCount", Fields.Viewers())
			]),

		Event(TwitchEventIds.ShoutoutReceived,
			Fields.ShoutoutReceivedName(),
			Fields.ShoutoutReceivedDescription(),
			_communityCategory,
			payload:
			[
				Text("fromUserLogin", Fields.FromLogin()),
				Text("fromUserName", Fields.FromName()),
				Number("viewerCount", Fields.Viewers())
			]),

		Event(TwitchEventIds.VipAdded,
			Fields.VipAddedName(),
			Fields.VipAddedDescription(),
			_communityCategory,
			payload: [.. User()]),

		Event(TwitchEventIds.VipRemoved,
			Fields.VipRemovedName(),
			Fields.VipRemovedDescription(),
			_communityCategory,
			payload: [.. User()]),

		Event(TwitchEventIds.Subscribe,
			Fields.SubscribeName(),
			Fields.SubscribeDescription(),
			_subscriptionCategory,
			[Tier()],
			[.. User(), Text("tier", Fields.Tier()), Bool("isGift", Fields.Gifted())]),

		Event(TwitchEventIds.SubscriptionMessage,
			Fields.SubscriptionMessageName(),
			Fields.SubscriptionMessageDescription(),
			_subscriptionCategory,
			[Tier()],
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
			_subscriptionCategory,
			[Tier()],
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
			_subscriptionCategory,
			[Tier()],
			[.. User(), Text("tier", Fields.Tier()), Bool("isGift", Fields.WasGifted())]),

		Event(TwitchEventIds.Cheer,
			Fields.CheerName(),
			Fields.CheerDescription(),
			_bitsCategory,
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
			_channelPointsCategory,
			[RewardFilter()],
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
			_channelPointsCategory,
			[RewardFilter()],
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
			_channelPointsCategory,
			payload: [.. User(), Text("rewardType", Fields.Reward()), Text("userInput", Fields.ViewerInput())]),

		Event(TwitchEventIds.PollBegin,
			Fields.PollBeginName(),
			Fields.PollBeginDescription(),
			_interactiveCategory,
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
			_interactiveCategory,
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
			_interactiveCategory,
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
			_interactiveCategory,
			payload: [Text("predictionId", Fields.PredictionId()), Text("title", Fields.Title())]),

		Event(TwitchEventIds.PredictionEnd,
			Fields.PredictionEndName(),
			Fields.PredictionEndDescription(),
			_interactiveCategory,
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
			_hypeTrainCategory,
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
			_hypeTrainCategory,
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
			_hypeTrainCategory,
			payload:
			[
				Number("level", Fields.Level()), Number("total", Fields.Total()), Text("endedAt", Fields.EndedAt())
			]),

		Event(TwitchEventIds.GoalBegin,
			Fields.GoalBeginName(),
			Fields.GoalBeginDescription(),
			_goalsCategory,
			payload: [.. Goal()]),
		Event(TwitchEventIds.GoalProgress,
			Fields.GoalProgressName(),
			Fields.GoalProgressDescription(),
			_goalsCategory,
			payload: [.. Goal()]),
		Event(TwitchEventIds.GoalEnd,
			Fields.GoalEndName(),
			Fields.GoalEndDescription(),
			_goalsCategory,
			payload: [.. Goal(), Bool("isAchieved", Fields.Achieved())]),

		Event(TwitchEventIds.ChatNotification,
			Fields.ChatNotificationName(),
			Fields.ChatNotificationDescription(),
			_chatCategory,
			[NoticeType()],
			[
				.. User(),
				Text("noticeType", Fields.Notice()),
				Text("message", Fields.Message()),
				Text("systemMessage", Fields.SystemMessage())
			]),

		Event(TwitchEventIds.ChatCleared,
			Fields.ChatClearedName(),
			Fields.ChatClearedDescription(),
			_chatCategory),

		Event(TwitchEventIds.ChatMessageDeleted,
			Fields.ChatMessageDeletedName(),
			Fields.ChatMessageDeletedDescription(),
			_chatCategory,
			payload: [.. User(), Text("messageId", Fields.MessageId())]),

		Event(TwitchEventIds.ChatSettingsUpdated,
			Fields.ChatSettingsUpdatedName(),
			Fields.ChatSettingsUpdatedDescription(),
			_chatCategory,
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
			_moderationCategory,
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
			_moderationCategory,
			payload:
			[
				.. User(), Text("moderatorLogin", Fields.ModeratorLogin()), Text("moderatorName", Fields.Moderator())
			]),

		Event(TwitchEventIds.Connected,
			MacroDeckStrings.Connection.Connected(),
			Fields.ConnectedDescription(),
			_connectionCategory),

		Event(TwitchEventIds.Disconnected,
			MacroDeckStrings.Connection.Disconnected(),
			Fields.DisconnectedDescription(),
			_connectionCategory),

		Event(TwitchEventIds.Any,
			Fields.AnyName(),
			Fields.AnyDescription(),
			_advancedCategory,
			[
				ActionParameter.DynamicChoice("type",
					Fields.EventType(),
					Fields.EventTypeDescription(),
					placeholder: Fields.AnyEventPlaceholder())
			],
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
	{
		return new EventDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = category,
			ConfigurationParameters = [Account(), .. configuration ?? []],
			PayloadParameters = [.. AccountPayload(), .. payload ?? []]
		};
	}

	private static ActionParameter Account()
	{
		return ActionParameter.DynamicChoice("account",
			Fields.Account(),
			Fields.AccountDescription(),
			placeholder: Fields.FirstAccountPlaceholder());
	}

	private static ActionParameter[] AccountPayload()
	{
		return
		[
			Selectable("account", Fields.AccountId()),
			Text("accountLogin", Fields.AccountLogin()),
			Text("accountName", Fields.Account())
		];
	}

	private static ActionParameter[] User()
	{
		return
			[Text("userId", Fields.UserId()), Text("userLogin", Fields.UserLogin()), Text("userName", Fields.User())];
	}

	private static ActionParameter[] Goal()
	{
		return
		[
			Text("goalId", Fields.GoalId()),
			Text("goalType", Fields.Type()),
			Text("description", Fields.Description()),
			Number("currentAmount", Fields.Current()),
			Number("targetAmount", Fields.Target())
		];
	}

	private static ActionParameter Tier()
	{
		return ActionParameter.Choice("tier",
			[
				new ActionParameterOption { Value = "1000", Label = Fields.TierOption1() },
				new ActionParameterOption { Value = "2000", Label = Fields.TierOption2() },
				new ActionParameterOption { Value = "3000", Label = Fields.TierOption3() },
				new ActionParameterOption { Value = "Prime", Label = Fields.TierOptionPrime() }
			],
			Fields.Tier(),
			Fields.TierDescription());
	}

	private static ActionParameter RewardFilter()
	{
		return ActionParameter.DynamicChoice("rewardTitle",
			Fields.Reward(),
			Fields.RewardDescription(),
			placeholder: Fields.AnyRewardPlaceholder());
	}

	private static ActionParameter NoticeType()
	{
		return ActionParameter.Choice("noticeType",
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
			Fields.Notice(),
			Fields.NoticeDescription());
	}

	private static ActionParameter Text(string name, LocalizedText label)
	{
		return ActionParameter.Text(name, label);
	}

	// Payload values the integration can list: the same options its matching filter offers, so a
	// condition on one is authored by picking a name while the stored value stays the raw id.
	private static ActionParameter Selectable(string name, LocalizedText label)
	{
		return ActionParameter.DynamicChoice(name, label);
	}

	private static ActionParameter Number(string name, LocalizedText label)
	{
		return ActionParameter.Number(name, label);
	}

	private static ActionParameter Bool(string name, LocalizedText label)
	{
		return ActionParameter.Toggle(name, label);
	}

	private static class Fields
	{
		public static LocalizedText StreamOnlineName()
		{
			return AppStrings.Integrations.Twitch.Events.StreamOnlineName();
		}

		public static LocalizedText StreamOnlineDescription()
		{
			return AppStrings.Integrations.Twitch.Events.StreamOnlineDescription();
		}

		public static LocalizedText StreamOfflineName()
		{
			return AppStrings.Integrations.Twitch.Events.StreamOfflineName();
		}

		public static LocalizedText StreamOfflineDescription()
		{
			return AppStrings.Integrations.Twitch.Events.StreamOfflineDescription();
		}

		public static LocalizedText ChannelUpdateName()
		{
			return AppStrings.Integrations.Twitch.Events.ChannelUpdateName();
		}

		public static LocalizedText ChannelUpdateDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ChannelUpdateDescription();
		}

		public static LocalizedText AdBreakBeginName()
		{
			return AppStrings.Integrations.Twitch.Events.AdBreakBeginName();
		}

		public static LocalizedText AdBreakBeginDescription()
		{
			return AppStrings.Integrations.Twitch.Events.AdBreakBeginDescription();
		}

		public static LocalizedText FollowName()
		{
			return AppStrings.Integrations.Twitch.Events.FollowName();
		}

		public static LocalizedText FollowDescription()
		{
			return AppStrings.Integrations.Twitch.Events.FollowDescription();
		}

		public static LocalizedText RaidIncomingName()
		{
			return AppStrings.Integrations.Twitch.Events.RaidIncomingName();
		}

		public static LocalizedText RaidIncomingDescription()
		{
			return AppStrings.Integrations.Twitch.Events.RaidIncomingDescription();
		}

		public static LocalizedText RaidOutgoingName()
		{
			return AppStrings.Integrations.Twitch.Events.RaidOutgoingName();
		}

		public static LocalizedText RaidOutgoingDescription()
		{
			return AppStrings.Integrations.Twitch.Events.RaidOutgoingDescription();
		}

		public static LocalizedText ShoutoutCreatedName()
		{
			return AppStrings.Integrations.Twitch.Events.ShoutoutCreatedName();
		}

		public static LocalizedText ShoutoutCreatedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ShoutoutCreatedDescription();
		}

		public static LocalizedText ShoutoutReceivedName()
		{
			return AppStrings.Integrations.Twitch.Events.ShoutoutReceivedName();
		}

		public static LocalizedText ShoutoutReceivedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ShoutoutReceivedDescription();
		}

		public static LocalizedText VipAddedName()
		{
			return AppStrings.Integrations.Twitch.Events.VipAddedName();
		}

		public static LocalizedText VipAddedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.VipAddedDescription();
		}

		public static LocalizedText VipRemovedName()
		{
			return AppStrings.Integrations.Twitch.Events.VipRemovedName();
		}

		public static LocalizedText VipRemovedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.VipRemovedDescription();
		}

		public static LocalizedText SubscribeName()
		{
			return AppStrings.Integrations.Twitch.Events.SubscribeName();
		}

		public static LocalizedText SubscribeDescription()
		{
			return AppStrings.Integrations.Twitch.Events.SubscribeDescription();
		}

		public static LocalizedText SubscriptionMessageName()
		{
			return AppStrings.Integrations.Twitch.Events.SubscriptionMessageName();
		}

		public static LocalizedText SubscriptionMessageDescription()
		{
			return AppStrings.Integrations.Twitch.Events.SubscriptionMessageDescription();
		}

		public static LocalizedText SubscriptionGiftName()
		{
			return AppStrings.Integrations.Twitch.Events.SubscriptionGiftName();
		}

		public static LocalizedText SubscriptionGiftDescription()
		{
			return AppStrings.Integrations.Twitch.Events.SubscriptionGiftDescription();
		}

		public static LocalizedText SubscriptionEndName()
		{
			return AppStrings.Integrations.Twitch.Events.SubscriptionEndName();
		}

		public static LocalizedText SubscriptionEndDescription()
		{
			return AppStrings.Integrations.Twitch.Events.SubscriptionEndDescription();
		}

		public static LocalizedText CheerName()
		{
			return AppStrings.Integrations.Twitch.Events.CheerName();
		}

		public static LocalizedText CheerDescription()
		{
			return AppStrings.Integrations.Twitch.Events.CheerDescription();
		}

		public static LocalizedText RewardRedeemedName()
		{
			return AppStrings.Integrations.Twitch.Events.RewardRedeemedName();
		}

		public static LocalizedText RewardRedeemedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.RewardRedeemedDescription();
		}

		public static LocalizedText RewardRedemptionUpdatedName()
		{
			return AppStrings.Integrations.Twitch.Events.RewardRedemptionUpdatedName();
		}

		public static LocalizedText RewardRedemptionUpdatedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.RewardRedemptionUpdatedDescription();
		}

		public static LocalizedText AutomaticRewardRedeemedName()
		{
			return AppStrings.Integrations.Twitch.Events.AutomaticRewardRedeemedName();
		}

		public static LocalizedText AutomaticRewardRedeemedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.AutomaticRewardRedeemedDescription();
		}

		public static LocalizedText PollBeginName()
		{
			return AppStrings.Integrations.Twitch.Events.PollBeginName();
		}

		public static LocalizedText PollBeginDescription()
		{
			return AppStrings.Integrations.Twitch.Events.PollBeginDescription();
		}

		public static LocalizedText PollEndName()
		{
			return AppStrings.Integrations.Twitch.Events.PollEndName();
		}

		public static LocalizedText PollEndDescription()
		{
			return AppStrings.Integrations.Twitch.Events.PollEndDescription();
		}

		public static LocalizedText PredictionBeginName()
		{
			return AppStrings.Integrations.Twitch.Events.PredictionBeginName();
		}

		public static LocalizedText PredictionBeginDescription()
		{
			return AppStrings.Integrations.Twitch.Events.PredictionBeginDescription();
		}

		public static LocalizedText PredictionLockName()
		{
			return AppStrings.Integrations.Twitch.Events.PredictionLockName();
		}

		public static LocalizedText PredictionLockDescription()
		{
			return AppStrings.Integrations.Twitch.Events.PredictionLockDescription();
		}

		public static LocalizedText PredictionEndName()
		{
			return AppStrings.Integrations.Twitch.Events.PredictionEndName();
		}

		public static LocalizedText PredictionEndDescription()
		{
			return AppStrings.Integrations.Twitch.Events.PredictionEndDescription();
		}

		public static LocalizedText HypeTrainBeginName()
		{
			return AppStrings.Integrations.Twitch.Events.HypeTrainBeginName();
		}

		public static LocalizedText HypeTrainBeginDescription()
		{
			return AppStrings.Integrations.Twitch.Events.HypeTrainBeginDescription();
		}

		public static LocalizedText HypeTrainProgressName()
		{
			return AppStrings.Integrations.Twitch.Events.HypeTrainProgressName();
		}

		public static LocalizedText HypeTrainProgressDescription()
		{
			return AppStrings.Integrations.Twitch.Events.HypeTrainProgressDescription();
		}

		public static LocalizedText HypeTrainEndName()
		{
			return AppStrings.Integrations.Twitch.Events.HypeTrainEndName();
		}

		public static LocalizedText HypeTrainEndDescription()
		{
			return AppStrings.Integrations.Twitch.Events.HypeTrainEndDescription();
		}

		public static LocalizedText GoalBeginName()
		{
			return AppStrings.Integrations.Twitch.Events.GoalBeginName();
		}

		public static LocalizedText GoalBeginDescription()
		{
			return AppStrings.Integrations.Twitch.Events.GoalBeginDescription();
		}

		public static LocalizedText GoalProgressName()
		{
			return AppStrings.Integrations.Twitch.Events.GoalProgressName();
		}

		public static LocalizedText GoalProgressDescription()
		{
			return AppStrings.Integrations.Twitch.Events.GoalProgressDescription();
		}

		public static LocalizedText GoalEndName()
		{
			return AppStrings.Integrations.Twitch.Events.GoalEndName();
		}

		public static LocalizedText GoalEndDescription()
		{
			return AppStrings.Integrations.Twitch.Events.GoalEndDescription();
		}

		public static LocalizedText ChatNotificationName()
		{
			return AppStrings.Integrations.Twitch.Events.ChatNotificationName();
		}

		public static LocalizedText ChatNotificationDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ChatNotificationDescription();
		}

		public static LocalizedText ChatClearedName()
		{
			return AppStrings.Integrations.Twitch.Events.ChatClearedName();
		}

		public static LocalizedText ChatClearedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ChatClearedDescription();
		}

		public static LocalizedText ChatMessageDeletedName()
		{
			return AppStrings.Integrations.Twitch.Events.ChatMessageDeletedName();
		}

		public static LocalizedText ChatMessageDeletedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ChatMessageDeletedDescription();
		}

		public static LocalizedText ChatSettingsUpdatedName()
		{
			return AppStrings.Integrations.Twitch.Events.ChatSettingsUpdatedName();
		}

		public static LocalizedText ChatSettingsUpdatedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ChatSettingsUpdatedDescription();
		}

		public static LocalizedText BanName()
		{
			return AppStrings.Integrations.Twitch.Events.BanName();
		}

		public static LocalizedText BanDescription()
		{
			return AppStrings.Integrations.Twitch.Events.BanDescription();
		}

		public static LocalizedText UnbanName()
		{
			return AppStrings.Integrations.Twitch.Events.UnbanName();
		}

		public static LocalizedText UnbanDescription()
		{
			return AppStrings.Integrations.Twitch.Events.UnbanDescription();
		}

		public static LocalizedText AnyName()
		{
			return AppStrings.Integrations.Twitch.Events.AnyName();
		}

		public static LocalizedText AnyDescription()
		{
			return AppStrings.Integrations.Twitch.Events.AnyDescription();
		}

		public static LocalizedText ConnectedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.ConnectedDescription();
		}

		public static LocalizedText DisconnectedDescription()
		{
			return AppStrings.Integrations.Twitch.Events.DisconnectedDescription();
		}

		public static LocalizedText Type()
		{
			return AppStrings.Integrations.Twitch.Events.Type();
		}

		public static LocalizedText StartedAt()
		{
			return AppStrings.Integrations.Twitch.Events.StartedAt();
		}

		public static LocalizedText Title()
		{
			return AppStrings.Integrations.Twitch.Events.Title();
		}

		public static LocalizedText CategoryId()
		{
			return AppStrings.Integrations.Twitch.Events.CategoryId();
		}

		public static LocalizedText Category()
		{
			return AppStrings.Integrations.Twitch.Events.Category();
		}

		public static LocalizedText Language()
		{
			return AppStrings.Integrations.Twitch.Events.Language();
		}

		public static LocalizedText ContentLabels()
		{
			return AppStrings.Integrations.Twitch.Events.ContentLabels();
		}

		public static LocalizedText DurationSeconds()
		{
			return AppStrings.Integrations.Twitch.Events.DurationSeconds();
		}

		public static LocalizedText Automatic()
		{
			return AppStrings.Integrations.Twitch.Events.Automatic();
		}

		public static LocalizedText StartedBy()
		{
			return AppStrings.Integrations.Twitch.Events.StartedBy();
		}

		public static LocalizedText FollowedAt()
		{
			return AppStrings.Integrations.Twitch.Events.FollowedAt();
		}

		public static LocalizedText FromUserId()
		{
			return AppStrings.Integrations.Twitch.Events.FromUserId();
		}

		public static LocalizedText FromLogin()
		{
			return AppStrings.Integrations.Twitch.Events.FromLogin();
		}

		public static LocalizedText FromName()
		{
			return AppStrings.Integrations.Twitch.Events.FromName();
		}

		public static LocalizedText Viewers()
		{
			return AppStrings.Integrations.Twitch.Events.Viewers();
		}

		public static LocalizedText ToUserId()
		{
			return AppStrings.Integrations.Twitch.Events.ToUserId();
		}

		public static LocalizedText ToLogin()
		{
			return AppStrings.Integrations.Twitch.Events.ToLogin();
		}

		public static LocalizedText ToName()
		{
			return AppStrings.Integrations.Twitch.Events.ToName();
		}

		public static LocalizedText UserId()
		{
			return AppStrings.Integrations.Twitch.Events.UserId();
		}

		public static LocalizedText UserLogin()
		{
			return AppStrings.Integrations.Twitch.Events.UserLogin();
		}

		public static LocalizedText User()
		{
			return AppStrings.Integrations.Twitch.Events.User();
		}

		public static LocalizedText Account()
		{
			return AppStrings.Integrations.Twitch.Events.Account();
		}

		public static LocalizedText AccountDescription()
		{
			return AppStrings.Integrations.Twitch.Events.AccountDescription();
		}

		public static LocalizedText FirstAccountPlaceholder()
		{
			return AppStrings.Integrations.Twitch.Events.FirstAccountPlaceholder();
		}

		public static LocalizedText AccountId()
		{
			return AppStrings.Integrations.Twitch.Events.AccountId();
		}

		public static LocalizedText AccountLogin()
		{
			return AppStrings.Integrations.Twitch.Events.AccountLogin();
		}

		public static LocalizedText Tier()
		{
			return AppStrings.Integrations.Twitch.Events.Tier();
		}

		public static LocalizedText TierDescription()
		{
			return AppStrings.Integrations.Twitch.Events.TierDescription();
		}

		public static LocalizedText TierOption1()
		{
			return AppStrings.Integrations.Twitch.Events.TierOption1();
		}

		public static LocalizedText TierOption2()
		{
			return AppStrings.Integrations.Twitch.Events.TierOption2();
		}

		public static LocalizedText TierOption3()
		{
			return AppStrings.Integrations.Twitch.Events.TierOption3();
		}

		public static LocalizedText TierOptionPrime()
		{
			return AppStrings.Integrations.Twitch.Events.TierOptionPrime();
		}

		public static LocalizedText Gifted()
		{
			return AppStrings.Integrations.Twitch.Events.Gifted();
		}

		public static LocalizedText MonthsTotal()
		{
			return AppStrings.Integrations.Twitch.Events.MonthsTotal();
		}

		public static LocalizedText MonthStreak()
		{
			return AppStrings.Integrations.Twitch.Events.MonthStreak();
		}

		public static LocalizedText MonthsBought()
		{
			return AppStrings.Integrations.Twitch.Events.MonthsBought();
		}

		public static LocalizedText Message()
		{
			return AppStrings.Integrations.Twitch.Events.Message();
		}

		public static LocalizedText GiftedNow()
		{
			return AppStrings.Integrations.Twitch.Events.GiftedNow();
		}

		public static LocalizedText GiftedInTotal()
		{
			return AppStrings.Integrations.Twitch.Events.GiftedInTotal();
		}

		public static LocalizedText Anonymous()
		{
			return AppStrings.Integrations.Twitch.Events.Anonymous();
		}

		public static LocalizedText WasGifted()
		{
			return AppStrings.Integrations.Twitch.Events.WasGifted();
		}

		public static LocalizedText Bits()
		{
			return AppStrings.Integrations.Twitch.Events.Bits();
		}

		public static LocalizedText RewardId()
		{
			return AppStrings.Integrations.Twitch.Events.RewardId();
		}

		public static LocalizedText Reward()
		{
			return AppStrings.Integrations.Twitch.Events.Reward();
		}

		public static LocalizedText Cost()
		{
			return AppStrings.Integrations.Twitch.Events.Cost();
		}

		public static LocalizedText RedemptionId()
		{
			return AppStrings.Integrations.Twitch.Events.RedemptionId();
		}

		public static LocalizedText ViewerInput()
		{
			return AppStrings.Integrations.Twitch.Events.ViewerInput();
		}

		public static LocalizedText Status()
		{
			return AppStrings.Integrations.Twitch.Events.Status();
		}

		public static LocalizedText RewardDescription()
		{
			return AppStrings.Integrations.Twitch.Events.RewardDescription();
		}

		public static LocalizedText AnyRewardPlaceholder()
		{
			return AppStrings.Integrations.Twitch.Events.AnyRewardPlaceholder();
		}

		public static LocalizedText PollId()
		{
			return AppStrings.Integrations.Twitch.Events.PollId();
		}

		public static LocalizedText Choices()
		{
			return AppStrings.Integrations.Twitch.Events.Choices();
		}

		public static LocalizedText EndsAt()
		{
			return AppStrings.Integrations.Twitch.Events.EndsAt();
		}

		public static LocalizedText WinningChoice()
		{
			return AppStrings.Integrations.Twitch.Events.WinningChoice();
		}

		public static LocalizedText WinningVotes()
		{
			return AppStrings.Integrations.Twitch.Events.WinningVotes();
		}

		public static LocalizedText PredictionId()
		{
			return AppStrings.Integrations.Twitch.Events.PredictionId();
		}

		public static LocalizedText Outcomes()
		{
			return AppStrings.Integrations.Twitch.Events.Outcomes();
		}

		public static LocalizedText LocksAt()
		{
			return AppStrings.Integrations.Twitch.Events.LocksAt();
		}

		public static LocalizedText WinningOutcome()
		{
			return AppStrings.Integrations.Twitch.Events.WinningOutcome();
		}

		public static LocalizedText Level()
		{
			return AppStrings.Integrations.Twitch.Events.Level();
		}

		public static LocalizedText Total()
		{
			return AppStrings.Integrations.Twitch.Events.Total();
		}

		public static LocalizedText Goal()
		{
			return AppStrings.Integrations.Twitch.Events.Goal();
		}

		public static LocalizedText ExpiresAt()
		{
			return AppStrings.Integrations.Twitch.Events.ExpiresAt();
		}

		public static LocalizedText Progress()
		{
			return AppStrings.Integrations.Twitch.Events.Progress();
		}

		public static LocalizedText EndedAt()
		{
			return AppStrings.Integrations.Twitch.Events.EndedAt();
		}

		public static LocalizedText GoalId()
		{
			return AppStrings.Integrations.Twitch.Events.GoalId();
		}

		public static LocalizedText Description()
		{
			return AppStrings.Integrations.Twitch.Events.Description();
		}

		public static LocalizedText Current()
		{
			return AppStrings.Integrations.Twitch.Events.Current();
		}

		public static LocalizedText Target()
		{
			return AppStrings.Integrations.Twitch.Events.Target();
		}

		public static LocalizedText Achieved()
		{
			return AppStrings.Integrations.Twitch.Events.Achieved();
		}

		public static LocalizedText Notice()
		{
			return AppStrings.Integrations.Twitch.Events.Notice();
		}

		public static LocalizedText NoticeDescription()
		{
			return AppStrings.Integrations.Twitch.Events.NoticeDescription();
		}

		public static LocalizedText OptionSubscription()
		{
			return AppStrings.Integrations.Twitch.Events.OptionSubscription();
		}

		public static LocalizedText OptionResubscription()
		{
			return AppStrings.Integrations.Twitch.Events.OptionResubscription();
		}

		public static LocalizedText OptionGiftedSubscription()
		{
			return AppStrings.Integrations.Twitch.Events.OptionGiftedSubscription();
		}

		public static LocalizedText OptionCommunityGift()
		{
			return AppStrings.Integrations.Twitch.Events.OptionCommunityGift();
		}

		public static LocalizedText OptionGiftUpgrade()
		{
			return AppStrings.Integrations.Twitch.Events.OptionGiftUpgrade();
		}

		public static LocalizedText OptionPrimeUpgrade()
		{
			return AppStrings.Integrations.Twitch.Events.OptionPrimeUpgrade();
		}

		public static LocalizedText OptionRaid()
		{
			return AppStrings.Integrations.Twitch.Events.OptionRaid();
		}

		public static LocalizedText OptionRaidCancelled()
		{
			return AppStrings.Integrations.Twitch.Events.OptionRaidCancelled();
		}

		public static LocalizedText OptionPayItForward()
		{
			return AppStrings.Integrations.Twitch.Events.OptionPayItForward();
		}

		public static LocalizedText OptionAnnouncement()
		{
			return AppStrings.Integrations.Twitch.Events.OptionAnnouncement();
		}

		public static LocalizedText OptionBitsBadge()
		{
			return AppStrings.Integrations.Twitch.Events.OptionBitsBadge();
		}

		public static LocalizedText OptionCharityDonation()
		{
			return AppStrings.Integrations.Twitch.Events.OptionCharityDonation();
		}

		public static LocalizedText SystemMessage()
		{
			return AppStrings.Integrations.Twitch.Events.SystemMessage();
		}

		public static LocalizedText MessageId()
		{
			return AppStrings.Integrations.Twitch.Events.MessageId();
		}

		public static LocalizedText EmoteOnly()
		{
			return AppStrings.Integrations.Twitch.Events.EmoteOnly();
		}

		public static LocalizedText FollowersOnly()
		{
			return AppStrings.Integrations.Twitch.Events.FollowersOnly();
		}

		public static LocalizedText FollowersOnlyMinutes()
		{
			return AppStrings.Integrations.Twitch.Events.FollowersOnlyMinutes();
		}

		public static LocalizedText SlowMode()
		{
			return AppStrings.Integrations.Twitch.Events.SlowMode();
		}

		public static LocalizedText SlowModeSeconds()
		{
			return AppStrings.Integrations.Twitch.Events.SlowModeSeconds();
		}

		public static LocalizedText SubscribersOnly()
		{
			return AppStrings.Integrations.Twitch.Events.SubscribersOnly();
		}

		public static LocalizedText UniqueChat()
		{
			return AppStrings.Integrations.Twitch.Events.UniqueChat();
		}

		public static LocalizedText ModeratorLogin()
		{
			return AppStrings.Integrations.Twitch.Events.ModeratorLogin();
		}

		public static LocalizedText Moderator()
		{
			return AppStrings.Integrations.Twitch.Events.Moderator();
		}

		public static LocalizedText Reason()
		{
			return AppStrings.Integrations.Twitch.Events.Reason();
		}

		public static LocalizedText Permanent()
		{
			return AppStrings.Integrations.Twitch.Events.Permanent();
		}

		public static LocalizedText Version()
		{
			return AppStrings.Integrations.Twitch.Events.Version();
		}

		public static LocalizedText RawData()
		{
			return AppStrings.Integrations.Twitch.Events.RawData();
		}

		public static LocalizedText EventType()
		{
			return AppStrings.Integrations.Twitch.Events.EventType();
		}

		public static LocalizedText EventTypeDescription()
		{
			return AppStrings.Integrations.Twitch.Events.EventTypeDescription();
		}

		public static LocalizedText AnyEventPlaceholder()
		{
			return AppStrings.Integrations.Twitch.Events.AnyEventPlaceholder();
		}
	}
}
