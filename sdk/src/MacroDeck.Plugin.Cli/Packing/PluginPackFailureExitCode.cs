namespace MacroDeck.Plugin.Cli.Packing;

/// <summary>The exit code for every <see cref="PluginPackFailureReason" /> that is not
/// <see cref="PluginPackFailureReason.ManifestInvalid" /> - that one carries its own
/// <see cref="ManifestValidationResult" /> with a data-dependent exit code
/// (<see cref="ExitCode.SubjectInvalid" /> or <see cref="ExitCode.InputUnreadable" />, depending on
/// whether the manifest was even readable), so it is reported from <see cref="PluginPackResult.Validation" />
/// directly rather than through this fixed mapping - see <see cref="PluginPackReporter.Report" />.</summary>
internal static class PluginPackFailureExitCode
{
	public static int For(PluginPackFailureReason? reason) => reason switch
	{
		PluginPackFailureReason.SourceNotFound => ExitCode.InputUnreadable,
		PluginPackFailureReason.OutputExists => ExitCode.UsageError,
		PluginPackFailureReason.SourceEntryRejected => ExitCode.SubjectInvalid,
		PluginPackFailureReason.LimitExceeded => ExitCode.SubjectInvalid,
		PluginPackFailureReason.WriteFailed => ExitCode.InternalError,
		PluginPackFailureReason.ManifestInvalid => ExitCode.SubjectInvalid,
		null => ExitCode.InternalError,
		_ => ExitCode.InternalError
	};
}
