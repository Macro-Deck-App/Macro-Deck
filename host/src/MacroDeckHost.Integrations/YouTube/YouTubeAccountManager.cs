using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.YouTube;

internal sealed class YouTubeAccountManager : IDisposable
{
	internal const string DuplicateIssuePrefix = "duplicate-account:";
	internal const string TokenIssuePrefix = "token-invalid:";
	internal const string QuotaIssuePrefix = "quota-exhausted:";

	private readonly Func<IYouTubeOAuthClient> _oauthClientFactory;
	private readonly Func<YouTubeAccount, YouTubeTokenProvider, YouTubeQuotaBudget, IYouTubeApiClient> _apiFactory;
	private readonly YouTubeQuotaBudgets _budgets;
	private readonly YouTubePollerOptions _options;
	private readonly ILogger _logger;

	private YouTubeEventEmitter? _emitter;
	private IStreamChatSink? _chatSink;

	private volatile List<YouTubeAccountConnection> _connections = [];
	private volatile List<StaleEntry> _staleEntries = [];

	private YouTubeTokenPersister? _persister;

	public YouTubeAccountManager(
		Func<IYouTubeOAuthClient> oauthClientFactory,
		ILogger logger,
		Func<YouTubeAccount, YouTubeTokenProvider, YouTubeQuotaBudget, IYouTubeApiClient>? apiFactory = null,
		YouTubeQuotaBudgets? budgets = null,
		YouTubePollerOptions? options = null)
	{
		_oauthClientFactory = oauthClientFactory;
		_logger = logger;
		_budgets = budgets ?? YouTubeQuotaBudgets.Shared;
		_options = options ?? new YouTubePollerOptions();
		_apiFactory = apiFactory ??
			((_, tokens, budget) => new YouTubeApiClient(tokens.GetAsync,
				tokens.ForceRefreshAsync,
				budget,
				logger));
	}

	public IReadOnlyList<YouTubeAccountConnection> Connections => _connections;

	public void UseChatSink(IStreamChatSink? chatSink) => _chatSink = chatSink;

	public IReadOnlyList<ChatAccount> ChatAccounts()
		=> [.. _connections.Select(c => new ChatAccount(c.Account.ChannelId, c.Account.Label))];

	public IReadOnlyList<StreamStatsAccount> StatsAccounts()
		=> [.. _connections.Select(c => new StreamStatsAccount(c.Account.ChannelId,
			c.Account.Label,
			YouTubeVariables.AccountPrefix(c.Account.VariableKey)))];

	public async Task ReloadAsync(
		IIntegrationConfig config,
		YouTubeEventEmitter? emitter = null,
		CancellationToken cancellationToken = default)
	{
		_emitter = emitter;
		await StopConnectionsAsync();

		if (_persister is { } previousPersister)
		{
			try
			{
				await previousPersister.CompleteAsync();
			}
			finally
			{
				previousPersister.Dispose();
			}
		}

		_persister = new YouTubeTokenPersister(config, _logger);

		var candidates = new List<Candidate>();
		var entries = await config.GetEntriesAsync(cancellationToken);
		for (var order = 0; order < entries.Count; order++)
		{
			var candidate = await ReadCandidate(config, entries[order], order, cancellationToken);
			if (candidate is not null)
			{
				candidates.Add(candidate);
			}
		}

		var (winners, stale) = Reconcile(candidates);

		var usedKeys = new HashSet<string>(StringComparer.Ordinal);
		var limits = winners.GroupBy(winner => winner.ClientId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Max(winner => winner.DailyQuota), StringComparer.Ordinal);
		_connections = winners.Select(winner => Build(winner, usedKeys, limits[winner.ClientId])).ToList();
		_staleEntries = stale;

		_logger.Information("YouTube configured with {Count} channel(s)", _connections.Count);
	}

	public YouTubeAccountConnection? Resolve(string? channelId)
	{
		var connections = _connections;

		if (string.IsNullOrEmpty(channelId))
		{
			return connections.Count > 0 ? connections[0] : null;
		}

		return connections.FirstOrDefault(c =>
			string.Equals(c.Account.ChannelId, channelId, StringComparison.Ordinal));
	}

	public IReadOnlyList<ActionParameterOption> AccountOptions()
		=> _connections
			.Select(c => new ActionParameterOption { Value = c.Account.ChannelId, Label = c.Account.Label })
			.ToList();

	public IReadOnlyList<IntegrationIssue> Issues()
	{
		var issues = new List<IntegrationIssue>();

		foreach (var connection in _connections.Where(c => c.NeedsReauthorization))
		{
			issues.Add(new IntegrationIssue
			{
				Id = TokenIssuePrefix + connection.Account.ChannelId,
				Title = AppStrings.Integrations.YouTube.Issues.SignInExpiredTitle(account: connection.Account.Label),
				Description = AppStrings.Integrations.YouTube.Issues.SignInExpiredDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.YouTube.Issues.ReconnectAction()
			});
		}

		foreach (var connection in _connections
			.Where(c => c.Budget.Level is YouTubeQuotaLevel.Paused)
			.DistinctBy(c => c.Account.ClientId, StringComparer.Ordinal))
		{
			issues.Add(new IntegrationIssue
			{
				Id = QuotaIssuePrefix + connection.Account.ClientId,
				Title = AppStrings.Integrations.YouTube.Issues.QuotaExhaustedTitle(),
				Description = AppStrings.Integrations.YouTube.Issues.QuotaExhaustedDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		foreach (var stale in _staleEntries)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DuplicateIssuePrefix + stale.ChannelId,
				Title = AppStrings.Integrations.YouTube.Issues.DuplicateChannelTitle(),
				Description = AppStrings.Integrations.YouTube.Issues.DuplicateChannelDescription(title: stale.Title),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return issues;
	}

	public void StartAll()
	{
		foreach (var connection in _connections)
		{
			connection.Start();
		}
	}

	public async Task ShutdownAsync()
	{
		await StopConnectionsAsync();
		var persister = _persister;
		_persister = null;
		if (persister is null)
		{
			return;
		}

		try
		{
			await persister.CompleteAsync();
		}
		finally
		{
			persister.Dispose();
		}
	}

	public void Dispose()
	{
		foreach (var connection in _connections)
		{
			connection.Dispose();
		}

		_connections = [];
		_staleEntries = [];
		_persister?.Dispose();
		_persister = null;
	}

	private static (List<Candidate> Winners, List<StaleEntry> Stale) Reconcile(List<Candidate> candidates)
	{
		var winners = new List<Candidate>();
		var stale = new List<StaleEntry>();

		foreach (var group in candidates.GroupBy(c => c.ChannelId, StringComparer.Ordinal))
		{
			var ordered = group.OrderByDescending(c => c.ConnectedAt).ThenByDescending(c => c.Order).ToList();
			winners.Add(ordered[0]);
			stale.AddRange(ordered.Skip(1).Select(c => new StaleEntry(c.ChannelId, c.Title)));
		}

		return (winners, stale);
	}

	private async Task<Candidate?> ReadCandidate(
		IIntegrationConfig config,
		ConfigEntrySnapshot entry,
		int order,
		CancellationToken cancellationToken)
	{
		var clientId = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.ClientId, cancellationToken);
		var channelId = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.ChannelId, cancellationToken);
		var clientSecret = await config.GetSecretAsync(entry.Id, YouTubeConfigKeys.ClientSecret, cancellationToken);
		var accessToken = await config.GetSecretAsync(entry.Id, YouTubeConfigKeys.AccessToken, cancellationToken);
		var refreshToken = await config.GetSecretAsync(entry.Id, YouTubeConfigKeys.RefreshToken, cancellationToken);

		if (string.IsNullOrEmpty(clientId) ||
			string.IsNullOrEmpty(channelId) ||
			string.IsNullOrEmpty(clientSecret) ||
			string.IsNullOrEmpty(accessToken) ||
			string.IsNullOrEmpty(refreshToken))
		{
			_logger.Warning("YouTube config entry {EntryId} is incomplete; skipping", entry.Id);
			return null;
		}

		var title = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.ChannelTitle, cancellationToken);
		var handle = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.ChannelHandle, cancellationToken);
		var quota = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.DailyQuota, cancellationToken);
		var scopes = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.Scopes, cancellationToken);
		var expiresAt = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.ExpiresAt, cancellationToken);
		var connectedAt = await config.GetStringAsync(entry.Id, YouTubeConfigKeys.ConnectedAt, cancellationToken);

		return new Candidate(entry.Id,
			entry.Title,
			clientId,
			clientSecret,
			channelId,
			string.IsNullOrEmpty(title) ? channelId : title,
			string.IsNullOrEmpty(title) ? null : title,
			string.IsNullOrEmpty(handle) ? null : handle,
			ParseQuota(quota),
			ParseTimestamp(connectedAt) ?? DateTimeOffset.MinValue,
			new YouTubeTokens(accessToken,
				refreshToken,
				ParseTimestamp(expiresAt) ?? DateTimeOffset.MinValue,
				scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []),
			order);
	}

	private YouTubeAccountConnection Build(Candidate candidate, HashSet<string> usedKeys, int dailyLimit)
	{
		var key = YouTubeSlug.ForChannel(candidate.Handle, candidate.StoredTitle, candidate.ChannelId);
		if (!usedKeys.Add(key))
		{
			key = YouTubeSlug.ForChannelId(candidate.ChannelId);
			usedKeys.Add(key);
		}

		var account = new YouTubeAccount(candidate.EntryId,
			candidate.ClientId,
			candidate.ChannelId,
			candidate.ChannelTitle,
			candidate.Handle,
			key,
			candidate.ConnectedAt);

		var budget = _budgets.For(candidate.ClientId, dailyLimit);
		var oauthClient = _oauthClientFactory();
		var tokens = new YouTubeTokenProvider(candidate.EntryId,
			candidate.ClientId,
			candidate.ClientSecret,
			candidate.Tokens,
			oauthClient,
			_persister!,
			_logger,
			_options.Time);

		return new YouTubeAccountConnection(account,
			tokens,
			oauthClient,
			_apiFactory(account, tokens, budget),
			budget,
			_emitter,
			_logger,
			_chatSink,
			_options);
	}

	private async Task StopConnectionsAsync()
	{
		var connections = _connections;
		_connections = [];
		_staleEntries = [];
		await Task.WhenAll(connections.Select(connection => connection.StopAsync()));
	}

	private static int ParseQuota(string? value)
		=> int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
			? parsed
			: YouTubeQuotaBudget.DefaultDailyLimit;

	private static DateTimeOffset? ParseTimestamp(string? value)
		=> DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out var parsed)
			? parsed
			: null;

	private sealed record Candidate(
		Guid EntryId,
		string Title,
		string ClientId,
		string ClientSecret,
		string ChannelId,
		string ChannelTitle,
		string? StoredTitle,
		string? Handle,
		int DailyQuota,
		DateTimeOffset ConnectedAt,
		YouTubeTokens Tokens,
		int Order);

	private sealed record StaleEntry(string ChannelId, string Title);
}
