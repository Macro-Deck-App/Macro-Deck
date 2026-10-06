using System.Globalization;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.GoogleCalendar;

internal sealed class GoogleCalendarAccount
{
	public GoogleCalendarAccount(Guid entryId, string displayName, string? email, GoogleTokenProvider tokens)
	{
		EntryId = entryId;
		Id = entryId.ToString("D");
		DisplayName = displayName;
		Email = email;
		Tokens = tokens;
	}

	public Guid EntryId { get; }

	public string Id { get; }

	public string DisplayName { get; }

	public string? Email { get; }

	public GoogleTokenProvider Tokens { get; }

	public GoogleCalendarList? RecentCalendars { get; set; }
}

internal sealed record GoogleCalendarList(IReadOnlyList<CalendarInfo> Calendars, DateTimeOffset ReadAt);

internal sealed class GoogleCalendarAccountManager
{
	internal const string SignInIssuePrefix = "sign-in-expired:";
	internal const string DuplicateIssuePrefix = "duplicate-account:";

	private readonly Func<IGoogleOAuthClient> _oauthClientFactory;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	private volatile IReadOnlyList<GoogleCalendarAccount> _accounts = [];
	private volatile IReadOnlyList<CalendarAccount> _calendarAccounts = [];
	private volatile IReadOnlyList<StaleEntry> _staleEntries = [];

	public GoogleCalendarAccountManager(Func<IGoogleOAuthClient> oauthClientFactory, TimeProvider time, ILogger logger)
	{
		_oauthClientFactory = oauthClientFactory;
		_time = time;
		_logger = logger;
	}

	public IReadOnlyList<CalendarAccount> CalendarAccounts => _calendarAccounts;

	public GoogleCalendarAccount? Resolve(string accountId)
		=> _accounts.FirstOrDefault(account => string.Equals(account.Id, accountId, StringComparison.Ordinal));

	public async Task ReloadAsync(IIntegrationConfig config, CancellationToken cancellationToken = default)
	{
		await StopAsync();

		var candidates = new List<Candidate>();
		var entries = await config.GetEntriesAsync(cancellationToken);
		for (var order = 0; order < entries.Count; order++)
		{
			if (await ReadCandidateAsync(config, entries[order], order, cancellationToken) is { } candidate)
			{
				candidates.Add(candidate);
			}
		}

		var winners = new List<Candidate>();
		var stale = new List<StaleEntry>();
		foreach (var group in candidates.GroupBy(c => c.AccountKey, StringComparer.OrdinalIgnoreCase))
		{
			var ordered = group.OrderByDescending(c => c.ConnectedAt).ThenByDescending(c => c.Order).ToList();
			winners.Add(ordered[0]);
			stale.AddRange(ordered.Skip(1).Select(c => new StaleEntry(c.EntryId, c.Title)));
		}

		var accounts = winners.OrderBy(c => c.Order).Select(c => Build(config, c)).ToList();
		_accounts = accounts;
		_calendarAccounts =
			[.. accounts.Select(a => new CalendarAccount { Id = a.Id, DisplayName = a.DisplayName })];
		_staleEntries = stale;

		_logger.Information("Google Calendar configured with {Count} account(s)", accounts.Count);
	}

	public IReadOnlyList<IntegrationIssue> Issues()
	{
		var issues = new List<IntegrationIssue>();

		foreach (var account in _accounts.Where(a => a.Tokens.NeedsReauthorization))
		{
			issues.Add(new IntegrationIssue
			{
				Id = SignInIssuePrefix + account.Id,
				Title = AppStrings.Integrations.GoogleCalendar.Issues.SignInExpiredTitle(account: account.DisplayName),
				Description = AppStrings.Integrations.GoogleCalendar.Issues.SignInExpiredDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.Twitch.Issues.ReconnectAction()
			});
		}

		foreach (var stale in _staleEntries)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DuplicateIssuePrefix + stale.EntryId.ToString("D"),
				Title = AppStrings.Integrations.GoogleCalendar.Issues.DuplicateAccountTitle(),
				Description = AppStrings.Integrations.GoogleCalendar.Issues.DuplicateAccountDescription(
					title: stale.Title),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return issues;
	}

	public async Task StopAsync()
	{
		var accounts = _accounts;
		_accounts = [];
		_calendarAccounts = [];
		_staleEntries = [];
		await Task.WhenAll(accounts.Select(account => account.Tokens.StopAsync()));
		foreach (var account in accounts)
		{
			account.Tokens.Dispose();
		}
	}

	private GoogleCalendarAccount Build(IIntegrationConfig config, Candidate candidate)
	{
		var entryId = candidate.EntryId;
		var tokens = new GoogleTokenProvider(entryId,
			candidate.ClientId,
			candidate.ClientSecret,
			candidate.Tokens,
			_oauthClientFactory(),
			(refreshed, rotated) => PersistAsync(config, entryId, refreshed, rotated),
			_time,
			_logger);

		var displayName = string.IsNullOrWhiteSpace(candidate.Title)
			? candidate.Email ?? entryId.ToString("D")
			: candidate.Title.Trim();

		return new GoogleCalendarAccount(entryId, displayName, candidate.Email, tokens);
	}

	private static async Task PersistAsync(IIntegrationConfig config, Guid entryId, GoogleTokens tokens, bool rotated)
	{
		// The rotated refresh token goes first: Google may already have retired the old one.
		if (rotated)
		{
			await config.SetSecretAsync(entryId, GoogleCalendarConfigKeys.RefreshToken, tokens.RefreshToken);
		}

		await config.SetSecretAsync(entryId, GoogleCalendarConfigKeys.AccessToken, tokens.AccessToken);
		await config.SetStringAsync(entryId,
			GoogleCalendarConfigKeys.ExpiresAt,
			tokens.ExpiresAt.ToString("o", CultureInfo.InvariantCulture));
	}

	private async Task<Candidate?> ReadCandidateAsync(
		IIntegrationConfig config,
		ConfigEntrySnapshot entry,
		int order,
		CancellationToken cancellationToken)
	{
		var clientId = await config.GetStringAsync(entry.Id, GoogleCalendarConfigKeys.ClientId, cancellationToken);
		var clientSecret
			= await config.GetSecretAsync(entry.Id, GoogleCalendarConfigKeys.ClientSecret, cancellationToken);
		var refreshToken
			= await config.GetSecretAsync(entry.Id, GoogleCalendarConfigKeys.RefreshToken, cancellationToken);

		if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(refreshToken))
		{
			_logger.Warning("Google Calendar config entry {EntryId} is incomplete; skipping", entry.Id);
			return null;
		}

		var accessToken = await config.GetSecretAsync(entry.Id, GoogleCalendarConfigKeys.AccessToken, cancellationToken);
		var expiresAt = await config.GetStringAsync(entry.Id, GoogleCalendarConfigKeys.ExpiresAt, cancellationToken);
		var email = await config.GetStringAsync(entry.Id, GoogleCalendarConfigKeys.Email, cancellationToken);
		var connectedAt
			= await config.GetStringAsync(entry.Id, GoogleCalendarConfigKeys.ConnectedAt, cancellationToken);

		return new Candidate(entry.Id,
			entry.Title,
			clientId,
			clientSecret,
			string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
			ParseTimestamp(connectedAt) ?? DateTimeOffset.MinValue,
			new GoogleTokens(accessToken ?? string.Empty,
				refreshToken,
				string.IsNullOrEmpty(accessToken) ? DateTimeOffset.MinValue : ParseTimestamp(expiresAt) ?? DateTimeOffset.MinValue),
			order);
	}

	private static DateTimeOffset? ParseTimestamp(string? value)
		=> DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
			? parsed
			: null;

	private sealed record Candidate(
		Guid EntryId,
		string Title,
		string ClientId,
		string ClientSecret,
		string? Email,
		DateTimeOffset ConnectedAt,
		GoogleTokens Tokens,
		int Order)
	{
		public string AccountKey => Email ?? EntryId.ToString("D");
	}

	private sealed record StaleEntry(Guid EntryId, string Title);
}
