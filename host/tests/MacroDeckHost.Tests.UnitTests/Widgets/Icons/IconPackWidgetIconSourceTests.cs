using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Domain.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Icons;

/// <summary>
/// <see cref="IconPackWidgetIconSource" /> is the only place a widget icon reference is ever parsed as a
/// GUID (WP2's stated invariant) - these cover that it still resolves an icon-pack reference to the same
/// bytes the bare id used to, and that an unparseable reference behaves exactly like an unknown legacy id
/// always has: no image, no exception.
/// </summary>
[TestFixture]
public class IconPackWidgetIconSourceTests
{
	private sealed class FakeIconPackCache : IIconPackCache
	{
		public Dictionary<Guid, IconEntity> Icons { get; } = new();

		public IconEntity? GetIconById(Guid iconId) => Icons.GetValueOrDefault(iconId);

		public Task InitializeCache() => throw new NotSupportedException();

		public IconPackEntity? GetPackById(Guid id) => throw new NotSupportedException();

		public List<IconPackEntity> GetAllPacks() => throw new NotSupportedException();

		public IconPackEntity? GetDefaultPack() => throw new NotSupportedException();

		public Task AddOrUpdatePack(IconPackEntity pack) => throw new NotSupportedException();

		public Task RemovePack(Guid id) => throw new NotSupportedException();

		public List<IconEntity> GetIconsByPackId(Guid packId) => throw new NotSupportedException();

		public List<IconEntity> GetIconsByBatchId(Guid batchId) => throw new NotSupportedException();

		public List<IconEntity> GetIconsByState(params IconProcessingState[] states) =>
			throw new NotSupportedException();

		public int GetIconCount(Guid packId) => throw new NotSupportedException();

		public IconEntity? FindBySourceContentHash(SourceContentHash hash, Guid? withinPackId = null) =>
			throw new NotSupportedException();

		public IconEntity? FindByMasterContentHash(MasterContentHash hash, Guid? withinPackId = null) =>
			throw new NotSupportedException();

		public List<IconEntity> GetIconsMissingMasterContentHash() => throw new NotSupportedException();

		public Task AddIcons(Guid packId, IReadOnlyList<IconEntity> icons) => throw new NotSupportedException();

		public Task UpdateIcon(IconEntity icon) => throw new NotSupportedException();

		public Task RemoveIcon(Guid iconId) => throw new NotSupportedException();

		public Task RemoveIcons(Guid packId, IReadOnlyList<Guid> iconIds) => throw new NotSupportedException();

		public Task FlushPendingWrites() => throw new NotSupportedException();
	}

	private sealed class FakeIconService : IIconService
	{
		public Func<Guid, int?, bool, Task<Result<IconImageResult, IconError>>>? OnGetImage { get; set; }

		public int GetImageCallCount { get; private set; }

		public Task<Result<IconEntity, IconError>> Rename(Guid iconId, string name) =>
			throw new NotSupportedException();

		public Task<Result<IconError>> Delete(Guid iconId) => throw new NotSupportedException();

		public Task<Result<int, IconError>> DeleteMany(IReadOnlyList<Guid> iconIds) =>
			throw new NotSupportedException();

		public Task<Result<IconImageResult, IconError>> GetImage(Guid iconId,
			int? size,
			bool acceptWebp,
			bool staticFrame,
			CancellationToken cancellationToken)
		{
			GetImageCallCount++;
			return OnGetImage!(iconId, size, acceptWebp);
		}
	}

	private static (IconPackWidgetIconSource Source, FakeIconPackCache PackCache, FakeIconService IconService) Create()
	{
		var packCache = new FakeIconPackCache();
		var iconService = new FakeIconService();
		var services = new ServiceCollection();
		services.AddScoped<IIconService>(_ => iconService);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		return (new IconPackWidgetIconSource(packCache, scopeFactory), packCache, iconService);
	}

	[Test]
	public void Type_IsIconPack() => Assert.That(Create().Source.Type, Is.EqualTo(WidgetIconReference.IconPackType));

	[Test]
	public async Task GetImageAsync_AValidReference_ResolvesToTheSameBytesTheBareIdUsedTo()
	{
		var (source, _, iconService) = Create();
		var id = Guid.NewGuid();
		var bytes = "icon-bytes"u8.ToArray();
		iconService.OnGetImage = (_, _, _) =>
			Task.FromResult(
				Result.Ok<IconImageResult, IconError>(
					new IconImageResult(new MemoryStream(bytes), "etag", "image/webp")));

		var image = await source.GetImageAsync(id.ToString(),
			256,
			acceptWebp: true,
			staticFrame: false,
			CancellationToken.None);

		Assert.That(image, Is.Not.Null);
		using var memory = new MemoryStream();
		await image!.Content.CopyToAsync(memory);
		Assert.Multiple(() =>
		{
			Assert.That(memory.ToArray(), Is.EqualTo(bytes));
			Assert.That(image.MediaType, Is.EqualTo("image/webp"));
		});
	}

	[Test]
	public async Task GetImageAsync_ANonGuidReference_YieldsNoImageWithoutCallingTheIconService()
	{
		var (source, _, iconService) = Create();

		var image = await source.GetImageAsync("not-a-guid",
			256,
			acceptWebp: true,
			staticFrame: false,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(image, Is.Null);
			Assert.That(iconService.GetImageCallCount, Is.EqualTo(0));
		});
	}

	[Test]
	public void GetVersion_AKnownIcon_ReturnsItsContentHash()
	{
		var (source, packCache, _) = Create();
		var id = Guid.NewGuid();
		packCache.Icons[id] = new IconEntity
		{
			Id = id,
			PackId = Guid.NewGuid(),
			Name = "star",
			MasterContentHash = "sha256:abc",
			ProcessingState = IconProcessingState.Ready
		};

		Assert.That(source.GetVersion(id.ToString()), Is.EqualTo("sha256:abc"));
	}

	[Test]
	public void GetVersion_AnUnknownOrUnparseableReference_ReturnsNull()
	{
		var (source, _, _) = Create();

		Assert.Multiple(() =>
		{
			Assert.That(source.GetVersion(Guid.NewGuid().ToString()), Is.Null);
			Assert.That(source.GetVersion("not-a-guid"), Is.Null);
		});
	}
}
