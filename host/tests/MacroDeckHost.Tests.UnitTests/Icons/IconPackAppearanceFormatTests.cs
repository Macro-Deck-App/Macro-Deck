using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.IconPacks;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Icons;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IconPackAppearanceFormatTests
{
	private IconTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new IconTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task An_export_nests_each_ready_appearance_under_its_icon_and_declares_its_master()
	{
		var pack = await _harness.CreatePack("Media");
		var play = await _harness.AddReadyIcon(pack.Id, "play", Bytes("play"));
		var dark = await _harness.AddReadyAppearance(play, "colorScheme=dark", Bytes("play-dark"));
		var pending = await _harness.AddReadyAppearance(play, "motion=static", Bytes("play-static"));
		pending.ProcessingState = IconProcessingState.Pending;
		await _harness.Cache.UpdateIcon(pending);

		var archive = await Export(pack.Id);
		var manifest = ReadManifest(archive);

		var entry = manifest.Icons.Single();
		var nested = entry.Appearances!.Single();
		Assert.Multiple(() =>
		{
			Assert.That(entry.Id, Is.EqualTo(play.Id));
			Assert.That(nested.Id, Is.EqualTo(dark.Id));
			Assert.That(nested.Traits, Is.EqualTo(new Dictionary<string, string> { ["colorScheme"] = "dark" }));
			Assert.That(nested.AvailableSizes, Is.Empty);
			Assert.That(nested.ImportBatchId, Is.Null);
			Assert.That(manifest.Files!.Select(file => file.Path),
				Is.EqualTo(new[] { $"icons/{dark.Id}/master.webp", $"icons/{play.Id}/master.webp" }.Order(StringComparer.Ordinal)));
			Assert.That(EntryBytes(archive, $"icons/{dark.Id}/master.webp"), Is.EqualTo(Bytes("play-dark")));
		});
	}

	[Test]
	public async Task A_pack_without_appearances_exports_and_restores_exactly_as_before()
	{
		var pack = await _harness.CreatePack("Plain");
		await _harness.AddReadyIcon(pack.Id, "stop", Bytes("stop"));

		var archive = await Export(pack.Id);
		var restored = await _harness.RestoreService.RestoreAsNewPack("Plain.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		var manifestText = EntryText(archive, "pack.json");
		var icon = _harness.Cache.GetIconsByPackId(restored.Data!.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(manifestText, Does.Not.Contain("\"appearances\"").And.Not.Contain("\"traits\""));
			Assert.That(_harness.Cache.GetAppearances(icon.Id), Is.Empty);
			Assert.That(IconImageVersion.Of(icon, []), Is.EqualTo(IconImageVersion.Of(icon)));
		});
	}

	[Test]
	public async Task Appearance_files_count_against_the_pack_entry_bound()
	{
		var pack = await _harness.CreatePack("Full");
		var icons = Enumerable.Range(0, IconPackArchiveLimits.MaxUnsignedEntries - 1)
			.Select(index => new IconEntity
			{
				Id = Guid.CreateVersion7(),
				PackId = pack.Id,
				Name = $"icon-{index}",
				ProcessingState = IconProcessingState.Ready,
				MasterContentHash = MasterContentHash.Compute("abc"u8).Value,
				CreatedAt = DateTime.UtcNow
			})
			.ToList();
		await _harness.Cache.AddIcons(pack.Id, icons);
		await _harness.AddReadyAppearance(icons[0], "colorScheme=dark", Bytes("dark"));
		using var destination = new MemoryStream();

		var result = await _harness.CreateExportService().Export(pack.Id, destination, CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconPackError.TooLarge));
	}

	[Test]
	public async Task A_restore_rebuilds_each_icons_appearances_with_new_ids_and_lists_only_the_icon()
	{
		var source = await _harness.CreatePack("Media");
		var play = await _harness.AddReadyIcon(source.Id, "play", Bytes("play"));
		var dark = await _harness.AddReadyAppearance(play, "colorScheme=dark", Bytes("play-dark"));
		var archive = await Export(source.Id);
		_harness.Mediator.Published.Clear();

		var result = await _harness.RestoreService.RestoreAsNewPack("Media.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		var icon = _harness.Cache.GetIconsByPackId(result.Data!.Id).Single();
		var appearance = _harness.Cache.GetAppearances(icon.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(appearance.Id, Is.Not.EqualTo(dark.Id));
			Assert.That(appearance.PackId, Is.EqualTo(result.Data.Id));
			Assert.That(appearance.AppearanceTraits, Is.EqualTo(dark.AppearanceTraits));
			Assert.That(appearance.ProcessingState, Is.EqualTo(IconProcessingState.Ready));
			Assert.That(appearance.MasterContentHash, Is.EqualTo(dark.MasterContentHash));
			Assert.That(_harness.Cache.GetIconCount(result.Data.Id), Is.EqualTo(1));
			Assert.That(_harness.Mediator.Published.OfType<IconPackCreatedNotification>().Single().IconCount,
				Is.EqualTo(1));
		});
		Assert.That(MasterBytes(appearance), Is.EqualTo(Bytes("play-dark")));
	}

	[Test]
	public async Task A_restore_keeps_unknown_traits_and_drops_invalid_repeated_and_excess_appearances()
	{
		var parentId = Guid.NewGuid();
		var appearances = new List<(Guid Id, JsonObject? Traits)>
		{
			(Guid.NewGuid(), new JsonObject { ["Bad Key"] = "x" }),
			(Guid.NewGuid(), null),
			(Guid.NewGuid(), new JsonObject { ["colorScheme"] = "dark" }),
			(Guid.NewGuid(), new JsonObject { ["colorScheme"] = "dark" }),
			(Guid.NewGuid(), new JsonObject { ["contrast"] = "high" })
		};
		appearances.AddRange(Enumerable.Range(0, 8)
			.Select(index => (Guid.NewGuid(), (JsonObject?)new JsonObject { ["variant"] = $"v{index}" })));
		var archive = HandWrittenPack(parentId, appearances);

		var result = await _harness.RestoreService.RestoreAsNewPack("Edited.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		var icon = _harness.Cache.GetIconsByPackId(result.Data!.Id).Single();
		var keys = _harness.Cache.GetAppearances(icon.Id)
			.Select(appearance => IconAppearanceTraits.ToKey(appearance.AppearanceTraits!))
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(keys, Has.Count.EqualTo(IconAppearanceTraits.MaxAppearancesPerIcon));
			Assert.That(keys, Does.Contain("colorScheme=dark").And.Contain("contrast=high"));
			Assert.That(keys, Is.Unique);
			Assert.That(_harness.Cache.GetIconCount(result.Data.Id), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Merging_a_pack_brings_its_appearances_but_reports_only_the_icons()
	{
		var source = await _harness.CreatePack("Source");
		var play = await _harness.AddReadyIcon(source.Id, "play", Bytes("play"));
		await _harness.AddReadyAppearance(play, "motion=static", Bytes("play-static"));
		var archive = await Export(source.Id);
		var target = await _harness.CreatePack("Target");
		var batchId = Guid.CreateVersion7();

		var result = await _harness.RestoreService.MergeIntoPack(target.Id,
			batchId,
			"Source.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		var merged = result.Data!.Single();
		var appearance = _harness.Cache.GetAppearances(merged.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconsByPackId(target.Id).Select(icon => icon.Id), Is.EqualTo(new[] { merged.Id }));
			Assert.That(appearance.PackId, Is.EqualTo(target.Id));
			Assert.That(appearance.ImportBatchId, Is.EqualTo(batchId));
		});
	}

	[Test]
	public async Task An_upgrade_that_changes_only_appearances_updates_the_icon_in_place()
	{
		var installed = await Install(await BuildArchive("1.0.0", ("colorScheme=dark", "dark-one"), ("contrast=high", "high")));
		var icon = _harness.Cache.GetIconsByPackId(installed.Id).Single();
		var darkId = AppearanceId(icon, "colorScheme=dark");
		_harness.Mediator.Published.Clear();

		var next = await BuildArchive("1.1.0", ("colorScheme=dark", "dark-two"), ("motion=static", "static"));
		var result = await _harness.RestoreService.UpgradePack(installed.Id,
			"pack.macroDeckIconPack",
			new MemoryStream(next),
			CancellationToken.None);

		var appearances = _harness.Cache.GetAppearances(icon.Id);
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconsByPackId(installed.Id).Single().Id, Is.EqualTo(icon.Id));
			Assert.That(appearances.Select(KeyOf), Is.EqualTo(new[] { "colorScheme=dark", "motion=static" }));
			Assert.That(AppearanceId(icon, "colorScheme=dark"), Is.EqualTo(darkId), "a kind the update keeps keeps its id");
			Assert.That(_harness.Mediator.Published.OfType<IconUpdatedNotification>().Select(n => n.Icon.Id),
				Is.EqualTo(new[] { icon.Id }));
			Assert.That(_harness.Mediator.Published.OfType<IconsAddedNotification>(), Is.Empty);
		});
		Assert.That(MasterBytes(appearances[0]), Is.EqualTo(Bytes("master:dark-two")));
	}

	[Test]
	public async Task An_upgrade_with_the_same_appearances_announces_no_change_and_one_without_them_removes_them()
	{
		var installed = await Install(await BuildArchive("1.0.0", ("colorScheme=dark", "dark")));
		var icon = _harness.Cache.GetIconsByPackId(installed.Id).Single();
		var darkId = AppearanceId(icon, "colorScheme=dark");
		_harness.Mediator.Published.Clear();

		await _harness.RestoreService.UpgradePack(installed.Id,
			"pack.macroDeckIconPack",
			new MemoryStream(await BuildArchive("1.0.1", ("colorScheme=dark", "dark"))),
			CancellationToken.None);
		var unchanged = _harness.Mediator.Published.OfType<IconUpdatedNotification>().ToList();
		await _harness.RestoreService.UpgradePack(installed.Id,
			"pack.macroDeckIconPack",
			new MemoryStream(await BuildArchive("2.0.0")),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unchanged, Is.Empty);
			Assert.That(_harness.Cache.GetAppearances(icon.Id), Is.Empty);
			Assert.That(_harness.Cache.GetIconById(darkId), Is.Null);
			Assert.That(_harness.Storage.ListVariants(installed.Id, darkId), Is.Empty);
			Assert.That(_harness.Mediator.Published.OfType<IconUpdatedNotification>().Select(n => n.Icon.Id),
				Is.EqualTo(new[] { icon.Id }));
		});
	}

	[Test]
	public async Task Replacing_a_plugin_pack_replaces_an_icons_appearances_wholesale()
	{
		var stamp = new IconPackSourceStamp(IconPackSourceType.Plugin, "com.example/logos", "rev-1");
		var installed = await Install(await BuildArchive("1.0.0", ("colorScheme=dark", "dark"), ("contrast=high", "high")));
		var icon = _harness.Cache.GetIconsByPackId(installed.Id).Single();
		_harness.Mediator.Published.Clear();

		var replaced = await _harness.RestoreService.ReplaceFromSource(installed.Id,
			"logos.macroDeckIconPack",
			new MemoryStream(await BuildArchive("1.1.0", ("motion=static", "static"))),
			stamp,
			new HashSet<Guid>(),
			CancellationToken.None);
		var again = await _harness.RestoreService.ReplaceFromSource(installed.Id,
			"logos.macroDeckIconPack",
			new MemoryStream(await BuildArchive("1.1.0", ("motion=static", "static"))),
			stamp,
			new HashSet<Guid>(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(replaced.Data!.IconsChanged, Is.True);
			Assert.That(again.Data!.IconsChanged, Is.False);
			Assert.That(_harness.Cache.GetIconsByPackId(installed.Id).Single().Id, Is.EqualTo(icon.Id));
			Assert.That(_harness.Cache.GetAppearances(icon.Id).Select(KeyOf), Is.EqualTo(new[] { "motion=static" }));
			Assert.That(_harness.Mediator.Published.OfType<IconUpdatedNotification>().Select(n => n.Icon.Id),
				Is.EqualTo(new[] { icon.Id }));
		});
	}

	[Test]
	public async Task A_host_that_predates_appearances_reads_an_exported_pack_as_its_icons_with_their_default_images()
	{
		var source = await _harness.CreatePack("Media");
		var play = await _harness.AddReadyIcon(source.Id, "play", Bytes("play"));
		await _harness.AddReadyAppearance(play, "colorScheme=dark", Bytes("play-dark"));
		var archive = await Export(source.Id);

		var olderManifest = JsonSerializer.Deserialize<OlderPackManifest>(EntryText(archive, "pack.json"),
			PersistenceJsonOptions.Default)!;
		var olderView = JsonNode.Parse(EntryText(archive, "pack.json"))!.AsObject();
		foreach (var entry in olderView["icons"]!.AsArray())
		{
			entry!.AsObject().Remove("appearances");
		}

		var restored = await _harness.RestoreService.RestoreAsNewPack("Media.macroDeckIconPack",
			new MemoryStream(ReplaceEntry(archive, "pack.json", Encoding.UTF8.GetBytes(olderView.ToJsonString()))),
			CancellationToken.None);

		var icon = _harness.Cache.GetIconsByPackId(restored.Data!.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(olderManifest.Icons.Select(entry => entry.Id), Is.EqualTo(new[] { play.Id }));
			Assert.That(olderManifest.Icons.Single().MasterContentHash, Is.EqualTo(play.MasterContentHash));
			Assert.That(_harness.Cache.GetAppearances(icon.Id), Is.Empty);
			Assert.That(_harness.Cache.GetIconCount(restored.Data.Id), Is.EqualTo(1));
		});
		Assert.That(MasterBytes(icon), Is.EqualTo(Bytes("play")));
	}

	[Test]
	public async Task An_upgrade_that_adds_or_drops_an_appearance_on_every_icon_writes_the_pack_a_bounded_number_of_times()
	{
		const int iconCount = 40;
		var store = new CountingIconPackStore(_harness.PackStore);
		using var cache = new IconPackCache(store, _harness.Storage, _harness.Logger);
		var restore = new IconPackRestoreService(cache,
			_harness.Storage,
			store,
			_harness.Paths,
			_harness.Mediator,
			new IconPackOwnerRegistry([]),
			_harness.Logger);
		var installed = await restore.RestoreAsNewPack("pack.macroDeckIconPack",
			new MemoryStream(await BuildManyIconArchive("1.0.0", iconCount, withDarkAppearance: false)),
			CancellationToken.None);
		var packId = installed.Data!.Id;

		store.Saves = 0;
		var withAppearances = await restore.UpgradePack(packId,
			"pack.macroDeckIconPack",
			new MemoryStream(await BuildManyIconArchive("1.1.0", iconCount, withDarkAppearance: true)),
			CancellationToken.None);
		var savesAdding = store.Saves;
		var appearanceCounts = cache.GetIconsByPackId(packId).Select(icon => cache.GetAppearances(icon.Id).Count).ToList();

		store.Saves = 0;
		var withoutAppearances = await restore.UpgradePack(packId,
			"pack.macroDeckIconPack",
			new MemoryStream(await BuildManyIconArchive("1.2.0", iconCount, withDarkAppearance: false)),
			CancellationToken.None);
		var savesDropping = store.Saves;

		Assert.Multiple(() =>
		{
			Assert.That(withAppearances.Success && withoutAppearances.Success, Is.True);
			Assert.That(appearanceCounts, Has.Count.EqualTo(iconCount).And.All.EqualTo(1));
			Assert.That(cache.GetIconsByPackId(packId).SelectMany(icon => cache.GetAppearances(icon.Id)), Is.Empty);
			Assert.That(savesAdding, Is.LessThanOrEqualTo(3), "pack writes while adding appearances");
			Assert.That(savesDropping, Is.LessThanOrEqualTo(3), "pack writes while dropping appearances");
		});
	}

	private sealed class CountingIconPackStore(IIconPackStore inner) : IIconPackStore
	{
		public int Saves { get; set; }

		public IReadOnlyList<IconPackManifest> LoadAll() => inner.LoadAll();

		public void Save(IconPackManifest manifest)
		{
			Saves++;
			inner.Save(manifest);
		}

		public void Delete(Guid packId) => inner.Delete(packId);
	}

	private async Task<byte[]> BuildManyIconArchive(string version, int iconCount, bool withDarkAppearance)
	{
		var source = await _harness.CreatePack($"Pack-{Guid.NewGuid():N}");
		source.Version = version;
		await _harness.Cache.AddOrUpdatePack(source);
		for (var index = 0; index < iconCount; index++)
		{
			var master = Bytes($"master:{index}");
			var icon = new IconEntity
			{
				Id = new Guid(index + 1, 0x7f3e, 0x4d8e, 0x9a, 0x55, 0x0d, 0x9f, 0x0f, 0x0a, 0x1c, 0x01),
				PackId = source.Id,
				Name = $"icon-{index}",
				MasterContentHash = MasterContentHash.Compute(master).Value,
				ProcessingState = IconProcessingState.Ready,
				CreatedAt = DateTime.UtcNow
			};
			await _harness.Cache.AddIcons(source.Id, [icon]);
			await _harness.Storage.WriteVariant(source.Id, icon.Id, IconVariants.Master, master, CancellationToken.None);
			if (withDarkAppearance)
			{
				await _harness.AddReadyAppearance(icon, "colorScheme=dark", Bytes($"master:dark-{index}"));
			}
		}

		var archive = await Export(source.Id);
		await _harness.Cache.RemovePack(source.Id);
		return archive;
	}

	private sealed record OlderPackManifest(string Name, List<OlderIconEntry> Icons);

	private sealed record OlderIconEntry(Guid Id, string Name, string? MasterContentHash);

	private async Task<IconPackEntity> Install(byte[] archive)
	{
		var result = await _harness.RestoreService.RestoreAsNewPack("pack.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);
		return result.Data!;
	}

	private async Task<byte[]> BuildArchive(string version, params (string Key, string Body)[] appearances)
	{
		var source = await _harness.CreatePack($"Pack-{Guid.NewGuid():N}");
		source.Version = version;
		await _harness.Cache.AddOrUpdatePack(source);
		var master = Bytes("master:play");
		var play = new IconEntity
		{
			Id = new Guid("6b1b3c51-7f3e-4d8e-9a55-0d9f0f0a1c01"),
			PackId = source.Id,
			Name = "play",
			MasterContentHash = MasterContentHash.Compute(master).Value,
			ProcessingState = IconProcessingState.Ready,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(source.Id, [play]);
		await _harness.Storage.WriteVariant(source.Id, play.Id, IconVariants.Master, master, CancellationToken.None);
		foreach (var (key, body) in appearances)
		{
			await _harness.AddReadyAppearance(play, key, Bytes($"master:{body}"));
		}

		var archive = await Export(source.Id);
		await _harness.Cache.RemovePack(source.Id);
		return archive;
	}

	private Guid AppearanceId(IconEntity icon, string key)
		=> _harness.Cache.GetAppearances(icon.Id).Single(appearance => KeyOf(appearance) == key).Id;

	private static string KeyOf(IconEntity appearance) => IconAppearanceTraits.ToKey(appearance.AppearanceTraits!);

	private async Task<byte[]> Export(Guid packId)
	{
		using var stream = new MemoryStream();
		var result = await _harness.CreateExportService().Export(packId, stream, CancellationToken.None);
		Assert.That(result.Success, Is.True);
		return stream.ToArray();
	}

	private byte[] MasterBytes(IconEntity icon)
	{
		using var stream = _harness.Storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master);
		using var buffer = new MemoryStream();
		stream!.CopyTo(buffer);
		return buffer.ToArray();
	}

	private static IconPackManifest ReadManifest(byte[] archive)
		=> JsonSerializer.Deserialize<IconPackManifest>(EntryText(archive, "pack.json"), PersistenceJsonOptions.Default)!;

	private static string EntryText(byte[] archive, string name) => Encoding.UTF8.GetString(EntryBytes(archive, name));

	private static byte[] EntryBytes(byte[] archive, string name)
	{
		using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
		using var stream = zip.GetEntry(name)!.Open();
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	private static byte[] ReplaceEntry(byte[] archive, string name, byte[] replacement)
	{
		using var source = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
		return Zip(source.Entries
			.Select(entry => (entry.FullName, entry.FullName == name ? replacement : EntryBytes(archive, entry.FullName)))
			.ToArray());
	}

	private static byte[] HandWrittenPack(Guid parentId, IReadOnlyList<(Guid Id, JsonObject? Traits)> appearances)
	{
		var nested = new JsonArray();
		foreach (var (id, traits) in appearances)
		{
			var entry = new JsonObject { ["id"] = id.ToString(), ["name"] = "play" };
			if (traits is not null)
			{
				entry["traits"] = traits;
			}

			nested.Add(entry);
		}

		var manifest = new JsonObject
		{
			["id"] = Guid.NewGuid().ToString(),
			["name"] = "Edited",
			["icons"] = new JsonArray(new JsonObject
			{
				["id"] = parentId.ToString(),
				["name"] = "play",
				["appearances"] = nested
			})
		};

		return Zip([
			("pack.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())),
			($"icons/{parentId}/master.webp", Bytes("play")),
			.. appearances.Select(appearance => ($"icons/{appearance.Id}/master.webp", Bytes($"appearance:{appearance.Id}")))
		]);
	}

	private static byte[] Zip((string Name, byte[] Content)[] entries)
	{
		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var (name, content) in entries)
			{
				using var entryStream = zip.CreateEntry(name).Open();
				entryStream.Write(content);
			}
		}

		return stream.ToArray();
	}

	private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
