namespace MacroDeckHost.Application.Store.Reviews;

public enum StorePlatformFailure
{
	None,
	Unavailable,
	RetryLater,
	SignInRequired,
	AccountSuspended,
	DownloadRequired,
	Forbidden,
	Moderated,
	Gone,
	Cooldown,
	Validation,
	NotFound
}

public sealed record StorePlatformResult<T>
{
	public bool Success => Failure == StorePlatformFailure.None;

	public T? Value { get; init; }

	public StorePlatformFailure Failure { get; init; }

	public TimeSpan? RetryAfter { get; init; }

	public string? Field { get; init; }
}

public static class StorePlatformResult
{
	public static StorePlatformResult<T> Ok<T>(T? value) => new() { Value = value };

	public static StorePlatformResult<T> Fail<T>(StorePlatformFailure failure,
		TimeSpan? retryAfter = null,
		string? field = null) =>
		new() { Failure = failure, RetryAfter = retryAfter, Field = field };
}

public sealed record StorePlatformRatingBucket(int Stars, int Count);

public sealed record StorePlatformRating(double? Rating, int RatingCount, IReadOnlyList<StorePlatformRatingBucket> Distribution);

public sealed record StorePlatformReviewAuthor(string DisplayName, string? AvatarUrl);

public sealed record StorePlatformReviewReply(string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsEdited);

public sealed record StorePlatformReview(
	Guid Id,
	int Rating,
	string? Title,
	string? Body,
	StorePlatformReviewAuthor Author,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	bool DownloadedBeforeReview,
	bool IsEdited,
	StorePlatformReviewReply? Reply = null);

public sealed record StorePlatformReviewPage(
	IReadOnlyList<StorePlatformReview> Items,
	int Page,
	int PageSize,
	int TotalCount,
	double? Rating,
	int RatingCount,
	int ReviewCount);

public sealed record StorePlatformOwnReview(
	Guid Id,
	string PackageId,
	int Rating,
	string? Title,
	string? Body,
	string Visibility,
	bool DownloadedBeforeReview,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	bool IsEdited,
	string? ModerationReason);

public enum StorePlatformReviewSort
{
	NewestFirst,
	OldestFirst
}

public enum StoreEntitlementStatus
{
	Entitled,
	NotEntitled,
	Unavailable
}

public enum StoreEntitlementClaimStatus
{
	Claimed,
	AlreadyEntitled,
	Unavailable
}

public sealed record StorePlatformTestBuild(
	Guid Id,
	string Version,
	string Build,
	string? Changelog,
	string FileName,
	string Sha256,
	long SizeInBytes,
	DateTimeOffset UploadedAt,
	DateTimeOffset AvailableAt);

public sealed record StorePlatformTest(
	string PackageId,
	string DisplayName,
	DateTimeOffset JoinedAt,
	IReadOnlyList<StorePlatformTestBuild> Builds);

public sealed record StorePlatformTestBuildDownload(
	Uri Url,
	string FileName,
	string Sha256,
	long SizeInBytes,
	DateTimeOffset ExpiresAt);
