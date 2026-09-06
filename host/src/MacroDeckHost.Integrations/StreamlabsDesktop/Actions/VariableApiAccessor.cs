using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class VariableApiAccessor
{
	public IVariableApi? Current { get; set; }
}
