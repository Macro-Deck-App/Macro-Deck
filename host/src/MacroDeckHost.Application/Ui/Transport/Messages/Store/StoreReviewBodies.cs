using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreRatingSummaryBody
{
	public double? Rating { get; set; }

	public int RatingCount { get; set; }
}

public class GetStoreRatingsResponse
{
	public bool Available { get; set; }

	public Dictionary<string, StoreRatingSummaryBody> Ratings { get; set; } = [];
}

public class StoreRatingBucketBody
{
	public int Stars { get; set; }

	public int Count { get; set; }
}

public class GetStoreRatingResponse
{
	public bool Available { get; set; }

	public double? Rating { get; set; }

	public int RatingCount { get; set; }

	public List<StoreRatingBucketBody> Distribution { get; set; } = [];
}

public class StoreReviewReplyBody
{
	public string Body { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }

	public bool IsEdited { get; set; }
}

public class StoreReviewBody
{
	public Guid Id { get; set; }

	public int Rating { get; set; }

	public string? Title { get; set; }

	public string? Body { get; set; }

	public string AuthorDisplayName { get; set; } = string.Empty;

	public string? AuthorAvatarUrl { get; set; }

	public DateTimeOffset CreatedAt { get; set; }

	public bool IsEdited { get; set; }

	public bool DownloadedBeforeReview { get; set; }

	public StoreReviewReplyBody? Reply { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreReviewSortOrder
{
	Newest,
	Oldest
}

public class GetStoreReviewsResponse
{
	public bool Available { get; set; }

	public List<StoreReviewBody> Items { get; set; } = [];

	public int Page { get; set; }

	public int PageSize { get; set; }

	public int TotalCount { get; set; }

	public int ReviewCount { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreReviewComposeState
{
	SignedOut,
	NotEntitled,
	Entitled,
	Unavailable
}

public class StoreOwnReviewBody
{
	public int Rating { get; set; }

	public string? Title { get; set; }

	public string? Body { get; set; }

	public string Visibility { get; set; } = string.Empty;

	public string? ModerationReason { get; set; }

	public DateTimeOffset CreatedAt { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }

	public bool IsEdited { get; set; }
}

public class GetStoreOwnReviewResponse
{
	public StoreReviewComposeState State { get; set; }

	public StoreOwnReviewBody? Review { get; set; }
}

public class PutStoreOwnReviewRequest
{
	public int Rating { get; set; }

	public string? Title { get; set; }

	public string? Body { get; set; }
}

public class StoreReviewWriteError
{
	public string Code { get; set; } = string.Empty;

	public string? Field { get; set; }

	public int? RetryAfterSeconds { get; set; }
}

public class StoreOwnReviewWriteResponse
{
	public bool Success { get; set; }

	public StoreOwnReviewBody? Review { get; set; }

	public StoreReviewWriteError? Error { get; set; }
}
