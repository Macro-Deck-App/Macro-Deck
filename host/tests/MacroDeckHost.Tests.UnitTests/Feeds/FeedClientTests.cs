using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Feeds;

[TestFixture]
internal sealed class FeedClientTests
{
	private const string FeedUrl = "https://news.example.com/feed.xml";

	private FakeFeedServer _server = null!;
	private FakeResolver _resolver = null!;
	private FeedClient _client = null!;

	[SetUp]
	public void SetUp()
	{
		_server = new FakeFeedServer();
		_resolver = new FakeResolver();
		_client = FeedsTestClients.Create(_server, _resolver);
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_server.Dispose();
	}

	[Test]
	public async Task A_repeated_request_sends_the_validators_and_keeps_the_feed_on_304()
	{
		_server.ServeXml(FeedUrl, FeedXml.Rss(), "\"v1\"");

		var first = await _client.FetchAsync(new FeedRequest(new Uri(FeedUrl), false), CancellationToken.None);
		var revalidate = new FeedRequest(new Uri(FeedUrl), false)
		{
			ETag = first.ETag,
			LastModified = "Mon, 01 Jun 2026 10:00:00 GMT"
		};
		var second = await _client.FetchAsync(revalidate, CancellationToken.None);

		var revalidation = _server.Requests[^1];
		Assert.Multiple(() =>
		{
			Assert.That(first.Status, Is.EqualTo(FeedFetchStatus.Ok));
			Assert.That(first.ETag, Is.EqualTo("\"v1\""));
			Assert.That(second.Status, Is.EqualTo(FeedFetchStatus.NotModified));
			Assert.That(second.ETag, Is.EqualTo("\"v1\""));
			Assert.That(revalidation.Headers.GetValues("If-None-Match"), Is.EqualTo(new[] { "\"v1\"" }));
			Assert.That(revalidation.Headers.GetValues("If-Modified-Since").Single(),
				Is.EqualTo("Mon, 01 Jun 2026 10:00:00 GMT"));
		});
	}

	[TestCase("http://127.0.0.1/feed")]
	[TestCase("http://[::1]/feed")]
	[TestCase("http://[::ffff:10.0.0.1]/feed")]
	[TestCase("http://169.254.169.254/latest/meta-data")]
	[TestCase("http://router.home/feed")]
	[TestCase("http://cgnat.example/feed")]
	public async Task A_local_or_private_address_is_refused_without_a_request(string url)
	{
		_resolver.Map("router.home", "192.168.1.1").Map("cgnat.example", "100.64.0.9");
		_server.ServeXml(url, FeedXml.Rss());

		var result = await _client.FetchAsync(new FeedRequest(new Uri(url), false), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(FeedErrorCodes.BlockedAddress));
			Assert.That(_server.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_redirect_into_a_private_network_is_refused()
	{
		_server.Redirect(FeedUrl, "http://10.0.0.5/admin/feed");
		_server.ServeXml("http://10.0.0.5/admin/feed", FeedXml.Rss());

		var result = await _client.FetchAsync(new FeedRequest(new Uri(FeedUrl), false), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(FeedErrorCodes.BlockedAddress));
			Assert.That(_server.Requests.Select(request => request.RequestUri!.AbsoluteUri),
				Is.EqualTo(new[] { FeedUrl }));
		});
	}

	[Test]
	public async Task A_feed_that_allows_the_local_network_reaches_it_and_follows_redirects()
	{
		_resolver.Map("nas.home", "192.168.1.20");
		_server.Redirect("http://nas.home/old.xml", "/feed.xml", HttpStatusCode.MovedPermanently);
		_server.ServeXml("http://nas.home/feed.xml", FeedXml.Rss());

		var result = await _client.FetchAsync(new FeedRequest(new Uri("http://nas.home/old.xml"), true),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FeedFetchStatus.Ok));
			Assert.That(result.FinalUrl, Is.EqualTo(new Uri("http://nas.home/feed.xml")));
		});
	}

	[Test]
	public async Task More_redirects_than_allowed_fail_with_the_redirect_status()
	{
		for (var hop = 0; hop <= FeedClient.MaxRedirects; hop++)
		{
			_server.Redirect($"https://news.example.com/{hop}", $"https://news.example.com/{hop + 1}");
		}

		var result = await _client.FetchAsync(new FeedRequest(new Uri("https://news.example.com/0"), false),
			CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo("http-302"));
	}

	[Test]
	public async Task A_rate_limited_answer_carries_its_retry_after()
	{
		_server.Serve(FeedUrl,
			_ =>
			{
				var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
				response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(7));
				return response;
			});

		var result = await _client.FetchAsync(new FeedRequest(new Uri(FeedUrl), false), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo("http-429"));
			Assert.That(result.RetryAfter, Is.EqualTo(TimeSpan.FromMinutes(7)));
		});
	}

	[Test]
	public async Task A_response_over_the_size_limit_is_refused()
	{
		_server.ServeXml(FeedUrl, FeedXml.Rss() + new string(' ', 4096));

		var request = new FeedRequest(new Uri(FeedUrl), false) { MaxBytes = 1024 };
		var result = await _client.FetchAsync(request, CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(FeedErrorCodes.TooLarge));
	}

	[Test]
	public void The_real_handler_connects_directly_and_follows_no_redirect_by_itself()
	{
		using var handler = new SocketsHttpHandler();

		FeedHttp.Configure(handler, false, new FakeResolver());

		Assert.Multiple(() =>
		{
			Assert.That(handler.UseProxy, Is.False);
			Assert.That(handler.AllowAutoRedirect, Is.False);
			Assert.That(handler.ConnectCallback, Is.Not.Null);
		});
	}

	[Test]
	public async Task The_connection_itself_refuses_a_loopback_address_unless_the_feed_allows_it()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		var serving = ServeOnceAsync(listener);
		var url = new Uri($"http://127.0.0.1:{port}/feed.xml");

		using var strict = new HttpClient(Handler(false));
		var refused = Assert.ThrowsAsync<HttpRequestException>(() => strict.GetAsync(url));

		using var local = new HttpClient(Handler(true));
		using var response = await local.GetAsync(url);
		await serving;

		Assert.Multiple(() =>
		{
			Assert.That(Chain(refused), Has.Some.InstanceOf<FeedAddressBlockedException>());
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[TestCase("10.1.2.3", true)]
	[TestCase("172.31.255.255", true)]
	[TestCase("192.168.0.10", true)]
	[TestCase("100.100.1.1", true)]
	[TestCase("0.0.0.0", true)]
	[TestCase("224.0.0.251", true)]
	[TestCase("::", true)]
	[TestCase("fe80::1", true)]
	[TestCase("fd12:3456::1", true)]
	[TestCase("ff02::1", true)]
	[TestCase("::ffff:127.0.0.1", true)]
	[TestCase("64:ff9b::a00:1", true)]
	[TestCase("93.184.216.34", false)]
	[TestCase("2606:2800:220:1:248:1893:25c8:1946", false)]
	public void Address_ranges_outside_the_public_internet_are_blocked(string address, bool blocked)
	{
		Assert.That(FeedAddressGuard.IsBlocked(IPAddress.Parse(address)), Is.EqualTo(blocked));
	}

	private static IEnumerable<Exception> Chain(Exception? exception)
	{
		for (var current = exception; current is not null; current = current.InnerException)
		{
			yield return current;
		}
	}

	private static SocketsHttpHandler Handler(bool allowLocalNetwork)
	{
		var handler = new SocketsHttpHandler();
		FeedHttp.Configure(handler, allowLocalNetwork, new FakeResolver());
		return handler;
	}

	private static async Task ServeOnceAsync(TcpListener listener)
	{
		using var client = await listener.AcceptTcpClientAsync();
		await using var stream = client.GetStream();
		var buffer = new byte[4096];
		_ = await stream.ReadAsync(buffer);
		var body = FeedXml.Rss();
		var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/rss+xml\r\nContent-Length: "
			+ $"{Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
		await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
	}
}
