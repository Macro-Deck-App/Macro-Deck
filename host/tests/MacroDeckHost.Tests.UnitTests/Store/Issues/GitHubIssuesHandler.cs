using System.Net;
using System.Text;
using System.Text.Json;

namespace MacroDeckHost.Tests.UnitTests.Store.Issues;

internal sealed class GitHubIssuesHandler : HttpMessageHandler
{
	private readonly Queue<Func<HttpResponseMessage>> _responses = new();
	private readonly Lock _gate = new();

	public List<HttpRequestMessage> Requests { get; } = [];

	public TaskCompletionSource? Hold { get; set; }

	public static object Issue(
		int number,
		string? htmlUrl = null,
		bool pullRequest = false,
		string title = "Lights flicker",
		string login = "octocat",
		int comments = 0)
	{
		var issue = new Dictionary<string, object?>
		{
			["number"] = number,
			["title"] = title,
			["html_url"] = htmlUrl ?? $"https://github.com/acme/hue/issues/{number}",
			["user"] = new Dictionary<string, object?> { ["login"] = login },
			["created_at"] = "2026-09-01T10:00:00Z",
			["comments"] = comments,
			["state"] = "open"
		};
		if (pullRequest)
		{
			issue["pull_request"] = new Dictionary<string, object?> { ["url"] = "https://api.github.com/x" };
		}

		return issue;
	}

	public void RespondWithIssues(IEnumerable<object> issues, string? etag = null)
	{
		var body = JsonSerializer.SerializeToUtf8Bytes(issues);
		Respond(() =>
		{
			var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
			if (etag is not null)
			{
				response.Headers.TryAddWithoutValidation("ETag", etag);
			}

			return response;
		});
	}

	public void RespondWithBody(string body)
	{
		Respond(() => new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) });
	}

	public void RespondWithStatus(HttpStatusCode status)
	{
		Respond(() => new HttpResponseMessage(status) { Content = new ByteArrayContent([]) });
	}

	public void Respond(Func<HttpResponseMessage> response)
	{
		lock (_gate)
		{
			_responses.Enqueue(response);
		}
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		Func<HttpResponseMessage> next;
		lock (_gate)
		{
			Requests.Add(request);
			next = _responses.Dequeue();
		}

		if (Hold is { } hold)
		{
			await hold.Task.WaitAsync(cancellationToken);
		}

		var response = next();
		response.RequestMessage = request;
		return response;
	}
}
