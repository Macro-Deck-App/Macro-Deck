namespace MacroDeckHost.Application.Ui.Transport;

public static class UiSessionGroups
{
	public static string For(string sessionId)
	{
		return $"ui-session:{sessionId}";
	}
}
