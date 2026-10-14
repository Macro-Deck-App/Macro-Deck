using System.Net;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Integrations.Steam.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamArtworkCacheTests
{
	private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

	private UiResourceStore _resources = null!;
	private FakeTimeProvider _time = null!;
	private StubHandler _handler = null!;
	private SteamArtworkCache _cache = null!;

	[SetUp]
	public void SetUp()
	{
		_resources = new UiResourceStore();
		_time = new FakeTimeProvider();
		_handler = new StubHandler();
		_cache = new SteamArtworkCache(new HttpClient(_handler),
			new BuiltInUiResources("app.macro-deck.steam", _resources),
			_time);
	}

	[TearDown]
	public void TearDown()
	{
		_cache.Dispose();
		_handler.Dispose();
	}

	[Test]
	public async Task The_cover_comes_from_the_steam_cdn_and_becomes_a_resource()
	{
		_handler.Respond = _ => Ok(_jpeg);

		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _cache.Find(730) is not null, "no cover was fetched");

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requests.Single(),
				Is.EqualTo(new Uri("https://cdn.cloudflare.steamstatic.com/steam/apps/730/library_600x900.jpg")));
			Assert.That(_resources.TryGet(_cache.Find(730)!.ResourceId, out var content), Is.True);
			Assert.That(content.MediaType, Is.EqualTo("image/jpeg"));
		});
	}

	[Test]
	public async Task After_the_integration_is_reenabled_with_a_new_registry_the_covers_are_registered_there()
	{
		_handler.Respond = _ => Ok(_jpeg);
		var first = new BuiltInUiResources("app.macro-deck.steam", _resources);
		using var cache = new SteamArtworkCache(new HttpClient(_handler, false), first, _time);
		cache.Track(730);
		await CalendarTree.WaitForAsync(() => cache.Find(730) is not null, "no cover was fetched");

		first.Release();
		var second = new BuiltInUiResources("app.macro-deck.steam", _resources);
		cache.UseResources(second);
		cache.Track(730);

		await CalendarTree.WaitForAsync(() => cache.Find(730) is not null, "the cover was not fetched again");
		Assert.That(_resources.TryGet(cache.Find(730)!.ResourceId, out _), Is.True);
	}

	[Test]
	public async Task Listeners_are_told_when_a_cover_arrives()
	{
		_handler.Respond = _ => Ok(_jpeg);
		var told = 0;
		_cache.Changed += (_, _) => Interlocked.Increment(ref told);

		_cache.Track(730);

		await CalendarTree.WaitForAsync(() => told == 1, "the change was not announced");
	}

	[Test]
	public async Task A_redirect_is_not_followed_so_the_host_stays_pinned_to_the_cdn()
	{
		_handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Redirect)
		{
			Headers = { Location = new Uri("https://evil.example/cover.jpg") }
		};

		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _handler.Requests.Count == 1, "nothing was requested");
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(_cache.Find(730), Is.Null);
			Assert.That(_handler.Requests.Select(request => request.Host),
				Is.All.EqualTo(SteamArtworkCache.AllowedHost));
		});
	}

	[Test]
	public void The_production_client_is_built_with_redirects_turned_off()
	{
		using var handler = new SocketsHttpHandler();

		SteamArtworkCache.PinToTheCdn(handler);

		Assert.That(handler.AllowAutoRedirect, Is.False);
	}

	[Test]
	public async Task A_body_that_is_not_a_jpeg_is_not_registered()
	{
		_handler.Respond = _ => Ok("<html>not an image</html>"u8.ToArray());

		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _handler.Requests.Count == 1, "nothing was requested");
		await Task.Delay(50);

		Assert.That(_cache.Find(730), Is.Null);
	}

	[Test]
	public async Task An_oversized_image_is_dropped()
	{
		_handler.Respond = _ => Ok(new byte[SteamArtworkCache.MaxImageBytes + 1]
			.Select((_, i) => i < 3 ? (byte)0xFF : (byte)0)
			.ToArray());

		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _handler.Requests.Count == 1, "nothing was requested");
		await Task.Delay(50);

		Assert.That(_cache.Find(730), Is.Null);
	}

	[Test]
	public async Task A_cover_that_failed_is_not_requested_again_until_the_retry_interval_has_passed()
	{
		_handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _handler.Requests.Count == 1, "nothing was requested");
		await Task.Delay(50);

		_cache.Track(730);
		await Task.Delay(50);
		Assert.That(_handler.Requests, Has.Count.EqualTo(1));

		_time.Advance(SteamArtworkCache.RetryInterval + TimeSpan.FromSeconds(1));
		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _handler.Requests.Count == 2, "no retry happened");
	}

	[Test]
	public async Task A_cover_that_is_already_known_is_not_requested_again()
	{
		_handler.Respond = _ => Ok(_jpeg);
		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _cache.Find(730) is not null, "no cover was fetched");

		_cache.Track(730);
		await Task.Delay(50);

		Assert.That(_handler.Requests, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task The_cache_keeps_at_most_a_bounded_number_of_covers_and_frees_the_oldest()
	{
		_handler.Respond = _ => Ok(_jpeg);
		var total = SteamArtworkCache.MaxEntries + 5;

		for (var appId = 1; appId <= total; appId++)
		{
			_cache.Track(appId);
			await CalendarTree.WaitForAsync(() => _cache.Find(appId) is not null, $"cover {appId} was not fetched");
			_time.Now = _time.Now.AddSeconds(1);
		}

		Assert.Multiple(() =>
		{
			Assert.That(_cache.Find(1), Is.Null, "the oldest was evicted");
			Assert.That(_cache.Find(total), Is.Not.Null, "the newest is kept");
		});
	}

	[TestCase(0)]
	[TestCase(-3)]
	public void An_invalid_app_id_is_never_requested(int appId)
	{
		_cache.Track(appId);

		Assert.That(_handler.Requests, Is.Empty);
	}

	[Test]
	public void Only_the_steam_cdn_over_https_is_an_allowed_source()
	{
		Assert.Multiple(() =>
		{
			Assert.That(
				SteamArtworkCache.IsAllowed(new Uri("https://cdn.cloudflare.steamstatic.com/steam/apps/1/header.jpg")),
				Is.True);
			Assert.That(
				SteamArtworkCache.IsAllowed(new Uri("http://cdn.cloudflare.steamstatic.com/steam/apps/1/header.jpg")),
				Is.False);
			Assert.That(
				SteamArtworkCache.IsAllowed(new Uri("https://cdn.akamai.steamstatic.com/steam/apps/1/header.jpg")),
				Is.False);
			Assert.That(
				SteamArtworkCache.IsAllowed(
					new Uri("https://cdn.cloudflare.steamstatic.com.evil.example/steam/apps/1/header.jpg")),
				Is.False);
			Assert.That(SteamArtworkCache.IsAllowed(new Uri("https://cdn.cloudflare.steamstatic.com/other/1.jpg")),
				Is.False);
			Assert.That(
				SteamArtworkCache.IsAllowed(
					new Uri("https://user@cdn.cloudflare.steamstatic.com/steam/apps/1/header.jpg")),
				Is.False);
		});
	}

	[Test]
	public async Task Disposing_the_cache_frees_its_resources()
	{
		_handler.Respond = _ => Ok(_jpeg);
		_cache.Track(730);
		await CalendarTree.WaitForAsync(() => _cache.Find(730) is not null, "no cover was fetched");
		var id = _cache.Find(730)!.ResourceId;

		_cache.Dispose();

		Assert.That(_resources.TryGet(id, out _), Is.False);
	}

	private static HttpResponseMessage Ok(byte[] bytes)
	{
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
	}

	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly Lock _sync = new();
		private readonly List<Uri> _requests = [];

		public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
			_ => new HttpResponseMessage(HttpStatusCode.NotFound);

		public List<Uri> Requests
		{
			get
			{
				lock (_sync)
				{
					return [.. _requests];
				}
			}
		}

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			lock (_sync)
			{
				_requests.Add(request.RequestUri!);
			}

			return Task.FromResult(Respond(request));
		}
	}
}
