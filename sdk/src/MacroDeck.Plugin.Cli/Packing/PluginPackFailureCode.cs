namespace MacroDeck.Plugin.Cli.Packing;

/// <summary>The diagnostic code for every <see cref="PluginPackFailureReason" /> - mirrors
/// <see cref="PluginPackFailureExitCode" />, total over the same enum for the same reason.</summary>
internal static class PluginPackFailureCode
{
	public static string For(PluginPackFailureReason? reason) => reason switch
	{
		PluginPackFailureReason.SourceNotFound => "source-not-found",
		PluginPackFailureReason.OutputExists => "output-exists",
		PluginPackFailureReason.SourceEntryRejected => "source-entry-rejected",
		PluginPackFailureReason.LimitExceeded => "limit-exceeded",
		PluginPackFailureReason.WriteFailed => "write-failed",
		PluginPackFailureReason.ManifestInvalid => "manifest-invalid",
		null => "pack-failed",
		_ => "pack-failed"
	};
}
