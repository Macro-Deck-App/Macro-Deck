using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.System.Config;

namespace MacroDeckHost.Integrations.System.Configuration;

public sealed class SystemConfigFlow : IConfigFlow
{
	private const string ApplicationStepId = "application";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		return Task.FromResult(ConfigFlowResult.Step(ApplicationStep(context)));
	}

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(stepId == ApplicationStepId
			? SubmitApplication(input, context)
			: ConfigFlowResult.Error(ApplicationStep(context), Strings.UnknownStep()));
	}

	private static ConfigFlowResult SubmitApplication(
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context)
	{
		var entryTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var name = entryTitle ?? (input.GetValueOrDefault(SystemConfigKeys.Name) as string ?? string.Empty).Trim();
		var application = (input.GetValueOrDefault(SystemConfigKeys.Application) as string ?? string.Empty).Trim();

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(name))
		{
			fieldErrors[SystemConfigKeys.Name] = Strings.EnterName();
		}

		if (application.Length == 0)
		{
			fieldErrors[SystemConfigKeys.Application] = Strings.EnterApplication();
		}

		if (fieldErrors.Count > 0)
		{
			return ConfigFlowResult.Error(ApplicationStep(context), fieldErrors: fieldErrors);
		}

		return ConfigFlowResult.Complete(name,
			new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
			{
				[SystemConfigKeys.Application] = ConfigFlowValue.Plain(application)
			});
	}

	private static ConfigFlowStep ApplicationStep(IConfigFlowContext context)
	{
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(SystemConfigKeys.Name,
				AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: Strings.NamePlaceholder(),
				required: true));
		}

		fields.Add(ActionParameter.Autocomplete(SystemConfigKeys.Application,
			Strings.ApplicationLabel(),
			Strings.ApplicationFieldDescription(),
			optionsSourceId: SystemConfigKeys.ProcessesOptionsSource,
			required: true));

		return new ConfigFlowStep
		{
			StepId = ApplicationStepId,
			Title = Strings.ApplicationTitle(),
			Description = Strings.ApplicationDescription(),
			Fields = fields
		};
	}
}
