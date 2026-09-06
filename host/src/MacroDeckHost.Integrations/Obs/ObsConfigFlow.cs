using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Obs;

public sealed class ObsConfigFlow : IConfigFlow
{
	internal const int DefaultPort = 4455;
	internal const string DefaultHost = "127.0.0.1";

	private static readonly ILogger _logger = IntegrationLog.For<ObsConfigFlow>(ObsIntegration.IntegrationId);
	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(8);

	private readonly Func<IObsClient> _clientFactory;

	public ObsConfigFlow()
		: this(() => new ObsClient())
	{
	}

	internal ObsConfigFlow(Func<IObsClient> clientFactory)
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
		return stepId switch
		{
			"connection" => await SubmitConnection(input, context, cancellationToken),
			_ => ConfigFlowResult.Error(ConnectionStep(context), AppStrings.Integrations.Obs.Config.UnknownStep())
		};
	}

	private async Task<ConfigFlowResult> SubmitConnection(
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		var entryTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var configurationName = entryTitle ??
			(input.GetValueOrDefault(ObsConfigKeys.ConfigurationName) as string ?? string.Empty).Trim();
		var host = (input.GetValueOrDefault(ObsConfigKeys.Host) as string ?? string.Empty).Trim();
		var password = input.GetValueOrDefault(ObsConfigKeys.Password) as string ?? string.Empty;
		var port = ParsePort(input.GetValueOrDefault(ObsConfigKeys.Port));

		var fieldErrors = new Dictionary<string, LocalizedText>();
		if (string.IsNullOrWhiteSpace(configurationName))
		{
			fieldErrors[ObsConfigKeys.ConfigurationName] = AppStrings.Errors.Config.TitleRequired();
		}

		if (string.IsNullOrWhiteSpace(host))
		{
			fieldErrors[ObsConfigKeys.Host] = AppStrings.Integrations.Obs.Config.EnterHost();
		}

		if (port is null or < 1 or > 65535)
		{
			fieldErrors[ObsConfigKeys.Port] = AppStrings.Integrations.Obs.Config.EnterValidPort();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(ConnectionStep(context), fieldErrors: fieldErrors);
		}

		var url = $"ws://{host}:{port}";
		var error = await TestConnectionAsync(url, password, cancellationToken);
		if (error is not null)
		{
			return ConfigFlowResult.Error(ConnectionStep(context),
				AppStrings.Integrations.Obs.Config.ConnectionFailed(host: host, port: port ?? 0, details: error.Value));
		}

		var values = new Dictionary<string, ConfigFlowValue>
		{
			[ObsConfigKeys.Host] = ConfigFlowValue.Plain(host),
			[ObsConfigKeys.Port] = ConfigFlowValue.Plain(port!.Value.ToString(CultureInfo.InvariantCulture))
		};

		if (!string.IsNullOrEmpty(password))
		{
			values[ObsConfigKeys.Password] = ConfigFlowValue.Secret(password);
		}

		return ConfigFlowResult.Complete(configurationName, values);
	}

	private async Task<LocalizedText?> TestConnectionAsync(string url,
		string? password,
		CancellationToken cancellationToken)
	{
		var client = _clientFactory();
		var completion = new TaskCompletionSource<LocalizedText?>(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnConnected(object? sender, EventArgs e) => completion.TrySetResult(null);

		void OnDisconnected(object? sender, string? reason)
			=> completion.TrySetResult(reason is { Length: > 0 }
				? reason
				: AppStrings.Integrations.Obs.Config.ConnectionRefused());

		client.Connected += OnConnected;
		client.Disconnected += OnDisconnected;

		try
		{
			client.Connect(url, password);

			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(_connectTimeout);

			var finished = await Task.WhenAny(completion.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));
			if (finished != completion.Task)
			{
				return AppStrings.Integrations.Obs.Config.ConnectTimedOut();
			}

			return await completion.Task;
		}
		catch (OperationCanceledException)
		{
			return AppStrings.Integrations.Obs.Config.ConnectTimedOut();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "OBS connection test failed for {Url}", url);
			return AppStrings.Integrations.Obs.Config.ConnectionTestFailed(details: ex.Message);
		}
		finally
		{
			client.Connected -= OnConnected;
			client.Disconnected -= OnDisconnected;
			client.Disconnect();
		}
	}

	private static int? ParsePort(object? value)
		=> value switch
		{
			null => DefaultPort,
			int i => i,
			long l => (int)l,
			double d => (int)d,
			string s when string.IsNullOrWhiteSpace(s) => DefaultPort,
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null
		};

	private static ConfigFlowStep ConnectionStep(IConfigFlowContext context)
		=> new()
		{
			StepId = "connection",
			Title = AppStrings.Integrations.Obs.Config.ConnectTitle(),
			Description = AppStrings.Integrations.Obs.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = AppStrings.Integrations.Obs.Config.InstructionOpenSettings() }
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Obs.Config.DocumentationLink(),
					Url = "https://github.com/obsproject/obs-websocket"
				}
			],
			Fields = ConfigurationFields(context)
		};

	private static List<ActionParameter> ConfigurationFields(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(ObsConfigKeys.ConfigurationName,
				label: AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: AppStrings.Integrations.Detail.ConfigurationNamePlaceholder(),
				required: true));
		}

		fields.AddRange([
			ActionParameter.Text(ObsConfigKeys.Host,
				label: AppStrings.Integrations.Obs.Config.HostLabel(),
				placeholder: DefaultHost,
				defaultValue: DefaultHost,
				required: true),
			ActionParameter.Number(ObsConfigKeys.Port,
				label: AppStrings.Integrations.Obs.Config.PortLabel(),
				min: 1,
				max: 65535,
				defaultValue: DefaultPort,
				required: true),
			ActionParameter.Secret(ObsConfigKeys.Password,
				label: AppStrings.Integrations.Obs.Config.PasswordLabel(),
				description: AppStrings.Integrations.Obs.Config.PasswordDescription())
		]);
		return fields;
	}
}
