using System.Diagnostics.CodeAnalysis;

namespace MacroDeckHost.Application.Plugins.Installation;

public interface IPluginArtifactCache
{
	bool TryGet(string sha256, [NotNullWhen(true)] out string? cachedPath);

	Task<string> Put(string sourcePath, string sha256, CancellationToken cancellationToken = default);

	void Prune();

	void Clear();
}
