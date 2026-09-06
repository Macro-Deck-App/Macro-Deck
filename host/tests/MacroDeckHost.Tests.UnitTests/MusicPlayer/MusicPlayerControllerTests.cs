using System.Globalization;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.MusicPlayer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
public class MusicPlayerControllerTests
{
	private const string ArtworkId = "a1b2c3d4e5f60718";
	private const string InstanceId = "spotify::default";

	private sealed class FakeArtworkService : IMusicPlayerArtworkService
	{
		private readonly ArtworkImageResult? _image;

		public FakeArtworkService(ArtworkImageResult? image)
		{
			_image = image;
		}

		public int ImageCalls { get; private set; }

		public string GetETag(string artworkId, int? size)
			=> $"\"{artworkId}-{(size is null ? "master" : size.Value.ToString(CultureInfo.InvariantCulture))}\"";

		public Task<ArtworkImageResult?> GetImage(string instanceId,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			ImageCalls++;
			return Task.FromResult(_image);
		}
	}

	private static MusicPlayerController CreateController(FakeArtworkService artwork)
	{
		var controller = new MusicPlayerController(artwork);
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
		return controller;
	}

	private static FakeArtworkService WithArtwork()
		=> new(new ArtworkImageResult([1, 2, 3, 4], "image/webp", $"\"{ArtworkId}-master\""));

	// The bytes behind an artwork id never change, so a client that has them must not have to
	// revalidate - not even with a conditional request (issue #421).
	[Test]
	public async Task GetArtwork_serves_the_image_with_immutable_caching()
	{
		var artwork = WithArtwork();
		var controller = CreateController(artwork);

		var result = await controller.GetArtwork(ArtworkId, InstanceId, null, CancellationToken.None);

		Assert.That(result, Is.InstanceOf<FileContentResult>());
		Assert.Multiple(() =>
		{
			Assert.That(((FileContentResult)result).ContentType, Is.EqualTo("image/webp"));
			Assert.That(controller.Response.Headers.CacheControl.ToString(),
				Is.EqualTo("public, max-age=31536000, immutable"));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.EqualTo($"\"{ArtworkId}-master\""));
			Assert.That(artwork.ImageCalls, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task GetArtwork_returns_304_without_resolving_the_artwork_when_the_etag_matches()
	{
		var artwork = WithArtwork();
		var controller = CreateController(artwork);
		controller.Request.Headers.IfNoneMatch = artwork.GetETag(ArtworkId, null);

		var result = await controller.GetArtwork(ArtworkId, InstanceId, null, CancellationToken.None);

		Assert.That(result, Is.InstanceOf<StatusCodeResult>());
		Assert.Multiple(() =>
		{
			Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
			Assert.That(artwork.ImageCalls, Is.Zero, "a conditional request must not touch the provider");
		});
	}

	[Test]
	public async Task GetArtwork_returns_404_with_no_store_when_the_artwork_is_unknown()
	{
		var controller = CreateController(new FakeArtworkService(null));

		var result = await controller.GetArtwork(ArtworkId, InstanceId, null, CancellationToken.None);

		Assert.That(result, Is.InstanceOf<NotFoundResult>());
		Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
	}
}
