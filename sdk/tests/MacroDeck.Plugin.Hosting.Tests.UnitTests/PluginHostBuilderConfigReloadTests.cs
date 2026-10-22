using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
[NonParallelizable]
public class PluginHostBuilderConfigReloadTests
{
	private static readonly string[] _variables =
	[
		"DOTNET_hostBuilder__reloadConfigOnChange",
		"ASPNETCORE_hostBuilder__reloadConfigOnChange"
	];

	private readonly Dictionary<string, string?> _saved = [];

	[SetUp]
	public void ClearReloadVariables()
	{
		foreach (var variable in _variables)
		{
			_saved[variable] = Environment.GetEnvironmentVariable(variable);
			Environment.SetEnvironmentVariable(variable, null);
		}
	}

	[TearDown]
	public void RestoreReloadVariables()
	{
		foreach (var (variable, value) in _saved)
		{
			Environment.SetEnvironmentVariable(variable, value);
		}
	}

	[Test]
	public void Configuration_files_are_not_watched_by_default()
	{
		var builder = MacroDeckPlugin.CreatePlugin();

		Assert.That(JsonSources(builder),
			Is.Not.Empty.And.All.Matches<JsonConfigurationSource>(source => !source.ReloadOnChange));
	}

	[Test]
	public void A_trailing_switch_without_value_keeps_files_unwatched_and_its_own_value()
	{
		var builder = MacroDeckPlugin.CreatePlugin(["--some:key"]);

		Assert.Multiple(() =>
		{
			Assert.That(JsonSources(builder),
				Is.All.Matches<JsonConfigurationSource>(source => !source.ReloadOnChange));
			Assert.That(builder.Configuration["some:key"], Is.Null);
		});
	}

	[Test]
	public void Reload_can_be_enabled_on_the_command_line()
	{
		var builder = MacroDeckPlugin.CreatePlugin(["--hostBuilder:reloadConfigOnChange=true"]);

		Assert.That(JsonSources(builder),
			Is.Not.Empty.And.All.Matches<JsonConfigurationSource>(source => source.ReloadOnChange));
	}

	[TestCase("DOTNET_hostBuilder__reloadConfigOnChange")]
	[TestCase("ASPNETCORE_hostBuilder__reloadConfigOnChange")]
	public void Reload_can_be_enabled_through_the_environment(string variable)
	{
		Environment.SetEnvironmentVariable(variable, "true");

		var builder = MacroDeckPlugin.CreatePlugin();

		Assert.That(JsonSources(builder),
			Is.Not.Empty.And.All.Matches<JsonConfigurationSource>(source => source.ReloadOnChange));
	}

	[Test]
	public void Command_line_configuration_still_reaches_the_plugin()
	{
		var builder = MacroDeckPlugin.CreatePlugin(["--MacroDeck:Plugin:HostUrl=http://127.0.0.1:9999"]);

		Assert.That(builder.Configuration["MacroDeck:Plugin:HostUrl"], Is.EqualTo("http://127.0.0.1:9999"));
	}

	private static List<JsonConfigurationSource> JsonSources(PluginHostBuilder builder)
	{
		return ((IConfigurationBuilder)builder.Configuration).Sources.OfType<JsonConfigurationSource>().ToList();
	}
}
