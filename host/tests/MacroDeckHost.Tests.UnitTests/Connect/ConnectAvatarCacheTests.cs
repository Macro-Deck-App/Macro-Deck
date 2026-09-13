using System.Net;
using System.Net.Http.Headers;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Infrastructure.Connect;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Connect;

[TestFixture]
[NonParallelizable]
public class ConnectAvatarCacheTests
{
	private const string PictureA = "https://auth.macro-deck.app/assets/v1/org-1/users/user-a/avatar";
	private const string PictureB = "https://auth.macro-deck.app/assets/v1/org-1/users/user-b/avatar";

	private static readonly byte[] _first = [0x89, 0x50, 0x4E, 0x47, 1];
	private static readonly byte[] _second = [0x89, 0x50, 0x4E, 0x47, 2];

	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;
	private FakeConnectSessionService _session = null!;
	private StubHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_session = new FakeConnectSessionService { Current = FakeConnectSessionService.SignedIn(PictureA) };
		_handler = new StubHandler();
	}

	[TearDown]
	public void TearDown()
	{
		_handler.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[Test]
	public async Task A_replaced_picture_gets_a_new_version_and_one_change()
	{
		using var cache = Cache();
		var changes = 0;
		cache.VersionChanged += (_, _) => changes++;

		_handler.Respond = _ => Image(_first);
		await cache.Revalidate();
		var first = cache.Version;

		await cache.Revalidate();
		var unchanged = cache.Version;
		var changesAfterSameBytes = changes;

		_handler.Respond = _ => Image(_second);
		await cache.Revalidate();

		Assert.Multiple(async () =>
		{
			Assert.That(first, Is.Not.Null);
			Assert.That(unchanged, Is.EqualTo(first));
			Assert.That(changesAfterSameBytes, Is.EqualTo(1), "identical bytes are not a change");
			Assert.That(cache.Version, Is.Not.EqualTo(first));
			Assert.That(changes, Is.EqualTo(2));
			Assert.That(await Bytes(cache), Is.EqualTo(_second));
		});
	}

	[TestCase("unavailable")]
	[TestCase("unreachable")]
	[TestCase("redirect")]
	[TestCase("followed-redirect")]
	[TestCase("oversized")]
	[TestCase("svg")]
	public async Task A_failed_download_keeps_the_cached_picture(string failure)
	{
		using var cache = Cache();
		_handler.Respond = _ => Image(_first);
		await cache.Revalidate();
		var cached = cache.Version;

		var changes = 0;
		cache.VersionChanged += (_, _) => changes++;
		_handler.Respond = failure switch
		{
			"unavailable" => _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
			"unreachable" => _ => throw new HttpRequestException("connection refused"),
			"redirect" => _ => new HttpResponseMessage(HttpStatusCode.Found)
			{
				Headers = { Location = new Uri("https://attacker.example/avatar.png") }
			},
			"followed-redirect" => _ => Image(_second, requestUri: "https://attacker.example/avatar.png"),
			"oversized" => _ => Image(new byte[ConnectAvatarCache.MaxAvatarBytes + 1]),
			_ => _ => Image(_second, "image/svg+xml")
		};

		await cache.Revalidate();

		Assert.Multiple(async () =>
		{
			Assert.That(cache.Version, Is.EqualTo(cached));
			Assert.That(changes, Is.Zero);
			Assert.That(await Bytes(cache), Is.EqualTo(_first));
		});
	}

	[Test]
	public async Task A_removed_picture_is_dropped_and_reported()
	{
		using var cache = Cache();
		_handler.Respond = _ => Image(_first);
		await cache.Revalidate();

		var changes = 0;
		cache.VersionChanged += (_, _) => changes++;
		_handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
		await cache.Revalidate();

		Assert.Multiple(async () =>
		{
			Assert.That(cache.Version, Is.Null);
			Assert.That(changes, Is.EqualTo(1));
			Assert.That(await cache.GetAvatar(), Is.Null);
		});
	}

	[Test]
	public async Task Another_account_never_gets_the_previous_account_s_picture()
	{
		using var cache = Cache();
		_handler.Respond = _ => Image(_first);
		await cache.Revalidate();

		_session.Current = FakeConnectSessionService.SignedIn(PictureB, "sub-2");
		_handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

		Assert.Multiple(async () =>
		{
			Assert.That(cache.Version, Is.Null);
			Assert.That(await cache.GetAvatar(), Is.Null);
		});
	}

	[Test]
	public async Task The_cached_picture_survives_a_restart_without_a_download()
	{
		string? version;
		using (var cache = Cache())
		{
			_handler.Respond = _ => Image(_first);
			await cache.Revalidate();
			version = cache.Version;
		}

		var requests = _handler.Requests;
		using var restarted = Cache();

		Assert.Multiple(async () =>
		{
			Assert.That(restarted.Version, Is.EqualTo(version));
			Assert.That(await Bytes(restarted), Is.EqualTo(_first));
			Assert.That(_handler.Requests, Is.EqualTo(requests));
		});
	}

	[Test]
	public async Task The_first_request_fetches_the_picture_and_reports_its_version()
	{
		using var cache = Cache();
		var changes = 0;
		cache.VersionChanged += (_, _) => changes++;
		_handler.Respond = _ => Image(_first, "image/jpeg");

		var avatar = await cache.GetAvatar();

		Assert.Multiple(() =>
		{
			Assert.That(avatar!.ContentType, Is.EqualTo("image/jpeg"));
			Assert.That(cache.Version, Is.Not.Null);
			Assert.That(changes, Is.EqualTo(1));
		});
		await avatar!.Content.DisposeAsync();
	}

	[TestCase("https://attacker.example/assets/v1/org-1/users/user-a/avatar")]
	[TestCase("http://auth.macro-deck.app/assets/v1/org-1/users/user-a/avatar")]
	[TestCase("https://auth.macro-deck.app:8443/assets/v1/org-1/users/user-a/avatar")]
	[TestCase("https://auth.macro-deck.app/oauth/v2/userinfo")]
	[TestCase("https://auth.macro-deck.app/assets/../oauth/v2/userinfo")]
	public async Task A_picture_outside_the_issuer_s_assets_is_never_fetched(string pictureUrl)
	{
		using var cache = Cache();
		_session.Current = FakeConnectSessionService.SignedIn(pictureUrl);
		_handler.Respond = _ => Image(_first);

		var avatar = await cache.GetAvatar();
		await cache.Revalidate();

		Assert.Multiple(() =>
		{
			Assert.That(avatar, Is.Null);
			Assert.That(cache.Version, Is.Null);
			Assert.That(_handler.Requests, Is.Zero);
		});
	}

	private ConnectAvatarCache Cache() => new(_session, _paths, _handler, Log.Logger);

	private static async Task<byte[]?> Bytes(ConnectAvatarCache cache)
	{
		var avatar = await cache.GetAvatar();
		if (avatar is null)
		{
			return null;
		}

		await using var content = avatar.Content;
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer);

		return buffer.ToArray();
	}

	private static HttpResponseMessage Image(byte[] bytes, string contentType = "image/png", string? requestUri = null)
	{
		var content = new ByteArrayContent(bytes);
		content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

		var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
		if (requestUri is not null)
		{
			response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
		}

		return response;
	}

	private sealed class StubHandler : HttpMessageHandler
	{
		public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
			_ => new HttpResponseMessage(HttpStatusCode.NotFound);

		public int Requests { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Requests++;
			return Task.FromResult(Respond(request));
		}
	}
}
