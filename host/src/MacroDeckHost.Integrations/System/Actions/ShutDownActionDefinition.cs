using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class ShutDownActionDefinition : PowerActionDefinitionBase
{
	public ShutDownActionDefinition(IPowerService power)
		: base(power, PowerOperation.ShutDown)
	{
	}

	public override string Id => "shut-down";
	public override LocalizedText Name => AppStrings.Integrations.System.Actions.ShutDown.Name();
	public override LocalizedText Description => AppStrings.Integrations.System.Actions.ShutDown.Description();

	public override IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Toggle("force",
			label: AppStrings.Integrations.System.Actions.ForceLabel(),
			description: AppStrings.Integrations.System.Actions.ShutDown.ForceDescription(),
			defaultValue: false)
	];
}
