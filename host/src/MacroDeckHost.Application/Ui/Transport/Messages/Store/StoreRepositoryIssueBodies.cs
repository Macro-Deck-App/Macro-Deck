using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreRepositoryIssuesError
{
	NoRepository,
	NotFound,
	IssuesDisabled,
	RateLimited,
	Unavailable
}

public class StoreRepositoryIssueBody
{
	public int Number { get; set; }

	public string Title { get; set; } = string.Empty;

	public string Url { get; set; } = string.Empty;

	public string? Author { get; set; }

	public DateTimeOffset? CreatedAt { get; set; }

	public int Comments { get; set; }
}

public class GetStoreRepositoryIssuesResponse
{
	public List<StoreRepositoryIssueBody> Issues { get; set; } = [];

	public int OpenCount { get; set; }

	public bool Truncated { get; set; }

	public string? NewIssueUrl { get; set; }

	public StoreRepositoryIssuesError? Error { get; set; }
}
