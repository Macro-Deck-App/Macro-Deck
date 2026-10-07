using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.MicrosoftCalendar;

internal sealed class MicrosoftCalendarAccount
{
	private readonly ConcurrentDictionary<string, byte> _unreadable = new(StringComparer.Ordinal);
	private long _notBeforeTicks;

	public MicrosoftCalendarAccount(
		Guid entryId,
		string displayName,
		string? email,
		string accountKey,
		IReadOnlySet<string> selectedCalendarIds,
		MicrosoftTokenProvider tokens)
	{
		EntryId = entryId;
		Id = entryId.ToString("D");
		DisplayName = displayName;
		Email = email;
		AccountKey = accountKey;
		SelectedCalendarIds = selectedCalendarIds;
		Tokens = tokens;
	}

	public Guid EntryId { get; }

	public string Id { get; }

	public string DisplayName { get; }

	public string? Email { get; }

	public string AccountKey { get; }

	public IReadOnlySet<string> SelectedCalendarIds { get; }

	public MicrosoftTokenProvider Tokens { get; }

	public MicrosoftCalendarList? RecentCalendars { get; set; }

	public DateTimeOffset ThrottledUntil
	{
		get => new(Interlocked.Read(ref _notBeforeTicks), TimeSpan.Zero);
		set => Interlocked.Exchange(ref _notBeforeTicks, value.UtcTicks);
	}

	public bool IsSelected(string calendarId) => SelectedCalendarIds.Contains(calendarId);

	public IReadOnlyCollection<string> UnreadableCalendarIds => _unreadable.Keys.ToList();

	public void MarkUnreadable(string calendarId) => _unreadable[calendarId] = 0;

	public void MarkReadable(string calendarId) => _unreadable.TryRemove(calendarId, out _);
}

internal sealed record MicrosoftCalendarList(IReadOnlyList<CalendarInfo> Calendars, DateTimeOffset ReadAt);

internal sealed class MicrosoftCalendarAccountManager
{
	internal const string SignInIssuePrefix = "sign-in-expired:";
	internal const string DuplicateIssuePrefix = "duplicate-account:";
	internal const string UnreadableCalendarIssuePrefix = "calendars-unreadable:";

	private readonly Func<IMicrosoftOAuthClient> _oauthClientFactory;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	private volatile IReadOnlyList<MicrosoftCalendarAccount> _accounts = [];
	private volatile IReadOnlyList<CalendarAccount> _calendarAccounts = [];
	private volatile IReadOnlyList<StaleEntry> _staleEntries = [];

	public MicrosoftCalendarAccountManager(
		Func<IMicrosoftOAuthClient> oauthClientFactory,
		TimeProvider time,
		ILogger logger)
	{
		_oauthClientFactory = oauthClientFactory;
		_time = time;
		_logger = logger;
	}

	public IReadOnlyList<CalendarAccount> CalendarAccounts => _calendarAccounts;

	public MicrosoftCalendarAccount? Resolve(string accountId)
		=> _accounts.FirstOrDefault(account => string.Equals(account.Id, accountId, StringComparison.Ordinal));

	public IReadOnlySet<string>? SelectionOf(string accountKey)
		=> _accounts.FirstOrDefault(account =>
				string.Equals(account.AccountKey, accountKey, StringComparison.OrdinalIgnoreCase))
			?.SelectedCalendarIds;

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

		_logger.Information("Outlook Calendar configured with {Count} account(s)", accounts.Count);
	}

	public IReadOnlyList<IntegrationIssue> Issues()
	{
		var issues = new List<IntegrationIssue>();

		foreach (var account in _accounts.Where(a => a.Tokens.NeedsReauthorization))
		{
			issues.Add(new IntegrationIssue
			{
				Id = SignInIssuePrefix + account.Id,
				Title = AppStrings.Integrations.MicrosoftCalendar.Issues.SignInExpiredTitle(
					account: account.DisplayName),
				Description = AppStrings.Integrations.MicrosoftCalendar.Issues.SignInExpiredDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.Twitch.Issues.ReconnectAction()
			});
		}

		foreach (var account in _accounts.Where(a => !a.Tokens.NeedsReauthorization &&
			(a.SelectedCalendarIds.Count == 0 || a.UnreadableCalendarIds.Count > 0)))
		{
			issues.Add(new IntegrationIssue
			{
				Id = UnreadableCalendarIssuePrefix + account.Id,
				Title = AppStrings.Integrations.MicrosoftCalendar.Issues.CalendarsUnreadableTitle(
					account: account.DisplayName),
				Description = AppStrings.Integrations.MicrosoftCalendar.Issues.CalendarsUnreadableDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		foreach (var stale in _staleEntries)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DuplicateIssuePrefix + stale.EntryId.ToString("D"),
				Title = AppStrings.Integrations.MicrosoftCalendar.Issues.DuplicateAccountTitle(),
				Description = AppStrings.Integrations.MicrosoftCalendar.Issues.DuplicateAccountDescription(
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

	internal static IReadOnlyList<string> ParseCalendarIds(object? value)
	{
		if (value is not string { Length: > 0 } text)
		{
			return [];
		}

		try
		{
			using var document = JsonDocument.Parse(text);
			return document.RootElement.ValueKind is JsonValueKind.Array
				? document.RootElement.EnumerateArray()
					.Where(item => item.ValueKind is JsonValueKind.String && item.GetString() is { Length: > 0 })
					.Select(item => item.GetString()!)
					.Distinct(StringComparer.Ordinal)
					.ToList()
				: [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private MicrosoftCalendarAccount Build(IIntegrationConfig config, Candidate candidate)
	{
		var entryId = candidate.EntryId;
		var tokens = new MicrosoftTokenProvider(entryId,
			candidate.ClientId,
			candidate.Tenant,
			candidate.Tokens,
			_oauthClientFactory(),
			(refreshed, rotated) => PersistAsync(config, entryId, refreshed, rotated),
			_time,
			_logger);

		var displayName = string.IsNullOrWhiteSpace(candidate.Title)
			? candidate.Email ?? entryId.ToString("D")
			: candidate.Title.Trim();

		return new MicrosoftCalendarAccount(entryId,
			displayName,
			candidate.Email,
			candidate.AccountKey,
			candidate.CalendarIds.ToHashSet(StringComparer.Ordinal),
			tokens);
	}

	private static async Task PersistAsync(
		IIntegrationConfig config,
		Guid entryId,
		MicrosoftTokens tokens,
		bool rotated)
	{
		// The rotated refresh token goes first: Microsoft may already have retired the old one.
		if (rotated)
		{
			await config.SetSecretAsync(entryId, MicrosoftCalendarConfigKeys.RefreshToken, tokens.RefreshToken);
		}

		await config.SetSecretAsync(entryId, MicrosoftCalendarConfigKeys.AccessToken, tokens.AccessToken);
		await config.SetStringAsync(entryId,
			MicrosoftCalendarConfigKeys.ExpiresAt,
			tokens.ExpiresAt.ToString("o", CultureInfo.InvariantCulture));
	}

	private async Task<Candidate?> ReadCandidateAsync(
		IIntegrationConfig config,
		ConfigEntrySnapshot entry,
		int order,
		CancellationToken cancellationToken)
	{
		var clientId = await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.ClientId, cancellationToken);
		var refreshToken
			= await config.GetSecretAsync(entry.Id, MicrosoftCalendarConfigKeys.RefreshToken, cancellationToken);

		if (string.IsNullOrEmpty(refreshToken))
		{
			_logger.Warning("Outlook Calendar config entry {EntryId} is incomplete; skipping", entry.Id);
			return null;
		}

		var tenant = await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.Tenant, cancellationToken);
		var signInClientId
			= await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.SignInClientId, cancellationToken);
		var signInTenant
			= await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.SignInTenant, cancellationToken);
		var accessToken
			= await config.GetSecretAsync(entry.Id, MicrosoftCalendarConfigKeys.AccessToken, cancellationToken);
		var expiresAt = await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.ExpiresAt, cancellationToken);
		var email = await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.Email, cancellationToken);
		var accountKey
			= await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.AccountKey, cancellationToken);
		var connectedAt
			= await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.ConnectedAt, cancellationToken);
		var calendarIds
			= await config.GetStringAsync(entry.Id, MicrosoftCalendarConfigKeys.CalendarIds, cancellationToken);

		email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
		return new Candidate(entry.Id,
			entry.Title,
			MicrosoftOAuth.EffectiveClientId(string.IsNullOrWhiteSpace(signInClientId) ? clientId : signInClientId),
			MicrosoftOAuth.NormalizeTenant(string.IsNullOrWhiteSpace(signInTenant) ? tenant : signInTenant),
			email,
			string.IsNullOrWhiteSpace(accountKey) ? email ?? entry.Id.ToString("D") : accountKey.Trim(),
			ParseCalendarIds(calendarIds),
			ParseTimestamp(connectedAt) ?? DateTimeOffset.MinValue,
			new MicrosoftTokens(accessToken ?? string.Empty,
				refreshToken,
				string.IsNullOrEmpty(accessToken)
					? DateTimeOffset.MinValue
					: ParseTimestamp(expiresAt) ?? DateTimeOffset.MinValue),
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
		string Tenant,
		string? Email,
		string AccountKey,
		IReadOnlyList<string> CalendarIds,
		DateTimeOffset ConnectedAt,
		MicrosoftTokens Tokens,
		int Order);

	private sealed record StaleEntry(Guid EntryId, string Title);
}
