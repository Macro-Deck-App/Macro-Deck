using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion;

internal sealed class CompanionConfigFlow : IConfigFlow
{
	private readonly Func<ICompanionGateway?> _gateway;

	public CompanionConfigFlow(Func<ICompanionGateway?> gateway)
	{
		_gateway = gateway;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(ConnectStep()));

	// Completing stores nothing (the adapter says so): an entry is created by the host when a device connects,
	// since it is keyed by that device. Submitting lets automatic creation resume and closes the dialog.
	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (_gateway() is { } gateway)
		{
			await gateway.ResumeAutoCreationAsync(cancellationToken);
		}

		return ConfigFlowResult.Complete(string.Empty);
	}

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
