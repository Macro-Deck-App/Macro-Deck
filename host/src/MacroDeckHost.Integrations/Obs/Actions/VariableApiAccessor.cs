using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class VariableApiAccessor
{
	public IVariableApi? Current { get; set; }
}
