using System.Globalization;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Protocol;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch;

internal sealed class TwitchAccountManager : IDisposable
{
	internal const string DuplicateIssuePrefix = "duplicate-account:";
	internal const string TokenIssuePrefix = "token-invalid:";
	internal const string MissingScopeIssuePrefix = "missing-scopes:";

	private readonly Func<ITwitchOAuthClient> _oauthClientFactory;
	private readonly Func<TwitchAccount, TwitchTokenProvider, ITwitchHelixClient> _helixFactory;
	private readonly ILogger _logger;

	private TwitchEventEmitter? _emitter;

	private volatile List<TwitchAccountConnection> _connections = [];
	private volatile List<StaleEntry> _staleEntries = [];

	private TwitchTokenPersister? _persister;

	public TwitchAccountManager(
		Func<ITwitchOAuthClient> oauthClientFactory,
		ILogger logger,
		Func<TwitchAccount, TwitchTokenProvider, ITwitchHelixClient>? helixFactory = null)
	{
		_oauthClientFactory = oauthClientFactory;
		_logger = logger;
		_helixFactory = helixFactory ??
			((account, tokens) =>
				new TwitchHelixClient(TwitchHelixClient.CreateApi(account.ClientId), tokens, logger));
	}

	public IReadOnlyList<TwitchAccountConnection> Connections => _connections;

	public async Task ReloadAsync(
		IIntegrationConfig config,
		TwitchEventEmitter? emitter = null,
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

		_persister = new TwitchTokenPersister(config, _logger);

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

		_connections = winners.Select(Build).ToList();
		_staleEntries = stale;

		_logger.Information("Twitch configured with {Count} account(s)", _connections.Count);
	}

	public TwitchAccountConnection? Resolve(string? userId)
	{
		var connections = _connections;

		if (string.IsNullOrEmpty(userId))
		{
			return connections.Count > 0 ? connections[0] : null;
		}

		return connections.FirstOrDefault(c => string.Equals(c.Account.UserId, userId, StringComparison.Ordinal));
	}

	public IReadOnlyList<ActionParameterOption> AccountOptions()
		=> _connections
			.Select(c => new ActionParameterOption { Value = c.Account.UserId, Label = c.Account.Label })
			.ToList();

	public IReadOnlyList<IntegrationIssue> Issues()
	{
		var issues = new List<IntegrationIssue>();

		foreach (var connection in _connections.Where(c => c.NeedsReauthorization))
		{
			issues.Add(new IntegrationIssue
			{
				Id = TokenIssuePrefix + connection.Account.UserId,
				Title = AppStrings.Integrations.Twitch.Issues.SignInExpiredTitle(account: connection.Account.Label),
				Description = AppStrings.Integrations.Twitch.Issues.SignInExpiredDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.Twitch.Issues.ReconnectAction()
			});
		}

		foreach (var connection in _connections.Where(c => c.MissingScopeEvents.Count > 0))
		{
			// Event ids, not the localized event names: the reader may resolve them in a different
			// culture than the host, so a name joined here could not be translated for them.
			var ids = string.Join(", ", connection.MissingScopeEvents);

			issues.Add(new IntegrationIssue
			{
				Id = MissingScopeIssuePrefix + connection.Account.UserId,
				Title = AppStrings.Integrations.Twitch.Issues.MissingScopeTitle(account: connection.Account.Label),
				Description = AppStrings.Integrations.Twitch.Issues.MissingScopeDescription(events: ids),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = AppStrings.Integrations.Twitch.Issues.ReconnectAction()
			});
		}

		foreach (var stale in _staleEntries)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DuplicateIssuePrefix + stale.UserId,
				Title = AppStrings.Integrations.Twitch.Issues.DuplicateAccountTitle(),
				Description = AppStrings.Integrations.Twitch.Issues.DuplicateAccountDescription(title: stale.Title),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return issues;
	}

	public Task FlushAsync() => _persister?.FlushAsync() ?? Task.CompletedTask;

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
		DisposeConnections();
		_persister?.Dispose();
		_persister = null;
	}

	private static (List<Candidate> Winners, List<StaleEntry> Stale) Reconcile(List<Candidate> candidates)
	{
		var winners = new List<Candidate>();
		var stale = new List<StaleEntry>();

		foreach (var group in candidates.GroupBy(c => c.UserId, StringComparer.Ordinal))
		{
			var ordered = group.OrderByDescending(c => c.ConnectedAt).ThenByDescending(c => c.Order).ToList();
			winners.Add(ordered[0]);
			stale.AddRange(ordered.Skip(1).Select(c => new StaleEntry(c.UserId, c.Title)));
		}

		return (winners, stale);
	}

	private async Task<Candidate?> ReadCandidate(
		IIntegrationConfig config,
		ConfigEntrySnapshot entry,
		int order,
		CancellationToken cancellationToken)
	{
		var clientId = await config.GetStringAsync(entry.Id, TwitchConfigKeys.ClientId, cancellationToken);
		var userId = await config.GetStringAsync(entry.Id, TwitchConfigKeys.UserId, cancellationToken);
		var login = await config.GetStringAsync(entry.Id, TwitchConfigKeys.Login, cancellationToken);
		var accessToken = await config.GetSecretAsync(entry.Id, TwitchConfigKeys.AccessToken, cancellationToken);
		var refreshToken = await config.GetSecretAsync(entry.Id, TwitchConfigKeys.RefreshToken, cancellationToken);

		if (string.IsNullOrEmpty(clientId) ||
			string.IsNullOrEmpty(userId) ||
			string.IsNullOrEmpty(login) ||
			string.IsNullOrEmpty(accessToken) ||
			string.IsNullOrEmpty(refreshToken))
		{
			// One broken entry must not take the working ones with it, so it is skipped, not thrown on.
			_logger.Warning("Twitch config entry {EntryId} is incomplete; skipping", entry.Id);
			return null;
		}

		var displayName = await config.GetStringAsync(entry.Id, TwitchConfigKeys.DisplayName, cancellationToken);
		var scopes = await config.GetStringAsync(entry.Id, TwitchConfigKeys.Scopes, cancellationToken);
		var expiresAt = await config.GetStringAsync(entry.Id, TwitchConfigKeys.ExpiresAt, cancellationToken);
		var connectedAt = await config.GetStringAsync(entry.Id, TwitchConfigKeys.ConnectedAt, cancellationToken);

		return new Candidate(entry.Id,
			entry.Title,
			clientId,
			userId,
			login,
			string.IsNullOrEmpty(displayName) ? login : displayName,
			ParseTimestamp(connectedAt) ?? DateTimeOffset.MinValue,
			new TwitchTokens(accessToken,
				refreshToken,
				ParseTimestamp(expiresAt) ?? DateTimeOffset.MinValue,
				scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []),
			order);
	}

	private TwitchAccountConnection Build(Candidate candidate)
	{
		var account = new TwitchAccount(candidate.EntryId,
			candidate.ClientId,
			candidate.UserId,
			candidate.Login,
			candidate.DisplayName,
			TwitchSlug.ForLogin(candidate.Login, candidate.UserId),
			candidate.ConnectedAt);

		var oauthClient = _oauthClientFactory();
		var tokens = new TwitchTokenProvider(candidate.EntryId,
			candidate.ClientId,
			candidate.Tokens,
			oauthClient,
			_persister!,
			_logger);

		return new TwitchAccountConnection(account,
			tokens,
			oauthClient,
			_helixFactory(account, tokens),
			_emitter,
			_logger);
	}

	public void StartAll()
	{
		foreach (var connection in _connections)
		{
			connection.Start();
		}
	}

	private void DisposeConnections()
	{
		foreach (var connection in _connections)
		{
			connection.Dispose();
		}

		_connections = [];
		_staleEntries = [];
	}

	private async Task StopConnectionsAsync()
	{
		var connections = _connections;
		_connections = [];
		_staleEntries = [];
		await Task.WhenAll(connections.Select(connection => connection.StopAsync()));
	}

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
		string UserId,
		string Login,
		string DisplayName,
		DateTimeOffset ConnectedAt,
		TwitchTokens Tokens,
		int Order);

	private sealed record StaleEntry(string UserId, string Title);
}
