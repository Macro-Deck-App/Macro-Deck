using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;
using SpotifyAPI.Web;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Spotify;

public sealed class SpotifyConfigFlow : IConfigFlow
{
	private static readonly ILogger _logger = IntegrationLog.For<SpotifyConfigFlow>(SpotifyIntegration.IntegrationId);
	private static readonly SpotifyRequestLimiter _setupLimiter = new(ceilingPerSecond: 0.2, burst: 3);

	private string _clientId = string.Empty;
	private string _clientSecret = string.Empty;

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(CredentialsStep(context.OAuth.RedirectUri)));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		return stepId switch
		{
			"credentials" => SubmitCredentials(input, context),
			"authorize" => await SubmitAuthorize(context, cancellationToken),
			_ => ConfigFlowResult.Error(CredentialsStep(context.OAuth.RedirectUri),
				AppStrings.Integrations.Spotify.Config.UnknownStep())
		};
	}

	private ConfigFlowResult SubmitCredentials(IReadOnlyDictionary<string, object?> input, IConfigFlowContext context)
	{
		_clientId = (input.GetValueOrDefault(SpotifyConfigKeys.ClientId) as string ?? string.Empty).Trim();
		_clientSecret = (input.GetValueOrDefault(SpotifyConfigKeys.ClientSecret) as string ?? string.Empty).Trim();

		var fieldErrors = new Dictionary<string, LocalizedText>();
		if (string.IsNullOrWhiteSpace(_clientId))
		{
			fieldErrors[SpotifyConfigKeys.ClientId] = AppStrings.Integrations.Spotify.Config.EnterClientId();
		}

		if (string.IsNullOrWhiteSpace(_clientSecret))
		{
			fieldErrors[SpotifyConfigKeys.ClientSecret] = AppStrings.Integrations.Spotify.Config.EnterClientSecret();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(CredentialsStep(context.OAuth.RedirectUri), fieldErrors: fieldErrors);
		}

		var loginRequest
			= new LoginRequest(new Uri(context.OAuth.RedirectUri), _clientId, LoginRequest.ResponseType.Code)
			{
				Scope = SpotifyScopes.Required.ToList(),
				State = context.OAuth.State
			};

		return ConfigFlowResult.External(loginRequest.ToUri().ToString(), "authorize");
	}

	private async Task<ConfigFlowResult> SubmitAuthorize(IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var code = context.OAuth.AuthorizationCode;
		if (string.IsNullOrEmpty(code))
		{
			return ConfigFlowResult.Error(AuthorizePendingStep(),
				AppStrings.Integrations.Spotify.Config.AuthorizationNotCompleted());
		}

		try
		{
			var setupConfig = SpotifyClientConfig.CreateDefault()
				.WithHTTPClient(new SpotifyThrottledHttpClient(
					SpotifyHttpClients.CreateBounded(SpotifyHttpClients.TokenEndpointTimeout),
					_setupLimiter))
				.WithRetryHandler(new SpotifyRetryHandler());
			var oauth = new OAuthClient(setupConfig);
			using var requestScope = SpotifyRequestScope.Interactive("authorization-code-exchange", "setup");
			var token = await oauth.RequestToken(
				new AuthorizationCodeTokenRequest(_clientId, _clientSecret, code, new Uri(context.OAuth.RedirectUri)),
				cancellationToken);

			var displayName = await ResolveDisplayName(token.AccessToken, cancellationToken);
			var expiresAt = token.CreatedAt.AddSeconds(token.ExpiresIn)
				.ToUniversalTime()
				.ToString("o", CultureInfo.InvariantCulture);

			// A grant Spotify declines to describe (empty/null Scope in the token response) must not store
			// a value that reads back as outdated on every start - the requested scopes are what the user
			// just granted, since Spotify's consent screen is all-or-nothing.
			var scope = string.IsNullOrEmpty(token.Scope) ? string.Join(' ', SpotifyScopes.Required) : token.Scope;

			var values = new Dictionary<string, ConfigFlowValue>
			{
				[SpotifyConfigKeys.AccessToken] = ConfigFlowValue.Secret(token.AccessToken),
				[SpotifyConfigKeys.RefreshToken] = ConfigFlowValue.Secret(token.RefreshToken),
				[SpotifyConfigKeys.ExpiresAt] = ConfigFlowValue.Plain(expiresAt),
				[SpotifyConfigKeys.Scope] = ConfigFlowValue.Plain(scope),
				[SpotifyConfigKeys.DisplayName] = ConfigFlowValue.Plain(displayName)
			};

			var title = string.IsNullOrWhiteSpace(displayName) ? "Spotify" : $"Spotify ({displayName})";
			return ConfigFlowResult.Complete(title, values);
		}
		catch (APIException ex)
		{
			_logger.Warning("Spotify token exchange failed ({Failure}, status {Status})",
				ex.GetType().Name,
				ex.Response is { } response ? (int)response.StatusCode : null);
			return ConfigFlowResult.Error(AuthorizePendingStep(),
				AppStrings.Integrations.Spotify.Config.TokenExchangeFailed());
		}
	}

	private static async Task<string?> ResolveDisplayName(string accessToken, CancellationToken cancellationToken)
	{
		try
		{
			var config = SpotifyClientConfig.CreateDefault()
				.WithToken(accessToken)
				.WithHTTPClient(new SpotifyThrottledHttpClient(
					SpotifyHttpClients.CreateBounded(SpotifyHttpClients.ApiTimeout),
					_setupLimiter))
				.WithRetryHandler(new SpotifyRetryHandler());
			var client = new SpotifyClient(config);
			using var scope = SpotifyRequestScope.Interactive("profile-name", "setup");
			var profile = await client.UserProfile.Current(cancellationToken);
			return profile.DisplayName ?? profile.Id;
		}
		catch (Exception ex)
		{
			_logger.Warning("Could not load Spotify profile name ({Failure})", ex.GetType().Name);
			return null;
		}
	}

	private static ConfigFlowStep CredentialsStep(string redirectUri)
		=> new()
		{
			StepId = "credentials",
			Title = AppStrings.Integrations.Spotify.Config.ConnectTitle(),
			Description = AppStrings.Integrations.Spotify.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = AppStrings.Integrations.Spotify.Config.CreateAppInstruction() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.Spotify.Config.AddRedirectUriInstruction(),
					Values =
					[
						new ConfigFlowCopyValue
							{ Label = AppStrings.Integrations.Spotify.Config.RedirectUriLabel(), Value = redirectUri }
					]
				},
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Spotify.Config.CopyCredentialsInstruction() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Spotify.Config.DashboardLinkLabel(),
					Url = "https://developer.spotify.com/dashboard"
				}
			],
			Fields =
			[
				ActionParameter.Text(SpotifyConfigKeys.ClientId,
					label: AppStrings.Integrations.Spotify.Config.ClientIdLabel(),
					placeholder: AppStrings.Integrations.Spotify.Config.ClientIdPlaceholder(),
					required: true),
				ActionParameter.Secret(SpotifyConfigKeys.ClientSecret,
					label: AppStrings.Integrations.Spotify.Config.ClientSecretLabel(),
					description: AppStrings.Integrations.Spotify.Config.ClientSecretDescription(),
					required: true)
			]
		};

	private static ConfigFlowStep AuthorizePendingStep()
		=> new()
		{
			StepId = "authorize",
			Title = AppStrings.Integrations.Spotify.Config.WaitingTitle(),
			Description = AppStrings.Integrations.Spotify.Config.WaitingDescription(),
			Fields = []
		};
}
