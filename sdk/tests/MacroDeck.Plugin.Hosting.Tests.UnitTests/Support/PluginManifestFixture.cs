namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>
/// Writes a <c>manifest.json</c> into a fresh temp directory and hands back a builder pointed at it via
/// <c>--contentRoot</c>. The test project's own <c>manifest.json</c> (id <c>com.example.test</c>, name
/// <c>Test</c>, version <c>1.0.0</c>) is enough for a call site that only needs a valid plugin to build;
/// this fixture is for the ones that assert specific manifest-driven values instead.
/// </summary>
internal sealed class PluginManifestFixture : IDisposable
{
	public string ContentRoot { get; } = Directory.CreateTempSubdirectory("macro-deck-plugin-manifest-tests").FullName;

	public PluginManifestFixture(string manifestJson) => File.WriteAllText(ManifestPath, manifestJson);

	public string ManifestPath => Path.Combine(ContentRoot, PluginManifestFileReader.FileName);

	public PluginHostBuilder CreateBuilder() => MacroDeckPlugin.CreatePlugin(["--contentRoot", ContentRoot]);

	public void WriteFile(string relativePath, byte[] content)
	{
		var full = Path.Combine(ContentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllBytes(full, content);
	}

	public void Dispose()
	{
		if (Directory.Exists(ContentRoot))
		{
			Directory.Delete(ContentRoot, recursive: true);
		}
	}
}
