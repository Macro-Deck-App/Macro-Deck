namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// Pairing progress written straight to stderr rather than through Serilog. Interactive pairing is the
/// one part of startup that waits on a human, and the plugin's <c>ILogger</c> is <c>Logger.None</c>
/// until the plugin author configures a sink - so a log event cannot be relied on to reach the developer
/// running <c>macrodeck-plugin run</c>, which forwards the child process's stderr. The matching
/// <see cref="Logging.HostingLog" /> events still fire for anyone who does have a sink.
/// </summary>
internal static class PairingProgress
{
	public static void Write(string message) => Console.Error.WriteLine(message);
}
