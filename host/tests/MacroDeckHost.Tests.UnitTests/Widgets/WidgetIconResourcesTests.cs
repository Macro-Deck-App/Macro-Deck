using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Widgets;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetIconResourcesTests
{
	private sealed class FakeWidgetIconSource : IWidgetIconSource
	{
		public Func<string, Task<WidgetIconImage?>>? OnGetImage { get; set; }

		public Func<string, int, bool, Task<WidgetIconImage?>>? OnGetRendition { get; set; }

		public int GetImageCallCount { get; private set; }

		public List<(int Size, bool StaticFrame)> Requests { get; } = [];

		public string Type => WidgetIconReference.IconPackType;

		public string? GetVersion(string reference) => null;

		public Task<WidgetIconImage?> GetImageAsync(string reference,
			int size,
			bool acceptWebp,
			bool staticFrame,
			CancellationToken cancellationToken)
		{
			GetImageCallCount++;
			Requests.Add((size, staticFrame));

			return OnGetRendition is { } rendition ? rendition(reference, size, staticFrame) : OnGetImage!(reference);
		}
	}

	private sealed class SingleSourceRegistry : IWidgetIconSourceRegistry
	{
		private readonly IWidgetIconSource _source;

		public SingleSourceRegistry(IWidgetIconSource source) => _source = source;

		public IWidgetIconSource? Find(string type) => type == _source.Type ? _source : null;
	}

	private static (IWidgetIconResources Resources, FakeWidgetIconSource Source, UiResourceStore Store) Create()
	{
		var source = new FakeWidgetIconSource();
		var store = new UiResourceStore();
		var resources = new WidgetIconResources(new SingleSourceRegistry(source), store, Log.Logger);

		return (resources, source, store);
	}

	private static WidgetIconReference IconPack(Guid id) => WidgetIconReference.IconPack(id.ToString());

	[Test]
	public async Task A_configured_icon_becomes_a_resolvable_UiResource()
	{
		var (resources, source, store) = Create();
		var iconId = Guid.NewGuid();
		var bytes = "icon-bytes"u8.ToArray();
		source.OnGetImage = _ =>
			Task.FromResult<WidgetIconImage?>(new WidgetIconImage(new MemoryStream(bytes), "image/webp"));

		var resource = await resources.ResolveAsync(IconPack(iconId), CancellationToken.None);

		Assert.That(resource, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(resource!.MediaType, Is.EqualTo("image/webp"));
			Assert.That(store.TryGet(resource.ResourceId, out var content), Is.True);
			Assert.That(content.Content.ToArray(), Is.EqualTo(bytes));
		});
	}

	[Test]
	public async Task Resolving_the_same_icon_twice_does_not_read_its_bytes_again()
	{
		var (resources, source, _) = Create();
		var iconId = Guid.NewGuid();
		source.OnGetImage = _ => Task.FromResult<WidgetIconImage?>(
			new WidgetIconImage(new MemoryStream("bytes"u8.ToArray()), "image/webp"));

		var first = await resources.ResolveAsync(IconPack(iconId), CancellationToken.None);
		var second = await resources.ResolveAsync(IconPack(iconId), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(second!.ResourceId, Is.EqualTo(first!.ResourceId));
			Assert.That(source.GetImageCallCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_missing_icon_yields_no_image()
	{
		var (resources, source, _) = Create();
		source.OnGetImage = _ => Task.FromResult<WidgetIconImage?>(null);

		var resource = await resources.ResolveAsync(IconPack(Guid.NewGuid()), CancellationToken.None);

		Assert.That(resource, Is.Null);
	}

	[Test]
	public async Task
		An_absent_reference_or_one_from_an_unregistered_provider_yields_no_image_without_reading_anything()
	{
		var (resources, source, _) = Create();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await resources.ResolveAsync(null, CancellationToken.None), Is.Null);
			Assert.That(await resources.ResolveAsync(new WidgetIconReference("future-provider", "x"),
					CancellationToken.None),
				Is.Null);
		});
		Assert.That(source.GetImageCallCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Evicting_an_icon_makes_the_next_resolve_read_its_bytes_again()
	{
		var (resources, source, _) = Create();
		var iconId = Guid.NewGuid();
		source.OnGetImage = _ => Task.FromResult<WidgetIconImage?>(
			new WidgetIconImage(new MemoryStream("bytes"u8.ToArray()), "image/webp"));

		await resources.ResolveAsync(IconPack(iconId), CancellationToken.None);
		resources.Evict(iconId);
		await resources.ResolveAsync(IconPack(iconId), CancellationToken.None);

		Assert.That(source.GetImageCallCount,
			Is.EqualTo(2),
			"a re-imported icon must not keep drawing its stale bytes after eviction");
	}

	[Test]
	public async Task The_cache_is_bounded_and_evicts_the_least_recently_used_entry()
	{
		// A test that only ever fills the cache in one pass and checks that entry 0 falls out cannot tell
		// LRU from plain FIFO - both would evict the oldest insertion identically. What actually
		// discriminates the two: touch an early entry (a read, which TryGetCached moves to the
		// most-recently-used end) partway through, then overflow by one. FIFO evicts index 0 regardless;
		// true LRU evicts index 1 instead, since 0 was just read and 1 was not.
		var (resources, source, _) = Create();
		source.OnGetImage = reference => Task.FromResult<WidgetIconImage?>(
			new WidgetIconImage(new MemoryStream(Guid.Parse(reference).ToByteArray()), "image/webp"));

		var ids = Enumerable.Range(0, WidgetIconResources.MaxEntries).Select(_ => Guid.NewGuid()).ToList();

		foreach (var id in ids)
		{
			await resources.ResolveAsync(IconPack(id), CancellationToken.None);
		}

		// Touch index 0 - a cache read, not a re-import - so it becomes the most-recently-used entry.
		await resources.ResolveAsync(IconPack(ids[0]), CancellationToken.None);

		// One more distinct icon overflows the bound by exactly one entry.
		await resources.ResolveAsync(IconPack(Guid.NewGuid()), CancellationToken.None);

		var callsBeforeReResolve = source.GetImageCallCount;

		// Checked as two separate reads, not just a combined total: a plain FIFO cache would evict index 0
		// instead (the touch would not have moved it), producing the same combined call count but
		// attributed to the other id - only checking each index on its own actually tells the two apart.
		await resources.ResolveAsync(IconPack(ids[0]), CancellationToken.None);
		var callsAfterIndex0 = source.GetImageCallCount;
		await resources.ResolveAsync(IconPack(ids[1]), CancellationToken.None);
		var callsAfterIndex1 = source.GetImageCallCount;

		Assert.Multiple(() =>
		{
			Assert.That(callsAfterIndex0,
				Is.EqualTo(callsBeforeReResolve),
				"index 0 was touched right before the overflow, so it must still be cached");
			Assert.That(callsAfterIndex1,
				Is.EqualTo(callsAfterIndex0 + 1),
				"index 1 was never touched, so it - not index 0 - must be the one evicted");
		});
	}

	[Test]
	public async Task An_animation_too_large_for_a_UI_resource_is_drawn_from_its_smaller_rendition()
	{
		var (resources, source, store) = Create();
		var iconId = Guid.NewGuid();
		var oversized = new byte[ProtocolLimits.MaxUiResourceBytes + 1];
		var smaller = "small-animated-gif"u8.ToArray();
		source.OnGetRendition = (_, size, _) => Task.FromResult<WidgetIconImage?>(
			new WidgetIconImage(new MemoryStream(size == 256 ? oversized : smaller), "image/gif"));

		var resource = await resources.ResolveAsync(IconPack(iconId), CancellationToken.None);

		Assert.That(resource, Is.Not.Null, "a heavy animation must still draw, just at a smaller rendition");
		Assert.Multiple(() =>
		{
			Assert.That(store.TryGet(resource!.ResourceId, out var content), Is.True);
			Assert.That(content.Content.ToArray(), Is.EqualTo(smaller));
			Assert.That(source.Requests, Is.EqualTo(new[] { (256, false), (128, false) }));
		});
	}

	[Test]
	public async Task An_animation_too_large_at_every_rendition_is_drawn_as_its_first_frame()
	{
		var (resources, source, store) = Create();
		var iconId = Guid.NewGuid();
		var oversized = new byte[ProtocolLimits.MaxUiResourceBytes + 1];
		var firstFrame = "first-frame-png"u8.ToArray();
		source.OnGetRendition = (_, _, staticFrame) => Task.FromResult<WidgetIconImage?>(staticFrame
			? new WidgetIconImage(new MemoryStream(firstFrame), "image/png")
			: new WidgetIconImage(new MemoryStream(oversized), "image/gif"));

		var resource = await resources.ResolveAsync(IconPack(iconId), CancellationToken.None);

		Assert.That(resource, Is.Not.Null, "the picked icon must show even when its motion cannot");
		Assert.Multiple(() =>
		{
			Assert.That(resource!.MediaType, Is.EqualTo("image/png"));
			Assert.That(store.TryGet(resource.ResourceId, out var content), Is.True);
			Assert.That(content.Content.ToArray(), Is.EqualTo(firstFrame));
		});
	}

	[Test]
	public async Task An_icon_that_fits_no_rendition_at_all_yields_no_image_instead_of_faulting()
	{
		var (resources, source, _) = Create();
		var oversized = new byte[ProtocolLimits.MaxUiResourceBytes + 1];
		source.OnGetRendition = (_, _, _) => Task.FromResult<WidgetIconImage?>(
			new WidgetIconImage(new MemoryStream(oversized), "image/png"));

		var resource = await resources.ResolveAsync(IconPack(Guid.NewGuid()), CancellationToken.None);

		Assert.That(resource, Is.Null);
	}
}
