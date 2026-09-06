using MacroDeck.Plugin.Testing.Conformance;

namespace MacroDeck.Plugin.Cli;

/// <summary>
/// The total mapping from a <see cref="ConformanceReport" /> to an <see cref="ExitCode" />. Total by
/// construction, not merely by convention: <see cref="ConformanceReport.Conformant" /> is a
/// <see cref="bool" />, so there are exactly two shapes to account for and both are handled below - see
/// <see cref="PluginInstallErrorExitCode" /> for the enum-shaped counterpart this mirrors.
/// </summary>
internal static class ConformanceReportExitCode
{
	public static int For(ConformanceReport report) => report.Conformant switch
	{
		true => ExitCode.Success,
		false => ExitCode.SubjectInvalid
	};
}
