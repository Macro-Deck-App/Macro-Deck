namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>The <see cref="PluginMetadata"/> a test reaches for when the values themselves do not
/// matter to what is under test.</summary>
internal static class TestMetadata
{
	public static PluginMetadata Default { get; } = new()
	{
		Id = "com.example.test",
		Name = "Test",
		Version = "1.0.0"
	};
}
