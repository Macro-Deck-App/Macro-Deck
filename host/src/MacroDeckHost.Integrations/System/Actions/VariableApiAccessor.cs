using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class VariableApiAccessor
{
	public IVariableApi? Current { get; set; }
}
