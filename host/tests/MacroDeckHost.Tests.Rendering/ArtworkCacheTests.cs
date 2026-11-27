using System.Text.Json;
using MacroDeckHost.Rendering;
using MacroDeckHost.Rendering.Paint;

namespace MacroDeckHost.Tests.Rendering;

public sealed class ArtworkCacheTests
{
	private static byte[] Fixture(string name)
	{
		var file = Fixtures.Read(Path.Combine(Fixtures.RepoRoot,
			"ui-model",
			"fixtures",
			"animated-images",
			"fixtures.json"));
		var fixture = file.GetProperty("fixtures")
			.EnumerateArray()
			.Single(item => item.GetProperty("name").GetString() == name);
		return Convert.FromBase64String(fixture.GetProperty("bytes").GetString()!);
	}

	private static JsonElement ImageTree()
	{
		using var document = JsonDocument.Parse("""
												{"revision":1,"root":{"id":"i","type":"ui.image",
												"properties":{"size":{"basis":0.5},"source":{"resourceId":"app.example.spinner"}}}}
												""");
		return document.RootElement.Clone();
	}

	private static int DecodesWhileRendering(RenderResource resource, int renders, bool animated)
	{
		var key = ArtworkCache.KeyOf(resource);
		var decodes = 0;

		void Count(string decoded)
		{
			if (decoded == key)
			{
				Interlocked.Increment(ref decodes);
			}
		}

		ArtworkCache.Decoded += Count;
		try
		{
			var renderer = new TileRenderer(Fixtures.Fonts);
			var resources = new OneResource(resource);
			for (var index = 0; index < renders; index++)
			{
				var output = renderer.Render(Fixtures.Input(ImageTree(), 120, 120, 120) with
				{
					Resources = resources,
					AnimatedImages = animated,
					Now = Fixtures.FixedNow.AddMilliseconds(index * 40)
				});
				Assert.That(output.MissingResources, Is.Empty);
			}
		}
		finally
		{
			ArtworkCache.Decoded -= Count;
		}

		return decodes;
	}

	[Test]
	public void An_animated_picture_drawn_again_and_again_is_decoded_once()
	{
		var resource = new RenderResource(Fixture("gif-timing"), "image/gif", Guid.NewGuid().ToString("N"));

		Assert.That(DecodesWhileRendering(resource, 12, true), Is.EqualTo(1));
	}

	[Test]
	public void A_still_picture_drawn_again_and_again_is_decoded_once()
	{
		var resource = new RenderResource(Fixture("gif-plays-once"), "image/gif", Guid.NewGuid().ToString("N"));

		Assert.That(DecodesWhileRendering(resource, 6, false), Is.EqualTo(1));
	}

	[Test]
	public void The_same_bytes_handed_over_by_two_owners_are_decoded_once()
	{
		var bytes = Fixture("webp-timing");
		var hash = Guid.NewGuid().ToString("N");
		var firstOwner = new object();
		var secondOwner = new object();
		var first = new RenderResource(bytes, "image/webp", hash, firstOwner);
		var second = new RenderResource(bytes.ToArray(), "image/webp", hash, secondOwner);

		var decodes = DecodesWhileRendering(first, 3, true) + DecodesWhileRendering(second, 3, true);

		Assert.That(decodes, Is.EqualTo(1));
		GC.KeepAlive(firstOwner);
		GC.KeepAlive(secondOwner);
	}

	private sealed class OneResource : IRenderResources
	{
		private readonly RenderResource _resource;

		public OneResource(RenderResource resource)
		{
			_resource = resource;
		}

		public RenderResource? TryGet(string resourceId, int? displayPx)
		{
			return _resource;
		}
	}
}
