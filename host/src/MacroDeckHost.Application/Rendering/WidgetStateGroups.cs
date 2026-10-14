namespace MacroDeckHost.Application.Rendering;

public static class WidgetStateGroups
{
	public static string For(string widgetId)
	{
		return $"widget-state:{widgetId}";
	}
}
