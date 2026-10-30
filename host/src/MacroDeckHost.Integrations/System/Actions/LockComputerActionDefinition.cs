using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Localization;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class LockComputerActionDefinition : PowerActionDefinitionBase
{
	public LockComputerActionDefinition(IPowerService power)
		: base(power, PowerOperation.Lock)
	{
	}

	public override string Id => "lock-computer";

	public override ActionRiskCategory RiskCategory => ActionRiskCategory.None;
	public override LocalizedText Name => AppStrings.Integrations.System.Actions.LockComputer.Name();

	public override LocalizedText Description =>
		AppStrings.Integrations.System.Actions.LockComputer.Description();
}
