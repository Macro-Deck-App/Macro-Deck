namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>What <see cref="PluginScaffolder.ScaffoldAsync" /> produced.</summary>
internal sealed record PluginScaffoldResult
{
	public required bool Success { get; init; }

	public string? OutputPath { get; init; }

	public string? ManifestPath { get; init; }

	public string? BuildConfigPath { get; init; }

	public PluginScaffoldFailureReason? FailureReason { get; init; }

	public string? FailureMessage { get; init; }

	public string? FailureDetail { get; init; }

	public static PluginScaffoldResult Ok(string outputPath, string manifestPath, string buildConfigPath) => new()
	{
		Success = true,
		OutputPath = outputPath,
		ManifestPath = manifestPath,
		BuildConfigPath = buildConfigPath
	};

	public static PluginScaffoldResult Fail(PluginScaffoldFailureReason reason, string message, string? detail = null)
		=> new() { Success = false, FailureReason = reason, FailureMessage = message, FailureDetail = detail };
}
