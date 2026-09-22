using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.LiveTennis;

internal sealed class LiveTennisConfigFlow : IConfigFlow
{
	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectionStep()));

	public Task<ConfigFlowResult> SubmitAsync(string stepId, IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context, CancellationToken cancellationToken)
	{
		var key = (input.GetValueOrDefault(LiveTennisIntegration.ApiKey) as string)?.Trim();
		if (stepId != "connection" || string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl))
		{
			return Task.FromResult(ConfigFlowResult.Error(ConnectionStep(),
				AppStrings.Integrations.LiveTennis.EnterApiKey()));
		}

		return Task.FromResult(ConfigFlowResult.Complete("Live Tennis API",
			new Dictionary<string, ConfigFlowValue>
			{
				[LiveTennisIntegration.ApiKey] = ConfigFlowValue.Secret(key),
				[LiveTennisIntegration.LastAttempt] = ConfigFlowValue.Plain(null)
			}));
	}

	private static ConfigFlowStep ConnectionStep() => new()
	{
		StepId = "connection",
		Title = AppStrings.Integrations.LiveTennis.Name(),
		Description = AppStrings.Integrations.LiveTennis.SetupDescription(),
		Fields =
		[
			ActionParameter.Secret(LiveTennisIntegration.ApiKey,
				label: AppStrings.Integrations.LiveTennis.ApiKeyLabel(), required: true)
		]
	};
}
