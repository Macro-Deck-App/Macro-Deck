using System.Globalization;
using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Discord;

public sealed class DiscordConfigFlow : IConfigFlow
{
	private static readonly ILogger _logger = IntegrationLog.For<DiscordConfigFlow>(DiscordIntegration.IntegrationId);

	private static readonly TimeSpan _authorizeTimeout = TimeSpan.FromMinutes(2);

	private readonly Func<IDiscordRpcClient> _clientFactory;
	private readonly IDiscordOAuthClient _oauth;

	private string _clientId = string.Empty;
	private string _clientSecret = string.Empty;

	public DiscordConfigFlow()
		: this(() => new DiscordRpcClient(new DiscordIpcTransport()), new DiscordOAuthClient())
	{
	}

	internal DiscordConfigFlow(Func<IDiscordRpcClient> clientFactory, IDiscordOAuthClient oauth)
	{
		_clientFactory = clientFactory;
		_oauth = oauth;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(CredentialsStep()));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId switch
		{
			"credentials" => SubmitCredentials(input),
			"authorize" => await AuthorizeAsync(cancellationToken).ConfigureAwait(false),
			_ => ConfigFlowResult.Error(CredentialsStep(), AppStrings.Integrations.Discord.Config.UnknownStep())
		};

	private static ConfigFlowStep CredentialsStep()
		=> new()
		{
			StepId = "credentials",
			Title = AppStrings.Integrations.Discord.Config.ConnectTitle(),
			Description = AppStrings.Integrations.Discord.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Discord.Config.CreateAppInstruction() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.Discord.Config.RedirectUriInstruction(),
					Values =
					[
						new ConfigFlowCopyValue
						{
							Label = AppStrings.Integrations.Discord.Config.RedirectUriLabel(),
							Value = DiscordOAuthClient.RedirectUri
						}
					]
				},
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.Discord.Config.CopyCredentialsInstruction()
				}
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Discord.Config.DeveloperPortalLinkLabel(),
					Url = "https://discord.com/developers/applications"
				}
			],
			Fields =
			[
				ActionParameter.Text(DiscordConfigKeys.ClientId,
					label: AppStrings.Integrations.Discord.Config.ClientIdLabel(),
					placeholder: AppStrings.Integrations.Discord.Config.ClientIdPlaceholder(),
					required: true),
				ActionParameter.Secret(DiscordConfigKeys.ClientSecret,
					label: AppStrings.Integrations.Discord.Config.ClientSecretLabel(),
					description: AppStrings.Integrations.Discord.Config.ClientSecretDescription(),
					required: true)
			]
		};

	private static ConfigFlowStep AuthorizeStep()
		=> new()
		{
			StepId = "authorize",
			Title = AppStrings.Integrations.Discord.Config.AuthorizeTitle(),
			Description = AppStrings.Integrations.Discord.Config.AuthorizeDescription(),
			Fields = []
		};

	private ConfigFlowResult SubmitCredentials(IReadOnlyDictionary<string, object?> input)
	{
		_clientId = (input.GetValueOrDefault(DiscordConfigKeys.ClientId) as string ?? string.Empty).Trim();
		_clientSecret = (input.GetValueOrDefault(DiscordConfigKeys.ClientSecret) as string ?? string.Empty).Trim();

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(_clientId))
		{
			fieldErrors[DiscordConfigKeys.ClientId] = AppStrings.Integrations.Discord.Config.EnterClientId();
		}
		else if (!_clientId.All(char.IsAsciiDigit))
		{
			fieldErrors[DiscordConfigKeys.ClientId] = AppStrings.Integrations.Discord.Config.ClientIdDigitsOnly();
		}

		if (string.IsNullOrWhiteSpace(_clientSecret))
		{
			fieldErrors[DiscordConfigKeys.ClientSecret] = AppStrings.Integrations.Discord.Config.EnterClientSecret();
		}

		return fieldErrors.Count > 0
			? ConfigFlowResult.Error(CredentialsStep(), fieldErrors: fieldErrors)
			: ConfigFlowResult.Step(AuthorizeStep());
	}

	private async Task<ConfigFlowResult> AuthorizeAsync(CancellationToken cancellationToken)
	{
		using var client = _clientFactory();

		try
		{
			await client.ConnectAsync(_clientId, cancellationToken).ConfigureAwait(false);
		}
		catch (DiscordIpcUnavailableException ex)
		{
			return ConfigFlowResult.Error(AuthorizeStep(),
				ex.AccessDenied
					? AppStrings.Integrations.Discord.Config.PrivilegeMismatch()
					: AppStrings.Integrations.Discord.Config.NoClientFound());
		}
		catch (Exception ex) when (ex is DiscordRpcException or TimeoutException or IOException)
		{
			_logger.Warning(ex, "Could not reach the Discord client for authorization");
			return ConfigFlowResult.Error(AuthorizeStep(),
				AppStrings.Integrations.Discord.Config.CouldNotTalkToClient());
		}

		string code;
		try
		{
			code = await RequestAuthorizationCodeAsync(client, cancellationToken).ConfigureAwait(false);
		}
		catch (DiscordRpcException ex)
		{
			return ConfigFlowResult.Error(AuthorizeStep(), DescribeAuthorizationFailure(ex));
		}
		catch (TimeoutException)
		{
			return ConfigFlowResult.Error(AuthorizeStep(),
				AppStrings.Integrations.Discord.Config.NoAuthorizationAnswer());
		}

		try
		{
			var tokens = await _oauth
				.ExchangeCodeAsync(_clientId, _clientSecret, code, cancellationToken)
				.ConfigureAwait(false);
			return await CompleteAsync(client, tokens, cancellationToken).ConfigureAwait(false);
		}
		catch (DiscordOAuthException ex)
		{
			return ConfigFlowResult.Error(AuthorizeStep(),
				AppStrings.Integrations.Discord.Config.OAuthFailed(details: ex.Message));
		}
		catch (HttpRequestException ex)
		{
			_logger.Warning(ex, "Discord token exchange could not reach discord.com");
			return ConfigFlowResult.Error(AuthorizeStep(),
				AppStrings.Integrations.Discord.Config.CouldNotReachDiscord());
		}
	}

	private async Task<string> RequestAuthorizationCodeAsync(
		IDiscordRpcClient client,
		CancellationToken cancellationToken)
	{
		try
		{
			return await AuthorizeWithScopesAsync(client, DiscordScopes.Full, cancellationToken).ConfigureAwait(false);
		}
		catch (DiscordRpcException ex) when (ex.IsScopeProblem)
		{
			_logger.Information("Discord refused the full scope set; retrying without notification access");
			return await AuthorizeWithScopesAsync(client, DiscordScopes.Core, cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task<string> AuthorizeWithScopesAsync(
		IDiscordRpcClient client,
		IReadOnlyList<string> scopes,
		CancellationToken cancellationToken)
	{
		var response = await client
			.SendCommandAsync(DiscordRpcCommands.Authorize,
				new AuthorizeArgs(_clientId, scopes),
				timeout: _authorizeTimeout,
				cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		var code = DiscordStateMapper.ReadString(response, "code");
		return string.IsNullOrEmpty(code)
			? throw new DiscordRpcException("Discord did not return an authorization code.")
			: code;
	}

	private static async Task<ConfigFlowResult> CompleteAsync(
		IDiscordRpcClient client,
		DiscordTokens tokens,
		CancellationToken cancellationToken)
	{
		var userName = await ResolveUserNameAsync(client, tokens.AccessToken, cancellationToken).ConfigureAwait(false);

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[DiscordConfigKeys.AccessToken] = ConfigFlowValue.Secret(tokens.AccessToken),
			[DiscordConfigKeys.Scope] = ConfigFlowValue.Plain(tokens.Scope ?? string.Empty),
			[DiscordConfigKeys.UserName] = ConfigFlowValue.Plain(userName ?? string.Empty)
		};

		if (tokens.RefreshToken is not null)
		{
			values[DiscordConfigKeys.RefreshToken] = ConfigFlowValue.Secret(tokens.RefreshToken);
		}

		if (tokens.ExpiresAt is not null)
		{
			values[DiscordConfigKeys.ExpiresAt] =
				ConfigFlowValue.Plain(tokens.ExpiresAt.Value.ToString("o", CultureInfo.InvariantCulture));
		}

		var title = string.IsNullOrWhiteSpace(userName) ? "Discord" : $"Discord ({userName})";
		return ConfigFlowResult.Complete(title, values);
	}

	private static async Task<string?> ResolveUserNameAsync(
		IDiscordRpcClient client,
		string accessToken,
		CancellationToken cancellationToken)
	{
		try
		{
			var authenticated = await client
				.SendCommandAsync(DiscordRpcCommands.Authenticate,
					new AuthenticateArgs(accessToken),
					cancellationToken: cancellationToken)
				.ConfigureAwait(false);
			return DiscordStateMapper.ReadUser(authenticated).Name;
		}
		catch (Exception ex) when (ex is DiscordRpcException or TimeoutException or IOException)
		{
			_logger.Information(ex, "Could not read the Discord account name during setup");
			return null;
		}
	}

	private static LocalizedText DescribeAuthorizationFailure(DiscordRpcException exception)
	{
		if (exception.IsScopeProblem)
		{
			return AppStrings.Integrations.Discord.Config.ScopeProblem();
		}

		return exception.Code == 4000
			? AppStrings.Integrations.Discord.Config.RejectedRequest()
			: AppStrings.Integrations.Discord.Config.AuthorizationDeclined(details: exception.Message);
	}

	private sealed record AuthorizeArgs(string ClientId, IReadOnlyList<string> Scopes);

	private sealed record AuthenticateArgs(string AccessToken);
}
