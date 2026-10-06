using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.GoogleCalendar;

internal sealed class GoogleCalendarConfigFlow : IConfigFlow
{
	internal const string CredentialsStepId = "credentials";
	internal const string AuthorizeStepId = "authorize";

	private static readonly ILogger _logger
		= IntegrationLog.For<GoogleCalendarConfigFlow>(GoogleCalendarIntegration.IntegrationId);

	private readonly IGoogleOAuthClient _client;
	private readonly TimeProvider _time;

	private string _clientId = string.Empty;
	private string _clientSecret = string.Empty;
	private string _codeVerifier = string.Empty;

	internal GoogleCalendarConfigFlow(IGoogleOAuthClient client, TimeProvider time)
	{
		_client = client;
		_time = time;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(CredentialsStep(context.OAuth.RedirectUri)));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId switch
		{
			CredentialsStepId => SubmitCredentials(input, context),
			AuthorizeStepId => await SubmitAuthorizeAsync(context, cancellationToken),
			_ => ConfigFlowResult.Error(CredentialsStep(context.OAuth.RedirectUri),
				AppStrings.Integrations.Spotify.Config.UnknownStep())
		};

	private ConfigFlowResult SubmitCredentials(IReadOnlyDictionary<string, object?> input, IConfigFlowContext context)
	{
		_clientId = (input.GetValueOrDefault(GoogleCalendarConfigKeys.ClientId) as string ?? string.Empty).Trim();
		_clientSecret = (input.GetValueOrDefault(GoogleCalendarConfigKeys.ClientSecret) as string ?? string.Empty)
			.Trim();

		var fieldErrors = new Dictionary<string, LocalizedText>();
		if (_clientId.Length == 0)
		{
			fieldErrors[GoogleCalendarConfigKeys.ClientId]
				= AppStrings.Integrations.GoogleCalendar.Config.EnterClientId();
		}

		if (_clientSecret.Length == 0)
		{
			fieldErrors[GoogleCalendarConfigKeys.ClientSecret]
				= AppStrings.Integrations.GoogleCalendar.Config.EnterClientSecret();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(CredentialsStep(context.OAuth.RedirectUri), fieldErrors: fieldErrors);
		}

		_codeVerifier = GoogleOAuth.CreateCodeVerifier();
		return ConfigFlowResult.External(
			GoogleOAuth.AuthorizeUrl(_clientId, context.OAuth.RedirectUri, context.OAuth.State, _codeVerifier),
			AuthorizeStepId);
	}

	private async Task<ConfigFlowResult> SubmitAuthorizeAsync(
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var code = context.OAuth.AuthorizationCode;
		if (string.IsNullOrEmpty(code) || _codeVerifier.Length == 0)
		{
			return Failed(context, AppStrings.Integrations.GoogleCalendar.Config.AuthorizationNotCompleted());
		}

		GoogleTokenResponse token;
		try
		{
			token = await _client.ExchangeCodeAsync(_clientId,
				_clientSecret,
				code,
				_codeVerifier,
				context.OAuth.RedirectUri,
				cancellationToken);
		}
		catch (Exception ex) when (ex is GoogleOAuthRejectedException or GoogleOAuthTransientException)
		{
			_logger.Warning("Google token exchange failed ({Failure}: {Reason})", ex.GetType().Name, ex.Message);
			return Failed(context, AppStrings.Integrations.GoogleCalendar.Config.TokenExchangeFailed());
		}

		if (string.IsNullOrEmpty(token.RefreshToken))
		{
			return Failed(context, AppStrings.Integrations.GoogleCalendar.Config.MissingRefreshToken());
		}

		var scope = string.IsNullOrEmpty(token.Scope) ? GoogleOAuth.RequestedScopes : token.Scope;
		if (!GoogleOAuth.GrantsCalendarAccess(scope))
		{
			return Failed(context, AppStrings.Integrations.GoogleCalendar.Config.MissingCalendarPermission());
		}

		var email = token.EmailFromIdToken() ?? await TryReadEmailAsync(token.AccessToken, cancellationToken);
		var now = _time.GetUtcNow();

		var values = new Dictionary<string, ConfigFlowValue>
		{
			[GoogleCalendarConfigKeys.AccessToken] = ConfigFlowValue.Secret(token.AccessToken),
			[GoogleCalendarConfigKeys.RefreshToken] = ConfigFlowValue.Secret(token.RefreshToken),
			[GoogleCalendarConfigKeys.ExpiresAt]
				= ConfigFlowValue.Plain((now + token.ExpiresIn).ToString("o", CultureInfo.InvariantCulture)),
			[GoogleCalendarConfigKeys.Scope] = ConfigFlowValue.Plain(scope),
			[GoogleCalendarConfigKeys.Email] = ConfigFlowValue.Plain(email),
			[GoogleCalendarConfigKeys.ConnectedAt] = ConfigFlowValue.Plain(now.ToString("o", CultureInfo.InvariantCulture))
		};

		return ConfigFlowResult.Complete(string.IsNullOrWhiteSpace(email) ? GoogleCalendarIntegration.BrandName : email,
			values);
	}

	private async Task<string?> TryReadEmailAsync(string accessToken, CancellationToken cancellationToken)
	{
		try
		{
			return await _client.GetEmailAsync(accessToken, cancellationToken);
		}
		catch (Exception ex) when (ex is GoogleOAuthRejectedException or GoogleOAuthTransientException)
		{
			_logger.Warning("Could not read the Google account's email address ({Failure})", ex.GetType().Name);
			return null;
		}
	}

	private static ConfigFlowResult Failed(IConfigFlowContext context, LocalizedText message)
		=> ConfigFlowResult.Error(CredentialsStep(context.OAuth.RedirectUri), message);

	internal static ConfigFlowStep CredentialsStep(string redirectUri)
		=> new()
		{
			StepId = CredentialsStepId,
			Title = AppStrings.Integrations.GoogleCalendar.Config.ConnectTitle(),
			Description = AppStrings.Integrations.GoogleCalendar.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.GoogleCalendar.Config.EnableApiInstruction() },
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.GoogleCalendar.Config.ConsentScreenInstruction() },
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.GoogleCalendar.Config.CreateClientInstruction() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.GoogleCalendar.Config.RedirectUriInstruction(),
					Values =
					[
						new ConfigFlowCopyValue
							{ Label = AppStrings.Integrations.Spotify.Config.RedirectUriLabel(), Value = redirectUri }
					]
				},
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.GoogleCalendar.Config.CopyCredentialsInstruction() },
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.GoogleCalendar.Config.UnverifiedAppInstruction() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.GoogleCalendar.Config.ConsoleLinkLabel(),
					Url = GoogleOAuth.ConsoleUrl
				},
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.GoogleCalendar.Config.CalendarApiLinkLabel(),
					Url = GoogleOAuth.CalendarApiUrl
				}
			],
			Fields =
			[
				ActionParameter.Text(GoogleCalendarConfigKeys.ClientId,
					label: AppStrings.Integrations.Spotify.Config.ClientIdLabel(),
					placeholder: AppStrings.Integrations.GoogleCalendar.Config.ClientIdPlaceholder(),
					required: true),
				ActionParameter.Secret(GoogleCalendarConfigKeys.ClientSecret,
					label: AppStrings.Integrations.Spotify.Config.ClientSecretLabel(),
					description: AppStrings.Integrations.GoogleCalendar.Config.ClientSecretDescription(),
					required: true)
			]
		};
}
