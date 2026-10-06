using System.Net;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.GoogleCalendar;

[MacroDeckIntegration]
public sealed class GoogleCalendarIntegration
	: IIntegration, IConfigFlowProvider, ICalendarProvider, IIntegrationIconProvider, IIntegrationIssueProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.google-calendar";

	// A brand name alone, not a sentence, so it stays a literal rather than a translated key.
	internal const string BrandName = "Google Calendar";

	private static readonly ILogger _logger = IntegrationLog.For<GoogleCalendarIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();
	private static readonly TimeSpan _calendarListReuse = TimeSpan.FromMinutes(2);

	private readonly Func<IGoogleOAuthClient> _oauthClientFactory;
	private readonly GoogleCalendarApiClient _api;
	private readonly TimeProvider _time;
	private readonly GoogleCalendarAccountManager _accounts;

	public GoogleCalendarIntegration()
		: this(() => new GoogleOAuthClient(), new GoogleCalendarApiClient(), TimeProvider.System)
	{
	}

	internal GoogleCalendarIntegration(
		Func<IGoogleOAuthClient> oauthClientFactory,
		GoogleCalendarApiClient api,
		TimeProvider time)
	{
		_oauthClientFactory = oauthClientFactory;
		_api = api;
		_time = time;
		_accounts = new GoogleCalendarAccountManager(oauthClientFactory, time, _logger);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => BrandName;

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions => [];

	public bool AllowsMultipleConfigurations => true;

	public string IconMimeType => "image/svg+xml";

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new GoogleCalendarConfigFlow(_oauthClientFactory(), _time);

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await _accounts.ReloadAsync(context.Config);
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		await _accounts.StopAsync();
	}

	public IReadOnlyList<CalendarAccount> GetAccounts() => _accounts.CalendarAccounts;

	public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(
		string accountId,
		CancellationToken cancellationToken)
	{
		var account = RequireAccount(accountId);
		var calendars = await _api.GetCalendarsAsync(account.Tokens, cancellationToken);
		account.RecentCalendars = new GoogleCalendarList(calendars, _time.GetUtcNow());
		return calendars;
	}

	public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		string accountId,
		CalendarEventQuery query,
		CancellationToken cancellationToken)
	{
		var account = RequireAccount(accountId);
		var calendarIds = query.CalendarIds.Count > 0
			? query.CalendarIds
			: (await CalendarsOfAsync(account, cancellationToken)).Select(calendar => calendar.Id).ToList();

		var events = new List<CalendarEvent>();
		HttpRequestException? lastLostAccess = null;
		var read = 0;
		foreach (var calendarId in calendarIds.Distinct(StringComparer.Ordinal))
		{
			try
			{
				events.AddRange(await _api.GetEventsAsync(account.Tokens,
					calendarId,
					query.From,
					query.To,
					cancellationToken));
				read++;
			}
			// Only a missing calendar is skipped: Google also answers 403 for rate limits, which must keep
			// the account's last good events rather than blank this calendar.
			catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
			{
				_logger.Warning(exception,
					"Google Calendar no longer has calendar {CalendarId}; skipping it",
					calendarId);
				lastLostAccess = exception;
			}
		}

		// Losing every calendar is an account failure, such as a revoked scope, not an empty calendar.
		if (read == 0 && lastLostAccess is not null)
		{
			throw lastLostAccess;
		}

		return events;
	}

	public Task<CalendarEvent?> GetEventAsync(
		string accountId,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken)
		=> _api.GetEventAsync(RequireAccount(accountId).Tokens, calendarId, eventId, cancellationToken);

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(_accounts.Issues());

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId.StartsWith(GoogleCalendarAccountManager.SignInIssuePrefix, StringComparison.Ordinal)
			? IssueResolution.Ok(AppStrings.Integrations.GoogleCalendar.Issues.ReconnectResolution(),
				IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	public void Dispose() => _api.Dispose();

	private GoogleCalendarAccount RequireAccount(string accountId)
		=> _accounts.Resolve(accountId) ??
			throw new InvalidOperationException($"Google Calendar has no account '{accountId}'.");

	private async Task<IReadOnlyList<CalendarInfo>> CalendarsOfAsync(
		GoogleCalendarAccount account,
		CancellationToken cancellationToken)
	{
		if (account.RecentCalendars is { } recent && _time.GetUtcNow() - recent.ReadAt < _calendarListReuse)
		{
			return recent.Calendars;
		}

		return await GetCalendarsAsync(account.Id, cancellationToken);
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(GoogleCalendarIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("google-calendar-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
