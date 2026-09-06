using System.Globalization;
using MacroDeckHost.Integrations.Meld.Protocol;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Meld;

public sealed class MeldConfigFlow : IConfigFlow
{
	private const string ConnectionStepId = "connection";
	private const string RemoteWarningStepId = "remote-warning";
	private const string ConfirmField = "confirm";

	private static readonly ILogger _logger = IntegrationLog.For<MeldConfigFlow>(MeldObjects.IntegrationId);
	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(8);

	private readonly Func<IQWebChannelClient> _clientFactory;

	private string _pendingHost = MeldEndpoint.DefaultHost;
	private int _pendingPort = MeldEndpoint.DefaultPort;

	public MeldConfigFlow()
		: this(() => new QWebChannelClient())
	{
	}

	internal MeldConfigFlow(Func<IQWebChannelClient> clientFactory)
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
			ConnectionStepId => await SubmitConnection(input, cancellationToken).ConfigureAwait(false),
			RemoteWarningStepId => await SubmitRemoteWarning(input, cancellationToken).ConfigureAwait(false),
			_ => ConfigFlowResult.Error(ConnectionStep(), AppStrings.Integrations.Meld.Config.UnknownStep())
		};

	private async Task<ConfigFlowResult> SubmitConnection(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		var hostInput = (input.GetValueOrDefault(MeldConfigKeys.Host) as string ?? string.Empty).Trim();
		var host = hostInput.Length == 0 ? MeldEndpoint.DefaultHost : hostInput;

		var port = ParsePort(input.GetValueOrDefault(MeldConfigKeys.Port));
		if (port is null or < 1 or > 65535)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[MeldConfigKeys.Port] =
						AppStrings.Integrations.Meld.Config.EnterValidPort(defaultPort: MeldEndpoint.DefaultPort)
				});
		}

		Uri endpoint;
		try
		{
			endpoint = MeldEndpoint.Build(host, port.Value);
		}
		catch (UriFormatException)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[MeldConfigKeys.Host] = AppStrings.Integrations.Meld.Config.EnterValidHost()
				});
		}

		if (!MeldEndpoint.IsLoopback(host))
		{
			_pendingHost = host;
			_pendingPort = port.Value;
			return ConfigFlowResult.Step(RemoteWarningStep(host, port.Value));
		}

		return await TestAndComplete(host, port.Value, endpoint, ConnectionStep, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<ConfigFlowResult> SubmitRemoteWarning(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		if (!ReadBool(input.GetValueOrDefault(ConfirmField)))
		{
			return ConfigFlowResult.Error(RemoteWarningStep(_pendingHost, _pendingPort),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[ConfirmField] = AppStrings.Integrations.Meld.Config.ConfirmRisk()
				});
		}

		var endpoint = MeldEndpoint.Build(_pendingHost, _pendingPort);
		return await TestAndComplete(_pendingHost,
				_pendingPort,
				endpoint,
				() => RemoteWarningStep(_pendingHost, _pendingPort),
				cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<ConfigFlowResult> TestAndComplete(
		string host,
		int port,
		Uri endpoint,
		Func<ConfigFlowStep> errorStep,
		CancellationToken cancellationToken)
	{
		var error = await TestConnectionAsync(endpoint, cancellationToken).ConfigureAwait(false);
		if (error is not null)
		{
			return ConfigFlowResult.Error(errorStep(), error.Value);
		}

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[MeldConfigKeys.Host] = ConfigFlowValue.Plain(host),
			[MeldConfigKeys.Port] = ConfigFlowValue.Plain(port.ToString(CultureInfo.InvariantCulture))
		};

		return ConfigFlowResult.Complete("Meld Studio", values);
	}

	private async Task<LocalizedText?> TestConnectionAsync(Uri endpoint, CancellationToken cancellationToken)
	{
		var client = _clientFactory();
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(_connectTimeout);

			var objects = await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);

			// Something answered the QWebChannel handshake, but not as Meld Studio - a distinct error
			// from "nothing is there", because the fix (check what is actually listening) differs.
			return objects.ContainsKey(MeldObjects.Object)
				? null
				: AppStrings.Integrations.Meld.Config.NotMeldStudio(host: endpoint.Host,
					port: endpoint.Port);
		}
		catch (OperationCanceledException)
		{
			return Unreachable(endpoint);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Meld Studio connection test failed for {Endpoint}", endpoint);
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
				_logger.Debug(ex, "Error while closing the Meld Studio test connection");
			}

			client.Dispose();
		}
	}

	private static LocalizedText Unreachable(Uri endpoint)
		=> AppStrings.Integrations.Meld.Config.Unreachable(host: endpoint.Host, port: endpoint.Port);

	private static bool ReadBool(object? value) => value switch
	{
		bool b => b,
		string s => bool.TryParse(s, out var parsed) && parsed,
		_ => false
	};

	private static int? ParsePort(object? value) => value switch
	{
		null => MeldEndpoint.DefaultPort,
		int i => i,
		long l => (int)l,
		double d => (int)d,
		string s when string.IsNullOrWhiteSpace(s) => MeldEndpoint.DefaultPort,
		string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
		_ => null
	};

	private static ConfigFlowStep ConnectionStep() => new()
	{
		StepId = ConnectionStepId,
		Title = AppStrings.Integrations.Meld.Config.ConnectTitle(),
		Description = AppStrings.Integrations.Meld.Config.ConnectDescription(),
		Instructions =
		[
			new ConfigFlowInstruction { Text = AppStrings.Integrations.Meld.Config.InstructionOpenSettings() },
			new ConfigFlowInstruction { Text = AppStrings.Integrations.Meld.Config.InstructionEnableWebSocket() }
		],
		Links =
		[
			new ConfigFlowLink
			{
				Label = AppStrings.Integrations.Meld.Config.SettingsDocumentationLink(),
				Url = "https://meldstudio.co/docs/settings/"
			}
		],
		Fields = [],
		AdvancedFields =
		[
			ActionParameter.Text(MeldConfigKeys.Host,
				label: AppStrings.Integrations.Meld.Config.HostLabel(),
				description: AppStrings.Integrations.Meld.Config.HostDescription(),
				placeholder: MeldEndpoint.DefaultHost),
			new ActionParameter
			{
				Name = MeldConfigKeys.Port,
				Type = ActionParameterType.Number,
				Label = AppStrings.Integrations.Meld.Config.PortLabel(),
				Description = AppStrings.Integrations.Meld.Config.PortDescription(),
				Min = 1,
				Max = 65535,
				Placeholder = MeldEndpoint.DefaultPort.ToString(CultureInfo.InvariantCulture)
			}
		]
	};

	private static ConfigFlowStep RemoteWarningStep(string host, int port) => new()
	{
		StepId = RemoteWarningStepId,
		Title = AppStrings.Integrations.Meld.Config.RemoteWarningTitle(),
		Description = AppStrings.Integrations.Meld.Config.RemoteWarningDescription(host: host, port: port),
		Fields =
		[
			ActionParameter.Toggle(ConfirmField, label: AppStrings.Integrations.Meld.Config.ConfirmRiskLabel())
		]
	};
}
