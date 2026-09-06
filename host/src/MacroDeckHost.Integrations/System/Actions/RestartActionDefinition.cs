using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class RestartActionDefinition : PowerActionDefinitionBase
{
	public RestartActionDefinition(IPowerService power)
		: base(power, PowerOperation.Restart)
	{
	}

	public override string Id => "restart";
	public override LocalizedText Name => AppStrings.Integrations.System.Actions.Restart.Name();
	public override LocalizedText Description => AppStrings.Integrations.System.Actions.Restart.Description();

	public override IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Toggle("force",
			label: AppStrings.Integrations.System.Actions.ForceLabel(),
			description: AppStrings.Integrations.System.Actions.Restart.ForceDescription(),
			defaultValue: false)
	];
}
