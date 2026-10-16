using System.Net;
using System.Text.Json;
using MacroDeckHost.Application.Store.Issues;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Store.Issues;

[TestFixture]
internal sealed class StoreRepositoryIssueServiceTests
{
	private const string Repository = "https://github.com/acme/hue";
	private const string NewIssueUrl = "https://github.com/acme/hue/issues/new";

	private static readonly string[] _apiVersion = ["2022-11-28"];
	private static readonly string[] _etag = ["W/\"abc\""];
	private static readonly int[] _issueSeven = [7];
	private static readonly int[] _oneIssueEach = [1, 1];
	private static readonly string[] _responseProperties = ["issues", "openCount", "truncated", "newIssueUrl", "error"];

	private GitHubIssuesHandler _handler = null!;
	private GitHubRepositoryIssueClient _client = null!;
	private ManualTimeProvider _time = null!;
	private StoreRepositoryIssueService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_handler = new GitHubIssuesHandler();
		_client = new GitHubRepositoryIssueClient(_handler, Serilog.Core.Logger.None);
		_time = new ManualTimeProvider();
		_service = new StoreRepositoryIssueService(_client, _time);
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_handler.Dispose();
	}

	[Test]
	public async Task Open_issues_are_listed_without_pull_requests_or_links_into_another_repository()
	{
		_handler.RespondWithIssues([
			GitHubIssuesHandler.Issue(1, "https://github.com/ACME/Hue/issues/1", comments: 3),
			GitHubIssuesHandler.Issue(2, pullRequest: true),
			GitHubIssuesHandler.Issue(3, "https://github.com/evil/hue/issues/3"),
			GitHubIssuesHandler.Issue(4, "https://github.com/acme/hue/issues/40"),
			GitHubIssuesHandler.Issue(5, "https://evil.example/acme/hue/issues/5")
		]);

		var response = await _service.GetIssues("https://github.com/acme/hue.git", CancellationToken.None);

		var request = _handler.Requests.Single();
		var issue = response.Issues.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
			Assert.That(request.RequestUri,
				Is.EqualTo(new Uri("https://api.github.com/repos/acme/hue/issues?state=open&per_page=100")));
			Assert.That(request.Headers.UserAgent.ToString(), Is.EqualTo("MacroDeck"));
			Assert.That(request.Headers.Accept.ToString(), Is.EqualTo("application/vnd.github+json"));
			Assert.That(request.Headers.GetValues("X-GitHub-Api-Version"), Is.EqualTo(_apiVersion));
			Assert.That((issue.Number, issue.Title, issue.Url, issue.Author, issue.Comments),
				Is.EqualTo((1, "Lights flicker", "https://github.com/ACME/Hue/issues/1", "octocat", 3)));
			Assert.That(issue.CreatedAt, Is.EqualTo(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero)));
			Assert.That(response.OpenCount, Is.EqualTo(1));
			Assert.That(response.Truncated, Is.False);
			Assert.That(response.NewIssueUrl, Is.EqualTo(NewIssueUrl));
			Assert.That(response.Error, Is.Null);
		});
	}

	[Test]
	public async Task A_full_page_is_truncated_while_the_count_only_covers_the_issues_shown()
	{
		_handler.RespondWithIssues(Enumerable.Range(1, 100)
			.Select(number => GitHubIssuesHandler.Issue(number, pullRequest: number <= 10)));

		var response = await _service.GetIssues(Repository, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Truncated, Is.True);
			Assert.That(response.OpenCount, Is.EqualTo(90));
			Assert.That(response.Issues, Has.Count.EqualTo(90));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("not a url")]
	[TestCase("https://gitlab.com/acme/hue")]
	[TestCase("http://github.com/acme/hue")]
	[TestCase("https://github.com.evil.example/acme/hue")]
	[TestCase("https://github.com/acme")]
	[TestCase("https://github.com/acme/%2E%2E")]
	[TestCase("https://github.com/acme/.git")]
	public async Task A_repository_that_is_not_on_github_answers_without_any_request(string? repository)
	{
		var response = await _service.GetIssues(repository, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.EqualTo(StoreRepositoryIssuesError.NoRepository));
			Assert.That(response.NewIssueUrl, Is.Null);
			Assert.That(response.Issues, Is.Empty);
			Assert.That(_handler.Requests, Is.Empty);
		});
	}

	[TestCase("https://github.com/Acme/Hue-Bridge/tree/main/docs?tab=readme#usage",
		"https://api.github.com/repos/Acme/Hue-Bridge/issues?state=open&per_page=100",
		"https://github.com/Acme/Hue-Bridge/issues/new")]
	[TestCase("https://GitHub.com/acme/hue.GIT",
		"https://api.github.com/repos/acme/hue/issues?state=open&per_page=100",
		"https://github.com/acme/hue/issues/new")]
	public async Task A_github_repository_link_with_a_subpath_or_suffix_names_its_owner_and_repository(
		string repository,
		string expectedRequest,
		string expectedNewIssueUrl)
	{
		_handler.RespondWithIssues([]);

		var response = await _service.GetIssues(repository, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requests.Single().RequestUri, Is.EqualTo(new Uri(expectedRequest)));
			Assert.That(response.NewIssueUrl, Is.EqualTo(expectedNewIssueUrl));
			Assert.That(response.Error, Is.Null);
		});
	}

	[TestCase(HttpStatusCode.NotFound, StoreRepositoryIssuesError.NotFound, NewIssueUrl)]
	[TestCase(HttpStatusCode.Gone, StoreRepositoryIssuesError.IssuesDisabled, null)]
	[TestCase(HttpStatusCode.MovedPermanently, StoreRepositoryIssuesError.Unavailable, NewIssueUrl)]
	[TestCase(HttpStatusCode.Forbidden, StoreRepositoryIssuesError.RateLimited, NewIssueUrl)]
	[TestCase(HttpStatusCode.TooManyRequests, StoreRepositoryIssuesError.RateLimited, NewIssueUrl)]
	[TestCase(HttpStatusCode.BadGateway, StoreRepositoryIssuesError.Unavailable, NewIssueUrl)]
	public async Task Each_github_failure_answers_with_its_own_error(
		HttpStatusCode status,
		StoreRepositoryIssuesError expected,
		string? expectedNewIssueUrl)
	{
		_handler.RespondWithStatus(status);

		var response = await _service.GetIssues(Repository, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Error, Is.EqualTo(expected));
			Assert.That(response.NewIssueUrl, Is.EqualTo(expectedNewIssueUrl));
			Assert.That(response.Issues, Is.Empty);
			Assert.That(response.OpenCount, Is.Zero);
		});
	}

	[TestCase("[{\"number\": \"one\"}]")]
	[TestCase("{\"message\": \"Not an array\"}")]
	[TestCase(null)]
	public async Task A_response_that_is_not_an_issue_page_or_too_large_is_unavailable(string? body)
	{
		if (body is null)
		{
			_handler.RespondWithBody($"[{new string(' ', GitHubRepositoryIssueClient.MaxResponseBytes)}]");
		}
		else
		{
			_handler.RespondWithBody(body);
		}

		var response = await _service.GetIssues(Repository, CancellationToken.None);

		Assert.That(response.Error, Is.EqualTo(StoreRepositoryIssuesError.Unavailable));
	}

	[Test]
	public async Task A_rate_limited_repository_is_not_asked_again_until_the_cooldown_has_passed()
	{
		_handler.RespondWithStatus(HttpStatusCode.Forbidden);
		_handler.RespondWithIssues([GitHubIssuesHandler.Issue(1)]);

		await _service.GetIssues(Repository, CancellationToken.None);
		_time.Advance(TimeSpan.FromMinutes(14));
		var cooling = await _service.GetIssues(Repository, CancellationToken.None);
		var requestsDuringCooldown = _handler.Requests.Count;
		_time.Advance(TimeSpan.FromMinutes(2));
		var recovered = await _service.GetIssues(Repository, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(cooling.Error, Is.EqualTo(StoreRepositoryIssuesError.RateLimited));
			Assert.That(requestsDuringCooldown, Is.EqualTo(1));
			Assert.That(recovered.Error, Is.Null);
			Assert.That(recovered.OpenCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_cached_list_is_served_without_a_request_and_revalidated_with_its_etag_once_stale()
	{
		_handler.RespondWithIssues([GitHubIssuesHandler.Issue(7)], "W/\"abc\"");
		_handler.RespondWithStatus(HttpStatusCode.NotModified);

		var first = await _service.GetIssues(Repository, CancellationToken.None);
		_time.Advance(TimeSpan.FromMinutes(9));
		var cached = await _service.GetIssues("https://github.com/ACME/HUE", CancellationToken.None);
		var requestsWhileFresh = _handler.Requests.Count;
		_time.Advance(TimeSpan.FromMinutes(2));
		var revalidated = await _service.GetIssues(Repository, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(requestsWhileFresh, Is.EqualTo(1));
			Assert.That(cached.Issues.Select(issue => issue.Number), Is.EqualTo(_issueSeven));
			Assert.That(_handler.Requests[0].Headers.Contains("If-None-Match"), Is.False);
			Assert.That(_handler.Requests[1].Headers.GetValues("If-None-Match"), Is.EqualTo(_etag));
			Assert.That(revalidated.Error, Is.Null);
			Assert.That(revalidated.Issues.Select(issue => issue.Number),
				Is.EqualTo(first.Issues.Select(issue => issue.Number)));
		});
	}

	[Test]
	public async Task Concurrent_requests_for_one_repository_share_a_single_github_request()
	{
		_handler.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_handler.RespondWithIssues([GitHubIssuesHandler.Issue(1)]);

		var first = _service.GetIssues(Repository, CancellationToken.None);
		var second = _service.GetIssues(Repository, CancellationToken.None);
		_handler.Hold.SetResult();
		var responses = await Task.WhenAll(first, second);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Requests, Has.Count.EqualTo(1));
			Assert.That(responses.Select(response => response.OpenCount), Is.EqualTo(_oneIssueEach));
		});
	}

	[Test]
	public async Task The_response_serializes_to_the_wire_contract()
	{
		_handler.RespondWithStatus(HttpStatusCode.TooManyRequests);

		var response = await _service.GetIssues(Repository, CancellationToken.None);

		var json = JsonSerializer.SerializeToElement(response, JsonSerializerOptions.Web);
		Assert.Multiple(() =>
		{
			Assert.That(json.EnumerateObject().Select(property => property.Name),
				Is.EquivalentTo(_responseProperties));
			Assert.That(json.GetProperty("error").GetString(), Is.EqualTo("RateLimited"));
		});
	}
}
