using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Persistence;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
internal sealed class PortableIconAppearanceTests
{
	private PortabilityTestHarness _source = null!;
	private PortabilityTestHarness _target = null!;

	[SetUp]
	public void SetUp()
	{
		_source = new PortabilityTestHarness();
		_target = new PortabilityTestHarness();
	}

	[TearDown]
	public void TearDown()
	{
		_source.Dispose();
		_target.Dispose();
	}

	[Test]
	public async Task An_icons_appearances_travel_with_it_into_another_installation()
	{
		var (play, dark) = await SourceIconWithDarkAppearance();
		var bundle = await Collect(play);

		var idMap = await _target.AssetManager.Import(Content(bundle), bundle.Files, [], CancellationToken.None);

		var imported = _target.Icons.Cache.GetIconById(idMap[play.Id])!;
		var appearance = _target.Icons.Cache.GetAppearances(imported.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(bundle.Files.Select(file => file.IconId), Is.EquivalentTo(new[] { play.Id, dark.Id }));
			Assert.That(appearance.Id, Is.Not.EqualTo(dark.Id));
			Assert.That(appearance.AppearanceTraits, Is.EqualTo(dark.AppearanceTraits));
			Assert.That(appearance.MasterContentHash, Is.EqualTo(dark.MasterContentHash));
			Assert.That(appearance.ProcessingState, Is.EqualTo(IconProcessingState.Ready));
			Assert.That(_target.Icons.Cache.GetIconCount(imported.PackId), Is.EqualTo(1));
			Assert.That(_target.Icons.Mediator.Published.OfType<IconPackCreatedNotification>().Single().IconCount,
				Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_reused_icon_in_an_editable_pack_gains_the_kinds_it_lacks()
	{
		var (play, _) = await SourceIconWithDarkAppearance();
		var bundle = await Collect(play);
		var pack = await _target.Icons.CreatePack("Mine");
		var local = await _target.Icons.AddReadyIcon(pack.Id, "play", MasterOf(_source, play));

		var idMap = await _target.AssetManager.Import(Content(bundle), bundle.Files, [], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(idMap[play.Id], Is.EqualTo(local.Id));
			Assert.That(_target.Icons.Cache.GetAppearances(local.Id).Select(KeyOf), Is.EqualTo(new[] { "colorScheme=dark" }));
			Assert.That(_target.Icons.Mediator.Published.OfType<IconUpdatedNotification>().Select(n => n.Icon.Id),
				Does.Contain(local.Id));
		});
	}

	[Test]
	public async Task A_reused_icon_in_a_read_only_pack_is_left_untouched()
	{
		var (play, _) = await SourceIconWithDarkAppearance();
		var bundle = await Collect(play);
		var pack = await _target.Icons.CreatePack("Store", isReadOnly: true);
		var local = await _target.Icons.AddReadyIcon(pack.Id, "play", MasterOf(_source, play));

		var idMap = await _target.AssetManager.Import(Content(bundle), bundle.Files, [], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(idMap[play.Id], Is.EqualTo(local.Id));
			Assert.That(_target.Icons.Cache.GetAppearances(local.Id), Is.Empty);
			Assert.That(_target.Icons.Cache.GetAllPacks(), Has.Count.EqualTo(1), "nothing else was imported");
		});
	}

	[Test]
	public async Task An_archive_icon_is_never_matched_to_an_appearance_with_the_same_bytes()
	{
		var (play, dark) = await SourceIconWithDarkAppearance();
		var bundle = await Collect(play);
		var pack = await _target.Icons.CreatePack("Mine");
		var other = await _target.Icons.AddReadyIcon(pack.Id, "other", Bytes("other"));
		var lookalike = await _target.Icons.AddReadyAppearance(other, "colorScheme=dark", MasterOf(_source, play));

		var idMap = await _target.AssetManager.Import(Content(bundle), bundle.Files, [], CancellationToken.None);

		var imported = _target.Icons.Cache.GetIconById(idMap[play.Id])!;
		Assert.Multiple(() =>
		{
			Assert.That(imported.Id, Is.Not.EqualTo(lookalike.Id));
			Assert.That(imported.AppearanceOfId, Is.Null);
			Assert.That(_target.Icons.Cache.GetAppearances(imported.Id).Single().MasterContentHash,
				Is.EqualTo(dark.MasterContentHash));
		});
	}

	[Test]
	public async Task A_host_that_predates_appearances_imports_the_icon_and_ignores_the_extra_files()
	{
		var (play, dark) = await SourceIconWithDarkAppearance();
		var bundle = await Collect(play);
		var json = JsonNode.Parse(JsonSerializer.Serialize(Content(bundle), PersistenceJsonOptions.Default))!.AsObject();
		foreach (var icon in json["icons"]!.AsArray())
		{
			icon!.AsObject().Remove("appearances");
		}

		var olderView = json.Deserialize<PortableContent>(PersistenceJsonOptions.Default)!;
		var idMap = await _target.AssetManager.Import(olderView, bundle.Files, [], CancellationToken.None);

		var imported = _target.Icons.Cache.GetIconById(idMap[play.Id])!;
		Assert.Multiple(() =>
		{
			Assert.That(idMap.Keys, Is.EqualTo(new[] { play.Id }));
			Assert.That(idMap.ContainsKey(dark.Id), Is.False);
			Assert.That(_target.Icons.Cache.GetAppearances(imported.Id), Is.Empty);
			Assert.That(_target.Icons.Cache.GetIconCount(imported.PackId), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_archive_without_appearances_serializes_as_before()
	{
		var pack = await _source.Icons.CreatePack("Mine");
		var plain = await _source.AddReadyIcon(pack.Id, "stop", []);

		var bundle = await Collect(plain);

		Assert.That(JsonSerializer.Serialize(Content(bundle), PersistenceJsonOptions.Default),
			Does.Not.Contain("appearances").IgnoreCase);
	}

	private async Task<(IconEntity Play, IconEntity Dark)> SourceIconWithDarkAppearance()
	{
		var pack = await _source.Icons.CreatePack("Media");
		var play = await _source.AddReadyIcon(pack.Id, "play", []);
		var dark = await _source.Icons.AddReadyAppearance(play, "colorScheme=dark", Bytes("play-dark"));
		return (play, dark);
	}

	private Task<PortableAssetBundle> Collect(IconEntity icon)
		=> _source.AssetManager.Collect([new PortableWidgetSource(Guid.NewGuid(), $"{{\"icon\":\"{icon.Id}\"}}")],
			PortableExportOptions.Default,
			CancellationToken.None);

	private static PortableContent Content(PortableAssetBundle bundle)
		=> new() { Kind = PortableArchiveKind.Profile, Icons = bundle.Icons.ToList() };

	private static byte[] MasterOf(PortabilityTestHarness harness, IconEntity icon)
	{
		using var stream = harness.Icons.Storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master)!;
		using var buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	private static string KeyOf(IconEntity appearance) => IconAppearanceTraits.ToKey(appearance.AppearanceTraits!);

	private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
