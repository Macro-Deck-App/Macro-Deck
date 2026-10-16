using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Spotify.Auth;
using MacroDeckHost.Integrations.Spotify.Protocol;
using MacroDeckHost.Localization;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Spotify.Configuration;

public sealed class SpotifyConfigFlow : IConfigFlow
{
	private static readonly ILogger _logger = IntegrationLog.For<SpotifyConfigFlow>(SpotifyIntegration.IntegrationId);
	private static readonly SpotifyRequestLimiter _setupLimiter = new(0.2, 3);

	private readonly ISpotifyOAuthClient _oauth;
	private readonly IHTTPClient _profileTransport;
	private readonly SpotifyRequestLimiter _profileLimiter;

	private string _clientId = string.Empty;
	private string _clientSecret = string.Empty;

	public SpotifyConfigFlow()
		: this(new SpotifyOAuthClient(), SpotifyHttpClients.Api)
	{
	}

	internal SpotifyConfigFlow(
		ISpotifyOAuthClient oauth,
		IHTTPClient profileTransport,
		SpotifyRequestLimiter? profileLimiter = null)
	{
		_oauth = oauth;
		_profileTransport = profileTransport;
		_profileLimiter = profileLimiter ?? _setupLimiter;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		IntegrationHttp.Use(context.Http);
		return Task.FromResult(ConfigFlowResult.Step(CredentialsStep(context.OAuth.RedirectUri)));
	}

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

	private async Task<ConfigFlowResult> SubmitAuthorize(
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var code = context.OAuth.AuthorizationCode;
		if (string.IsNullOrEmpty(code))
		{
			return ConfigFlowResult.Error(AuthorizePendingStep(),
				AppStrings.Integrations.Spotify.Config.AuthorizationNotCompleted());
		}

		SpotifyAuthorizedToken token;
		try
		{
			token = await _oauth.ExchangeCodeAsync(_clientId,
				_clientSecret,
				code,
				context.OAuth.RedirectUri,
				cancellationToken);
		}
		catch (SpotifyAuthRejectedException ex)
		{
			_logger.Warning("Spotify token exchange was rejected ({Failure}, status {Status})",
				ex.InnerException?.GetType().Name,
				ex.InnerException is APIException { Response: { } response } ? (int)response.StatusCode : null);
			return ConfigFlowResult.Error(AuthorizePendingStep(),
				AppStrings.Integrations.Spotify.Config.TokenExchangeFailed());
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.Warning("Spotify token exchange failed ({Failure})",
				(ex.InnerException ?? ex).GetType().Name);
			return ConfigFlowResult.Error(CredentialsStep(context.OAuth.RedirectUri),
				AppStrings.Integrations.Spotify.Config.SpotifyUnreachable());
		}

		var displayName = await ResolveDisplayName(token.AccessToken, cancellationToken);
		var expiresAt = token.CreatedAt.AddSeconds(token.ExpiresIn)
			.ToUniversalTime()
			.ToString("o", CultureInfo.InvariantCulture);

		// Spotify may omit Scope in the token response; its consent screen is all-or-nothing, so the
		// requested scopes are what was granted and storing them avoids reading back as outdated.
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

	private async Task<string?> ResolveDisplayName(string accessToken, CancellationToken cancellationToken)
	{
		try
		{
			var config = SpotifyClientConfig.CreateDefault()
				.WithToken(accessToken)
				.WithHTTPClient(new SpotifyThrottledHttpClient(_profileTransport, _profileLimiter))
				.WithRetryHandler(new SpotifyRetryHandler());
			var client = new SpotifyClient(config);
			using var scope = SpotifyRequestScope.Interactive("profile-name", "setup");
			var profile = await client.UserProfile.Current(cancellationToken);
			return profile.DisplayName ?? profile.Id;
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.Warning("Could not load Spotify profile name ({Failure})", ex.GetType().Name);
			return null;
		}
	}

	private static ConfigFlowStep CredentialsStep(string redirectUri)
	{
		return new ConfigFlowStep
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
					AppStrings.Integrations.Spotify.Config.ClientIdLabel(),
					placeholder: AppStrings.Integrations.Spotify.Config.ClientIdPlaceholder(),
					required: true),
				ActionParameter.Secret(SpotifyConfigKeys.ClientSecret,
					AppStrings.Integrations.Spotify.Config.ClientSecretLabel(),
					AppStrings.Integrations.Spotify.Config.ClientSecretDescription(),
					true)
			]
		};
	}

	private static ConfigFlowStep AuthorizePendingStep()
	{
		return new ConfigFlowStep
		{
			StepId = "authorize",
			Title = AppStrings.Integrations.Spotify.Config.WaitingTitle(),
			Description = AppStrings.Integrations.Spotify.Config.WaitingDescription(),
			Fields = []
		};
	}
}
