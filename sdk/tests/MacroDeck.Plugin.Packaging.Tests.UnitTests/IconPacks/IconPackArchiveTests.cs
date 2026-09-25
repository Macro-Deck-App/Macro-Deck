using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.IconPacks;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.IconPacks;

[TestFixture]
internal sealed class IconPackArchiveTests
{
	private static readonly string[] _allNames = ["spotify", "discord"];

	private static readonly string[] _duplicate = ["Spotify"];

	private static readonly string[] _unusable = ["", " padded", "a/b", "tab\tname", new('x', 129)];

	[Test]
	public void A_well_formed_pack_reports_its_name_version_ai_and_icons()
	{
		using var pack = Pack(_allNames, ai: new JsonObject { ["generatedAssets"] = true });

		var result = IconPackArchive.Read(pack);

		Assert.That(result.Success, Is.True, result.Error);
		Assert.Multiple(() =>
		{
			Assert.That(result.Info!.Name, Is.EqualTo("Logos"));
			Assert.That(result.Info.Version, Is.EqualTo("1.0.0"));
			Assert.That(result.Info.Ai?.GeneratedAssets, Is.True);
			Assert.That(result.Info.Icons.Select(icon => icon.Name), Is.EqualTo(_allNames));
			Assert.That(result.Info.Icons.All(icon => icon.HasMaster), Is.True);
			Assert.That(result.Info.NamesAreValid, Is.True);
		});
	}

	[Test]
	public void Icon_names_that_differ_only_in_case_are_reported_as_duplicates()
	{
		using var pack = Pack(["Spotify", "spotify", "discord"]);

		var info = IconPackArchive.Read(pack).Info!;

		Assert.Multiple(() =>
		{
			Assert.That(info.DuplicateNames, Is.EqualTo(_duplicate));
			Assert.That(info.NamesAreValid, Is.False);
		});
	}

	[Test]
	public void Names_that_cannot_address_an_icon_are_reported_as_unusable()
	{
		using var pack = Pack([.. _unusable, "fine"]);

		var info = IconPackArchive.Read(pack).Info!;

		Assert.Multiple(() =>
		{
			Assert.That(info.UnusableNames, Is.EquivalentTo(_unusable));
			Assert.That(info.NamesAreValid, Is.False);
		});
	}

	[Test]
	public void An_icon_without_a_master_variant_is_flagged()
	{
		using var pack = Pack(_allNames, withoutMaster: "discord");

		var icons = IconPackArchive.Read(pack).Info!.Icons;

		Assert.Multiple(() =>
		{
			Assert.That(icons.Single(icon => icon.Name == "discord").HasMaster, Is.False);
			Assert.That(icons.Single(icon => icon.Name == "spotify").HasMaster, Is.True);
		});
	}

	[Test]
	public void A_file_that_is_not_a_zip_archive_is_rejected()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));

		var result = IconPackArchive.Read(stream);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.Not.Empty);
		});
	}

	[Test]
	public void An_archive_without_a_pack_manifest_is_rejected()
	{
		var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			using var entry = archive.CreateEntry($"icons/{Guid.NewGuid()}/master.webp").Open();
			entry.Write("bytes"u8);
		}

		stream.Position = 0;
		var result = IconPackArchive.Read(stream);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Does.Contain("pack.json"));
		});
		stream.Dispose();
	}

	[Test]
	public void A_pack_at_the_entry_bound_is_read()
	{
		using var pack = PaddedPack(IconPackArchiveLimits.MaxEntries);

		var result = IconPackArchive.Read(pack);

		Assert.That(result.Success, Is.True, result.Error);
	}

	[Test]
	public void A_pack_above_the_entry_bound_is_rejected()
	{
		using var pack = PaddedPack(IconPackArchiveLimits.MaxEntries + 1);

		var result = IconPackArchive.Read(pack);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Does.Contain(IconPackArchiveLimits.MaxEntries.ToString(CultureInfo.InvariantCulture)));
		});
	}

	[Test]
	public void A_pack_json_larger_than_the_plugin_manifest_bound_is_read()
	{
		using var pack = PaddedPack(2, manifestPadding: 20 * 1024 * 1024);

		var result = IconPackArchive.Read(pack);

		Assert.That(result.Success, Is.True, result.Error);
	}

	[Test]
	public void A_pack_json_above_the_manifest_bound_is_rejected()
	{
		using var pack = PaddedPack(2, manifestPadding: IconPackArchiveLimits.MaxManifestBytes);

		var result = IconPackArchive.Read(pack);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Does.Contain("pack.json"));
		});
	}

	private static MemoryStream PaddedPack(int entries, int manifestPadding = 0)
	{
		var id = Guid.NewGuid();
		var manifest = new JsonObject
		{
			["name"] = "Logos",
			["description"] = new string('x', manifestPadding),
			["icons"] = new JsonArray(new JsonObject { ["id"] = id.ToString(), ["name"] = "spotify" })
		};

		var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			using (var writer = new StreamWriter(archive.CreateEntry("pack.json").Open()))
			{
				writer.Write(manifest.ToJsonString());
			}

			using (var master = archive.CreateEntry($"icons/{id}/master.webp").Open())
			{
				master.Write("RIFF-webp-bytes"u8);
			}

			for (var index = 2; index < entries; index++)
			{
				archive.CreateEntry($"filler/{index}");
			}
		}

		stream.Position = 0;
		return stream;
	}

	private static MemoryStream Pack(IReadOnlyList<string> names, JsonObject? ai = null, string? withoutMaster = null)
	{
		var icons = names.Select(name => (Id: Guid.NewGuid(), Name: name)).ToList();
		var manifest = new JsonObject
		{
			["name"] = "Logos",
			["version"] = "1.0.0",
			["icons"] = new JsonArray(icons
				.Select(icon => (JsonNode)new JsonObject { ["id"] = icon.Id.ToString(), ["name"] = icon.Name })
				.ToArray())
		};

		if (ai is not null)
		{
			manifest["ai"] = ai;
		}

		var stream = new MemoryStream();
		using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			using (var writer = new StreamWriter(archive.CreateEntry("pack.json").Open()))
			{
				writer.Write(manifest.ToJsonString());
			}

			foreach (var icon in icons.Where(icon => icon.Name != withoutMaster))
			{
				using var entry = archive.CreateEntry($"icons/{icon.Id}/master.webp").Open();
				entry.Write("RIFF-webp-bytes"u8);
			}
		}

		stream.Position = 0;
		return stream;
	}
}
