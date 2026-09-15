using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginRuntimeConfigReaderTests
{
	private string _directory = null!;

	[SetUp]
	public void CreateDirectory()
	{
		_directory = Path.Combine(Path.GetTempPath(), "md-runtimeconfig-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
	}

	[TearDown]
	public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

	private string Entrypoint(string? runtimeConfig)
	{
		if (runtimeConfig is not null)
		{
			File.WriteAllText(Path.Combine(_directory, "Plugin.runtimeconfig.json"), runtimeConfig);
		}

		return Path.Combine(_directory, "Plugin.dll");
	}

	[Test]
	public void Every_framework_of_an_aspnet_plugin_is_required()
	{
		var entrypoint = Entrypoint("""
									{
									  "runtimeOptions": {
									    "tfm": "net10.0",
									    "rollForward": "Major",
									    "frameworks": [
									      { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
									      { "name": "Microsoft.AspNetCore.App", "version": "10.0.0", "rollForward": "Disable" }
									    ]
									  }
									}
									""");

		var requirements = PluginRuntimeConfigReader.TryRead(entrypoint);

		Assert.That(requirements, Is.EqualTo(new[]
		{
			new DotnetFrameworkRequirement
			{
				Name = "Microsoft.NETCore.App", Version = new Version(10, 0, 0), RollForward = DotnetRollForward.Major
			},
			new DotnetFrameworkRequirement
			{
				Name = "Microsoft.AspNetCore.App",
				Version = new Version(10, 0, 0),
				RollForward = DotnetRollForward.Disable
			}
		}));
	}

	[Test]
	public void A_single_framework_defaults_to_rolling_forward_within_its_major()
	{
		var entrypoint = Entrypoint("""
									{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "9.0.0" } } }
									""");

		var requirements = PluginRuntimeConfigReader.TryRead(entrypoint);

		Assert.That(requirements, Is.EqualTo(new[]
		{
			new DotnetFrameworkRequirement
			{
				Name = "Microsoft.NETCore.App", Version = new Version(9, 0, 0), RollForward = DotnetRollForward.Minor
			}
		}));
	}

	[TestCase(null)]
	[TestCase("{ not json")]
	[TestCase("""{ "runtimeOptions": { "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "10.0.12" } ] } }""")]
	public void Without_a_readable_framework_list_there_is_nothing_to_go_by(string? runtimeConfig)
	{
		Assert.That(PluginRuntimeConfigReader.TryRead(Entrypoint(runtimeConfig)), Is.Null);
	}
}
