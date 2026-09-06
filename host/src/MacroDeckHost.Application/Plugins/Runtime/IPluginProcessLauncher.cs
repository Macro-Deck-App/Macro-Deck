namespace MacroDeckHost.Application.Plugins.Runtime;

public sealed record PluginProcessStartRequest
{
	public required string ExecutablePath { get; init; }

	public required string WorkingDirectory { get; init; }

	public IReadOnlyList<string> Arguments { get; init; } = [];

	public required IReadOnlyDictionary<string, string?> Environment { get; init; }

	public required int BootstrapOutputMaxLines { get; init; }

	public required int BootstrapOutputMaxBytes { get; init; }
}

public interface IPluginProcessLauncher
{
	IPluginProcess Start(PluginProcessStartRequest request);
}
