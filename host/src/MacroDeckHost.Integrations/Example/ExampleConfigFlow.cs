using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Example;

public class ExampleConfigFlow : IConfigFlow
{
	private static readonly ILogger _logger = IntegrationLog.For<ExampleConfigFlow>(ExampleIntegration.IntegrationId);

	private string _baseUrl = string.Empty;

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(stepId switch
		{
			"connection" => SubmitConnection(input),
			"workspace" => SubmitWorkspace(input),
			_ => ConfigFlowResult.Error(ConnectionStep(), AppStrings.Integrations.Example.Config.UnknownStepError())
		});
	}

	private ConfigFlowResult SubmitConnection(IReadOnlyDictionary<string, object?> input)
	{
		var baseUrl = input.GetValueOrDefault("baseUrl") as string ?? string.Empty;
		var apiKey = input.GetValueOrDefault("apiKey") as string ?? string.Empty;

		var fieldErrors = new Dictionary<string, LocalizedText>();

		if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
		{
			fieldErrors["baseUrl"] = AppStrings.Integrations.Example.Config.BaseUrlInvalid();
		}

		if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 8)
		{
			fieldErrors["apiKey"] = AppStrings.Integrations.Example.Config.ApiKeyTooShort();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(ConnectionStep(),
				AppStrings.Integrations.Example.Config.ConnectionFailed(),
				fieldErrors);
		}

		_baseUrl = baseUrl;
		_logger.Information("Example config flow: connection validated for {BaseUrl}", baseUrl);

		return ConfigFlowResult.Step(WorkspaceStep());
	}

	private ConfigFlowResult SubmitWorkspace(IReadOnlyDictionary<string, object?> input)
	{
		var workspace = input.GetValueOrDefault("workspace") as string;

		if (string.IsNullOrWhiteSpace(workspace))
		{
			return ConfigFlowResult.Error(WorkspaceStep(),
				fieldErrors: new Dictionary<string, LocalizedText>
					{ ["workspace"] = AppStrings.Integrations.Example.Config.WorkspaceRequired() });
		}

		return ConfigFlowResult.Complete($"Example ({_baseUrl})");
	}

	private static ConfigFlowStep ConnectionStep()
		=> new()
		{
			StepId = "connection",
			Title = AppStrings.Integrations.Example.Config.ConnectionTitle(),
			Description = AppStrings.Integrations.Example.Config.ConnectionDescription(),
			Instructions =
			[
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Example.Config.ConnectionInstructionBaseUrl() },
				new ConfigFlowInstruction
					{ Text = AppStrings.Integrations.Example.Config.ConnectionInstructionApiKey() }
			],
			Fields =
			[
				ActionParameter.Url("baseUrl",
					label: AppStrings.Integrations.Example.Config.BaseUrlLabel(),
					placeholder: "https://api.example.com",
					required: true),
				ActionParameter.Password("apiKey",
					label: AppStrings.Integrations.Example.Config.ApiKeyLabel(),
					description: AppStrings.Integrations.Example.Config.ApiKeyDescription(),
					required: true)
			]
		};

	private static ConfigFlowStep WorkspaceStep()
		=> new()
		{
			StepId = "workspace",
			Title = AppStrings.Integrations.Example.Config.WorkspaceTitle(),
			Description = AppStrings.Integrations.Example.Config.WorkspaceDescription(),
			Fields =
			[
				ActionParameter.Choice("workspace",
					options:
					[
						new ActionParameterOption
							{ Value = "personal", Label = AppStrings.Integrations.Example.Config.WorkspacePersonal() },
						new ActionParameterOption
							{ Value = "team", Label = AppStrings.Integrations.Example.Config.WorkspaceTeam() },
						new ActionParameterOption
							{ Value = "shared", Label = AppStrings.Integrations.Example.Config.WorkspaceShared() }
					],
					label: AppStrings.Integrations.Example.Config.WorkspaceLabel(),
					required: true)
			]
		};
}
