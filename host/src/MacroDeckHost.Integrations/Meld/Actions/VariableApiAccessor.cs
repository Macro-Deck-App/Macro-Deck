using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class VariableApiAccessor
{
	public IVariableApi? Current { get; set; }
}
