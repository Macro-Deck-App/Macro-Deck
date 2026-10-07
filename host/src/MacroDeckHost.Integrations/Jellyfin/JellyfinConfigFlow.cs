using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Jellyfin.Protocol;
using MacroDeckHost.Localization;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin.Config;

namespace MacroDeckHost.Integrations.Jellyfin;

internal static class JellyfinConfigKeys
{
	public const string ConfigurationName = "configurationName";
	public const string Url = "url";
	public const string AuthMethod = "authMethod";
	public const string ApiKey = "apiKey";
	public const string Username = "username";
	public const string Password = "password";
	public const string AccessToken = "accessToken";
	public const string DeviceId = "deviceId";
	public const string Devices = "devices";

	public const string AuthApiKey = "api-key";
	public const string AuthLogin = "login";
}

public sealed class JellyfinConfigFlow : IConfigFlow
{
	private const string StepId = "connection";

	private static readonly ILogger _logger = IntegrationLog.For<JellyfinConfigFlow>(JellyfinIntegration.IntegrationId);
	private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(15);

	private readonly Func<JellyfinServerSettings, IJellyfinClient> _clientFactory;

	public JellyfinConfigFlow()
		: this(settings => new JellyfinClient(settings))
	{
	}

	internal JellyfinConfigFlow(Func<JellyfinServerSettings, IJellyfinClient> clientFactory)
	{
		_clientFactory = clientFactory;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep(context)));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != StepId)
		{
			return ConfigFlowResult.Error(ConnectionStep(context), Strings.UnknownStep());
		}

		var entryTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var name = entryTitle ?? Read(input, JellyfinConfigKeys.ConfigurationName).Trim();
		var url = Read(input, JellyfinConfigKeys.Url).Trim();
		var method = Read(input, JellyfinConfigKeys.AuthMethod) is JellyfinConfigKeys.AuthLogin
			? JellyfinConfigKeys.AuthLogin
			: JellyfinConfigKeys.AuthApiKey;
		var apiKey = Read(input, JellyfinConfigKeys.ApiKey).Trim();
		var username = Read(input, JellyfinConfigKeys.Username).Trim();
		var password = Read(input, JellyfinConfigKeys.Password);
		var storedToken = Read(input, JellyfinConfigKeys.AccessToken);
		var deviceId = Read(input, JellyfinConfigKeys.DeviceId) is { Length: > 0 } stored
			? stored
			: Guid.NewGuid().ToString("N");

		var fieldErrors = new Dictionary<string, LocalizedText>();
		if (string.IsNullOrWhiteSpace(name))
		{
			fieldErrors[JellyfinConfigKeys.ConfigurationName] = AppStrings.Errors.Config.TitleRequired();
		}

		if (!TryParseUrl(url, out var baseUri))
		{
			fieldErrors[JellyfinConfigKeys.Url] = Strings.EnterValidUrl();
		}

		var reuseToken = method == JellyfinConfigKeys.AuthLogin &&
			string.IsNullOrEmpty(password) &&
			!string.IsNullOrEmpty(storedToken);

		if (method == JellyfinConfigKeys.AuthApiKey && apiKey.Length == 0)
		{
			fieldErrors[JellyfinConfigKeys.ApiKey] = Strings.EnterApiKey();
		}
		else if (method == JellyfinConfigKeys.AuthLogin && username.Length == 0)
		{
			fieldErrors[JellyfinConfigKeys.Username] = Strings.EnterUsername();
		}
		else if (method == JellyfinConfigKeys.AuthLogin && !reuseToken && string.IsNullOrEmpty(password))
		{
			fieldErrors[JellyfinConfigKeys.Password] = Strings.EnterPassword();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(ConnectionStep(context), fieldErrors: fieldErrors);
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_testTimeout);

		var values = new Dictionary<string, ConfigFlowValue>
		{
			[JellyfinConfigKeys.Url] = ConfigFlowValue.Plain(baseUri!.ToString()),
			[JellyfinConfigKeys.AuthMethod] = ConfigFlowValue.Plain(method),
			[JellyfinConfigKeys.DeviceId] = ConfigFlowValue.Plain(deviceId),
			// The host keeps every secret step field; overwriting it with an empty plain value is what
			// keeps the password out of storage once it has been exchanged for a token.
			[JellyfinConfigKeys.Password] = ConfigFlowValue.Plain(string.Empty)
		};

		try
		{
			await _clientFactory(new JellyfinServerSettings(baseUri, null, deviceId))
				.GetPublicInfoAsync(timeout.Token)
				.ConfigureAwait(false);

			if (method == JellyfinConfigKeys.AuthApiKey)
			{
				await _clientFactory(new JellyfinServerSettings(baseUri, apiKey, deviceId))
					.VerifyTokenAsync(timeout.Token)
					.ConfigureAwait(false);
				values[JellyfinConfigKeys.Username] = ConfigFlowValue.Plain(string.Empty);
			}
			else if (reuseToken)
			{
				await _clientFactory(new JellyfinServerSettings(baseUri, storedToken, deviceId))
					.VerifyTokenAsync(timeout.Token)
					.ConfigureAwait(false);
				values[JellyfinConfigKeys.Username] = ConfigFlowValue.Plain(username);
			}
			else
			{
				var result = await _clientFactory(new JellyfinServerSettings(baseUri, null, deviceId))
					.AuthenticateAsync(username, password, timeout.Token)
					.ConfigureAwait(false);
				values[JellyfinConfigKeys.Username] = ConfigFlowValue.Plain(username);
				values[JellyfinConfigKeys.AccessToken] = ConfigFlowValue.Secret(result.AccessToken!);
			}
		}
		catch (JellyfinAuthenticationException)
		{
			var field = method == JellyfinConfigKeys.AuthApiKey ? JellyfinConfigKeys.ApiKey : JellyfinConfigKeys.Password;
			return ConfigFlowResult.Error(ConnectionStep(context),
				fieldErrors: new Dictionary<string, LocalizedText>
				{
					[field] = method == JellyfinConfigKeys.AuthApiKey
						? Strings.ApiKeyRejected()
						: Strings.LoginRejected()
				});
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return ConfigFlowResult.Error(ConnectionStep(context), Strings.ConnectionTimedOut(url: baseUri.ToString()));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Jellyfin connection test failed for {Url}", baseUri);
			return ConfigFlowResult.Error(ConnectionStep(context), Strings.ConnectionFailed(url: baseUri.ToString()));
		}

		return ConfigFlowResult.Complete(name, values);
	}

	internal static bool TryParseUrl(string text, out Uri? uri)
	{
		uri = null;
		if (text.Length == 0)
		{
			return false;
		}

		var candidate = text.Contains("://", StringComparison.Ordinal) ? text : $"http://{text}";
		if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
			(parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
			string.IsNullOrEmpty(parsed.Host))
		{
			return false;
		}

		uri = new UriBuilder(parsed) { Query = string.Empty, Fragment = string.Empty }.Uri;
		return true;
	}

	private static string Read(IReadOnlyDictionary<string, object?> input, string key)
		=> input.GetValueOrDefault(key) as string ?? string.Empty;

	private static ConfigFlowStep ConnectionStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(JellyfinConfigKeys.ConfigurationName,
				label: AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: AppStrings.Integrations.Detail.ConfigurationNamePlaceholder(),
				required: true));
		}

		fields.AddRange([
			ActionParameter.Url(JellyfinConfigKeys.Url,
				label: Strings.UrlLabel(),
				description: Strings.UrlDescription(),
				placeholder: "http://192.168.1.10:8096",
				required: true),
			ActionParameter.Choice(JellyfinConfigKeys.AuthMethod,
				[
					new ActionParameterOption { Value = JellyfinConfigKeys.AuthApiKey, Label = Strings.AuthApiKey() },
					new ActionParameterOption { Value = JellyfinConfigKeys.AuthLogin, Label = Strings.AuthLogin() }
				],
				label: Strings.AuthMethodLabel(),
				defaultValue: JellyfinConfigKeys.AuthApiKey,
				required: true),
			ActionParameter.Secret(JellyfinConfigKeys.ApiKey,
					label: Strings.ApiKeyLabel(),
					description: Strings.ApiKeyDescription())
				.OnlyWhen(JellyfinConfigKeys.AuthMethod, JellyfinConfigKeys.AuthApiKey),
			ActionParameter.Text(JellyfinConfigKeys.Username, label: Strings.UsernameLabel())
				.OnlyWhen(JellyfinConfigKeys.AuthMethod, JellyfinConfigKeys.AuthLogin),
			ActionParameter.Secret(JellyfinConfigKeys.Password,
					label: Strings.PasswordLabel(),
					description: Strings.PasswordDescription())
				.OnlyWhen(JellyfinConfigKeys.AuthMethod, JellyfinConfigKeys.AuthLogin)
		]);

		return new ConfigFlowStep
		{
			StepId = StepId,
			Title = Strings.ConnectTitle(),
			Description = Strings.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = Strings.InstructionApiKey() },
				new ConfigFlowInstruction { Text = Strings.InstructionLogin() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = Strings.DocumentationLink(),
					Url = "https://jellyfin.org/docs/general/server/configuration"
				}
			],
			Fields = fields
		};
	}
}
