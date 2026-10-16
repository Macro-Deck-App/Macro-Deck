using MacroDeckHost.Integrations.Twitch.Events;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed record TwitchSubscriptionReport(
	IReadOnlyList<string> Created,
	IReadOnlyList<string> MissingScope,
	IReadOnlyList<string> Unsupported,
	IReadOnlyList<string> Failed);

internal sealed class TwitchSubscriptionManager
{
	private const int MaxParallelism = 4;

	private readonly ITwitchHelixClient _helix;
	private readonly string _userId;
	private readonly Func<IReadOnlyList<string>> _grantedScopes;
	private readonly ILogger _logger;
	private readonly HashSet<TwitchSubscriptionSpec> _rejected = [];

	public TwitchSubscriptionManager(
		ITwitchHelixClient helix,
		string userId,
		Func<IReadOnlyList<string>> grantedScopes,
		ILogger logger)
	{
		_helix = helix;
		_userId = userId;
		_grantedScopes = grantedScopes;
		_logger = logger;
	}

	public async Task<TwitchSubscriptionReport> SubscribeAllAsync(
		string sessionId,
		CancellationToken cancellationToken)
	{
		var created = new List<string>();
		var missingScope = new List<string>();
		var unsupported = new List<string>();
		var failed = new List<string>();
		var granted = _grantedScopes();
		var recordLock = new Lock();

		var specs = TwitchEventCatalog.All.Where(spec => !_rejected.Contains(spec)).ToList();
		var skipped = TwitchEventCatalog.All.Where(_rejected.Contains).ToList();
		foreach (var spec in skipped)
		{
			Record(spec, TwitchSubscriptionResult.Unsupported);
		}

		if (skipped.Count > 0)
		{
			_logger.Information("Skipping {Count} Twitch event(s) Twitch rejected earlier: {Events}",
				skipped.Count,
				string.Join(", ", skipped.Select(spec => spec.EventId)));
		}

		if (specs.FirstOrDefault(spec => spec.Scope is null) is { } first)
		{
			specs.Remove(first);
			Record(first, await CreateAsync(first, sessionId, cancellationToken));
		}

		await Parallel.ForEachAsync(specs,
			new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
			async (spec, token) =>
			{
				var result = granted.Count > 0
					&& spec.Scope is { } scope
					&& !granted.Contains(scope, StringComparer.Ordinal)
						? TwitchSubscriptionResult.MissingScope
						: await CreateAsync(spec, sessionId, token);

				lock (recordLock)
				{
					Record(spec, result);
				}
			});

		if (missingScope.Count > 0)
		{
			_logger.Information("Twitch did not grant the permission for {Count} event(s): {Events}",
				missingScope.Count,
				string.Join(", ", missingScope));
		}

		return new TwitchSubscriptionReport(created, missingScope, unsupported, failed);

		void Record(TwitchSubscriptionSpec spec, TwitchSubscriptionResult result)
		{
			switch (result)
			{
				case TwitchSubscriptionResult.Created:
				case TwitchSubscriptionResult.Duplicate:
					created.Add(spec.EventId);
					break;

				case TwitchSubscriptionResult.MissingScope when !spec.IsFeed:
					missingScope.Add(spec.EventId);
					break;

				case TwitchSubscriptionResult.MissingScope:
					break;

				case TwitchSubscriptionResult.Unsupported:
					unsupported.Add(spec.EventId);
					_rejected.Add(spec);
					break;

				default:
					failed.Add(spec.EventId);
					break;
			}
		}
	}

	private Task<TwitchSubscriptionResult> CreateAsync(
		TwitchSubscriptionSpec spec,
		string sessionId,
		CancellationToken cancellationToken)
	{
		return _helix.CreateEventSubSubscriptionAsync(spec.Type,
			spec.Version,
			TwitchEventCatalog.BuildCondition(spec.Condition, _userId),
			sessionId,
			cancellationToken);
	}
}
