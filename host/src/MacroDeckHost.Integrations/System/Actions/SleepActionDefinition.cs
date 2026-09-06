using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Localization;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class SleepActionDefinition : PowerActionDefinitionBase
{
	public SleepActionDefinition(IPowerService power)
		: base(power, PowerOperation.Sleep)
	{
	}

	public override string Id => "sleep";
	public override LocalizedText Name => AppStrings.Integrations.System.Actions.Sleep.Name();
	public override LocalizedText Description => AppStrings.Integrations.System.Actions.Sleep.Description();
}
