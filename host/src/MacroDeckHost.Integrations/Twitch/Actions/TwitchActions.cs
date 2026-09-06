using MacroDeckHost.Integrations.Twitch.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Actions;

internal static class TwitchActions
{
	private static readonly ILogger
		_logger = IntegrationLog.For(TwitchIntegration.IntegrationId, typeof(TwitchActions));

	private static readonly TimeSpan _clipConfirmationTimeout = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan _clipPollInterval = TimeSpan.FromSeconds(1);

	private static class Strings
	{
		public static class SetStreamInfo
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.SetStreamInfo.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.SetStreamInfo.Description();

			public static LocalizedText TitleLabel() =>
				AppStrings.Integrations.Twitch.Actions.SetStreamInfo.TitleLabel();

			public static LocalizedText CategoryLabel() =>
				AppStrings.Integrations.Twitch.Actions.SetStreamInfo.CategoryLabel();

			public static LocalizedText CategoryDescription() =>
				AppStrings.Integrations.Twitch.Actions.SetStreamInfo.CategoryDescription();

			public static LocalizedText TagsLabel() => AppStrings.Integrations.Twitch.Actions.SetStreamInfo.TagsLabel();

			public static LocalizedText TagsDescription() =>
				AppStrings.Integrations.Twitch.Actions.SetStreamInfo.TagsDescription();
		}

		public static class SendChatMessage
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.SendChatMessage.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.SendChatMessage.Description();

			public static LocalizedText MessageLabel() =>
				AppStrings.Integrations.Twitch.Actions.SendChatMessage.MessageLabel();

			public static LocalizedText MessageDescription() =>
				AppStrings.Integrations.Twitch.Actions.SendChatMessage.MessageDescription();

			public static LocalizedText ReplyToMessageIdLabel() =>
				AppStrings.Integrations.Twitch.Actions.SendChatMessage.ReplyToMessageIdLabel();

			public static LocalizedText ReplyToMessageIdDescription() =>
				AppStrings.Integrations.Twitch.Actions.SendChatMessage.ReplyToMessageIdDescription();
		}

		public static class SendAnnouncement
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.SendAnnouncement.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.Description();

			public static LocalizedText MessageLabel() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.MessageLabel();

			public static LocalizedText ColourLabel() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.ColourLabel();

			public static LocalizedText ColourChannelColour() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.ColourChannelColour();

			public static LocalizedText ColourBlue() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.ColourBlue();

			public static LocalizedText ColourGreen() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.ColourGreen();

			public static LocalizedText ColourOrange() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.ColourOrange();

			public static LocalizedText ColourPurple() =>
				AppStrings.Integrations.Twitch.Actions.SendAnnouncement.ColourPurple();
		}

		public static class SendShoutout
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.SendShoutout.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.SendShoutout.Description();
		}

		public static class ClearChat
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.ClearChat.Name();
			public static LocalizedText Description() => AppStrings.Integrations.Twitch.Actions.ClearChat.Description();
		}

		public static class DeleteChatMessage
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.DeleteChatMessage.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.DeleteChatMessage.Description();

			public static LocalizedText MessageIdLabel() =>
				AppStrings.Integrations.Twitch.Actions.DeleteChatMessage.MessageIdLabel();
		}

		public static class SetChatMode
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.SetChatMode.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.SetChatMode.Description();

			public static LocalizedText ModeLabel() => AppStrings.Integrations.Twitch.Actions.SetChatMode.ModeLabel();

			public static LocalizedText ModeEmoteOnly() =>
				AppStrings.Integrations.Twitch.Actions.SetChatMode.ModeEmoteOnly();

			public static LocalizedText ModeFollowersOnly() =>
				AppStrings.Integrations.Twitch.Actions.SetChatMode.ModeFollowersOnly();

			public static LocalizedText ModeSlow() => AppStrings.Integrations.Twitch.Actions.SetChatMode.ModeSlow();

			public static LocalizedText ModeSubscribersOnly() =>
				AppStrings.Integrations.Twitch.Actions.SetChatMode.ModeSubscribersOnly();

			public static LocalizedText ModeUniqueChat() =>
				AppStrings.Integrations.Twitch.Actions.SetChatMode.ModeUniqueChat();

			public static LocalizedText OnLabel() => AppStrings.Integrations.Twitch.Actions.SetChatMode.OnLabel();

			public static LocalizedText DurationLabel() =>
				AppStrings.Integrations.Twitch.Actions.SetChatMode.DurationLabel();

			public static LocalizedText DurationDescription() =>
				AppStrings.Integrations.Twitch.Actions.SetChatMode.DurationDescription();
		}

		public static class RunCommercial
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.RunCommercial.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.Description();

			public static LocalizedText LengthLabel() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.LengthLabel();

			public static LocalizedText Length30Seconds() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.Length30Seconds();

			public static LocalizedText Length60Seconds() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.Length60Seconds();

			public static LocalizedText Length90Seconds() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.Length90Seconds();

			public static LocalizedText Length2Minutes() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.Length2Minutes();

			public static LocalizedText Length2Point5Minutes() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.Length2Point5Minutes();

			public static LocalizedText Length3Minutes() =>
				AppStrings.Integrations.Twitch.Actions.RunCommercial.Length3Minutes();
		}

		public static class SnoozeAd
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.SnoozeAd.Name();
			public static LocalizedText Description() => AppStrings.Integrations.Twitch.Actions.SnoozeAd.Description();
		}

		public static class StartRaid
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.StartRaid.Name();
			public static LocalizedText Description() => AppStrings.Integrations.Twitch.Actions.StartRaid.Description();
		}

		public static class CancelRaid
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.CancelRaid.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.CancelRaid.Description();
		}

		public static class CreateClip
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.CreateClip.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.CreateClip.Description();

			public static LocalizedText HasDelayLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreateClip.HasDelayLabel();

			public static LocalizedText HasDelayDescription() =>
				AppStrings.Integrations.Twitch.Actions.CreateClip.HasDelayDescription();

			public static LocalizedText TargetVariableLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreateClip.TargetVariableLabel();

			public static LocalizedText TargetVariableDescription() =>
				AppStrings.Integrations.Twitch.Actions.CreateClip.TargetVariableDescription();
		}

		public static class CreateStreamMarker
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.CreateStreamMarker.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.CreateStreamMarker.Description();

			public static LocalizedText DescriptionLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreateStreamMarker.DescriptionLabel();
		}

		public static class BanUser
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.BanUser.Name();
			public static LocalizedText Description() => AppStrings.Integrations.Twitch.Actions.BanUser.Description();

			public static LocalizedText DurationSecondsLabel() =>
				AppStrings.Integrations.Twitch.Actions.BanUser.DurationSecondsLabel();

			public static LocalizedText DurationSecondsDescription() =>
				AppStrings.Integrations.Twitch.Actions.BanUser.DurationSecondsDescription();

			public static LocalizedText ReasonLabel() => AppStrings.Integrations.Twitch.Actions.BanUser.ReasonLabel();
		}

		public static class UnbanUser
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.UnbanUser.Name();
			public static LocalizedText Description() => AppStrings.Integrations.Twitch.Actions.UnbanUser.Description();
		}

		public static class CreatePoll
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.CreatePoll.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.CreatePoll.Description();

			public static LocalizedText QuestionLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreatePoll.QuestionLabel();

			public static LocalizedText ChoicesLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreatePoll.ChoicesLabel();

			public static LocalizedText ChoicesDescription() =>
				AppStrings.Integrations.Twitch.Actions.CreatePoll.ChoicesDescription();

			public static LocalizedText DurationSecondsLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreatePoll.DurationSecondsLabel();

			public static LocalizedText ChannelPointsPerVoteLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreatePoll.ChannelPointsPerVoteLabel();

			public static LocalizedText ChannelPointsPerVoteDescription() =>
				AppStrings.Integrations.Twitch.Actions.CreatePoll.ChannelPointsPerVoteDescription();
		}

		public static class EndPoll
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.EndPoll.Name();
			public static LocalizedText Description() => AppStrings.Integrations.Twitch.Actions.EndPoll.Description();
			public static LocalizedText ArchiveLabel() => AppStrings.Integrations.Twitch.Actions.EndPoll.ArchiveLabel();

			public static LocalizedText ArchiveDescription() =>
				AppStrings.Integrations.Twitch.Actions.EndPoll.ArchiveDescription();
		}

		public static class CreatePrediction
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.CreatePrediction.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.CreatePrediction.Description();

			public static LocalizedText QuestionLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreatePrediction.QuestionLabel();

			public static LocalizedText OutcomesLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreatePrediction.OutcomesLabel();

			public static LocalizedText OutcomesDescription() =>
				AppStrings.Integrations.Twitch.Actions.CreatePrediction.OutcomesDescription();

			public static LocalizedText WindowSecondsLabel() =>
				AppStrings.Integrations.Twitch.Actions.CreatePrediction.WindowSecondsLabel();
		}

		public static class EndPrediction
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.EndPrediction.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.EndPrediction.Description();

			public static LocalizedText OutcomeLabel() =>
				AppStrings.Integrations.Twitch.Actions.EndPrediction.OutcomeLabel();

			public static LocalizedText OutcomeResolve() =>
				AppStrings.Integrations.Twitch.Actions.EndPrediction.OutcomeResolve();

			public static LocalizedText OutcomeLock() =>
				AppStrings.Integrations.Twitch.Actions.EndPrediction.OutcomeLock();

			public static LocalizedText OutcomeCancelAndRefund() =>
				AppStrings.Integrations.Twitch.Actions.EndPrediction.OutcomeCancelAndRefund();

			public static LocalizedText WinningOutcomeLabel() =>
				AppStrings.Integrations.Twitch.Actions.EndPrediction.WinningOutcomeLabel();

			public static LocalizedText WinningOutcomeDescription() =>
				AppStrings.Integrations.Twitch.Actions.EndPrediction.WinningOutcomeDescription();
		}

		public static class SetRedemptionStatus
		{
			public static LocalizedText Name() => AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.Name();

			public static LocalizedText Description() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.Description();

			public static LocalizedText RewardLabel() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.RewardLabel();

			public static LocalizedText RewardDescription() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.RewardDescription();

			public static LocalizedText RewardPlaceholder() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.RewardPlaceholder();

			public static LocalizedText RedemptionIdLabel() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.RedemptionIdLabel();

			public static LocalizedText RedemptionIdDescription() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.RedemptionIdDescription();

			public static LocalizedText ResultLabel() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.ResultLabel();

			public static LocalizedText ResultCompleted() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.ResultCompleted();

			public static LocalizedText ResultRefund() =>
				AppStrings.Integrations.Twitch.Actions.SetRedemptionStatus.ResultRefund();
		}

		public static LocalizedText LoginChannelLabel() => AppStrings.Integrations.Twitch.Actions.LoginChannelLabel();
		public static LocalizedText LoginUserLabel() => AppStrings.Integrations.Twitch.Actions.LoginUserLabel();
		public static LocalizedText LoginDescription() => AppStrings.Integrations.Twitch.Actions.LoginDescription();

		public static LocalizedText EventMessageIdReferenceDescription() =>
			AppStrings.Integrations.Twitch.Actions.EventMessageIdReferenceDescription();
	}

	public static IReadOnlyList<IActionDefinition> Create(
		Func<TwitchAccountManager> accounts,
		Func<IVariableApi?> variables,
		Func<TimeSpan, CancellationToken, Task>? delay = null)
	{
		var wait = delay ?? Task.Delay;

		TwitchActionDefinition Action(
			string id,
			LocalizedText name,
			LocalizedText description,
			IReadOnlyList<ActionParameter> parameters,
			Func<TwitchActionScope, Task> execute)
			=> new(accounts, variables, id, name, description, parameters, execute);

		TwitchChatModeActionDefinition ChatMode(
			string id,
			LocalizedText name,
			LocalizedText description,
			IReadOnlyList<ActionParameter> parameters,
			Func<TwitchActionScope, Task> execute)
			=> new(accounts, variables, id, name, description, parameters, execute);

		TwitchActionDefinition ResultAction(
			string id,
			LocalizedText name,
			LocalizedText description,
			IReadOnlyList<ActionParameter> parameters,
			Func<TwitchActionScope, Task<ActionResult>> execute)
			=> new(accounts, variables, id, name, description, parameters, execute);

		return
		[
			Action("set-stream-info",
				Strings.SetStreamInfo.Name(),
				Strings.SetStreamInfo.Description(),
				[
					ActionParameter.Text("title", label: Strings.SetStreamInfo.TitleLabel(), maxLength: 140),
					ActionParameter.Text("category",
						label: Strings.SetStreamInfo.CategoryLabel(),
						description: Strings.SetStreamInfo.CategoryDescription()),
					ActionParameter.Text("tags",
						label: Strings.SetStreamInfo.TagsLabel(),
						description: Strings.SetStreamInfo.TagsDescription())
				],
				async scope =>
				{
					var category = TwitchActionValues.ReadText(scope.Parameters, "category");
					var categoryId = category is null
						? null
						: await scope.Helix.ResolveCategoryIdAsync(category, scope.CancellationToken);

					await scope.Helix.ModifyChannelAsync(scope.UserId,
						TwitchActionValues.ReadText(scope.Parameters, "title"),
						categoryId,
						TwitchActionValues.ReadList(scope.Parameters, "tags"),
						scope.CancellationToken);
				}),

			Action("send-chat-message",
				Strings.SendChatMessage.Name(),
				Strings.SendChatMessage.Description(),
				[
					ActionParameter.MultilineText("message",
						label: Strings.SendChatMessage.MessageLabel(),
						description: Strings.SendChatMessage.MessageDescription(),
						required: true,
						maxLength: 500),
					ActionParameter.Text("replyToMessageId",
						label: Strings.SendChatMessage.ReplyToMessageIdLabel(),
						description: Strings.SendChatMessage.ReplyToMessageIdDescription())
				],
				async scope =>
				{
					if (TwitchActionValues.ReadText(scope.Parameters, "message") is not { } message)
					{
						return;
					}

					await scope.Helix.SendChatMessageAsync(scope.UserId,
						scope.UserId,
						message,
						TwitchActionValues.ReadText(scope.Parameters, "replyToMessageId"),
						scope.CancellationToken);
				}),

			Action("send-announcement",
				Strings.SendAnnouncement.Name(),
				Strings.SendAnnouncement.Description(),
				[
					ActionParameter.MultilineText("message",
						label: Strings.SendAnnouncement.MessageLabel(),
						required: true,
						maxLength: 500),
					ActionParameter.Choice("color",
						[
							new ActionParameterOption
								{ Value = "primary", Label = Strings.SendAnnouncement.ColourChannelColour() },
							new ActionParameterOption { Value = "blue", Label = Strings.SendAnnouncement.ColourBlue() },
							new ActionParameterOption
								{ Value = "green", Label = Strings.SendAnnouncement.ColourGreen() },
							new ActionParameterOption
								{ Value = "orange", Label = Strings.SendAnnouncement.ColourOrange() },
							new ActionParameterOption
								{ Value = "purple", Label = Strings.SendAnnouncement.ColourPurple() }
						],
						label: Strings.SendAnnouncement.ColourLabel(),
						defaultValue: "primary")
				],
				async scope =>
				{
					if (TwitchActionValues.ReadText(scope.Parameters, "message") is not { } message)
					{
						return;
					}

					await scope.Helix.SendAnnouncementAsync(scope.UserId,
						scope.UserId,
						message,
						TwitchActionValues.ReadText(scope.Parameters, "color") ?? "primary",
						scope.CancellationToken);
				}),

			Action("send-shoutout",
				Strings.SendShoutout.Name(),
				Strings.SendShoutout.Description(),
				[Login("targetLogin", Strings.LoginChannelLabel())],
				async scope =>
				{
					if (await ResolveUserId(scope, "targetLogin") is not { } targetId)
					{
						return;
					}

					await scope.Helix.SendShoutoutAsync(scope.UserId, targetId, scope.UserId, scope.CancellationToken);
				}),

			Action("clear-chat",
				Strings.ClearChat.Name(),
				Strings.ClearChat.Description(),
				[],
				scope => scope.Helix.DeleteChatMessagesAsync(scope.UserId,
					scope.UserId,
					null,
					scope.CancellationToken)),

			Action("delete-chat-message",
				Strings.DeleteChatMessage.Name(),
				Strings.DeleteChatMessage.Description(),
				[
					ActionParameter.Text("messageId",
						label: Strings.DeleteChatMessage.MessageIdLabel(),
						description: Strings.EventMessageIdReferenceDescription(),
						required: true)
				],
				scope => scope.Helix.DeleteChatMessagesAsync(scope.UserId,
					scope.UserId,
					TwitchActionValues.ReadText(scope.Parameters, "messageId"),
					scope.CancellationToken)),

			ChatMode("set-chat-mode",
				Strings.SetChatMode.Name(),
				Strings.SetChatMode.Description(),
				[
					ActionParameter.Choice("mode",
						[
							new ActionParameterOption
								{ Value = "emote-only", Label = Strings.SetChatMode.ModeEmoteOnly() },
							new ActionParameterOption
								{ Value = "followers-only", Label = Strings.SetChatMode.ModeFollowersOnly() },
							new ActionParameterOption { Value = "slow", Label = Strings.SetChatMode.ModeSlow() },
							new ActionParameterOption
								{ Value = "subscribers-only", Label = Strings.SetChatMode.ModeSubscribersOnly() },
							new ActionParameterOption
								{ Value = "unique-chat", Label = Strings.SetChatMode.ModeUniqueChat() }
						],
						label: Strings.SetChatMode.ModeLabel(),
						defaultValue: "emote-only",
						required: true),
					ActionParameter.Toggle("enabled", label: Strings.SetChatMode.OnLabel(), defaultValue: true),
					ActionParameter.Number("duration",
						label: Strings.SetChatMode.DurationLabel(),
						description: Strings.SetChatMode.DurationDescription(),
						min: 0)
				],
				scope => scope.Helix.SetChatModeAsync(scope.UserId,
					scope.UserId,
					ParseChatMode(TwitchActionValues.ReadText(scope.Parameters, "mode")),
					TwitchActionValues.ReadBool(scope.Parameters, "enabled", fallback: true),
					TwitchActionValues.ReadInt(scope.Parameters, "duration"),
					scope.CancellationToken)),

			Action("run-commercial",
				Strings.RunCommercial.Name(),
				Strings.RunCommercial.Description(),
				[
					ActionParameter.Choice("length",
						[
							new ActionParameterOption { Value = "30", Label = Strings.RunCommercial.Length30Seconds() },
							new ActionParameterOption { Value = "60", Label = Strings.RunCommercial.Length60Seconds() },
							new ActionParameterOption { Value = "90", Label = Strings.RunCommercial.Length90Seconds() },
							new ActionParameterOption { Value = "120", Label = Strings.RunCommercial.Length2Minutes() },
							new ActionParameterOption
								{ Value = "150", Label = Strings.RunCommercial.Length2Point5Minutes() },
							new ActionParameterOption { Value = "180", Label = Strings.RunCommercial.Length3Minutes() }
						],
						label: Strings.RunCommercial.LengthLabel(),
						defaultValue: "60",
						required: true)
				],
				scope => scope.Helix.StartCommercialAsync(scope.UserId,
					TwitchActionValues.ReadInt(scope.Parameters, "length") ?? 60,
					scope.CancellationToken)),

			Action("snooze-ad",
				Strings.SnoozeAd.Name(),
				Strings.SnoozeAd.Description(),
				[],
				scope => scope.Helix.SnoozeNextAdAsync(scope.UserId, scope.CancellationToken)),

			Action("start-raid",
				Strings.StartRaid.Name(),
				Strings.StartRaid.Description(),
				[Login("targetLogin", Strings.LoginChannelLabel())],
				async scope =>
				{
					if (await ResolveUserId(scope, "targetLogin") is not { } targetId)
					{
						return;
					}

					await scope.Helix.StartRaidAsync(scope.UserId, targetId, scope.CancellationToken);
				}),

			Action("cancel-raid",
				Strings.CancelRaid.Name(),
				Strings.CancelRaid.Description(),
				[],
				scope => scope.Helix.CancelRaidAsync(scope.UserId, scope.CancellationToken)),

			ResultAction("create-clip",
				Strings.CreateClip.Name(),
				Strings.CreateClip.Description(),
				[
					ActionParameter.Toggle("hasDelay",
						label: Strings.CreateClip.HasDelayLabel(),
						description: Strings.CreateClip.HasDelayDescription()),
					ActionParameter.Text("targetVariable",
						label: Strings.CreateClip.TargetVariableLabel(),
						description: Strings.CreateClip.TargetVariableDescription())
				],
				async scope =>
				{
					var created = await scope.Helix.CreateClipAsync(scope.UserId,
						TwitchActionValues.ReadBool(scope.Parameters, "hasDelay"),
						scope.CancellationToken);

					if (created is null)
					{
						return ActionResult.Failed("CLIP_NOT_CREATED",
							AppStrings.Integrations.Twitch.Errors.ClipNotCreated());
					}

					var maxAttempts = (int)(_clipConfirmationTimeout.Ticks / _clipPollInterval.Ticks);
					var attempts = 0;
					var failedAttempts = 0;
					var confirmed = false;

					while (attempts < maxAttempts)
					{
						await wait(_clipPollInterval, scope.CancellationToken);
						attempts++;

						try
						{
							if (await scope.Helix.ClipExistsAsync(created.Id, scope.CancellationToken))
							{
								confirmed = true;
								break;
							}
						}
						catch (Exception ex) when (ex is not OperationCanceledException)
						{
							failedAttempts++;
							_logger.Warning(ex, "Twitch clip confirmation check failed for {ClipId}", created.Id);
						}
					}

					if (confirmed)
					{
						if (TwitchActionValues.ReadText(scope.Parameters, "targetVariable") is { } variable)
						{
							await TwitchVariableWriter.WriteAsync(scope.Variables,
								variable,
								VariableType.Text,
								created.EditUrl);
						}

						return ActionResult.Success();
					}

					if (attempts > 0 && failedAttempts == attempts)
					{
						return ActionResult.Failed(ActionErrorCodes.ProviderError,
							AppStrings.Integrations.Twitch.Errors.ClipConfirmationFailed());
					}

					return ActionResult.Failed("CLIP_NOT_CONFIRMED",
						AppStrings.Integrations.Twitch.Errors.ClipNotConfirmed());
				}),

			Action("create-stream-marker",
				Strings.CreateStreamMarker.Name(),
				Strings.CreateStreamMarker.Description(),
				[
					ActionParameter.Text("description",
						label: Strings.CreateStreamMarker.DescriptionLabel(),
						maxLength: 140)
				],
				scope => scope.Helix.CreateStreamMarkerAsync(scope.UserId,
					TwitchActionValues.ReadText(scope.Parameters, "description"),
					scope.CancellationToken)),

			Action("ban-user",
				Strings.BanUser.Name(),
				Strings.BanUser.Description(),
				[
					Login("targetLogin", Strings.LoginUserLabel()),
					ActionParameter.Number("durationSeconds",
						label: Strings.BanUser.DurationSecondsLabel(),
						description: Strings.BanUser.DurationSecondsDescription(),
						min: 0,
						max: 1209600),
					ActionParameter.Text("reason", label: Strings.BanUser.ReasonLabel(), maxLength: 500)
				],
				async scope =>
				{
					if (await ResolveUserId(scope, "targetLogin") is not { } targetId)
					{
						return;
					}

					await scope.Helix.BanUserAsync(scope.UserId,
						scope.UserId,
						targetId,
						TwitchActionValues.ReadInt(scope.Parameters, "durationSeconds"),
						TwitchActionValues.ReadText(scope.Parameters, "reason"),
						scope.CancellationToken);
				}),

			Action("unban-user",
				Strings.UnbanUser.Name(),
				Strings.UnbanUser.Description(),
				[Login("targetLogin", Strings.LoginUserLabel())],
				async scope =>
				{
					if (await ResolveUserId(scope, "targetLogin") is not { } targetId)
					{
						return;
					}

					await scope.Helix.UnbanUserAsync(scope.UserId, scope.UserId, targetId, scope.CancellationToken);
				}),

			Action("create-poll",
				Strings.CreatePoll.Name(),
				Strings.CreatePoll.Description(),
				[
					ActionParameter.Text("title",
						label: Strings.CreatePoll.QuestionLabel(),
						required: true,
						maxLength: 60),
					ActionParameter.MultilineText("choices",
						label: Strings.CreatePoll.ChoicesLabel(),
						description: Strings.CreatePoll.ChoicesDescription(),
						required: true),
					ActionParameter.Number("durationSeconds",
						label: Strings.CreatePoll.DurationSecondsLabel(),
						min: 15,
						max: 1800,
						defaultValue: 60),
					ActionParameter.Number("channelPointsPerVote",
						label: Strings.CreatePoll.ChannelPointsPerVoteLabel(),
						description: Strings.CreatePoll.ChannelPointsPerVoteDescription(),
						min: 0)
				],
				scope =>
				{
					var title = TwitchActionValues.ReadText(scope.Parameters, "title");
					var choices = TwitchActionValues.ReadLines(scope.Parameters, "choices");

					return title is null || choices.Count < 2
						? Task.CompletedTask
						: scope.Helix.CreatePollAsync(scope.UserId,
							title,
							choices,
							TwitchActionValues.ReadInt(scope.Parameters, "durationSeconds") ?? 60,
							TwitchActionValues.ReadInt(scope.Parameters, "channelPointsPerVote"),
							scope.CancellationToken);
				}),

			Action("end-poll",
				Strings.EndPoll.Name(),
				Strings.EndPoll.Description(),
				[
					ActionParameter.Toggle("archive",
						label: Strings.EndPoll.ArchiveLabel(),
						description: Strings.EndPoll.ArchiveDescription())
				],
				async scope =>
				{
					if (await scope.Helix.GetActivePollAsync(scope.UserId, scope.CancellationToken) is not { } poll)
					{
						return;
					}

					await scope.Helix.EndPollAsync(scope.UserId,
						poll.Id,
						TwitchActionValues.ReadBool(scope.Parameters, "archive"),
						scope.CancellationToken);
				}),

			Action("create-prediction",
				Strings.CreatePrediction.Name(),
				Strings.CreatePrediction.Description(),
				[
					ActionParameter.Text("title",
						label: Strings.CreatePrediction.QuestionLabel(),
						required: true,
						maxLength: 45),
					ActionParameter.MultilineText("outcomes",
						label: Strings.CreatePrediction.OutcomesLabel(),
						description: Strings.CreatePrediction.OutcomesDescription(),
						required: true),
					ActionParameter.Number("windowSeconds",
						label: Strings.CreatePrediction.WindowSecondsLabel(),
						min: 30,
						max: 1800,
						defaultValue: 120)
				],
				scope =>
				{
					var title = TwitchActionValues.ReadText(scope.Parameters, "title");
					var outcomes = TwitchActionValues.ReadLines(scope.Parameters, "outcomes");

					return title is null || outcomes.Count < 2
						? Task.CompletedTask
						: scope.Helix.CreatePredictionAsync(scope.UserId,
							title,
							outcomes,
							TwitchActionValues.ReadInt(scope.Parameters, "windowSeconds") ?? 120,
							scope.CancellationToken);
				}),

			Action("end-prediction",
				Strings.EndPrediction.Name(),
				Strings.EndPrediction.Description(),
				[
					ActionParameter.Choice("status",
						[
							new ActionParameterOption
								{ Value = "RESOLVED", Label = Strings.EndPrediction.OutcomeResolve() },
							new ActionParameterOption { Value = "LOCKED", Label = Strings.EndPrediction.OutcomeLock() },
							new ActionParameterOption
								{ Value = "CANCELED", Label = Strings.EndPrediction.OutcomeCancelAndRefund() }
						],
						label: Strings.EndPrediction.OutcomeLabel(),
						defaultValue: "RESOLVED",
						required: true),
					ActionParameter.Text("winningOutcome",
						label: Strings.EndPrediction.WinningOutcomeLabel(),
						description: Strings.EndPrediction.WinningOutcomeDescription())
				],
				async scope =>
				{
					var prediction
						= await scope.Helix.GetActivePredictionAsync(scope.UserId, scope.CancellationToken);

					if (prediction is null)
					{
						return;
					}

					var status = TwitchActionValues.ReadText(scope.Parameters, "status") ?? "RESOLVED";
					var winning = TwitchActionValues.ReadText(scope.Parameters, "winningOutcome");
					var winningId = winning is null
						? null
						: prediction.Outcomes
							.FirstOrDefault(outcome =>
								string.Equals(outcome.Title, winning, StringComparison.OrdinalIgnoreCase))
							?.Id;

					await scope.Helix.EndPredictionAsync(scope.UserId,
						prediction.Id,
						status,
						winningId,
						scope.CancellationToken);
				}),

			Action("set-redemption-status",
				Strings.SetRedemptionStatus.Name(),
				Strings.SetRedemptionStatus.Description(),
				[
					ActionParameter.DynamicChoice("rewardId",
						label: Strings.SetRedemptionStatus.RewardLabel(),
						description: Strings.SetRedemptionStatus.RewardDescription(),
						placeholder: Strings.SetRedemptionStatus.RewardPlaceholder(),
						required: true),
					ActionParameter.Text("redemptionId",
						label: Strings.SetRedemptionStatus.RedemptionIdLabel(),
						description: Strings.SetRedemptionStatus.RedemptionIdDescription(),
						required: true),
					ActionParameter.Choice("status",
						[
							new ActionParameterOption
								{ Value = "fulfilled", Label = Strings.SetRedemptionStatus.ResultCompleted() },
							new ActionParameterOption
								{ Value = "canceled", Label = Strings.SetRedemptionStatus.ResultRefund() }
						],
						label: Strings.SetRedemptionStatus.ResultLabel(),
						defaultValue: "fulfilled",
						required: true)
				],
				scope =>
				{
					var rewardId = TwitchActionValues.ReadText(scope.Parameters, "rewardId");
					var redemptionId = TwitchActionValues.ReadText(scope.Parameters, "redemptionId");

					return rewardId is null || redemptionId is null
						? Task.CompletedTask
						: scope.Helix.UpdateRedemptionStatusAsync(scope.UserId,
							rewardId,
							redemptionId,
							string.Equals(TwitchActionValues.ReadText(scope.Parameters, "status"),
								"fulfilled",
								StringComparison.OrdinalIgnoreCase),
							scope.CancellationToken);
				})
		];
	}

	private static ActionParameter Login(string name, LocalizedText label)
		=> ActionParameter.Text(name,
			label: label,
			description: Strings.LoginDescription(),
			required: true);

	private static async Task<string?> ResolveUserId(TwitchActionScope scope, string parameterName)
	{
		if (TwitchActionValues.ReadLogin(scope.Parameters, parameterName) is not { } login)
		{
			return null;
		}

		var user = await scope.Helix.GetUserAsync(null, login, scope.CancellationToken);
		return user?.Id;
	}

	private static TwitchChatMode ParseChatMode(string? mode)
		=> mode switch
		{
			"followers-only" => TwitchChatMode.FollowersOnly,
			"slow" => TwitchChatMode.SlowMode,
			"subscribers-only" => TwitchChatMode.SubscribersOnly,
			"unique-chat" => TwitchChatMode.UniqueChat,
			_ => TwitchChatMode.EmoteOnly
		};
}
