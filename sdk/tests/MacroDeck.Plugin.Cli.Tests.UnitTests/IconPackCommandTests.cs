using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Cli.Manifests;
using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class IconPackCommandTests
{
	private static readonly string[] _logoIcons = ["spotify", "discord", "obs"];

	private static readonly string[] _logosKey = ["logos"];

	private static readonly string[] _brandsKey = ["brands"];

	private static readonly string[] _originalPropertyOrder =
		["$schema", "manifestVersion", "id", "name", "version", "entrypoints", "somethingFromTheFuture"];

	private string _project = null!;
	private string _outside = null!;

	[SetUp]
	public void SetUp()
	{
		_project = Directory.CreateTempSubdirectory("macrodeck-plugin-icon-pack-project-").FullName;
		_outside = Directory.CreateTempSubdirectory("macrodeck-plugin-icon-pack-source-").FullName;
		WriteManifest();
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_project, recursive: true);
		Directory.Delete(_outside, recursive: true);
	}

	[Test]
	public async Task Add_copies_a_pack_from_outside_the_project_and_declares_it_without_disturbing_the_manifest()
	{
		var source = Path.Combine(_outside, "Service Logos.macroDeckIconPack");
		IconPackFixtures.Write(source, "Service Logos", _logoIcons);

		var (output, error, exitCode) = await Add(source);

		using var manifest = IconPackFixtures.ReadManifest(_project);
		var root = manifest.RootElement;
		var entry = root.GetProperty("bundledIconPacks")[0];

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(File.Exists(source), Is.True, "A pack from outside the project is copied, never moved.");
			Assert.That(File.Exists(Path.Combine(_project, "icon-packs", "service-logos.macroDeckIconPack")), Is.True);
			Assert.That(entry.GetProperty("key").GetString(), Is.EqualTo("service-logos"));
			Assert.That(entry.GetProperty("path").GetString(), Is.EqualTo("icon-packs/service-logos.macroDeckIconPack"));
			Assert.That(root.EnumerateObject().Select(property => property.Name).Take(_originalPropertyOrder.Length),
				Is.EqualTo(_originalPropertyOrder));
			Assert.That(root.GetProperty("somethingFromTheFuture").GetProperty("kept").GetBoolean(), Is.True);
			Assert.That(output, Does.Contain("3 icon(s)"));
		});
	}

	[Test]
	public async Task Add_keeps_a_tab_indented_manifest_with_a_trailing_comma_readable_and_tab_indented()
	{
		var original = File.ReadAllText(ManifestPath);
		var tabbed = string.Join('\n', original.Split('\n').Select(line =>
			new string('\t', (line.Length - line.TrimStart(' ').Length) / 2) + line.TrimStart(' ')));
		File.WriteAllText(ManifestPath, tabbed.TrimEnd().TrimEnd('}').TrimEnd() + ",\n}\n");
		var source = Path.Combine(_outside, "logos.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons);

		var (_, error, exitCode) = await Add(source);

		var written = File.ReadAllText(ManifestPath);
		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(written, Does.Contain("\n\t\"bundledIconPacks\""));
			Assert.That(written, Does.Not.Contain("\n  \""));
		});
	}

	[Test]
	public async Task Add_moves_a_pack_that_is_already_inside_the_project_and_says_from_where()
	{
		var source = Path.Combine(_project, "downloads", "logos.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons);

		var (output, error, exitCode) = await Add(source);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(File.Exists(source), Is.False);
			Assert.That(File.Exists(Path.Combine(_project, "icon-packs", "logos.macroDeckIconPack")), Is.True);
			Assert.That(output, Does.Contain("Moved 'downloads/logos.macroDeckIconPack'"));
			Assert.That(output, Does.Contain("icon-packs/logos.macroDeckIconPack"));
		});
	}

	[Test]
	public async Task Add_with_copy_keeps_a_pack_inside_the_project_in_place()
	{
		var source = Path.Combine(_project, "design", "logos.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons);

		var (output, error, exitCode) = await Add(source, "--copy");

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(File.Exists(source), Is.True);
			Assert.That(File.Exists(Path.Combine(_project, "icon-packs", "logos.macroDeckIconPack")), Is.True);
			Assert.That(output, Does.Not.Contain("Moved"));
		});
	}

	[Test]
	public async Task Add_uses_a_pack_already_at_the_target_path_as_it_is()
	{
		var target = Path.Combine(_project, "icon-packs", "logos.macroDeckIconPack");
		IconPackFixtures.Write(target, "Logos", _logoIcons);
		var before = await File.ReadAllBytesAsync(target);

		var (output, error, exitCode) = await Add(target);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(File.ReadAllBytes(target), Is.EqualTo(before));
			Assert.That(output, Does.Not.Contain("Moved").And.Not.Contain("Copied"));
			Assert.That(DeclaredKeys(), Is.EqualTo(_logosKey));
		});
	}

	[Test]
	public async Task Add_takes_the_key_from_the_key_option_and_rejects_an_unusable_one()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Service Logos", _logoIcons);

		var invalid = await Add(source, "--key", "Not A Key");
		var chosen = await Add(source, "--key", "brands");

		Assert.Multiple(() =>
		{
			Assert.That(invalid.ExitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(invalid.Error, Does.Contain("error invalid-key:"));
			Assert.That(chosen.ExitCode, Is.EqualTo(ExitCode.Success), chosen.Error);
			Assert.That(DeclaredKeys(), Is.EqualTo(_brandsKey));
			Assert.That(File.Exists(Path.Combine(_project, "icon-packs", "brands.macroDeckIconPack")), Is.True);
		});
	}

	[Test]
	public async Task Add_asks_for_a_key_when_the_pack_name_yields_no_slug()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "!!!", _logoIcons);

		var (_, error, exitCode) = await Add(source);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Contain("--key"));
			Assert.That(DeclaredKeys(), Is.Empty);
		});
	}

	[Test]
	public async Task Add_refuses_an_existing_key_unless_forced_and_force_replaces_file_and_entry()
	{
		var first = Path.Combine(_outside, "first.macroDeckIconPack");
		var second = Path.Combine(_outside, "second.macroDeckIconPack");
		IconPackFixtures.Write(first, "Logos", _logoIcons);
		IconPackFixtures.Write(second, "Logos", ["twitch"]);
		var target = Path.Combine(_project, "icon-packs", "logos.macroDeckIconPack");

		await Add(first);
		var refused = await Add(second);
		var refusedBytes = await File.ReadAllBytesAsync(target);
		var forced = await Add(second, "--force");

		Assert.Multiple(() =>
		{
			Assert.That(refused.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(refused.Error, Does.Contain("error icon-pack-exists:"));
			Assert.That(refusedBytes, Is.EqualTo(File.ReadAllBytes(first)));
			Assert.That(forced.ExitCode, Is.EqualTo(ExitCode.Success), forced.Error);
			Assert.That(File.ReadAllBytes(target), Is.EqualTo(File.ReadAllBytes(second)));
			Assert.That(DeclaredKeys(), Is.EqualTo(_logosKey));
		});
	}

	[Test]
	public async Task Add_rejects_duplicate_and_unusable_icon_names_and_lists_them()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", ["Spotify", "spotify", "a/b", " padded", "obs"]);

		var (_, error, exitCode) = await Add(source);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Contain("error icon-pack-names-invalid:"));
			Assert.That(error, Does.Contain("'Spotify'").IgnoreCase);
			Assert.That(error, Does.Contain("'a/b'"));
			Assert.That(error, Does.Contain("' padded'"));
			Assert.That(error, Does.Not.Contain("'obs'"));
			Assert.That(DeclaredKeys(), Is.Empty);
			Assert.That(Directory.Exists(Path.Combine(_project, "icon-packs")), Is.False);
		});
	}

	[Test]
	public async Task Add_rejects_a_pack_with_an_icon_that_has_no_master_image()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons, withoutMaster: ["discord"]);

		var (_, error, exitCode) = await Add(source);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Contain("'discord'"));
			Assert.That(DeclaredKeys(), Is.Empty);
		});
	}

	[Test]
	public async Task Add_refuses_a_pack_with_ai_generated_assets_unless_the_plugin_declares_them()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons, ai: new JsonObject { ["generatedAssets"] = true });

		var refused = await Add(source);
		WriteManifest(new JsonObject { ["generatedAssets"] = true });
		var accepted = await Add(source);

		Assert.Multiple(() =>
		{
			Assert.That(refused.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(refused.Error, Does.Contain("ai.generatedAssets"));
			Assert.That(accepted.ExitCode, Is.EqualTo(ExitCode.Success), accepted.Error);
		});
	}

	[Test]
	public async Task Add_warns_when_the_pack_declares_nothing_about_ai()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons, withAi: false);

		var (_, error, exitCode) = await Add(source);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(error, Does.Contain("warning icon-pack-ai-undeclared:"));
		});
	}

	[Test]
	public async Task Add_rejects_a_file_that_is_not_an_icon_pack()
	{
		var notAZip = Path.Combine(_outside, "broken.macroDeckIconPack");
		await File.WriteAllTextAsync(notAZip, "not a zip");

		var (_, error, exitCode) = await Add(notAZip);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Contain("error icon-pack-invalid:"));
			Assert.That(DeclaredKeys(), Is.Empty);
		});
	}

	[Test]
	public async Task Add_refuses_a_new_pack_once_the_manifest_declares_the_maximum()
	{
		var manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
		manifest["bundledIconPacks"] = new JsonArray(Enumerable.Range(0, 32)
			.Select(index => (JsonNode)new JsonObject
				{ ["key"] = $"pack-{index}", ["path"] = $"icon-packs/pack-{index}.macroDeckIconPack" })
			.ToArray());
		await File.WriteAllTextAsync(ManifestPath, manifest.ToJsonString());

		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons);

		var (_, error, exitCode) = await Add(source);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(error, Does.Contain("32"));
			Assert.That(DeclaredKeys(), Has.Count.EqualTo(32));
		});
	}

	[Test]
	public async Task List_reports_key_path_name_icon_count_and_presence_as_text_and_json()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons);
		await Add(source);
		var manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
		manifest["bundledIconPacks"]!.AsArray()
			.Add(new JsonObject { ["key"] = "gone", ["path"] = "icon-packs/gone.macroDeckIconPack" });
		await File.WriteAllTextAsync(ManifestPath, manifest.ToJsonString());

		var text = await CliRunner.Run("icon-pack", "list", "--source", _project, "--no-color");
		var json = await CliRunner.Run("icon-pack", "list", "--source", _project, "--output", "json");

		using var document = JsonDocument.Parse(json.Output);
		var packs = document.RootElement.GetProperty("bundledIconPacks");

		Assert.Multiple(() =>
		{
			Assert.That(text.ExitCode, Is.EqualTo(ExitCode.Success), text.Error);
			Assert.That(text.Output, Does.Contain("logos: icon-packs/logos.macroDeckIconPack - Logos, 3 icon(s)"));
			Assert.That(text.Output, Does.Contain("gone: icon-packs/gone.macroDeckIconPack - missing"));
			Assert.That(json.ExitCode, Is.EqualTo(ExitCode.Success), json.Error);
			Assert.That(packs.GetArrayLength(), Is.EqualTo(2));
			Assert.That(packs[0].GetProperty("key").GetString(), Is.EqualTo("logos"));
			Assert.That(packs[0].GetProperty("path").GetString(), Is.EqualTo("icon-packs/logos.macroDeckIconPack"));
			Assert.That(packs[0].GetProperty("name").GetString(), Is.EqualTo("Logos"));
			Assert.That(packs[0].GetProperty("iconCount").GetInt32(), Is.EqualTo(3));
			Assert.That(packs[0].GetProperty("present").GetBoolean(), Is.True);
			Assert.That(packs[1].GetProperty("present").GetBoolean(), Is.False);
			Assert.That(packs[1].GetProperty("iconCount").ValueKind, Is.EqualTo(JsonValueKind.Null));
		});
	}

	[Test]
	public async Task Remove_drops_the_entry_and_deletes_the_file_and_an_unknown_key_is_an_error()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons);
		await Add(source);

		var unknown = await CliRunner.Run("icon-pack", "remove", "brands", "--source", _project);
		var removed = await CliRunner.Run("icon-pack", "remove", "logos", "--source", _project);

		using var manifest = IconPackFixtures.ReadManifest(_project);

		Assert.Multiple(() =>
		{
			Assert.That(unknown.ExitCode, Is.EqualTo(ExitCode.SubjectInvalid));
			Assert.That(unknown.Error, Does.Contain("'brands'"));
			Assert.That(removed.ExitCode, Is.EqualTo(ExitCode.Success), removed.Error);
			Assert.That(File.Exists(Path.Combine(_project, "icon-packs", "logos.macroDeckIconPack")), Is.False);
			Assert.That(manifest.RootElement.TryGetProperty("bundledIconPacks", out var packs) &&
				packs.GetArrayLength() > 0, Is.False);
			Assert.That(manifest.RootElement.GetProperty("somethingFromTheFuture").GetProperty("kept").GetBoolean(),
				Is.True);
		});
	}

	[Test]
	public async Task A_manifest_edited_by_add_still_reads_as_a_valid_manifest()
	{
		var source = Path.Combine(_outside, "pack.macroDeckIconPack");
		IconPackFixtures.Write(source, "Logos", _logoIcons);
		await Add(source);

		var read = ArtifactReaders.ManifestReader.Read(ManifestPath, ManifestFixtures.PluginId, ManifestFixtures.Version);

		Assert.Multiple(() =>
		{
			Assert.That(read.Success, Is.True, read.ErrorMessage);
			Assert.That(read.Manifest!.BundledIconPacks!.Single().Key, Is.EqualTo("logos"));
		});
	}

	private string ManifestPath => Path.Combine(_project, PluginArtifactFiles.ManifestFileName);

	private Task<(string Output, string Error, int ExitCode)> Add(string source, params string[] extra)
		=> CliRunner.Run(["icon-pack", "add", source, "--source", _project, "--no-color", .. extra]);

	private List<string> DeclaredKeys()
	{
		using var manifest = IconPackFixtures.ReadManifest(_project);

		return manifest.RootElement.TryGetProperty("bundledIconPacks", out var packs)
			? packs.EnumerateArray().Select(pack => pack.GetProperty("key").GetString()!).ToList()
			: [];
	}

	private void WriteManifest(JsonObject? ai = null)
	{
		File.WriteAllText(Path.Combine(_project, ManifestFixtures.EntrypointFileName), string.Empty);

		var manifest = JsonNode.Parse(ManifestFixtures.ValidManifestJson())!.AsObject();
		var ordered = new JsonObject { ["$schema"] = "https://schemas.macro-deck.app/plugin-manifest-v1.schema.json" };

		foreach (var (name, value) in manifest.ToList())
		{
			manifest.Remove(name);
			ordered[name] = value;
		}

		ordered["somethingFromTheFuture"] = new JsonObject { ["kept"] = true };

		if (ai is not null)
		{
			ordered["ai"] = ai;
		}

		File.WriteAllText(ManifestPath, ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}
}
