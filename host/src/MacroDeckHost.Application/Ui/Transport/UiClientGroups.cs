namespace MacroDeckHost.Application.Ui.Transport;

public static class UiClientGroups
{
	public static string For(string clientId)
	{
		return $"ui-client:{clientId}";
	}
}
