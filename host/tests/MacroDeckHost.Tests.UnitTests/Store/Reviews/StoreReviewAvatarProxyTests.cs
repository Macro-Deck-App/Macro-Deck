using System.Net;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Http;

namespace MacroDeckHost.Tests.UnitTests.Store.Reviews;

[TestFixture]
internal sealed class StoreReviewAvatarProxyTests
{
	private const string Trusted = "https://auth.macro-deck.app/assets/v1/org/users/1/avatar";

	private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];

	[Test]
	public async Task A_trusted_raster_avatar_is_served_with_the_type_its_bytes_have()
	{
		using var proxy = new StoreReviewAvatarProxy(new FakeHttpMessageHandler(_png, contentType: "text/html"),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var avatar = await proxy.Fetch(Trusted, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(avatar?.ContentType, Is.EqualTo("image/png"));
			Assert.That(avatar?.Content, Is.EqualTo(_png));
		});
	}

	[TestCase("https://tracker.example/assets/pixel.png")]
	[TestCase("http://auth.macro-deck.app/assets/v1/avatar")]
	[TestCase("https://auth.macro-deck.app/oauth/v2/authorize")]
	[TestCase(null)]
	public async Task An_avatar_outside_the_issuers_assets_is_never_fetched(string? source)
	{
		var handler = new CountingHandler(_png);
		using var proxy = new StoreReviewAvatarProxy(handler, TimeProvider.System, Serilog.Core.Logger.None);

		var avatar = await proxy.Fetch(source, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(avatar, Is.Null);
			Assert.That(handler.Calls, Is.Zero);
		});
	}

	[Test]
	public async Task An_svg_avatar_is_refused()
	{
		using var proxy = new StoreReviewAvatarProxy(
			new FakeHttpMessageHandler("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>"u8.ToArray(),
				contentType: "image/png"),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		Assert.That(await proxy.Fetch(Trusted, CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task A_redirect_is_not_followed()
	{
		using var proxy = new StoreReviewAvatarProxy(new FakeHttpMessageHandler(status: HttpStatusCode.Redirect,
				headers: [("Location", "https://tracker.example/pixel.png")]),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		Assert.That(await proxy.Fetch(Trusted, CancellationToken.None), Is.Null);
	}

	private sealed class CountingHandler(byte[] body) : HttpMessageHandler
	{
		public int Calls { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
		}
	}
}
