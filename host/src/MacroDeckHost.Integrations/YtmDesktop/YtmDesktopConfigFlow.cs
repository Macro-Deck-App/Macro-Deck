using System.Globalization;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.YtmDesktop;

public sealed class YtmDesktopConfigFlow : IConfigFlow
{
	private const string EnableStepId = "enable";
	private const string AuthorizeStepId = "authorize";

	private const string AppId = "macrodeck";

	private const string AppName = "Macro Deck";

	private const string ReleasesUrl = "https://github.com/ytmdesktop/ytmdesktop/releases";

	private const string DocumentationUrl =
		"https://github.com/ytmdesktop/ytmdesktop/wiki/v2-%E2%80%90-Companion-Server-API-v1";

	private static readonly ILogger _logger =
		IntegrationLog.For<YtmDesktopConfigFlow>(YtmDesktopIntegration.IntegrationId);

	private readonly Func<YtmDesktopEndpoint, IYtmDesktopApiClient> _clientFactory;

	private YtmDesktopEndpoint _endpoint = new(YtmDesktopEndpoint.DefaultHost, YtmDesktopEndpoint.DefaultPort);
	private string? _code;

	public YtmDesktopConfigFlow()
		: this(endpoint => new YtmDesktopApiClient(endpoint))
	{
	}

	internal YtmDesktopConfigFlow(Func<YtmDesktopEndpoint, IYtmDesktopApiClient> clientFactory)
	{
		_clientFactory = clientFactory;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(EnableStep()));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId switch
		{
			EnableStepId => await SubmitEnableAsync(input, cancellationToken),
			AuthorizeStepId => await SubmitAuthorizeAsync(input, cancellationToken),
			_ => ConfigFlowResult.Error(EnableStep(), AppStrings.Integrations.YtmDesktop.Config.UnknownStep())
		};

	private async Task<ConfigFlowResult> SubmitEnableAsync(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		var host = input.GetValueOrDefault(YtmDesktopConfigKeys.Host)?.ToString();
		if (!TryReadPort(input.GetValueOrDefault(YtmDesktopConfigKeys.Port), out var port))
		{
			return ConfigFlowResult.Error(EnableStep(),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[YtmDesktopConfigKeys.Port] = AppStrings.Integrations.YtmDesktop.Config.EnterValidPort()
				});
		}

		_endpoint = YtmDesktopEndpoint.Create(host, port);

		using var client = _clientFactory(_endpoint);

		try
		{
			var versions = await client.GetApiVersionsAsync(cancellationToken);
			if (!versions.Contains("v1", StringComparer.Ordinal))
			{
				return ConfigFlowResult.Error(EnableStep(),
					AppStrings.Integrations.YtmDesktop.Config.ApiVersionUnsupported());
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not reach the Companion Server at {Address}", _endpoint.DisplayAddress);
			return ConfigFlowResult.Error(EnableStep(),
				AppStrings.Integrations.YtmDesktop.Config
					.CompanionServerUnreachable(address: _endpoint.DisplayAddress));
		}

		return await RequestCodeAsync(client, cancellationToken);
	}

	private async Task<ConfigFlowResult> SubmitAuthorizeAsync(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		var host = input.GetValueOrDefault(YtmDesktopConfigKeys.Host)?.ToString();
		if (TryReadPort(input.GetValueOrDefault(YtmDesktopConfigKeys.Port), out var port))
		{
			_endpoint = YtmDesktopEndpoint.Create(host, port);
		}

		using var client = _clientFactory(_endpoint);

		if (_code is not { } code)
		{
			return await RequestCodeAsync(client, cancellationToken);
		}

		string token;
		try
		{
			token = await client.RequestTokenAsync(AppId, code, cancellationToken);
		}
		catch (YtmDesktopApiException ex)
		{
			return await HandleAuthorizationFailureAsync(client, ex, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "The authorization request did not complete");
			return ConfigFlowResult.Error(AuthorizeStep(code),
				AppStrings.Integrations.YtmDesktop.Config.StoppedAnswering());
		}

		client.UseToken(token);
		try
		{
			await client.GetStateAsync(cancellationToken);
		}
		catch (YtmDesktopAuthorizationException ex)
		{
			_logger.Warning(ex, "The token YouTube Music Desktop App issued was rejected immediately");
			_code = null;
			return ConfigFlowResult.Error(EnableStep(),
				AppStrings.Integrations.YtmDesktop.Config.TokenRejectedImmediately());
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not verify the new token; continuing anyway");
		}

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[YtmDesktopConfigKeys.Host] = ConfigFlowValue.Plain(_endpoint.Host),
			[YtmDesktopConfigKeys.Port] = ConfigFlowValue.Plain(_endpoint.Port.ToString(CultureInfo.InvariantCulture)),
			[YtmDesktopConfigKeys.Token] = ConfigFlowValue.Secret(token)
		};

		var title = string.Equals(_endpoint.Host, YtmDesktopEndpoint.DefaultHost, StringComparison.Ordinal)
			? "YouTube Music Desktop App"
			: $"YouTube Music Desktop App ({_endpoint.Host})";

		return ConfigFlowResult.Complete(title, values);
	}

	private async Task<ConfigFlowResult> HandleAuthorizationFailureAsync(
		IYtmDesktopApiClient client,
		YtmDesktopApiException ex,
		CancellationToken cancellationToken)
	{
		switch (ex.ErrorCode)
		{
			case YtmErrorCodes.AuthorizationDisabled:
				_code = null;
				return ConfigFlowResult.Error(EnableStep(),
					AppStrings.Integrations.YtmDesktop.Config.AuthorizationDisabledAgain());

			case YtmErrorCodes.AuthorizationTooMany:
				// Deliberately keeps the current code: asking for another one here would spend one of the
				// five per minute on a request that was never going to be shown.
				return ConfigFlowResult.Error(AuthorizeStep(_code!),
					AppStrings.Integrations.YtmDesktop.Config.AnotherAuthorizationPending());

			case YtmErrorCodes.AuthorizationDenied:
			case YtmErrorCodes.AuthorizationTimeOut:
				return await RequestCodeAsync(client,
					cancellationToken,
					AppStrings.Integrations.YtmDesktop.Config.RequestDeclinedOrTimedOut());

			case YtmErrorCodes.AuthorizationInvalid:
				return await RequestCodeAsync(client,
					cancellationToken,
					AppStrings.Integrations.YtmDesktop.Config.CodeNoLongerValid());

			default:
				if ((int)ex.StatusCode == 429)
				{
					return ConfigFlowResult.Error(AuthorizeStep(_code!),
						AppStrings.Integrations.YtmDesktop.Config.TooManyAttempts());
				}

				_logger.Warning(ex, "The authorization request was rejected");
				return ConfigFlowResult.Error(AuthorizeStep(_code!),
					AppStrings.Integrations.YtmDesktop.Config.AuthorizationRequestRejected());
		}
	}

	private async Task<ConfigFlowResult> RequestCodeAsync(
		IYtmDesktopApiClient client,
		CancellationToken cancellationToken,
		LocalizedText? message = null)
	{
		try
		{
			_code = await client.RequestAuthCodeAsync(AppId,
				AppName,
				YtmDesktopIntegration.IntegrationVersion,
				cancellationToken);
		}
		catch (YtmDesktopApiException ex) when (ex.ErrorCode == YtmErrorCodes.AuthorizationDisabled)
		{
			_code = null;
			return ConfigFlowResult.Error(EnableStep(),
				AppStrings.Integrations.YtmDesktop.Config.AuthorizationDisabled());
		}
		catch (YtmDesktopApiException ex) when ((int)ex.StatusCode == 429)
		{
			_code = null;
			return ConfigFlowResult.Error(EnableStep(),
				AppStrings.Integrations.YtmDesktop.Config.TooManyAttempts());
		}
		catch (Exception ex)
		{
			_code = null;
			_logger.Warning(ex, "Could not request an authorization code");
			return ConfigFlowResult.Error(EnableStep(),
				AppStrings.Integrations.YtmDesktop.Config.NoAuthorizationCode());
		}

		var step = AuthorizeStep(_code);
		return message is null ? ConfigFlowResult.Step(step) : ConfigFlowResult.Error(step, message.Value);
	}

	private ConfigFlowStep EnableStep()
		=> new()
		{
			StepId = EnableStepId,
			Title = AppStrings.Integrations.YtmDesktop.Config.EnableTitle(),
			Description = AppStrings.Integrations.YtmDesktop.Config.EnableDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.YtmDesktop.Config.OpenSettingsInstruction() },
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.YtmDesktop.Config.EnableCompanionServerInstruction() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.YtmDesktop.Config.EnableCompanionAuthorizationInstruction()
				},
				new ConfigFlowInstruction { Text = AppStrings.Integrations.YtmDesktop.Config.ContinueInstruction() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.YtmDesktop.Config.ReleasesLinkLabel(),
					Url = ReleasesUrl
				},
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.YtmDesktop.Config.DocumentationLinkLabel(),
					Url = DocumentationUrl
				}
			],
			Fields = [],
			AdvancedFields = AddressFields()
		};

	private ConfigFlowStep AuthorizeStep(string code)
		=> new()
		{
			StepId = AuthorizeStepId,
			Title = AppStrings.Integrations.YtmDesktop.Config.AuthorizeTitle(),
			Description = AppStrings.Integrations.YtmDesktop.Config.AuthorizeDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.YtmDesktop.Config.PressContinueInstruction() },
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.YtmDesktop.Config.CheckCodeInstruction(),
					Values =
					[
						new ConfigFlowCopyValue
						{
							Label = AppStrings.Integrations.YtmDesktop.Config.AuthorizationCodeLabel(), Value = code
						}
					]
				},
				new ConfigFlowInstruction { Text = AppStrings.Integrations.YtmDesktop.Config.ClickAllowInstruction() }
			],
			Fields = [],
			AdvancedFields = AddressFields()
		};

	private IReadOnlyList<ActionParameter> AddressFields()
		=>
		[
			ActionParameter.Text(YtmDesktopConfigKeys.Host,
				label: AppStrings.Integrations.YtmDesktop.Config.AddressLabel(),
				description: AppStrings.Integrations.YtmDesktop.Config.AddressDescription(),
				defaultValue: _endpoint.Host),
			ActionParameter.Number(YtmDesktopConfigKeys.Port,
				label: AppStrings.Integrations.YtmDesktop.Config.PortLabel(),
				description: AppStrings.Integrations.YtmDesktop.Config.PortDescription(),
				min: 1,
				max: 65535,
				defaultValue: _endpoint.Port)
		];

	private static bool TryReadPort(object? raw, out int port)
	{
		port = YtmDesktopEndpoint.DefaultPort;

		switch (raw)
		{
			case null:
				return true;
			case int value:
				port = value;
				break;
			case long value:
				port = (int)value;
				break;
			case double value:
				port = (int)value;
				break;
			case string text when string.IsNullOrWhiteSpace(text):
				return true;
			case string text when double.TryParse(text,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parsed):
				port = (int)parsed;
				break;
			default:
				return false;
		}

		return port is > 0 and <= 65535;
	}
}
