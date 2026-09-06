namespace MacroDeckHost.Application.Plugins.Runtime;

public sealed record DotnetMuxer
{
	public required string ExecutablePath { get; init; }

	public required IReadOnlyList<Version> InstalledRuntimeVersions { get; init; }
}

public interface IDotnetMuxerLocator
{
	DotnetMuxer? Locate();

	void Invalidate();
}
