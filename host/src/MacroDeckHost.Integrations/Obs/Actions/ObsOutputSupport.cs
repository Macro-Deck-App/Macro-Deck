using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsOutputSupport
{
	public const string OutputParameter = "output";

	public static ActionParameter OutputChoice()
		=> ActionParameter.DynamicChoice(OutputParameter,
			label: AppStrings.Integrations.Obs.Params.Output(),
			required: true);

	public static string? SelectedOutput(IReadOnlyDictionary<string, object> parameters)
		=> parameters.GetValueOrDefault(OutputParameter) is string { } output && !string.IsNullOrWhiteSpace(output)
			? output
			: null;

	public static async Task<DynamicOptionsResult> OptionsAsync(
		ObsTargetResolver resolver,
		DynamicOptionsContext context)
	{
		if (context.ParameterName == ObsTargetResolver.ConfigurationParameter)
		{
			return resolver.ConfigurationOptions();
		}

		var connection = resolver.ForOptions(context.CurrentParameters);
		var names = connection is null ? [] : await connection.GetOutputNamesAsync();
		var options = names.Select(name => new ActionParameterOption { Value = name, Label = name }).ToList();

		if (context.CurrentParameters.GetValueOrDefault(OutputParameter) is string { } stored &&
			!string.IsNullOrWhiteSpace(stored) &&
			!names.Contains(stored, StringComparer.Ordinal))
		{
			options.Add(new ActionParameterOption
			{
				Value = stored,
				Label = AppStrings.Integrations.Obs.Params.OutputUnavailable(name: stored)
			});
		}

		return new DynamicOptionsResult { Options = options, CacheSeconds = 5 };
	}

	public static ActionResult FromOutcome(ObsOutputOutcome outcome, string output)
		=> outcome switch
		{
			ObsOutputOutcome.Done => ActionResult.Success(),
			ObsOutputOutcome.NotConnected => ActionResult.Failed(ActionErrorCodes.NotConnected,
				AppStrings.Integrations.Obs.Errors.NotConnected()),
			ObsOutputOutcome.NotFound => ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Obs.Errors.OutputNotFound(output: output)),
			ObsOutputOutcome.Unsupported => ActionResult.Failed(ActionErrorCodes.ProviderError,
				AppStrings.Integrations.Obs.Errors.OutputsNeedNewerObs()),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderError,
				AppStrings.Integrations.Obs.Errors.OutputCommandFailed(output: output))
		};

	public static ActionResult NoOutputSelected()
		=> ActionResult.Failed(ActionErrorCodes.InvalidParameter,
			AppStrings.Integrations.Obs.Errors.NoOutputSelected());
}
