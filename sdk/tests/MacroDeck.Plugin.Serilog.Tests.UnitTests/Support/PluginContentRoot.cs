namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>A temp directory holding the minimal valid <c>manifest.json</c> a plugin needs to build,
/// pointed at through <c>--contentRoot</c>.</summary>
internal sealed class PluginContentRoot : IDisposable
{
	public PluginContentRoot() =>
		File.WriteAllText(Path.Combine(RootPath, "manifest.json"),
			"""
			{
			  "manifestVersion": 1,
			  "id": "com.example.test",
			  "name": "Test Plugin",
			  "version": "1.0.0"
			}
			""");

	public string RootPath { get; } = Directory.CreateTempSubdirectory("macro-deck-plugin-serilog-tests").FullName;

	public void Dispose()
	{
		if (Directory.Exists(RootPath))
		{
			Directory.Delete(RootPath, recursive: true);
		}
	}
}
