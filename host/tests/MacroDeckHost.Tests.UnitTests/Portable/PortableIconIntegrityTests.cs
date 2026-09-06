using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableIconIntegrityTests
{
	private static readonly byte[] _master = [10, 20, 30];

	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Import_ReusesAnIconFromAnyPack_NotJustTheImportedIconsPack()
	{
		var userPack = await _harness.Icons.CreatePack("My Pack");
		var existing = await _harness.Icons.AddReadyIcon(userPack.Id, "star", _master);

		var idMap = await Import(BundledIcon(out var files), files);

		Assert.Multiple(() =>
		{
			Assert.That(idMap.Values.Single(), Is.EqualTo(existing.Id));
			Assert.That(_harness.Icons.Cache.GetIconsByPackId(userPack.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Import_ReusesAnIconFromAReadOnlyPack()
	{
		var readOnly = await _harness.Icons.CreatePack("Stream Deck", isReadOnly: true);
		var existing = await _harness.Icons.AddReadyIcon(readOnly.Id, "star", _master);

		var idMap = await Import(BundledIcon(out var files), files);

		Assert.That(idMap.Values.Single(), Is.EqualTo(existing.Id));
	}

	// The heart of it: a hand-edited archive can claim any source hash it likes, because the source bytes
	// are not in the archive and nothing can check the claim. It must therefore never be a lookup key.
	[Test]
	public async Task Import_DoesNotReuse_WhenOnlyTheDeclaredSourceHashMatches()
	{
		var pack = await _harness.Icons.CreatePack("My Pack");
		var unrelated = await _harness.Icons.AddReadyIcon(pack.Id, "unrelated", [99, 99], source: [1, 1]);

		var content = BundledIcon(out var files);
		content.Icons[0].SourceContentHash = unrelated.SourceContentHash;

		var idMap = await Import(content, files);

		var imported = _harness.Icons.Cache.GetIconById(idMap.Values.Single())!;
		Assert.Multiple(() =>
		{
			Assert.That(imported.Id, Is.Not.EqualTo(unrelated.Id), "a claimed source hash must not match");
			Assert.That(imported.SourceContentHash, Is.Null, "nothing local hashed those original bytes");
			Assert.That(imported.DeclaredSourceContentHash,
				Is.EqualTo(unrelated.SourceContentHash),
				"the claim is kept as provenance");
			Assert.That(imported.MasterContentHash, Is.EqualTo(MasterContentHash.Compute(_master).Value));
		});
	}

	[Test]
	public async Task Import_DoesNotReuse_WhenOnlyTheLegacyChecksumMatches()
	{
		var pack = await _harness.Icons.CreatePack("My Pack");
		var unrelated = await _harness.Icons.AddReadyIcon(pack.Id, "unrelated", [99, 99], source: [1, 1]);

		var content = BundledIcon(out var files);
		content.Icons[0].Checksum = unrelated.SourceContentHash![ContentHash.Sha256Prefix.Length..];

		var idMap = await Import(content, files);

		Assert.That(idMap.Values.Single(), Is.Not.EqualTo(unrelated.Id));
	}

	[Test]
	public async Task Import_SkipsAnIconWhoseBundledMasterFailsItsDeclaredHash()
	{
		await _harness.Icons.CreatePack("My Pack");
		var content = BundledIcon(out var files);
		content.Icons[0].FileContentHashes[IconVariants.Master] = ContentHash.Compute("something else"u8);

		var idMap = await Import(content, files);

		Assert.Multiple(() =>
		{
			Assert.That(idMap, Is.Empty, "the reference is left dangling rather than pointed at wrong pixels");
			Assert.That(_harness.Icons.Cache.FindByMasterContentHash(MasterContentHash.Compute(_master)), Is.Null);
		});
	}

	[Test]
	public async Task Import_SkipsAnIconWhoseBundledSizeVariantFailsItsDeclaredHash()
	{
		await _harness.Icons.CreatePack("My Pack");
		var content = BundledIcon(out var files, withSize: true);
		content.Icons[0].FileContentHashes["128"] = ContentHash.Compute("something else"u8);

		var idMap = await Import(content, files);

		Assert.That(idMap, Is.Empty);
	}

	// Archives written before file hashes existed declare none. They still import, and still deduplicate,
	// because the importer hashes the bundled bytes itself either way.
	[Test]
	public async Task Import_ArchiveWithoutDeclaredHashes_StillReusesByBundledContent()
	{
		var pack = await _harness.Icons.CreatePack("My Pack");
		var existing = await _harness.Icons.AddReadyIcon(pack.Id, "star", _master);

		var content = BundledIcon(out var files);
		content.Icons[0].FileContentHashes.Clear();

		var idMap = await Import(content, files);

		Assert.That(idMap.Values.Single(), Is.EqualTo(existing.Id));
	}

	[Test]
	public async Task Import_TwoIdenticalIconsInOneArchive_ProduceOneIcon()
	{
		await _harness.Icons.CreatePack("Any");
		var content = BundledIcon(out var files);
		var twin = new PortableIcon
		{
			Id = Guid.NewGuid(),
			Name = "star-copy",
			FileContentHashes = new Dictionary<string, string>(content.Icons[0].FileContentHashes)
		};
		content.Icons.Add(twin);
		files.Add(new PortableIconFile(twin.Id, IconVariants.Master, _master));

		var idMap = await Import(content, files);

		Assert.Multiple(() =>
		{
			Assert.That(idMap, Has.Count.EqualTo(2), "both archive ids are remapped");
			Assert.That(idMap.Values.Distinct().Count(), Is.EqualTo(1), "onto one local icon");
		});
	}

	[Test]
	public async Task Import_ContentTheCatalogLacks_LandsInTheSharedImportedIconsPack()
	{
		var idMap = await Import(BundledIcon(out var files), files);

		var imported = _harness.Icons.Cache.GetIconById(idMap.Values.Single())!;
		Assert.Multiple(() =>
		{
			Assert.That(imported.ProcessingState, Is.EqualTo(IconProcessingState.Ready));
			Assert.That(_harness.Icons.Cache.GetPackById(imported.PackId)!.SourceType,
				Is.EqualTo(IconPackSourceType.MacroDeckImport));
		});
	}

	private Task<IReadOnlyDictionary<Guid, Guid>> Import(PortableContent content, List<PortableIconFile> files)
		=> _harness.AssetManager.Import(content, files, CancellationToken.None);

	private static PortableContent BundledIcon(out List<PortableIconFile> files, bool withSize = false)
	{
		var iconId = Guid.NewGuid();
		files = [new PortableIconFile(iconId, IconVariants.Master, _master)];
		var hashes = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[IconVariants.Master] = ContentHash.Compute(_master)
		};

		if (withSize)
		{
			byte[] variant = [7];
			files.Add(new PortableIconFile(iconId, "128", variant));
			hashes["128"] = ContentHash.Compute(variant);
		}

		return new PortableContent
		{
			Kind = PortableArchiveKind.Profile,
			Icons =
			[
				new PortableIcon
				{
					Id = iconId,
					Name = "star",
					AvailableSizes = withSize ? [128] : [],
					FileContentHashes = hashes
				}
			]
		};
	}
}
