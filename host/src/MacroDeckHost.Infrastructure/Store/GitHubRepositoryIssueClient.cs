using System.Net;
using System.Text.Json;
using MacroDeckHost.Application.Store.Issues;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class GitHubRepositoryIssueClient : IStoreRepositoryIssueClient, IDisposable
{
	public const string HttpClientName = "github-api";
	public const int PageSize = 100;
	public const int MaxResponseBytes = 1024 * 1024;

	private static readonly Uri _baseAddress = new("https://api.github.com/");

	private static readonly JsonSerializerOptions _json =
		new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly ILogger _logger;

	public GitHubRepositoryIssueClient(IHttpClientFactory httpClientFactory, ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, logger)
	{
	}

	internal GitHubRepositoryIssueClient(HttpMessageHandler handler, ILogger logger)
		: this(Configure(new HttpClient(handler, false)), true, logger)
	{
	}

	private GitHubRepositoryIssueClient(HttpClient http, bool ownsClient, ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_logger = logger.ForContext<GitHubRepositoryIssueClient>();
	}

	internal static HttpClient Configure(HttpClient client)
	{
		client.BaseAddress = _baseAddress;
		client.Timeout = TimeSpan.FromSeconds(10);
		client.DefaultRequestHeaders.UserAgent.ParseAdd("MacroDeck");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
		return client;
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	public async Task<StoreRepositoryIssueFetch> GetOpenIssues(
		StoreRepository repository,
		string? etag,
		CancellationToken cancellationToken)
	{
		var path = $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}"
			+ $"/issues?state=open&per_page={PageSize}";
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		if (etag is not null)
		{
			request.Headers.TryAddWithoutValidation("If-None-Match", etag);
		}

		try
		{
			using var response
				= await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			return response.StatusCode switch
			{
				HttpStatusCode.OK => await ReadIssues(repository, response, cancellationToken),
				HttpStatusCode.NotModified => Fetch(StoreRepositoryIssueFetchStatus.NotModified),
				HttpStatusCode.NotFound => Fetch(StoreRepositoryIssueFetchStatus.NotFound),
				HttpStatusCode.Gone => Fetch(StoreRepositoryIssueFetchStatus.IssuesDisabled),
				HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
					=> Fetch(StoreRepositoryIssueFetchStatus.RateLimited),
				_ => Unavailable(response.StatusCode)
			};
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
			|| ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
		{
			_logger.Warning("The GitHub issues request for a Store package repository failed: {Error}",
				ex.GetType().Name);
			return Fetch(StoreRepositoryIssueFetchStatus.Unavailable);
		}
	}

	private async Task<StoreRepositoryIssueFetch> ReadIssues(
		StoreRepository repository,
		HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength > MaxResponseBytes
			|| await ReadCapped(response.Content, cancellationToken) is not { } body)
		{
			_logger.Warning("The GitHub issues response for a Store package repository exceeded {Limit} bytes",
				MaxResponseBytes);
			return Fetch(StoreRepositoryIssueFetchStatus.Unavailable);
		}

		var page = JsonSerializer.Deserialize<List<GitHubIssue?>>(body, _json);
		if (page is null)
		{
			return Fetch(StoreRepositoryIssueFetchStatus.Unavailable);
		}

		var issues = page
			.OfType<GitHubIssue>()
			.Where(issue => issue.PullRequest is null
				&& !string.IsNullOrWhiteSpace(issue.Title)
				&& repository.IsIssueUrl(issue.HtmlUrl, issue.Number))
			.Select(issue => new StoreRepositoryIssue
			{
				Number = issue.Number,
				Title = issue.Title!,
				Url = issue.HtmlUrl!,
				Author = issue.User?.Login,
				CreatedAt = issue.CreatedAt,
				Comments = issue.Comments
			})
			.ToList();
		return new StoreRepositoryIssueFetch
		{
			Status = StoreRepositoryIssueFetchStatus.Ok,
			Issues = issues,
			Truncated = page.Count >= PageSize,
			ETag = response.Headers.ETag?.ToString()
		};
	}

	private StoreRepositoryIssueFetch Unavailable(HttpStatusCode status)
	{
		_logger.Warning("GitHub answered {Status} to an issues request for a Store package repository",
			(int)status);
		return Fetch(StoreRepositoryIssueFetchStatus.Unavailable);
	}

	private static StoreRepositoryIssueFetch Fetch(StoreRepositoryIssueFetchStatus status)
	{
		return new StoreRepositoryIssueFetch { Status = status };
	}

	private static async Task<byte[]?> ReadCapped(HttpContent content, CancellationToken cancellationToken)
	{
		await using var stream = await content.ReadAsStreamAsync(cancellationToken);
		using var buffer = new MemoryStream();
		var chunk = new byte[81920];
		int read;

		while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
		{
			if (buffer.Length + read > MaxResponseBytes)
			{
				return null;
			}

			buffer.Write(chunk, 0, read);
		}

		return buffer.ToArray();
	}

	private sealed record GitHubIssue
	{
		public int Number { get; init; }

		public string? Title { get; init; }

		public string? HtmlUrl { get; init; }

		public GitHubUser? User { get; init; }

		public DateTimeOffset? CreatedAt { get; init; }

		public int Comments { get; init; }

		public JsonElement? PullRequest { get; init; }
	}

	private sealed record GitHubUser
	{
		public string? Login { get; init; }
	}
}
