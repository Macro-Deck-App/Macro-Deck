using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.MicrosoftCalendar;

internal sealed class MicrosoftCalendarConfigFlow : IConfigFlow
{
	internal const string AppStepId = "app";
	internal const string AuthorizeStepId = "authorize";
	internal const string CalendarsStepId = "calendars";

	private const int PublicClientRequiredError = 7000218;
	private const int ConsentRequiredError = 65001;

	private static readonly ILogger _logger
		= IntegrationLog.For<MicrosoftCalendarConfigFlow>(MicrosoftCalendarIntegration.IntegrationId);

	private readonly IMicrosoftOAuthClient _client;
	private readonly MicrosoftGraphClient _graph;
	private readonly Func<string, IReadOnlySet<string>?> _previousSelection;
	private readonly TimeProvider _time;

	private string _clientId = string.Empty;
	private string _tenant = MicrosoftOAuth.DefaultTenant;
	private string _codeVerifier = string.Empty;
	private IReadOnlyList<string> _requestedSelection = [];
	private SignedIn? _signedIn;

	internal MicrosoftCalendarConfigFlow(
		IMicrosoftOAuthClient client,
		MicrosoftGraphClient graph,
		Func<string, IReadOnlySet<string>?> previousSelection,
		TimeProvider time)
	{
		_client = client;
		_graph = graph;
		_previousSelection = previousSelection;
		_time = time;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(AppStep(context.OAuth.RedirectUri)));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId switch
		{
			AppStepId => SubmitApp(input, context),
			AuthorizeStepId => await SubmitAuthorizeAsync(context, cancellationToken),
			CalendarsStepId => SubmitCalendars(input, context),
			_ => ConfigFlowResult.Error(AppStep(context.OAuth.RedirectUri),
				AppStrings.Integrations.Spotify.Config.UnknownStep())
		};

	private ConfigFlowResult SubmitApp(IReadOnlyDictionary<string, object?> input, IConfigFlowContext context)
	{
		_signedIn = null;
		_clientId = MicrosoftOAuth.EffectiveClientId(input.GetValueOrDefault(MicrosoftCalendarConfigKeys.ClientId) as string);
		_tenant = MicrosoftOAuth.NormalizeTenant(input.GetValueOrDefault(MicrosoftCalendarConfigKeys.Tenant) as string);
		_requestedSelection
			= MicrosoftCalendarAccountManager.ParseCalendarIds(input.GetValueOrDefault(MicrosoftCalendarConfigKeys.CalendarIds));

		var fieldErrors = new Dictionary<string, LocalizedText>();
		if (!Guid.TryParse(_clientId, out _))
		{
			fieldErrors[MicrosoftCalendarConfigKeys.ClientId]
				= AppStrings.Integrations.MicrosoftCalendar.Config.EnterClientId();
		}

		if (!MicrosoftOAuth.IsValidTenant(_tenant))
		{
			fieldErrors[MicrosoftCalendarConfigKeys.Tenant]
				= AppStrings.Integrations.MicrosoftCalendar.Config.InvalidTenant();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(AppStep(context.OAuth.RedirectUri), fieldErrors: fieldErrors);
		}

		_codeVerifier = MicrosoftOAuth.CreateCodeVerifier();
		return ConfigFlowResult.External(
			MicrosoftOAuth.AuthorizeUrl(_clientId, _tenant, context.OAuth.RedirectUri, context.OAuth.State, _codeVerifier),
			AuthorizeStepId);
	}

	private async Task<ConfigFlowResult> SubmitAuthorizeAsync(
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var code = context.OAuth.AuthorizationCode;
		if (string.IsNullOrEmpty(code) || _codeVerifier.Length == 0)
		{
			return Failed(context, AppStrings.Integrations.MicrosoftCalendar.Config.AuthorizationNotCompleted());
		}

		MicrosoftTokenResponse token;
		try
		{
			token = await _client.ExchangeCodeAsync(_clientId,
				_tenant,
				code,
				_codeVerifier,
				context.OAuth.RedirectUri,
				cancellationToken);
		}
		catch (MicrosoftOAuthRejectedException ex)
		{
			_logger.Warning("Microsoft token exchange was refused ({Reason})", ex.Message);
			return Failed(context, ex.ErrorCodes switch
			{
				var codes when codes.Contains(PublicClientRequiredError)
					=> AppStrings.Integrations.MicrosoftCalendar.Config.PublicClientRequired(),
				var codes when codes.Contains(ConsentRequiredError)
					=> AppStrings.Integrations.MicrosoftCalendar.Config.ConsentRequired(),
				_ => AppStrings.Integrations.MicrosoftCalendar.Config.TokenExchangeFailed()
			});
		}
		catch (MicrosoftOAuthTransientException ex)
		{
			_logger.Warning("Microsoft token exchange failed ({Reason})", ex.Message);
			return Failed(context, AppStrings.Integrations.MicrosoftCalendar.Config.TokenExchangeFailed());
		}

		if (string.IsNullOrEmpty(token.RefreshToken))
		{
			return Failed(context, AppStrings.Integrations.MicrosoftCalendar.Config.MissingRefreshToken());
		}

		var scope = string.IsNullOrEmpty(token.Scope) ? MicrosoftOAuth.RequestedScopes : token.Scope;
		if (!MicrosoftOAuth.GrantsCalendarAccess(scope))
		{
			return Failed(context, AppStrings.Integrations.MicrosoftCalendar.Config.MissingCalendarPermission());
		}

		var identity = token.Identity();
		var now = _time.GetUtcNow();
		var tokens = new MicrosoftTokenProvider(Guid.Empty,
			_clientId,
			_tenant,
			new MicrosoftTokens(token.AccessToken, token.RefreshToken, now + token.ExpiresIn),
			_client,
			(_, _) => Task.CompletedTask,
			_time,
			_logger);

		IReadOnlyList<MicrosoftCalendarSummary> calendars;
		try
		{
			calendars = await _graph.GetCalendarsAsync(tokens, null, cancellationToken);
		}
		catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException
			or MicrosoftOAuthRejectedException or MicrosoftOAuthTransientException)
		{
			_logger.Warning("Could not list the Microsoft account's calendars during setup ({Failure})",
				ex.GetType().Name);
			return Failed(context, AppStrings.Integrations.MicrosoftCalendar.Config.CalendarsUnavailable());
		}
		finally
		{
			tokens.Dispose();
		}

		if (calendars.Count == 0)
		{
			return Failed(context, AppStrings.Integrations.MicrosoftCalendar.Config.CalendarsUnavailable());
		}

		var signedIn = new SignedIn(token, scope, identity, calendars, now);
		_signedIn = signedIn;
		return ConfigFlowResult.Step(CalendarsStep(signedIn, DefaultSelection(signedIn)));
	}

	private ConfigFlowResult SubmitCalendars(IReadOnlyDictionary<string, object?> input, IConfigFlowContext context)
	{
		if (_signedIn is not { } signedIn)
		{
			return Failed(context, AppStrings.Integrations.MicrosoftCalendar.Config.AuthorizationNotCompleted());
		}

		var available = signedIn.Calendars.Select(calendar => calendar.Id).ToHashSet(StringComparer.Ordinal);
		var selected = MicrosoftCalendarAccountManager
			.ParseCalendarIds(input.GetValueOrDefault(MicrosoftCalendarConfigKeys.CalendarIds))
			.Where(available.Contains)
			.ToList();
		if (selected.Count == 0)
		{
			return ConfigFlowResult.Error(CalendarsStep(signedIn, []),
				fieldErrors: new Dictionary<string, LocalizedText>
				{
					[MicrosoftCalendarConfigKeys.CalendarIds]
						= AppStrings.Integrations.MicrosoftCalendar.Config.SelectCalendar()
				});
		}

		var identity = signedIn.Identity;
		var token = signedIn.Token;
		var values = new Dictionary<string, ConfigFlowValue>
		{
			[MicrosoftCalendarConfigKeys.AccessToken] = ConfigFlowValue.Secret(token.AccessToken),
			[MicrosoftCalendarConfigKeys.RefreshToken] = ConfigFlowValue.Secret(token.RefreshToken!),
			[MicrosoftCalendarConfigKeys.ExpiresAt] = ConfigFlowValue.Plain(
				(signedIn.SignedInAt + token.ExpiresIn).ToString("o", CultureInfo.InvariantCulture)),
			[MicrosoftCalendarConfigKeys.SignInClientId] = ConfigFlowValue.Plain(_clientId),
			[MicrosoftCalendarConfigKeys.SignInTenant] = ConfigFlowValue.Plain(_tenant),
			[MicrosoftCalendarConfigKeys.Scope] = ConfigFlowValue.Plain(signedIn.Scope),
			[MicrosoftCalendarConfigKeys.Email] = ConfigFlowValue.Plain(identity.Email),
			[MicrosoftCalendarConfigKeys.AccountKey] = ConfigFlowValue.Plain(identity.AccountKey ?? identity.Email),
			[MicrosoftCalendarConfigKeys.ConnectedAt]
				= ConfigFlowValue.Plain(_time.GetUtcNow().ToString("o", CultureInfo.InvariantCulture))
		};

		var title = identity.Email ?? identity.Name;
		return ConfigFlowResult.Complete(string.IsNullOrWhiteSpace(title) ? MicrosoftCalendarIntegration.BrandName : title,
			values);
	}

	private List<string> DefaultSelection(SignedIn signedIn)
	{
		var available = signedIn.Calendars.Select(calendar => calendar.Id).ToList();
		var previous = _requestedSelection.Count > 0
			? _requestedSelection
			: (signedIn.Identity.AccountKey ?? signedIn.Identity.Email) is { } key
				? _previousSelection(key)?.ToList() ?? []
				: [];
		var kept = available.Where(previous.Contains).ToList();
		return kept.Count > 0 ? kept : available;
	}

	private static ConfigFlowResult Failed(IConfigFlowContext context, LocalizedText message)
		=> ConfigFlowResult.Error(AppStep(context.OAuth.RedirectUri), message);

	private static ConfigFlowStep CalendarsStep(SignedIn signedIn, IReadOnlyList<string> selection)
		=> new()
		{
			StepId = CalendarsStepId,
			Title = AppStrings.Integrations.MicrosoftCalendar.Config.CalendarsTitle(),
			Description = AppStrings.Integrations.MicrosoftCalendar.Config.CalendarsDescription(),
			Fields =
			[
				new ActionParameter
				{
					Name = MicrosoftCalendarConfigKeys.CalendarIds,
					Type = ActionParameterType.MultiSelect,
					Options = [.. signedIn.Calendars.Select(calendar => OptionFor(calendar, signedIn.Identity))],
					Label = AppStrings.Integrations.MicrosoftCalendar.Config.CalendarsLabel(),
					Required = true,
					DefaultValue = selection.ToArray()
				}
			]
		};

	private static ActionParameterOption OptionFor(MicrosoftCalendarSummary calendar, MicrosoftIdentity account)
		=> new()
		{
			Value = calendar.Id,
			Label = IsShared(calendar, account) && calendar.OwnerName is { Length: > 0 } owner
				? AppStrings.Integrations.MicrosoftCalendar.Config.SharedCalendarName(calendar: calendar.Name,
					owner: owner)
				: calendar.Name
		};

	// A personal account signed in with another provider's address owns its calendars under an
	// outlook alias, so the owner's name has to differ too.
	private static bool IsShared(MicrosoftCalendarSummary calendar, MicrosoftIdentity account)
		=> calendar.OwnerAddress is { Length: > 0 } ownerAddress &&
			account.Email is { Length: > 0 } email &&
			!string.Equals(ownerAddress, email, StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(calendar.OwnerName, account.Name, StringComparison.OrdinalIgnoreCase);

	internal static ConfigFlowStep AppStep(string redirectUri)
		=> new()
		{
			StepId = AppStepId,
			Title = AppStrings.Integrations.MicrosoftCalendar.Config.ConnectTitle(),
			Description = AppStrings.Integrations.MicrosoftCalendar.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.MicrosoftCalendar.Config.AdminConsentInstruction() }
			],
			Fields = [],
			AdvancedFields =
			[
				ActionParameter.Text(MicrosoftCalendarConfigKeys.ClientId,
					label: AppStrings.Integrations.MicrosoftCalendar.Config.ClientIdLabel(),
					description: AppStrings.Integrations.MicrosoftCalendar.Config.ClientIdDescription(redirectUri: redirectUri),
					placeholder: AppStrings.Integrations.MicrosoftCalendar.Config.ClientIdPlaceholder()),
				ActionParameter.Text(MicrosoftCalendarConfigKeys.Tenant,
					label: AppStrings.Integrations.MicrosoftCalendar.Config.TenantLabel(),
					description: AppStrings.Integrations.MicrosoftCalendar.Config.TenantDescription(),
					placeholder: MicrosoftOAuth.DefaultTenant)
			]
		};

	private sealed record SignedIn(
		MicrosoftTokenResponse Token,
		string Scope,
		MicrosoftIdentity Identity,
		IReadOnlyList<MicrosoftCalendarSummary> Calendars,
		DateTimeOffset SignedInAt);
}
