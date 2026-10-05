using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.YouTube.Protocol;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.YouTube.Errors;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.YouTube.Actions;

namespace MacroDeckHost.Integrations.YouTube.Actions;

internal static class YouTubeActions
{
	internal const int MaxChatMessageLength = 200;
	internal const int MaxTitleLength = 100;
	internal const int MaxDescriptionBytes = 5000;
	internal const int MaxTagsLength = 500;
	private const int TestingChecks = 30;
	private static readonly TimeSpan TestingCheckInterval = TimeSpan.FromSeconds(2);

	public static IReadOnlyList<IActionDefinition> Create(Func<YouTubeAccountManager> accounts)
	{
		YouTubeActionDefinition Action(
			string id,
			LocalizedText name,
			LocalizedText description,
			IReadOnlyList<ActionParameter> parameters,
			Func<YouTubeActionScope, Task<ActionResult>> execute)
			=> new(accounts, id, name, description, parameters, execute);

		return
		[
			Action("send-chat-message",
				Strings.SendChatMessage.Name(),
				Strings.SendChatMessage.Description(),
				[
					ActionParameter.MultilineText("message",
						label: Strings.SendChatMessage.MessageLabel(),
						description: Strings.SendChatMessage.MessageDescription(),
						required: true,
						maxLength: MaxChatMessageLength)
				],
				SendChatMessageAsync),

			Action("set-title",
				Strings.SetTitle.Name(),
				Strings.SetTitle.Description(),
				[
					ActionParameter.Text("title",
						label: Strings.SetTitle.TitleLabel(),
						required: true,
						maxLength: MaxTitleLength)
				],
				scope => UpdateSnippetAsync(scope, ValidateTitle, (snippet, title) => snippet with { Title = title })),

			Action("set-description",
				Strings.SetDescription.Name(),
				Strings.SetDescription.Description(),
				[ActionParameter.MultilineText("description", label: Strings.SetDescription.DescriptionLabel())],
				scope => UpdateSnippetAsync(scope,
					ValidateDescription,
					(snippet, description) => snippet with { Description = description })),

			Action("set-tags",
				Strings.SetTags.Name(),
				Strings.SetTags.Description(),
				[
					ActionParameter.MultilineText("tags",
						label: Strings.SetTags.TagsLabel(),
						description: Strings.SetTags.TagsDescription())
				],
				scope => UpdateSnippetAsync(scope,
					ValidateTags,
					(snippet, tags) => snippet with { Tags = YouTubeActionValues.ReadTags(scope.Parameters, "tags") })),

			Action("start-ad-break",
				Strings.StartAdBreak.Name(),
				Strings.StartAdBreak.Description(),
				[
					ActionParameter.Choice("duration",
						[
							new ActionParameterOption { Value = "30", Label = Strings.StartAdBreak.Duration30Seconds() },
							new ActionParameterOption { Value = "60", Label = Strings.StartAdBreak.Duration60Seconds() },
							new ActionParameterOption { Value = "90", Label = Strings.StartAdBreak.Duration90Seconds() },
							new ActionParameterOption { Value = "120", Label = Strings.StartAdBreak.Duration2Minutes() },
							new ActionParameterOption { Value = "180", Label = Strings.StartAdBreak.Duration3Minutes() }
						],
						label: Strings.StartAdBreak.DurationLabel(),
						defaultValue: "60",
						required: true)
				],
				StartAdBreakAsync),

			Action("go-live",
				Strings.GoLive.Name(),
				Strings.GoLive.Description(),
				[],
				GoLiveAsync),

			Action("end-stream",
				Strings.EndStream.Name(),
				Strings.EndStream.Description(),
				[],
				EndStreamAsync)
		];
	}

	private static async Task<ActionResult> SendChatMessageAsync(YouTubeActionScope scope)
	{
		if (YouTubeActionValues.ReadText(scope.Parameters, "message") is not { } message)
		{
			return Invalid(Errors.MessageEmpty());
		}

		if (message.Length > MaxChatMessageLength)
		{
			return Invalid(Errors.MessageTooLong());
		}

		var liveChatId = scope.Connection.ActiveLiveChatId ??
			(scope.Connection.State is { IsLive: true, LiveChatId: { } known } ? known : null) ??
			(await FindLiveAsync(scope))?.LiveChatId;

		if (liveChatId is null)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound, Errors.NoLiveChat());
		}

		await scope.Api.InsertChatMessageAsync(liveChatId, message, scope.CancellationToken);
		return ActionResult.Success();
	}

	private static async Task<ActionResult> UpdateSnippetAsync(
		YouTubeActionScope scope,
		Func<YouTubeActionScope, (string? Value, ActionResult? Error)> validate,
		Func<YouTubeVideoSnippet, string, YouTubeVideoSnippet> apply)
	{
		var (value, error) = validate(scope);
		if (error is not null)
		{
			return error;
		}

		if (await FindTargetBroadcastAsync(scope) is not { } broadcast ||
			await scope.Api.GetVideoAsync(broadcast.Id, scope.CancellationToken) is not { } video)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound, Errors.NoBroadcast());
		}

		await scope.Api.UpdateVideoSnippetAsync(video.Id, apply(video.Snippet, value ?? string.Empty),
			scope.CancellationToken);

		return ActionResult.Success();
	}

	private static async Task<ActionResult> StartAdBreakAsync(YouTubeActionScope scope)
	{
		if (await FindLiveAsync(scope) is not { } live)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound, Errors.NotLive());
		}

		await scope.Api.InsertCuepointAsync(live.Id,
			YouTubeActionValues.ReadInt(scope.Parameters, "duration") ?? 60,
			scope.CancellationToken);

		return ActionResult.Success();
	}

	private static async Task<ActionResult> GoLiveAsync(YouTubeActionScope scope)
	{
		var upcoming = await scope.Api.ListBroadcastsAsync(YouTubeBroadcastStatus.Upcoming, scope.CancellationToken);
		var candidate = Ordered(upcoming).FirstOrDefault(IsStartable);

		if (candidate is null)
		{
			var active = await scope.Api.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, scope.CancellationToken);
			if (active.Any(IsLive))
			{
				return ActionResult.Success();
			}

			candidate = Ordered(active).FirstOrDefault(IsStartable);
		}

		if (candidate is null)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound, Errors.NoReadyBroadcast());
		}

		if (candidate.EnableAutoStart)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.AutoStart());
		}

		if (candidate is { LifeCycleStatus: YouTubeLifeCycleStatus.Ready, EnableMonitorStream: true })
		{
			// A broadcast with a monitor stream has to pass through testing before YouTube accepts live.
			await TransitionAsync(scope, candidate.Id, YouTubeBroadcastTransition.Testing);

			switch (await WaitForTestingAsync(scope, candidate.Id))
			{
				case YouTubeLifeCycleStatus.Live:
					return ActionResult.Success();
				case not YouTubeLifeCycleStatus.Testing:
					return ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.TestStillStarting());
			}
		}

		return await TransitionAsync(scope, candidate.Id, YouTubeBroadcastTransition.Live);
	}

	private static async Task<string?> WaitForTestingAsync(YouTubeActionScope scope, string broadcastId)
	{
		for (var attempt = 0; attempt < TestingChecks; attempt++)
		{
			await scope.Options.Delay(TestingCheckInterval, scope.CancellationToken);

			var status = await StatusOfAsync(scope, broadcastId, YouTubeBroadcastStatus.Active) ??
				await StatusOfAsync(scope, broadcastId, YouTubeBroadcastStatus.Upcoming);

			if (status is YouTubeLifeCycleStatus.Testing or YouTubeLifeCycleStatus.Live)
			{
				return status;
			}
		}

		return null;
	}

	private static async Task<string?> StatusOfAsync(YouTubeActionScope scope, string broadcastId, string listed)
		=> (await scope.Api.ListBroadcastsAsync(listed, scope.CancellationToken))
			.FirstOrDefault(broadcast => broadcast.Id == broadcastId)?.LifeCycleStatus;

	private static async Task<ActionResult> EndStreamAsync(YouTubeActionScope scope)
		=> await FindLiveAsync(scope) is { } live
			? await TransitionAsync(scope, live.Id, YouTubeBroadcastTransition.Complete)
			: ActionResult.Failed(ActionErrorCodes.NotFound, Errors.NotLive());

	private static async Task<ActionResult> TransitionAsync(YouTubeActionScope scope, string broadcastId, string status)
	{
		try
		{
			await scope.Api.TransitionBroadcastAsync(broadcastId, status, scope.CancellationToken);
		}
		catch (YouTubeApiException ex) when (ex.Reason is "redundantTransition")
		{
		}

		return ActionResult.Success();
	}

	private static async Task<YouTubeBroadcast?> FindLiveAsync(YouTubeActionScope scope)
		=> (await scope.Api.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, scope.CancellationToken))
			.FirstOrDefault(IsLive);

	private static async Task<YouTubeBroadcast?> FindTargetBroadcastAsync(YouTubeActionScope scope)
	{
		var active = await scope.Api.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, scope.CancellationToken);
		var current = active.FirstOrDefault(IsLive) ?? active.FirstOrDefault(broadcast => !IsFinished(broadcast));
		if (current is not null)
		{
			return current;
		}

		var upcoming = await scope.Api.ListBroadcastsAsync(YouTubeBroadcastStatus.Upcoming, scope.CancellationToken);
		return Ordered(upcoming).FirstOrDefault(broadcast => !IsFinished(broadcast));
	}

	private static IEnumerable<YouTubeBroadcast> Ordered(IReadOnlyList<YouTubeBroadcast> broadcasts)
		=> broadcasts.OrderBy(broadcast => broadcast.ScheduledStartTime ?? DateTimeOffset.MaxValue);

	private static bool IsLive(YouTubeBroadcast broadcast)
		=> string.Equals(broadcast.LifeCycleStatus, YouTubeLifeCycleStatus.Live, StringComparison.Ordinal);

	private static bool IsStartable(YouTubeBroadcast broadcast)
		=> broadcast.LifeCycleStatus is YouTubeLifeCycleStatus.Ready or YouTubeLifeCycleStatus.Testing;

	private static bool IsFinished(YouTubeBroadcast broadcast)
		=> broadcast.LifeCycleStatus is YouTubeLifeCycleStatus.Complete or YouTubeLifeCycleStatus.Revoked;

	private static (string? Value, ActionResult? Error) ValidateTitle(YouTubeActionScope scope)
	{
		var title = YouTubeActionValues.ReadText(scope.Parameters, "title");

		if (title is null)
		{
			return (null, Invalid(Errors.TitleRequired()));
		}

		return title.Length > MaxTitleLength || ContainsAngleBrackets(title)
			? (null, Invalid(Errors.TitleInvalid()))
			: (title, null);
	}

	private static (string? Value, ActionResult? Error) ValidateDescription(YouTubeActionScope scope)
	{
		var description = YouTubeActionValues.ReadRawText(scope.Parameters, "description").Trim();

		return Encoding.UTF8.GetByteCount(description) > MaxDescriptionBytes || ContainsAngleBrackets(description)
			? (null, Invalid(Errors.DescriptionInvalid()))
			: (description, null);
	}

	private static (string? Value, ActionResult? Error) ValidateTags(YouTubeActionScope scope)
	{
		var tags = YouTubeActionValues.ReadTags(scope.Parameters, "tags");

		return TagsLength(tags) > MaxTagsLength || tags.Any(ContainsAngleBrackets)
			? (null, Invalid(Errors.TagsInvalid()))
			: (string.Join(',', tags), null);
	}

	// YouTube counts the commas between tags and the quotes it adds around a tag that contains a space.
	internal static int TagsLength(IReadOnlyList<string> tags)
		=> tags.Sum(tag => tag.Length + (tag.Contains(' ', StringComparison.Ordinal) ? 2 : 0)) +
			Math.Max(0, tags.Count - 1);

	private static bool ContainsAngleBrackets(string value)
		=> value.Contains('<', StringComparison.Ordinal) || value.Contains('>', StringComparison.Ordinal);

	private static ActionResult Invalid(LocalizedText message)
		=> ActionResult.Failed(ActionErrorCodes.InvalidParameter, message);
}
