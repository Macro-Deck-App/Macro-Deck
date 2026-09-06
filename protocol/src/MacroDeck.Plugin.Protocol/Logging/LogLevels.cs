namespace MacroDeck.Plugin.Protocol.Logging;

/// <summary>
/// Every severity <c>log.publish</c> speaks. Not an enum, for the same reason <c>MessageTypes</c> is
/// not one: <see cref="MacroDeck.Plugin.Protocol.Serialization.PluginProtocolJson.Options" /> registers
/// no <see cref="System.Text.Json.Serialization.JsonStringEnumConverter" />, so an enum would serialize
/// by ordinal and silently reclassify a severity if the declaration order ever changed.
/// </summary>
public static class LogLevels
{
	public const string Verbose = "verbose";

	public const string Debug = "debug";

	public const string Information = "information";

	public const string Warning = "warning";

	public const string Error = "error";

	public const string Fatal = "fatal";

	public static readonly IReadOnlyList<string> All = [Verbose, Debug, Information, Warning, Error, Fatal];
}
