using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion;

internal sealed class CompanionConfigFlow : IConfigFlow
{
	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectStep()));

	// An entry is keyed by its device, so it is created when a device connects, never from the dialog.
	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.CompleteWithoutEntry());

	private static ConfigFlowStep ConnectStep()
		=> new()
		{
			StepId = "connect",
			Title = AppStrings.Integrations.Companion.Config.ConnectTitle(),
			Description = AppStrings.Integrations.Companion.Config.ConnectDescription(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = AppStrings.Integrations.Companion.Config.InstructionConnect() }
			],
			Fields = []
		};
}
