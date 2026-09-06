using System.Text.Json;
using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class PluginBuildConfigTests
{
	private static readonly string[] _forbiddenPropertyNames =
		["command", "script", "shell", "run", "cmd", "csproj", "project", "tfm", "targetFramework", "sdk", "msbuild"];

	private static PluginScaffoldRequest CanonicalRequest(IReadOnlyList<string>? platforms = null) => new()
	{
		Name = "Spotify Controller",
		Id = "com.example.spotify",
		Publisher = "Example Publisher",
		Description = "A Macro Deck plugin.",
		License = "MIT",
		ProjectName = "SpotifyController",
		Output = "SpotifyController",
		Platforms = platforms ?? ["win-x64", "osx-arm64", "linux-x64"]
	};

	[Test]
	public void Version_is_the_json_number_one()
	{
		using var document = JsonDocument.Parse(PluginBuildConfig.Build(CanonicalRequest()));
		var version = document.RootElement.GetProperty("version");

		Assert.That(version.ValueKind, Is.EqualTo(JsonValueKind.Number));
		Assert.That(version.GetInt32(), Is.EqualTo(1));
	}

	[Test]
	public void Target_keys_equal_the_selection()
	{
		using var document
			= JsonDocument.Parse(PluginBuildConfig.Build(CanonicalRequest(["osx-arm64", "linux-arm64"])));
		var keys = document.RootElement.GetProperty("targets").EnumerateObject().Select(p => p.Name).ToList();

		Assert.That(keys, Is.EquivalentTo(new List<string> { "osx-arm64", "linux-arm64" }));
	}

	[Test]
	public void Each_target_carries_dotnet_publish_with_the_right_rid_and_a_distinct_output()
	{
		using var document = JsonDocument.Parse(PluginBuildConfig.Build(CanonicalRequest()));
		var targets = document.RootElement.GetProperty("targets");

		var outputs = new List<string>();

		foreach (var property in targets.EnumerateObject())
		{
			var target = property.Value;
			Assert.That(target.GetProperty("executable").GetString(), Is.EqualTo("dotnet"));

			var arguments = target.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ToList();
			Assert.That(arguments, Does.Contain("publish"));
			Assert.That(arguments, Does.Contain("-c"));
			Assert.That(arguments, Does.Contain("Release"));
			Assert.That(arguments, Does.Contain("-r"));
			Assert.That(arguments, Does.Contain(property.Name));
			Assert.That(arguments, Does.Contain("--self-contained"));

			// -r and the rid are two separate elements, never one "-r <rid>" string.
			var ridIndex = arguments.IndexOf("-r");
			Assert.That(arguments[ridIndex + 1], Is.EqualTo(property.Name));

			var output = target.GetProperty("output").GetString()!;
			Assert.That(output, Is.EqualTo(arguments[arguments.IndexOf("-o") + 1]));
			outputs.Add(output);

			var propertyNames = target.EnumerateObject().Select(p => p.Name).ToList();
			Assert.That(propertyNames,
				Is.SubsetOf(new List<string> { "executable", "arguments", "output", "workingDirectory" }));
			Assert.That(propertyNames, Is.EquivalentTo(new List<string> { "executable", "arguments", "output" }));
		}

		Assert.That(outputs, Is.Unique);
	}

	[Test]
	public void No_forbidden_property_name_appears_anywhere_in_the_document()
	{
		using var document = JsonDocument.Parse(PluginBuildConfig.Build(CanonicalRequest()));

		var names = new HashSet<string>();
		CollectPropertyNames(document.RootElement, names);

		Assert.That(names.Intersect(_forbiddenPropertyNames, StringComparer.OrdinalIgnoreCase), Is.Empty);
	}

	private static void CollectPropertyNames(JsonElement element, HashSet<string> names)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					names.Add(property.Name);
					CollectPropertyNames(property.Value, names);
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					CollectPropertyNames(item, names);
				}

				break;
		}
	}
}
