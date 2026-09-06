using System.Globalization;
using System.Text;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

/// <summary>Buttons reference icons by id, so a store update of an installed pack has to land on the same
/// pack and the same icon ids rather than importing a second copy.</summary>
[TestFixture]
public class IconPackUpgradeTests
{
	private IconTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new IconTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task An_upgrade_keeps_the_pack_id_and_the_ids_of_icons_the_new_version_still_carries()
	{
		var installed = await InstallPack("1.0.0", ("home", "one"), ("play", "one"), ("stop", "one"));
		var installedIcons = _harness.Cache.GetIconsByPackId(installed.Id);
		var homeId = installedIcons.Single(icon => icon.Name == "home").Id;
		var playId = installedIcons.Single(icon => icon.Name == "play").Id;

		// 1.1.0 changes home's bytes, keeps play, drops stop and adds record.
		var next = await BuildArchive("1.1.0", ("home", "two"), ("play", "one"), ("record", "one"));
		var result = await _harness.RestoreService.UpgradePack(installed.Id,
			"pack.macroDeckIconPack",
			new MemoryStream(next),
			CancellationToken.None);

		var icons = _harness.Cache.GetIconsByPackId(installed.Id);
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Id, Is.EqualTo(installed.Id));
			Assert.That(result.Data!.Version, Is.EqualTo("1.1.0"));
			Assert.That(icons.Single(icon => icon.Name == "home").Id, Is.EqualTo(homeId));
			Assert.That(icons.Single(icon => icon.Name == "play").Id, Is.EqualTo(playId));
			Assert.That(icons.Select(icon => icon.Name), Does.Contain("record"));
			Assert.That(icons.Select(icon => icon.Name),
				Does.Contain("stop"),
				"an icon the new version dropped stays installed so buttons using it keep working");
		});

		await using var master = _harness.Storage.OpenVariant(installed.Id, homeId, IconVariants.Master);
		using var reader = new StreamReader(master!);
		Assert.That(await reader.ReadToEndAsync(),
			Is.EqualTo("master:two"),
			"the surviving icon id must resolve to the new version's bytes");
	}

	[Test]
	public async Task An_upgrade_of_a_pack_installed_before_source_ids_were_recorded_still_keeps_its_ids()
	{
		var installed = await InstallPack("1.0.0", ("home", "one"));
		var icon = _harness.Cache.GetIconsByPackId(installed.Id).Single();
		var originalId = icon.Id;
		icon.SourceIconId = null;
		await _harness.Cache.UpdateIcon(icon);

		var next = await BuildArchive("1.1.0", ("home", "two"));
		await _harness.RestoreService.UpgradePack(installed.Id,
			"pack.macroDeckIconPack",
			new MemoryStream(next),
			CancellationToken.None);

		var icons = _harness.Cache.GetIconsByPackId(installed.Id);
		Assert.Multiple(() =>
		{
			Assert.That(icons, Has.Count.EqualTo(1));
			Assert.That(icons[0].Id, Is.EqualTo(originalId));
		});
	}

	private async Task<IconPackEntity> InstallPack(string version, params (string Name, string Body)[] icons)
	{
		var archive = await BuildArchive(version, icons);
		var result = await _harness.RestoreService.RestoreAsNewPack("pack.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);
		return result.Data!;
	}

	private async Task<byte[]> BuildArchive(string version, params (string Name, string Body)[] icons)
	{
		var source = await _harness.CreatePack($"Pack-{Guid.NewGuid():N}");
		source.Version = version;
		await _harness.Cache.AddOrUpdatePack(source);

		foreach (var (name, body) in icons)
		{
			var master = Encoding.UTF8.GetBytes($"master:{body}");
			var icon = new IconEntity
			{
				Id = DeterministicIconId(name),
				PackId = source.Id,
				Name = name,
				MasterContentHash = MasterContentHash.Compute(master).Value,
				ProcessingState = IconProcessingState.Ready,
				AvailableSizes = [128],
				CreatedAt = DateTime.UtcNow
			};

			await _harness.Cache.AddIcons(source.Id, [icon]);
			await _harness.Storage.WriteVariant(source.Id,
				icon.Id,
				IconVariants.Master,
				master,
				CancellationToken.None);
			await _harness.Storage.WriteVariant(source.Id,
				icon.Id,
				128.ToString(CultureInfo.InvariantCulture),
				new byte[] { 4 },
				CancellationToken.None);
		}

		var stream = new MemoryStream();
		var export = await _harness.CreateExportService().Export(source.Id, stream, CancellationToken.None);
		Assert.That(export.Success, Is.True);
		await _harness.Cache.RemovePack(source.Id);
		return stream.ToArray();
	}

	// Two exports of the same logical pack must carry the same per-icon ids, the way a publisher's
	// successive releases do; a fresh Guid per build would make the correlation untestable.
	private static Guid DeterministicIconId(string name)
	{
		var bytes = new byte[16];
		Encoding.UTF8.GetBytes(name).CopyTo(bytes.AsSpan(0, Math.Min(16, name.Length)));
		return new Guid(bytes);
	}
}
