using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.SinusBot;

public sealed class SinusBotConfigFlow : IConfigFlow
{
	private static readonly ILogger _logger = IntegrationLog.For<SinusBotConfigFlow>(SinusBotIntegration.IntegrationId);

	private readonly Func<string, ISinusBotClient> _clientFactory;

	private string _serverUrl = string.Empty;
	private string _username = string.Empty;
	private string _password = string.Empty;
	private IReadOnlyList<SinusBotInstance> _instances = [];

	public SinusBotConfigFlow()
		: this(_ => new SinusBotClient(_))
	{
	}

	internal SinusBotConfigFlow(Func<string, ISinusBotClient> clientFactory)
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
			"connection" => await SubmitConnection(input, cancellationToken),
			"instance" => SubmitInstance(input),
			_ => ConfigFlowResult.Error(ConnectionStep(), AppStrings.Integrations.SinusBot.Config.UnknownStep())
		};
	}

	private async Task<ConfigFlowResult> SubmitConnection(IReadOnlyDictionary<string, object?> input,
		CancellationToken cancellationToken)
	{
		_serverUrl = (input.GetValueOrDefault(SinusBotConfigKeys.ServerUrl) as string ?? string.Empty).Trim();
		_username = (input.GetValueOrDefault(SinusBotConfigKeys.Username) as string ?? string.Empty).Trim();
		_password = input.GetValueOrDefault(SinusBotConfigKeys.Password) as string ?? string.Empty;

		var fieldErrors = new Dictionary<string, LocalizedText>();
		if (string.IsNullOrWhiteSpace(_serverUrl))
		{
			fieldErrors[SinusBotConfigKeys.ServerUrl] = AppStrings.Integrations.SinusBot.Config.EnterServerUrl();
		}

		if (string.IsNullOrWhiteSpace(_username))
		{
			fieldErrors[SinusBotConfigKeys.Username] = AppStrings.Integrations.SinusBot.Config.EnterUsername();
		}

		if (string.IsNullOrWhiteSpace(_password))
		{
			fieldErrors[SinusBotConfigKeys.Password] = AppStrings.Integrations.SinusBot.Config.EnterPassword();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(ConnectionStep(), fieldErrors: fieldErrors);
		}

		IReadOnlyList<SinusBotInstance> instances;
		try
		{
			var client = _clientFactory(_serverUrl);
			await client.AuthenticateAsync(_username, _password, cancellationToken);
			instances = await client.GetInstancesAsync(cancellationToken);
		}
		catch (SinusBotAuthException ex)
		{
			_logger.Warning(ex, "SinusBot connection failed for {ServerUrl}", _serverUrl);
			return ConfigFlowResult.Error(ConnectionStep(),
				AppStrings.Integrations.SinusBot.Config.AuthenticationFailed(details: ex.Message));
		}

		var selectable = instances.Where(i => !string.IsNullOrWhiteSpace(i.Uuid)).ToList();
		if (selectable.Count == 0)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				AppStrings.Integrations.SinusBot.Config.NoInstancesFound());
		}

		_instances = selectable;
		return ConfigFlowResult.Step(InstanceStep(selectable));
	}

	private ConfigFlowResult SubmitInstance(IReadOnlyDictionary<string, object?> input)
	{
		var instanceId = (input.GetValueOrDefault(SinusBotConfigKeys.InstanceId) as string ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(instanceId))
		{
			return ConfigFlowResult.Error(InstanceStep(_instances),
				AppStrings.Integrations.SinusBot.Config.SelectInstance());
		}

		var instance = _instances.FirstOrDefault(i => i.Uuid == instanceId);
		var instanceName = instance?.Name ?? instanceId;

		var values = new Dictionary<string, ConfigFlowValue>
		{
			[SinusBotConfigKeys.ServerUrl] = ConfigFlowValue.Plain(_serverUrl),
			[SinusBotConfigKeys.Username] = ConfigFlowValue.Plain(_username),
			[SinusBotConfigKeys.Password] = ConfigFlowValue.Secret(_password),
			[SinusBotConfigKeys.InstanceId] = ConfigFlowValue.Plain(instanceId),
			[SinusBotConfigKeys.InstanceName] = ConfigFlowValue.Plain(instanceName)
		};

		return ConfigFlowResult.Complete($"SinusBot ({instanceName})", values);
	}

	private static ConfigFlowStep ConnectionStep()
		=> new()
		{
			StepId = "connection",
			Title = AppStrings.Integrations.SinusBot.Config.ConnectionTitle(),
			Description = AppStrings.Integrations.SinusBot.Config.ConnectionDescription(),
			Links =
			[
				new ConfigFlowLink
					{ Label = AppStrings.Integrations.SinusBot.Config.WebsiteLinkLabel(), Url = "https://sinusbot.com" }
			],
			Fields =
			[
				ActionParameter.Url(SinusBotConfigKeys.ServerUrl,
					label: AppStrings.Integrations.SinusBot.Config.ServerUrlLabel(),
					placeholder: "http://your-bot:8087",
					required: true),
				ActionParameter.Text(SinusBotConfigKeys.Username,
					label: AppStrings.Integrations.SinusBot.Config.UsernameLabel(),
					placeholder: AppStrings.Integrations.SinusBot.Config.UsernamePlaceholder(),
					required: true),
				ActionParameter.Secret(SinusBotConfigKeys.Password,
					label: AppStrings.Integrations.SinusBot.Config.PasswordLabel(),
					description: AppStrings.Integrations.SinusBot.Config.PasswordDescription(),
					required: true)
			]
		};

	private static ConfigFlowStep InstanceStep(IReadOnlyList<SinusBotInstance> instances)
	{
		var options = instances
			.Select(i => new ActionParameterOption { Value = i.Uuid!, Label = i.Name ?? i.Uuid! })
			.ToList();

		return new ConfigFlowStep
		{
			StepId = "instance",
			Title = AppStrings.Integrations.SinusBot.Config.InstanceTitle(),
			Description = AppStrings.Integrations.SinusBot.Config.InstanceDescription(),
			Fields =
			[
				ActionParameter.Choice(SinusBotConfigKeys.InstanceId,
					options: options,
					label: AppStrings.Integrations.SinusBot.Config.InstanceLabel(),
					required: true)
			]
		};
	}
}
