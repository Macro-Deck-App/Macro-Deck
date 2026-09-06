using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class HibernateActionDefinition : PowerActionDefinitionBase
{
	public HibernateActionDefinition(IPowerService power)
		: base(power, PowerOperation.Hibernate)
	{
	}

	public override string Id => "hibernate";
	public override LocalizedText Name => AppStrings.Integrations.System.Actions.Hibernate.Name();

	public override LocalizedText Description =>
		AppStrings.Integrations.System.Actions.Hibernate.Description();

	public override MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.Linux;
}
