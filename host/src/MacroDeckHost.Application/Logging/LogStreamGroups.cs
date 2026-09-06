namespace MacroDeckHost.Application.Logging;

public static class LogStreamGroups
{
	public static string For(string connectionId) => $"log-stream:{connectionId}";
}
