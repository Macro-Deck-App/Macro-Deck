using System.Net;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Infrastructure.Twitch;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Stats;

[TestFixture]
internal sealed class TwitchStreamThumbnailCacheTests
{
	private const string Url = "https://static-cdn.jtvnw.net/previews-ttv/live_user_streamer-440x248.jpg";

	private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

	private FakeTimeProvider _time = null!;
	private UiResourceStore _store = null!;
	private Handler _handler = null!;
	private TwitchStreamThumbnailCache _cache = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_store = new UiResourceStore();
		_handler = new Handler();
		_cache = new TwitchStreamThumbnailCache(_handler, _store, _time, Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_cache.Dispose();
		_handler.Dispose();
	}

	[Test]
	public async Task A_thumbnail_is_fetched_and_served_as_a_jpeg_resource()
	{
		await TrackAsync("111", Url);

		var resource = _cache.Find("111");

		Assert.Multiple(() =>
		{
			Assert.That(resource, Is.Not.Null);
			Assert.That(_store.TryGet(resource!.ResourceId, out var content), Is.True);
			Assert.That(content.MediaType, Is.EqualTo("image/jpeg"));
		});
	}

	[TestCase("http://static-cdn.jtvnw.net/previews-ttv/a.jpg")]
	[TestCase("https://evil.example/previews-ttv/a.jpg")]
	[TestCase("https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0")]
	[TestCase("https://static-cdn.jtvnw.net:8443/previews-ttv/a.jpg")]
	public async Task A_url_outside_the_twitch_preview_path_is_never_requested(string url)
	{
		await TrackAsync("111", url);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requests, Is.Zero);
			Assert.That(_cache.Find("111"), Is.Null);
		});
	}

	[Test]
	public async Task A_response_that_is_not_a_jpeg_is_refused()
	{
		_handler.Body = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

		await TrackAsync("111", Url);

		Assert.That(_cache.Find("111"), Is.Null);
	}

	[Test]
	public async Task A_redirect_is_not_followed()
	{
		_handler.Status = HttpStatusCode.Found;

		await TrackAsync("111", Url);

		Assert.That(_cache.Find("111"), Is.Null);
	}

	[Test]
	public async Task The_same_url_is_refetched_only_after_the_refresh_interval()
	{
		await TrackAsync("111", Url);
		await TrackAsync("111", Url);
		_time.Advance(TwitchStreamThumbnailCache.RefreshInterval + TimeSpan.FromSeconds(1));
		await TrackAsync("111", Url);

		Assert.That(_handler.Requests, Is.EqualTo(2));
	}

	[Test]
	public async Task Going_offline_removes_the_resource()
	{
		await TrackAsync("111", Url);
		var id = _cache.Find("111")!.ResourceId;

		_cache.Track("111", null);

		Assert.Multiple(() =>
		{
			Assert.That(_cache.Find("111"), Is.Null);
			Assert.That(_store.TryGet(id, out _), Is.False);
		});
	}

	private async Task TrackAsync(string userId, string url)
	{
		var changed = new TaskCompletionSource();
		var requestsBefore = _handler.Requests;
		void OnChanged(object? sender, string id) => changed.TrySetResult();

		_cache.Changed += OnChanged;
		_cache.Track(userId, url);

		await Task.WhenAny(changed.Task, Task.Delay(300));
		await Task.Delay(requestsBefore == _handler.Requests && _handler.Requests == 0 ? 50 : 0);

		_cache.Changed -= OnChanged;
	}

	private sealed class Handler : HttpMessageHandler
	{
		private int _requests;

		public int Requests => _requests;

		public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

		public byte[] Body { get; set; } = _jpeg;

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _requests);

			return Task.FromResult(new HttpResponseMessage(Status)
			{
				Content = new ByteArrayContent(Body),
				RequestMessage = request,
			});
		}
	}
}
