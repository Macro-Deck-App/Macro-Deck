using System.Net;
using System.Text;
using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Infrastructure.Announcements;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Announcements;

[TestFixture]
internal sealed class PlatformAnnouncementClientTests
{
	private static readonly StorePlatformOptions Options = new() { BaseUrl = new Uri("https://platform.test/") };

	[Test]
	public async Task The_latest_announcement_is_read_anonymously_from_the_public_endpoint()
	{
		var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """
			{
			  "number": 4,
			  "title": "Macro Deck 3 is here",
			  "content": "## What's new",
			  "publishedAt": "2026-09-28T15:58:49.672+00:00",
			  "updatedAt": "2026-09-28T16:08:13.377+00:00"
			}
			"""));
		using var client = Client(handler);

		var result = await client.GetLatest(default);

		var request = handler.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
			Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://platform.test/api/v1/public/announcements/latest")));
			Assert.That(request.Headers.Authorization, Is.Null);
			Assert.That(result, Is.EqualTo(new AnnouncementFetch.Published(new Announcement(4,
				"Macro Deck 3 is here",
				"## What's new",
				new DateTimeOffset(2026, 9, 28, 15, 58, 49, 672, TimeSpan.Zero),
				new DateTimeOffset(2026, 9, 28, 16, 8, 13, 377, TimeSpan.Zero)))));
		});
	}

	[Test]
	public async Task No_content_means_nothing_is_published()
	{
		using var client = Client(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));

		Assert.That(await client.GetLatest(default), Is.InstanceOf<AnnouncementFetch.NonePublished>());
	}

	[TestCase(HttpStatusCode.InternalServerError)]
	[TestCase(HttpStatusCode.TooManyRequests)]
	[TestCase(HttpStatusCode.Found)]
	public async Task A_non_success_answer_is_a_silent_failure(HttpStatusCode status)
	{
		using var client = Client(new ScriptedHandler(_ => new HttpResponseMessage(status)));

		Assert.That(await client.GetLatest(default), Is.InstanceOf<AnnouncementFetch.Failed>());
	}

	[TestCase("not json")]
	[TestCase("""{ "number": 0, "title": "t", "content": "c", "publishedAt": "2026-09-28T15:58:49Z" }""")]
	[TestCase("""{ "number": 3, "content": "c", "publishedAt": "2026-09-28T15:58:49Z" }""")]
	public async Task A_malformed_answer_is_a_silent_failure(string body)
	{
		using var client = Client(new ScriptedHandler(_ => Json(HttpStatusCode.OK, body)));

		Assert.That(await client.GetLatest(default), Is.InstanceOf<AnnouncementFetch.Failed>());
	}

	[Test]
	public async Task An_oversized_answer_is_a_silent_failure()
	{
		var content = new string('a', (int)PlatformAnnouncementClient.MaximumResponseBytes + 1);
		using var client = Client(new ScriptedHandler(_ => Json(HttpStatusCode.OK,
			$$"""{ "number": 3, "title": "t", "content": "{{content}}", "publishedAt": "2026-09-28T15:58:49Z" }""")));

		Assert.That(await client.GetLatest(default), Is.InstanceOf<AnnouncementFetch.Failed>());
	}

	[Test]
	public async Task A_transport_error_is_a_silent_failure()
	{
		using var client = Client(new ScriptedHandler(_ => throw new HttpRequestException("offline")));

		Assert.That(await client.GetLatest(default), Is.InstanceOf<AnnouncementFetch.Failed>());
	}

	private static PlatformAnnouncementClient Client(ScriptedHandler handler)
		=> new(handler, Options, new LoggerConfiguration().CreateLogger());

	private static HttpResponseMessage Json(HttpStatusCode status, string body)
		=> new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
	{
		public List<HttpRequestMessage> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(answer(request));
		}
	}
}
