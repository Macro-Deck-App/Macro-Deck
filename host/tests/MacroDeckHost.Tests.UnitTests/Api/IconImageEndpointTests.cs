using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Tests.UnitTests.Icons;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Api;

[TestFixture]
public class IconImageEndpointTests
{
	private static readonly byte[] _original = [1, 1, 1];
	private static readonly byte[] _replacement = [2, 2, 2];

	private IconTestHarness _harness = null!;
	private IconService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_service = new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			_harness.Coalescer,
			_harness.Mediator,
			new IconPackOwnerRegistry([]));
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task A_url_carrying_the_version_from_the_icon_listing_is_cached_for_good()
	{
		var icon = await AddReadyIcon(_original);
		var version = IconMapper.ToDto(icon).ContentHash;
		var controller = Controller();

		var result = await controller.GetImage(icon.Id.ToString(), 128, version, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(version, Is.Not.Null.And.Not.Empty, "the listing must give a client a version to ask for");
			Assert.That(BytesOf(result), Is.EqualTo(_original));
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Does.Contain("immutable"));
		});
	}

	[Test]
	public async Task Replacing_an_icons_bytes_under_the_same_id_gives_it_a_new_url_and_stops_the_old_one_caching()
	{
		var icon = await AddReadyIcon(_original);
		var before = IconMapper.ToDto(icon).ContentHash;
		await Replace(icon, _replacement);
		var after = IconMapper.ToDto(_harness.Cache.GetIconById(icon.Id)!).ContentHash;

		var stale = Controller();
		var staleResult = await stale.GetImage(icon.Id.ToString(), 128, before, CancellationToken.None);
		var current = Controller();
		var currentResult = await current.GetImage(icon.Id.ToString(), 128, after, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(after, Is.Not.EqualTo(before));
			Assert.That(BytesOf(staleResult), Is.EqualTo(_replacement), "an old url is answered with today's bytes");
			Assert.That(stale.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
			Assert.That(BytesOf(currentResult), Is.EqualTo(_replacement));
			Assert.That(current.Response.Headers.CacheControl.ToString(), Does.Contain("immutable"));
		});
	}

	[Test]
	public async Task A_request_without_a_version_still_works_and_revalidates_so_a_replacement_shows()
	{
		var icon = await AddReadyIcon(_original);

		var first = Controller();
		var firstResult = await first.GetImage(icon.Id.ToString(), 128, null, CancellationToken.None);
		var firstETag = first.Response.Headers.ETag.ToString();

		var unchanged = Controller(ifNoneMatch: firstETag);
		var unchangedResult = await unchanged.GetImage(icon.Id.ToString(), 128, null, CancellationToken.None);

		await Replace(icon, _replacement);
		var replaced = Controller(ifNoneMatch: firstETag);
		var replacedResult = await replaced.GetImage(icon.Id.ToString(), 128, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(BytesOf(firstResult), Is.EqualTo(_original));
			Assert.That(first.Response.Headers.CacheControl.ToString(), Does.Not.Contain("immutable"));
			Assert.That(first.Response.Headers.CacheControl.ToString(), Does.Contain("no-cache"));
			Assert.That(firstETag, Is.Not.Empty);
			Assert.That((unchangedResult as StatusCodeResult)?.StatusCode,
				Is.EqualTo(StatusCodes.Status304NotModified));
			Assert.That(unchanged.Response.Headers.CacheControl.ToString(), Does.Contain("no-cache"));
			Assert.That(BytesOf(replacedResult), Is.EqualTo(_replacement));
		});
	}

	[Test]
	public async Task An_empty_version_from_a_client_that_does_not_know_it_yet_revalidates()
	{
		var icon = await AddReadyIcon(_original);
		var controller = Controller();

		var result = await controller.GetImage(icon.Id.ToString(), 128, string.Empty, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(BytesOf(result), Is.EqualTo(_original));
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache"));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.Not.Empty);
		});
	}

	[Test]
	public async Task A_client_without_webp_support_gets_the_replaced_picture_under_the_same_id()
	{
		var icon = await AddReadyIcon(await WebpOf(Color.Red));
		var first = Controller(accept: "image/png");
		var firstResult = await first.GetImage(icon.Id.ToString(), 128, null, CancellationToken.None);

		await Replace(icon, await WebpOf(Color.Blue));
		var second = Controller(accept: "image/png");
		var secondResult = await second.GetImage(icon.Id.ToString(), 128, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(CentrePixelOf(firstResult), Is.EqualTo(Color.Red.ToPixel<Rgba32>()));
			Assert.That(CentrePixelOf(secondResult), Is.EqualTo(Color.Blue.ToPixel<Rgba32>()));
			Assert.That(second.Response.Headers.ETag.ToString(), Is.Not.EqualTo(first.Response.Headers.ETag.ToString()));
		});
	}

	[Test]
	public async Task An_unknown_icon_is_not_found_and_not_stored()
	{
		var controller = Controller();

		var result = await controller.GetImage(Guid.NewGuid().ToString(), 128, "sha256:abc", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
		});
	}

	private static async Task<byte[]> WebpOf(Color color)
	{
		using var image = new Image<Rgba32>(8, 8, color.ToPixel<Rgba32>());
		using var buffer = new MemoryStream();
		await image.SaveAsWebpAsync(buffer, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
		return buffer.ToArray();
	}

	private static Rgba32 CentrePixelOf(IActionResult result)
	{
		using var image = Image.Load<Rgba32>(BytesOf(result));
		return image[image.Width / 2, image.Height / 2];
	}

	private IconsController Controller(string? ifNoneMatch = null, string accept = "image/webp")
	{
		var httpContext = new DefaultHttpContext();
		httpContext.Request.Headers.Accept = accept;
		if (ifNoneMatch is not null)
		{
			httpContext.Request.Headers.IfNoneMatch = ifNoneMatch;
		}

		return new IconsController(null!, null!, null!, null!, null!, null!, null!, null!, _service, null!)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext }
		};
	}

	private async Task<IconEntity> AddReadyIcon(byte[] content)
	{
		var pack = await _harness.CreatePack();
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = "icon",
			CreatedAt = DateTime.UtcNow,
			ProcessingState = IconProcessingState.Ready,
			AvailableSizes = [128]
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		await Replace(icon, content);
		return icon;
	}

	private async Task Replace(IconEntity icon, byte[] content)
	{
		await _harness.Storage.WriteVariant(icon.PackId, icon.Id, "128", content, CancellationToken.None);
		await _harness.Storage.WriteVariant(icon.PackId, icon.Id, IconVariants.Master, content, CancellationToken.None);
		icon.MasterContentHash = MasterContentHash.Compute(content).Value;
		await _harness.Cache.UpdateIcon(icon);
	}

	private static byte[] BytesOf(IActionResult result)
	{
		var stream = ((FileStreamResult)result).FileStream;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		stream.Dispose();
		return memory.ToArray();
	}
}
