using System.Globalization;
using MacroDeckHost.Integrations.Streamerbot.Protocol;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot;

public sealed class StreamerbotConfigFlow : IConfigFlow
{
	private const string ConnectionStepId = "connection";

	private static readonly ILogger _logger =
		IntegrationLog.For<StreamerbotConfigFlow>(StreamerbotIntegration.IntegrationId);

	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

	private readonly Func<IStreamerbotClient> _clientFactory;

	public StreamerbotConfigFlow()
		: this(() => new StreamerbotClient())
	{
	}

	internal StreamerbotConfigFlow(Func<IStreamerbotClient> clientFactory)
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
	{
		return stepId switch
		{
			ConnectionStepId => await SubmitConnection(input, cancellationToken),
			_ => ConfigFlowResult.Error(ConnectionStep(), AppStrings.Integrations.Streamerbot.Config.UnknownStep())
		};
	}

	private async Task<ConfigFlowResult> SubmitConnection(
		IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		var host = (input.GetValueOrDefault(StreamerbotConfigKeys.Host) as string ?? string.Empty).Trim();
		var endpoint = input.GetValueOrDefault(StreamerbotConfigKeys.Endpoint) as string;
		var password = input.GetValueOrDefault(StreamerbotConfigKeys.Password) as string ?? string.Empty;
		var port = ParsePort(input.GetValueOrDefault(StreamerbotConfigKeys.Port));

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(host))
		{
			fieldErrors[StreamerbotConfigKeys.Host] = AppStrings.Integrations.Streamerbot.Config.EnterAddressError();
		}

		if (port is null or < 1 or > 65535)
		{
			fieldErrors[StreamerbotConfigKeys.Port]
				= AppStrings.Integrations.Streamerbot.Config.EnterValidPortError(
					defaultPort: StreamerbotEndpoint.DefaultPort);
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(ConnectionStep(), fieldErrors: fieldErrors);
		}

		var normalisedHost = StreamerbotEndpoint.NormaliseHost(host);
		var normalisedEndpoint = StreamerbotEndpoint.NormaliseEndpoint(endpoint);
		var uri = StreamerbotEndpoint.Build(normalisedHost, port!.Value, normalisedEndpoint);

		var (info, error) = await TestConnectionAsync(uri, password, cancellationToken);
		if (!error.IsEmpty)
		{
			return ConfigFlowResult.Error(ConnectionStep(), error);
		}

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[StreamerbotConfigKeys.Host] = ConfigFlowValue.Plain(normalisedHost),
			[StreamerbotConfigKeys.Port] = ConfigFlowValue.Plain(port.Value.ToString(CultureInfo.InvariantCulture)),
			[StreamerbotConfigKeys.Endpoint] = ConfigFlowValue.Plain(normalisedEndpoint)
		};

		if (!string.IsNullOrEmpty(password))
		{
			values[StreamerbotConfigKeys.Password] = ConfigFlowValue.Secret(password);
		}

		var title = string.IsNullOrEmpty(info?.Version)
			? $"Streamer.bot ({normalisedHost}:{port.Value.ToString(CultureInfo.InvariantCulture)})"
			: $"Streamer.bot {info.Version} ({normalisedHost}:{port.Value.ToString(CultureInfo.InvariantCulture)})";

		return ConfigFlowResult.Complete(title, values);
	}

	private async Task<(StreamerbotInstanceInfo? Info, LocalizedText Error)> TestConnectionAsync(
		Uri uri,
		string password,
		CancellationToken cancellationToken)
	{
		using var client = _clientFactory();
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_connectTimeout);

		try
		{
			var hello = await client.ConnectAsync(uri, timeout.Token);

			if (hello?.Authentication is { } challenge)
			{
				if (string.IsNullOrEmpty(password))
				{
					try
					{
						await client.RequestAsync("GetInfo", cancellationToken: timeout.Token);
					}
					catch (StreamerbotRequestException)
					{
						return (null, AppStrings.Integrations.Streamerbot.Config.PasswordRequiredError());
					}
				}
				else
				{
					var response
						= StreamerbotAuthentication.CreateResponse(password, challenge.Salt, challenge.Challenge);
					try
					{
						await client.RequestAsync("Authenticate",
							new Dictionary<string, object?>(StringComparer.Ordinal) { ["authentication"] = response },
							timeout.Token);
					}
					catch (StreamerbotRequestException)
					{
						return (null, AppStrings.Integrations.Streamerbot.Config.PasswordRejectedError());
					}
				}
			}

			var info = hello?.Info;
			if (info is null)
			{
				var response = await client.RequestAsync("GetInfo", cancellationToken: timeout.Token);
				info = StreamerbotResponses.ReadInstanceInfo(response);
			}

			return (info, default);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return (null,
				AppStrings.Integrations.Streamerbot.Config.ConnectTimedOutError(uri: uri.ToString()));
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Streamer.bot connection test failed for {Uri}", uri);
			return (null,
				AppStrings.Integrations.Streamerbot.Config.ConnectFailedError(uri: uri.ToString()));
		}
		finally
		{
			await client.DisconnectAsync();
		}
	}

	private static int? ParsePort(object? value)
		=> value switch
		{
			null => StreamerbotEndpoint.DefaultPort,
			int i => i,
			long l => (int)l,
			double d => (int)d,
			string s when string.IsNullOrWhiteSpace(s) => StreamerbotEndpoint.DefaultPort,
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null
		};

	private static ConfigFlowStep ConnectionStep()
		=> new()
		{
			StepId = ConnectionStepId,
			Title = AppStrings.Integrations.Streamerbot.Config.ConnectTitle(),
			Description = AppStrings.Integrations.Streamerbot.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
				{
					Text = AppStrings.Integrations.Streamerbot.Config.WebSocketServerInstruction()
				}
			],
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Streamerbot.Config.DocumentationLinkLabel(),
					Url = "https://docs.streamer.bot/api/websocket"
				}
			],
			Fields =
			[
				ActionParameter.Text(StreamerbotConfigKeys.Host,
					label: AppStrings.Integrations.Streamerbot.Config.AddressLabel(),
					placeholder: StreamerbotEndpoint.DefaultHost,
					defaultValue: StreamerbotEndpoint.DefaultHost,
					required: true),
				ActionParameter.Number(StreamerbotConfigKeys.Port,
					label: AppStrings.Integrations.Streamerbot.Config.PortLabel(),
					min: 1,
					max: 65535,
					defaultValue: StreamerbotEndpoint.DefaultPort,
					required: true),
				ActionParameter.Text(StreamerbotConfigKeys.Endpoint,
					label: AppStrings.Integrations.Streamerbot.Config.EndpointLabel(),
					description: AppStrings.Integrations.Streamerbot.Config.EndpointDescription(),
					placeholder: StreamerbotEndpoint.DefaultEndpoint,
					defaultValue: StreamerbotEndpoint.DefaultEndpoint),
				ActionParameter.Secret(StreamerbotConfigKeys.Password,
					label: AppStrings.Integrations.Streamerbot.Config.PasswordLabel(),
					description: AppStrings.Integrations.Streamerbot.Config.PasswordDescription())
			]
		};
}
