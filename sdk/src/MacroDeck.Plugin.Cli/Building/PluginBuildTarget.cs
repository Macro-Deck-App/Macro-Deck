namespace MacroDeck.Plugin.Cli.Building;

/// <summary>One runtime identifier's build recipe, as read from <c>macrodeck-build.json</c>.
/// <see cref="Arguments" /> is a vector, never a command string: nothing here is evaluated by a shell, and
/// nothing in this record is .NET-specific.</summary>
internal sealed record PluginBuildTarget
{
	public required string Rid { get; init; }

	public required string Executable { get; init; }

	public IReadOnlyList<string> Arguments { get; init; } = [];

	/// <summary>The directory the tool writes its output to, relative to the project root, exactly as
	/// written in the config.</summary>
	public required string Output { get; init; }

	/// <summary>Relative to the project root. Null means the project root itself.</summary>
	public string? WorkingDirectory { get; init; }
}
