using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.Connections;
using MacroDeckHost.Integrations.Feeds.Events;
using MacroDeckHost.Integrations.Feeds.Protocol;
using MacroDeckHost.Integrations.Feeds.State;
using ActionStrings = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Actions;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Errors;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Params;
using WidgetStrings = MacroDeckHost.Localization.AppStrings.Widgets.Feeds;

namespace MacroDeckHost.Integrations.Feeds.Actions;

internal sealed class FeedRefreshAction : IDynamicOptionsActionDefinition
{
	// The id is bound in users' flows, so it stays as it is.
	public const string ActionId = "refresh";

	public const string FeedParameter = FeedEventPayload.Feed;

	public static readonly TimeSpan Bound = TimeSpan.FromMinutes(2);

	private readonly FeedsIntegration _integration;
	private readonly TimeProvider _time;

	public FeedRefreshAction(FeedsIntegration integration, TimeProvider time)
	{
		_integration = integration;
		_time = time;
	}

	public string Id => ActionId;

	public LocalizedText Name => ActionStrings.Refresh.Name();

	public LocalizedText Description => ActionStrings.Refresh.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(FeedParameter,
			Params.Feed(),
			Params.FeedDescription(),
			placeholder: Params.AllFeeds())
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(this);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(_integration.FeedOptions());
	}

	private async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
	{
		var runtimes = _integration.Runtimes;
		IReadOnlyList<FeedRuntime> targets;
		if (context.Parameters.GetValueOrDefault(FeedParameter) is string { Length: > 0 } selected)
		{
			if (!Guid.TryParse(selected, out var entryId)
				|| runtimes.FirstOrDefault(runtime => runtime.EntryId == entryId) is not { } runtime)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound, Errors.FeedNotFound());
			}

			targets = [runtime];
		}
		else if (runtimes.Count == 0)
		{
			return ActionResult.Failed(ActionErrorCodes.NotConfigured, WidgetStrings.NoFeeds());
		}
		else
		{
			targets = runtimes;
		}

		using var bound = new CancellationTokenSource(Bound, _time);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, bound.Token);
		string?[] errors;
		try
		{
			errors = await Task.WhenAll(targets.Select(runtime => RefreshAsync(runtime, linked.Token)))
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (bound.IsCancellationRequested
			&& !context.CancellationToken.IsCancellationRequested)
		{
			return ActionResult.Failed(ActionErrorCodes.Timeout, Errors.Timeout());
		}

		var failed = errors.OfType<string>().ToList();
		if (failed.Count == 0)
		{
			return ActionResult.Success();
		}

		return ActionResult.Failed(ActionErrorCodes.ProviderError,
			targets.Count == 1 ? FeedErrorText.Describe(failed[0]) : Errors.SomeFeedsFailed());
	}

	private static async Task<string?> RefreshAsync(FeedRuntime runtime, CancellationToken cancellationToken)
	{
		try
		{
			await runtime.RefreshAsync(cancellationToken).ConfigureAwait(false);
		}
		// A configuration reload can stop the runtime mid-refresh; its successor polls on its own.
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
			&& !cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		var snapshot = runtime.Snapshot;
		return snapshot.Status == FeedStatus.Error ? snapshot.Error ?? FeedErrorCodes.Network : null;
	}

	private sealed class Executor(FeedRefreshAction action) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			ArgumentNullException.ThrowIfNull(context);

			return action.ExecuteAsync(context);
		}
	}
}
