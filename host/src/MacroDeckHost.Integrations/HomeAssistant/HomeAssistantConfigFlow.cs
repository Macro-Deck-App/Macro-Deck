using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant;

public sealed class HomeAssistantConfigFlow : IConfigFlow
{
	private const string ConnectionStepId = "connection";

	private static readonly ILogger _logger =
		IntegrationLog.For<HomeAssistantConfigFlow>(HomeAssistantIntegration.IntegrationId);

	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(15);

	private readonly Func<IHomeAssistantClient> _clientFactory;

	public HomeAssistantConfigFlow()
		: this(() => new HomeAssistantClient())
	{
	}

	internal HomeAssistantConfigFlow(Func<IHomeAssistantClient> clientFactory)
	{
		_clientFactory = clientFactory;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		return stepId == ConnectionStepId
			? SubmitConnection(input, cancellationToken)
			: Task.FromResult(ConfigFlowResult.Error(ConnectionStep(),
				AppStrings.Integrations.HomeAssistant.Config.UnknownStep()));
	}

	private static ConfigFlowStep ConnectionStep()
		=> new()
		{
			StepId = ConnectionStepId,
			Title = AppStrings.Integrations.HomeAssistant.Config.ConnectTitle(),
			Description = AppStrings.Integrations.HomeAssistant.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.HomeAssistant.Config.InstructionOpenProfile()
				},
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.HomeAssistant.Config.InstructionCreateToken() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.HomeAssistant.Config.InstructionPasteAddress()
				}
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.HomeAssistant.Config.AuthDocumentationLabel(),
					Url = "https://www.home-assistant.io/docs/authentication/#your-account-profile"
				}
			],
			Fields =
			[
				ActionParameter.Url(HomeAssistantConfigKeys.BaseUrl,
					label: AppStrings.Integrations.HomeAssistant.Config.AddressLabel(),
					description: AppStrings.Integrations.HomeAssistant.Config.AddressDescription(
						exampleUrl: HomeAssistantEndpoint.ExampleUrl),
					placeholder: HomeAssistantEndpoint.ExampleUrl,
					required: true,
					autoPrefixHttps: true),
				ActionParameter.Secret(HomeAssistantConfigKeys.Token,
					label: AppStrings.Integrations.HomeAssistant.Config.TokenLabel(),
					description: AppStrings.Integrations.HomeAssistant.Config.TokenDescription(),
					required: true)
			]
		};

	private async Task<ConfigFlowResult> SubmitConnection(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		var baseUrl = (input.GetValueOrDefault(HomeAssistantConfigKeys.BaseUrl) as string ?? string.Empty).Trim();
		var token = (input.GetValueOrDefault(HomeAssistantConfigKeys.Token) as string ?? string.Empty).Trim();

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		var uri = HomeAssistantEndpoint.TryBuild(baseUrl);
		if (uri is null)
		{
			fieldErrors[HomeAssistantConfigKeys.BaseUrl] =
				AppStrings.Integrations.HomeAssistant.Config.EnterAddress(exampleUrl: HomeAssistantEndpoint.ExampleUrl);
		}

		if (token.Length == 0)
		{
			fieldErrors[HomeAssistantConfigKeys.Token] =
				AppStrings.Integrations.HomeAssistant.Config.EnterToken();
		}

		if (fieldErrors.Count > 0 || uri is null)
		{
			return ConfigFlowResult.Error(ConnectionStep(), fieldErrors: fieldErrors);
		}

		var test = await TestConnectionAsync(uri, token, cancellationToken);
		if (test.TokenError is { } tokenError)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[HomeAssistantConfigKeys.Token] = tokenError
				});
		}

		if (test.Error is { } error)
		{
			return ConfigFlowResult.Error(ConnectionStep(), error);
		}

		// The address and the token are deliberately absent: the host already accumulated both from this
		// step and encrypted the token, and repeating them here would store a second secret. The watched-
		// entity keys are absent too, now that the browser replaces that picker - leaving them out of this
		// dictionary leaves whatever an older install already stored untouched, which is what lets
		// HomeAssistantWatchedEntityMigration still find it after this flow is reconfigured.
		var title = test.LocationName is { Length: > 0 } location
			? $"Home Assistant ({location})"
			: $"Home Assistant ({uri.Host})";

		return ConfigFlowResult.Complete(title, new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal));
	}

	private async Task<TestResult> TestConnectionAsync(Uri uri, string token, CancellationToken cancellationToken)
	{
		using var client = _clientFactory();
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_connectTimeout);

		// Once the handshake is through, the address and the token are both proven, so a later failure
		// must not be reported as "check the address".
		var authenticated = false;

		try
		{
			await client.ConnectAsync(uri, token, timeout.Token);
			authenticated = true;

			await client.SendCommandAsync("get_states", cancellationToken: timeout.Token);
			var (locationName, _) = HomeAssistantResponses.ReadConfig(
				await client.SendCommandAsync("get_config", cancellationToken: timeout.Token));

			return new TestResult(locationName, null, null);
		}
		catch (HomeAssistantAuthenticationException ex)
		{
			_logger.Warning("Home Assistant rejected the token during setup: {Reason}", ex.Message);
			return new TestResult(null,
				null,
				"Home Assistant rejected this token. Create a new long-lived access token and paste it here.");
		}
		catch (HomeAssistantTlsException ex)
		{
			// .NET honours the operating system's certificate store - and the macOS Keychain - but not a
			// Linux browser's own store, so the wording has to say where to trust it.
			_logger.Warning(ex, "Home Assistant certificate validation failed for {Uri}", uri);
			return new TestResult(null,
				$"The certificate presented by {uri.Host} could not be validated. Trust it in your operating " +
				"system certificate store, or connect over http:// on your local network.",
				null);
		}
		catch (HomeAssistantRequestException ex)
		{
			_logger.Warning(ex, "Home Assistant handshake failed for {Uri}", uri);
			return new TestResult(null, "That address answered, but it is not the Home Assistant WebSocket API.", null);
		}
		catch (Exception ex) when (ex is OperationCanceledException or TimeoutException &&
			!cancellationToken.IsCancellationRequested)
		{
			_logger.Debug(ex, "Home Assistant connection test timed out for {Uri}", uri);
			return new TestResult(null, authenticated ? TooSlow(uri) : Unreachable(uri), null);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Home Assistant connection test failed for {Uri}", uri);
			return new TestResult(null, authenticated ? TooSlow(uri) : Unreachable(uri), null);
		}
		finally
		{
			await client.DisconnectAsync();
		}
	}

	private static string TooSlow(Uri uri)
		=> $"Connected to Home Assistant at {uri.Host}, but it did not answer in time. Try again in a moment - " +
			"a Home Assistant that is still starting up can be slow to respond.";

	private static string Unreachable(Uri uri)
		=> $"Could not reach Home Assistant at {uri.Host}. Check the address and that Home Assistant is " +
			"running. The default port is 8123.";

	private sealed record TestResult(string? LocationName, string? Error, string? TokenError);
}
