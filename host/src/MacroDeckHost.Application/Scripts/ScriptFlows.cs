using MacroDeckHost.Application.Actions;

namespace MacroDeckHost.Application.Scripts;

public static class ScriptFlows
{
	public const string TriggerType = "onRun";

	public static string ToFlowsSource(string? flows) => WidgetFlowsJson.ToSource(flows);
}
