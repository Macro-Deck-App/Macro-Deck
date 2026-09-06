using MacroDeck.Plugin.Hosting;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal static class TestMetadata
{
	public static PluginMetadata Default { get; } = new()
	{
		Id = "com.example.contract",
		Name = "Contract Plugin",
		Version = "1.0.0"
	};
}
