namespace MacroDeck.Plugin.Cli;

/// <summary>How much a command narrates while it works. Every command accepts this; not every command's
/// output changes at every level.</summary>
internal enum Verbosity
{
	Quiet,
	Normal,
	Diagnostic
}
