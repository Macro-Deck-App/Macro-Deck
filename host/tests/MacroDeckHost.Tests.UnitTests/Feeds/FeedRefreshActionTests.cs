using System.Net;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Feeds;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Feeds;

[TestFixture]
internal sealed class FeedRefreshActionTests
{
	private const string NewsUrl = "https://news.example.com/feed.xml";
	private const string BlogUrl = "https://blog.example.com/feed.xml";

	private static readonly Guid _newsId = Guid.Parse("0b6f4b7e-0000-0000-0000-000000000021");
	private static readonly Guid _blogId = Guid.Parse("0b6f4b7e-0000-0000-0000-000000000022");

	private FakeFeedServer _server = null!;
	private FeedsIntegration _integration = null!;

	[SetUp]
	public async Task SetUp()
	{
		_server = new FakeFeedServer();
		_server.ServeXml(NewsUrl, FeedsIntegrationTests.News(FeedsIntegrationTests.First));
		_server.ServeXml(BlogUrl, FeedsIntegrationTests.News(FeedsIntegrationTests.Second));
		var time = new FakeTimeProvider();
		var context = new FeedsTestContext();
		context.AddFeed("News", NewsUrl, "news", id: _newsId);
		context.AddFeed("Blog", BlogUrl, "blog", id: _blogId);
		_integration = new FeedsIntegration(FeedsTestClients.Create(_server, null, time), time, false);
		await _integration.InitializeAsync(context);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _integration.ShutdownAsync();
		_integration.Dispose();
		_server.Dispose();
	}

	[Test]
	public async Task The_action_offers_an_optional_choice_of_the_configured_feeds()
	{
		var action = (IDynamicOptionsActionDefinition)Refresh();
		var parameter = action.Parameters.Single();

		var options = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = parameter.Name,
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(parameter.Type, Is.EqualTo(ActionParameterType.DynamicChoice));
			Assert.That(parameter.Required, Is.False);
			Assert.That(options.Options.Select(option => option.Value),
				Is.EquivalentTo(new[] { _newsId.ToString("D"), _blogId.ToString("D") }));
		});
	}

	[Test]
	public async Task Without_a_feed_every_feed_is_refreshed()
	{
		var result = await ExecuteAsync(new Dictionary<string, object>());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(RequestedUrls(), Is.EquivalentTo(new[] { NewsUrl, BlogUrl }));
			Assert.That(_integration.Feeds.Find(_blogId)!.Items.Select(item => item.Title),
				Is.EqualTo(new[] { "Second" }));
		});
	}

	[Test]
	public async Task A_chosen_feed_is_the_only_one_refreshed()
	{
		var result = await ExecuteAsync(new Dictionary<string, object> { ["feed"] = _newsId.ToString("D") });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(RequestedUrls(), Is.EqualTo(new[] { NewsUrl }));
		});
	}

	[Test]
	public async Task An_unknown_feed_fails_without_refreshing_anything()
	{
		var result = await ExecuteAsync(new Dictionary<string, object> { ["feed"] = Guid.NewGuid().ToString("D") });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(RequestedUrls(), Is.Empty);
		});
	}

	[Test]
	public async Task A_feed_that_cannot_be_refreshed_fails_the_action()
	{
		_server.Serve(NewsUrl, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

		var result = await ExecuteAsync(new Dictionary<string, object> { ["feed"] = _newsId.ToString("D") });

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	private IActionDefinition Refresh()
	{
		return _integration.Actions.Single(action => action.Id == "refresh");
	}

	private Task<ActionResult> ExecuteAsync(IReadOnlyDictionary<string, object> parameters)
	{
		return Refresh().CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private List<string> RequestedUrls()
	{
		return [.. _server.Requests.Select(request => request.RequestUri!.AbsoluteUri)];
	}
}
