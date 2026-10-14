namespace MacroDeckHost.Infrastructure.Adb;

internal static class AdbShellQuote
{
	public static string Quote(string value)
	{
		return "'" + value.Replace("'", "'\\''") + "'";
	}
}
