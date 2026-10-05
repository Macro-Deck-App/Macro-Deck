using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Infrastructure.Twitch;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

[TestFixture]
internal sealed class TwitchChatImageCacheTests
{
	private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

	private FakeTimeProvider _time = null!;
	private UiResourceStore _store = null!;
	private ImageHandler _handler = null!;
	private TwitchChatImageCache _cache = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_store = new UiResourceStore();
		_handler = new ImageHandler();
		_cache = new TwitchChatImageCache(_handler, _store, _time, Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_cache.Dispose();
		_handler.Dispose();
	}

	[Test]
	public async Task An_emote_is_fetched_from_twitch_and_served_as_a_ui_resource()
	{
		var resolved = new List<TwitchChatImage>();
		_cache.Resolved += (_, image) => resolved.Add(image);

		await FetchAsync(TwitchChatImage.Emote("25"));

		var resource = _cache.Find(TwitchChatImage.Emote("25"));
		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requested.Single().ToString(),
				Is.EqualTo("https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0"));
			Assert.That(resolved, Is.EqualTo(new[] { TwitchChatImage.Emote("25") }));
			Assert.That(resource, Is.Not.Null);
			Assert.That(_store.TryGet(resource!.ResourceId, out var content), Is.True);
			Assert.That(content.MediaType, Is.EqualTo("image/png"));
		});
	}

	[TestCase("http://static-cdn.jtvnw.net/badges/v1/abc/2")]
	[TestCase("https://evil.example/badges/v1/abc/2")]
	[TestCase("https://static-cdn.jtvnw.net.evil.example/badges/v1/abc/2")]
	[TestCase("https://user@static-cdn.jtvnw.net/badges/v1/abc/2")]
	[TestCase("https://static-cdn.jtvnw.net:8443/badges/v1/abc/2")]
	public async Task Nothing_outside_twitchs_image_host_is_ever_requested(string url)
	{
		await FetchAsync(TwitchChatImage.Badge("subscriber", "12", url));

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requested, Is.Empty);
			Assert.That(_cache.Find(TwitchChatImage.Badge("subscriber", "12", url)), Is.Null);
		});
	}

	[TestCase("../25")]
	[TestCase("25/../../x")]
	[TestCase("")]
	public async Task A_malformed_emote_id_is_never_requested(string emoteId)
	{
		await FetchAsync(TwitchChatImage.Emote(emoteId));

		Assert.That(_handler.Requested, Is.Empty);
	}

	[Test]
	public async Task An_overlong_emote_id_is_never_requested()
	{
		await FetchAsync(TwitchChatImage.Emote(new string('a', 65)));

		Assert.That(_handler.Requested, Is.Empty);
	}

	[Test]
	public async Task A_redirect_is_not_followed()
	{
		_handler.Respond = _ =>
		{
			var response = new HttpResponseMessage(HttpStatusCode.Found);
			response.Headers.Location = new Uri("https://evil.example/x.png");
			return response;
		};

		await FetchAsync(TwitchChatImage.Emote("25"));

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requested, Has.Count.EqualTo(1));
			Assert.That(_cache.Find(TwitchChatImage.Emote("25")), Is.Null);
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task An_image_over_half_a_mebibyte_is_refused(bool declaresLength)
	{
		var oversized = new byte[(512 * 1024) + 1];
		_png.CopyTo(oversized, 0);
		_handler.Respond = _ => Ok(oversized, declaresLength);

		await FetchAsync(TwitchChatImage.Emote("25"));

		Assert.That(_cache.Find(TwitchChatImage.Emote("25")), Is.Null);
	}

	[Test]
	public async Task Something_that_is_not_a_png_gif_or_webp_is_refused_whatever_it_claims()
	{
		_handler.Respond = _ =>
		{
			var response = Ok(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));
			response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
			return response;
		};

		await FetchAsync(TwitchChatImage.Emote("25"));

		Assert.That(_cache.Find(TwitchChatImage.Emote("25")), Is.Null);
	}

	[Test]
	public async Task A_failed_image_is_not_asked_for_again_for_ten_minutes()
	{
		_handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
		await FetchAsync(TwitchChatImage.Emote("25"));

		await FetchAsync(TwitchChatImage.Emote("25"));
		var withinWindow = _handler.Requested.Count;

		_time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
		await FetchAsync(TwitchChatImage.Emote("25"));

		Assert.Multiple(() =>
		{
			Assert.That(withinWindow, Is.EqualTo(1));
			Assert.That(_handler.Requested, Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task The_least_recently_used_image_is_evicted_and_removed_from_the_resource_store()
	{
		var first = TwitchChatImage.Emote("e0");
		await FetchAsync(first);
		var firstId = _cache.Find(first)!.ResourceId;

		for (var index = 1; index <= TwitchChatImageCache.MaxEntries; index++)
		{
			_cache.Request(TwitchChatImage.Emote("e" + index));
		}

		await IdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_cache.Count, Is.EqualTo(TwitchChatImageCache.MaxEntries));
			Assert.That(_cache.Find(first), Is.Null);
			Assert.That(_store.TryGet(firstId, out _), Is.False);
		});
	}

	[Test]
	public async Task An_image_still_shown_in_chat_is_never_evicted()
	{
		var shown = TwitchChatImage.Emote("e0");
		var notShown = TwitchChatImage.Emote("e1");
		await FetchAsync(shown);
		await FetchAsync(notShown);
		_cache.Pin([shown]);

		for (var index = 2; index <= TwitchChatImageCache.MaxEntries; index++)
		{
			_cache.Request(TwitchChatImage.Emote("e" + index));
		}

		await IdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_cache.Find(shown), Is.Not.Null);
			Assert.That(_cache.Find(notShown), Is.Null);
		});
	}

	[Test]
	public async Task When_everything_cached_is_on_screen_new_images_are_not_fetched()
	{
		var shown = new List<TwitchChatImage>();

		for (var index = 0; index < TwitchChatImageCache.MaxEntries; index++)
		{
			shown.Add(TwitchChatImage.Emote("e" + index));
			_cache.Request(shown[^1]);
		}

		await IdleAsync();
		_cache.Pin(shown);
		var before = _handler.Requested.Count;

		await FetchAsync(TwitchChatImage.Emote("extra"));

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requested, Has.Count.EqualTo(before));
			Assert.That(_cache.Find(TwitchChatImage.Emote("extra")), Is.Null);
			Assert.That(shown.All(image => _cache.Find(image) is not null), Is.True);
		});
	}

	private async Task FetchAsync(TwitchChatImage image)
	{
		_cache.Request(image);
		await IdleAsync();
	}

	private async Task IdleAsync()
	{
		var deadline = DateTime.UtcNow.AddSeconds(30);

		while (_cache.InFlightCount > 0)
		{
			Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the image fetches never finished");
			await Task.Delay(5);
		}
	}

	private static HttpResponseMessage Ok(byte[] body, bool declaresLength = true)
	{
		HttpContent content = declaresLength
			? new ByteArrayContent(body)
			: new StreamContent(new UnknownLengthStream(body));

		return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
	}

	private sealed class ImageHandler : HttpMessageHandler
	{
		private readonly ConcurrentQueue<Uri> _requested = new();

		public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Ok(_png);

		public IReadOnlyList<Uri> Requested => [.. _requested];

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			_requested.Enqueue(request.RequestUri!);
			var response = Respond(request);
			response.RequestMessage = request;
			return Task.FromResult(response);
		}
	}

	private sealed class UnknownLengthStream : MemoryStream
	{
		public UnknownLengthStream(byte[] buffer)
			: base(buffer)
		{
		}

		public override bool CanSeek => false;
	}
}
