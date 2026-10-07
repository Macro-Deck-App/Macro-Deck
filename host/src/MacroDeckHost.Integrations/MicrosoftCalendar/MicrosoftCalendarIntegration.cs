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

namespace MacroDeckHost.Integrations.MicrosoftCalendar;

[MacroDeckIntegration]
public sealed class MicrosoftCalendarIntegration
	: IIntegration, IConfigFlowProvider, ICalendarProvider, IIntegrationIconProvider, IIntegrationIssueProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.microsoft-calendar";

	// A brand name alone, not a sentence, so it stays a literal rather than a translated key.
	internal const string BrandName = "Outlook Calendar";

	private static readonly ILogger _logger = IntegrationLog.For<MicrosoftCalendarIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();
	private static readonly TimeSpan _calendarListReuse = TimeSpan.FromMinutes(2);

	private readonly Func<IMicrosoftOAuthClient> _oauthClientFactory;
	private readonly MicrosoftGraphClient _graph;
	private readonly TimeProvider _time;
	private readonly MicrosoftCalendarAccountManager _accounts;

	public MicrosoftCalendarIntegration()
		: this(() => new MicrosoftOAuthClient(), new MicrosoftGraphClient(TimeProvider.System), TimeProvider.System)
	{
	}

	internal MicrosoftCalendarIntegration(
		Func<IMicrosoftOAuthClient> oauthClientFactory,
		MicrosoftGraphClient graph,
		TimeProvider time)
	{
		_oauthClientFactory = oauthClientFactory;
		_graph = graph;
		_time = time;
		_accounts = new MicrosoftCalendarAccountManager(oauthClientFactory, time, _logger);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => BrandName;

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions => [];

	public bool AllowsMultipleConfigurations => true;

	public string IconMimeType => "image/svg+xml";

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow()
		=> new MicrosoftCalendarConfigFlow(_oauthClientFactory(), _graph, _accounts.SelectionOf, _time);

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
		var listed = await _graph.GetCalendarsAsync(account.Tokens, account, cancellationToken);
		var listedIds = listed.Select(calendar => calendar.Id).ToHashSet(StringComparer.Ordinal);
		foreach (var calendarId in account.SelectedCalendarIds)
		{
			if (listedIds.Contains(calendarId))
			{
				account.MarkReadable(calendarId);
			}
			else
			{
				account.MarkUnreadable(calendarId);
			}
		}

		var calendars = listed
			.Where(calendar => account.IsSelected(calendar.Id))
			.Select(calendar => new CalendarInfo
			{
				Id = calendar.Id,
				Name = calendar.Name,
				Color = calendar.Color,
				IsPrimary = calendar.IsDefault
			})
			.ToList();
		account.RecentCalendars = new MicrosoftCalendarList(calendars, _time.GetUtcNow());
		return calendars;
	}

	public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
		string accountId,
		CalendarEventQuery query,
		CancellationToken cancellationToken)
	{
		var account = RequireAccount(accountId);
		var calendarIds = query.CalendarIds.Count > 0
			? query.CalendarIds.Where(account.IsSelected).ToList()
			: (await CalendarsOfAsync(account, cancellationToken)).Select(calendar => calendar.Id).ToList();

		var events = new List<CalendarEvent>();
		HttpRequestException? lastLostAccess = null;
		var read = 0;
		foreach (var calendarId in calendarIds.Distinct(StringComparer.Ordinal))
		{
			try
			{
				events.AddRange(await _graph.GetEventsAsync(account,
					calendarId,
					query.From,
					query.To,
					cancellationToken));
				account.MarkReadable(calendarId);
				read++;
			}
			// A deleted or no longer shared calendar is skipped and reported as an issue; throttling is a 429
			// and must keep the account's last good events instead.
			catch (HttpRequestException exception)
				when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
			{
				_logger.Warning(exception,
					"Outlook Calendar can no longer read one of the account's calendars; skipping it");
				account.MarkUnreadable(calendarId);
				lastLostAccess = exception;
			}
		}

		if (read == 0 && lastLostAccess is not null)
		{
			throw lastLostAccess;
		}

		return events;
	}

	public async Task<CalendarEvent?> GetEventAsync(
		string accountId,
		string calendarId,
		string eventId,
		CancellationToken cancellationToken)
	{
		var account = RequireAccount(accountId);
		return account.IsSelected(calendarId)
			? await _graph.GetEventAsync(account, calendarId, eventId, cancellationToken)
			: null;
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(_accounts.Issues());

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId.StartsWith(MicrosoftCalendarAccountManager.SignInIssuePrefix, StringComparison.Ordinal)
			? IssueResolution.Ok(AppStrings.Integrations.MicrosoftCalendar.Issues.ReconnectResolution(),
				IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	public void Dispose() => _graph.Dispose();

	private MicrosoftCalendarAccount RequireAccount(string accountId)
		=> _accounts.Resolve(accountId) ??
			throw new InvalidOperationException($"Outlook Calendar has no account '{accountId}'.");

	private async Task<IReadOnlyList<CalendarInfo>> CalendarsOfAsync(
		MicrosoftCalendarAccount account,
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
		var assembly = typeof(MicrosoftCalendarIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("outlook-calendar-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
