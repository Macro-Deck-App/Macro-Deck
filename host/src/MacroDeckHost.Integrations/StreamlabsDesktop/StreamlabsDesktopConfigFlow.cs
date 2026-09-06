using System.Globalization;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

public sealed class StreamlabsDesktopConfigFlow : IConfigFlow
{
	private const string StepId = "connection";

	private static readonly ILogger _logger =
		IntegrationLog.For<StreamlabsDesktopConfigFlow>(StreamlabsDesktopIntegration.IntegrationId);

	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(8);

	private readonly Func<IStreamlabsClient> _clientFactory;

	public StreamlabsDesktopConfigFlow()
		: this(() => new StreamlabsJsonRpcClient())
	{
	}

	internal StreamlabsDesktopConfigFlow(Func<IStreamlabsClient> clientFactory)
	{
		_clientFactory = clientFactory;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> stepId switch
		{
			StepId => await SubmitConnection(input, cancellationToken).ConfigureAwait(false),
			_ => ConfigFlowResult.Error(ConnectionStep(),
				AppStrings.Integrations.StreamlabsDesktop.Config.UnknownStep())
		};

	internal static ConfigFlowStep ConnectionStep() => new()
	{
		StepId = StepId,
		Title = AppStrings.Integrations.StreamlabsDesktop.Config.ConnectTitle(),
		Description = AppStrings.Integrations.StreamlabsDesktop.Config.ConnectDescription(),
		Instructions =
		[
			new ConfigFlowInstruction
				{ Text = AppStrings.Integrations.StreamlabsDesktop.Config.InstructionOpenSettings() },
			new ConfigFlowInstruction
			{
				Text = AppStrings.Integrations.StreamlabsDesktop.Config.InstructionOpenRemoteControl()
			},
			new ConfigFlowInstruction
				{ Text = AppStrings.Integrations.StreamlabsDesktop.Config.InstructionRevealToken() },
			new ConfigFlowInstruction
				{ Text = AppStrings.Integrations.StreamlabsDesktop.Config.InstructionPasteToken() }
		],
		Links =
		[
			new ConfigFlowLink
			{
				Label = AppStrings.Integrations.StreamlabsDesktop.Config.ApiDocumentationLinkLabel(),
				Url = "https://stream-labs.github.io/streamlabs-desktop-api-docs/docs/index.html"
			}
		],
		Fields =
		[
			ActionParameter.Secret(StreamlabsDesktopConfigKeys.Token,
				label: AppStrings.Integrations.StreamlabsDesktop.Config.ApiTokenLabel(),
				description: AppStrings.Integrations.StreamlabsDesktop.Config.ApiTokenDescription(),
				required: true)
		],
		AdvancedFields =
		[
			ActionParameter.Text(StreamlabsDesktopConfigKeys.Host,
				label: AppStrings.Integrations.StreamlabsDesktop.Config.AddressLabel(),
				description: AppStrings.Integrations.StreamlabsDesktop.Config.AddressDescription(),
				placeholder: StreamlabsDesktopEndpoint.DefaultHost),
			ActionParameter.Number(StreamlabsDesktopConfigKeys.Port,
				label: AppStrings.Integrations.StreamlabsDesktop.Config.PortLabel(),
				description: AppStrings.Integrations.StreamlabsDesktop.Config.PortDescription(),
				min: 1,
				max: 65535)
		]
	};

	private async Task<ConfigFlowResult> SubmitConnection(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		var pasted = StreamlabsTokenReader.Parse(input.GetValueOrDefault(StreamlabsDesktopConfigKeys.Token) as string);
		if (pasted is not { } connection)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[StreamlabsDesktopConfigKeys.Token]
						= AppStrings.Integrations.StreamlabsDesktop.Config.PasteApiToken()
				});
		}

		var port = ParsePort(input.GetValueOrDefault(StreamlabsDesktopConfigKeys.Port));
		if (port is 0)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[StreamlabsDesktopConfigKeys.Port] =
						AppStrings.Integrations.StreamlabsDesktop.Config.EnterValidPort()
				});
		}

		var host = input.GetValueOrDefault(StreamlabsDesktopConfigKeys.Host) as string;
		var endpoint = StreamlabsDesktopEndpoint.Create(host, port ?? connection.Port);

		var error = await TestConnectionAsync(endpoint, connection.Token, cancellationToken).ConfigureAwait(false);
		if (error is not null)
		{
			return ConfigFlowResult.Error(ConnectionStep(), error.Value);
		}

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[StreamlabsDesktopConfigKeys.Host] = ConfigFlowValue.Plain(endpoint.Host),
			[StreamlabsDesktopConfigKeys.Port] =
				ConfigFlowValue.Plain(endpoint.Port.ToString(CultureInfo.InvariantCulture)),
			[StreamlabsDesktopConfigKeys.Token] = ConfigFlowValue.Secret(connection.Token)
		};

		var title = endpoint.Host == StreamlabsDesktopEndpoint.DefaultHost
			? "Streamlabs Desktop"
			: string.Create(CultureInfo.InvariantCulture, $"Streamlabs Desktop ({endpoint.Host})");

		return ConfigFlowResult.Complete(title, values);
	}

	private async Task<LocalizedText?> TestConnectionAsync(
		StreamlabsDesktopEndpoint endpoint,
		string token,
		CancellationToken cancellationToken)
	{
		var client = _clientFactory();
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(_connectTimeout);

			await client.ConnectAsync(endpoint.WebSocketUri(), token, timeout.Token).ConfigureAwait(false);

			await client.InvokeAsync(StreamlabsServices.Scenes, StreamlabsServices.GetScenes, null, timeout.Token)
				.ConfigureAwait(false);

			return null;
		}
		catch (StreamlabsAuthenticationException)
		{
			return AppStrings.Integrations.StreamlabsDesktop.Config.TokenRejected();
		}
		catch (OperationCanceledException)
		{
			return Unreachable(endpoint);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Streamlabs Desktop connection test failed for {Address}", endpoint.DisplayAddress);
			return Unreachable(endpoint);
		}
		finally
		{
			try
			{
				await client.DisconnectAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Error while closing the Streamlabs Desktop test connection");
			}

			client.Dispose();
		}
	}

	private static LocalizedText Unreachable(StreamlabsDesktopEndpoint endpoint)
		=> AppStrings.Integrations.StreamlabsDesktop.Config.Unreachable(address: endpoint.DisplayAddress);

	private static int? ParsePort(object? value) => value switch
	{
		null => null,
		int number => Valid(number),
		long number => Valid((int)number),
		double number => Valid((int)number),
		string text when string.IsNullOrWhiteSpace(text) => null,
		string text when int.TryParse(text,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var parsed) => Valid(parsed),
		_ => 0
	};

	private static int Valid(int port) => port is > 0 and <= 65535 ? port : 0;
}
