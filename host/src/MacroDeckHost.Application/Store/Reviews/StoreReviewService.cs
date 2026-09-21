using System.Collections.Concurrent;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;

namespace MacroDeckHost.Application.Store.Reviews;

public interface IStoreReviewService
{
	Task<GetStoreRatingsResponse> GetRatings(IReadOnlyCollection<string> packageIds, CancellationToken cancellationToken);

	Task<GetStoreRatingResponse> GetRating(StoreExtensionKind kind, string id, CancellationToken cancellationToken);

	Task<GetStoreReviewsResponse> GetReviews(StoreExtensionKind kind,
		string id,
		int page,
		int pageSize,
		StoreReviewSortOrder sort,
		int? rating,
		CancellationToken cancellationToken);

	Task<GetStoreOwnReviewResponse> GetOwnReview(StoreExtensionKind kind, string id, CancellationToken cancellationToken);

	Task<StoreOwnReviewWriteResponse> PutOwnReview(StoreExtensionKind kind,
		string id,
		PutStoreOwnReviewRequest request,
		CancellationToken cancellationToken);

	Task<StoreOwnReviewWriteResponse> DeleteOwnReview(StoreExtensionKind kind, string id, CancellationToken cancellationToken);

	Task<StoreReportResponse> ReportEntry(StoreExtensionKind kind,
		string id,
		ReportStoreContentRequest request,
		CancellationToken cancellationToken);

	Task<StoreReportResponse> ReportReview(StoreExtensionKind kind,
		string id,
		Guid reviewId,
		ReportStoreContentRequest request,
		CancellationToken cancellationToken);
}

public sealed class StoreReviewService : IStoreReviewService
{
	public const int MaxPageSize = 50;
	public const string ReportUnavailableCode = "report_unavailable";
	public const int MaxTitleLength = 120;
	public const int MinBodyLength = 3;
	public const int MaxBodyLength = 2000;
	public const string AvatarRoute = "/api/store/review-avatars";
	public const int MaxReportDetailLength = 1000;
	public const string OtherReportCategory = "Other";

	public static readonly IReadOnlyList<string> EntryReportCategories =
		["InappropriateContent", "Misleading", "Impersonation", "Malicious", "Spam", OtherReportCategory];

	public static readonly IReadOnlyList<string> ReviewReportCategories =
		["Spam", "Abuse", "OffTopic", OtherReportCategory];

	private readonly IStorePlatformClient _platform;
	private readonly IStoreOfficialPackages _packages;
	private readonly IConnectSessionService _session;
	private readonly StorePlatformOptions _options;
	private readonly TimeProvider _timeProvider;
	private readonly ConcurrentDictionary<string, CachedRating> _ratings = new(StringComparer.Ordinal);
	private long _ratingsUnavailableUntilTicks;

	public StoreReviewService(IStorePlatformClient platform,
		IStoreOfficialPackages packages,
		IConnectSessionService session,
		StorePlatformOptions options,
		TimeProvider timeProvider)
	{
		_platform = platform;
		_packages = packages;
		_session = session;
		_options = options;
		_timeProvider = timeProvider;
	}

	public async Task<GetStoreRatingsResponse> GetRatings(IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken)
	{
		var ids = _packages.ListedPackageIds(packageIds);
		var now = _timeProvider.GetUtcNow();
		var missing = ids
			.Where(id => !_ratings.TryGetValue(id, out var cached) || cached.ExpiresAt <= now)
			.ToList();

		if (missing.Count > 0)
		{
			if (now.UtcTicks < Interlocked.Read(ref _ratingsUnavailableUntilTicks))
			{
				return new GetStoreRatingsResponse { Available = false };
			}

			var fetched = await _platform.GetRatings(missing, cancellationToken);
			if (!fetched.Success)
			{
				Interlocked.Exchange(ref _ratingsUnavailableUntilTicks, (now + _options.RatingsCacheLifetime).UtcTicks);
				return new GetStoreRatingsResponse { Available = false };
			}

			var expiresAt = now + _options.RatingsCacheLifetime;
			foreach (var id in missing)
			{
				_ratings[id] = new CachedRating(fetched.Value!.GetValueOrDefault(id), expiresAt);
			}
		}

		var response = new GetStoreRatingsResponse { Available = true };
		foreach (var id in ids)
		{
			if (_ratings.TryGetValue(id, out var cached) && cached.Rating is { RatingCount: > 0 } rating)
			{
				response.Ratings[id] = new StoreRatingSummaryBody { Rating = rating.Rating, RatingCount = rating.RatingCount };
			}
		}

		return response;
	}

	public async Task<GetStoreRatingResponse> GetRating(StoreExtensionKind kind,
		string id,
		CancellationToken cancellationToken)
	{
		if (_packages.ResolveListed(kind, id) is not { } packageId)
		{
			return new GetStoreRatingResponse { Available = false };
		}

		var result = await _platform.GetRating(packageId, cancellationToken);
		if (!result.Success || result.Value is not { } rating)
		{
			return new GetStoreRatingResponse { Available = false };
		}

		return new GetStoreRatingResponse
		{
			Available = true,
			Rating = rating.RatingCount > 0 ? rating.Rating : null,
			RatingCount = rating.RatingCount,
			Distribution = Enumerable.Range(1, 5)
				.Select(stars => new StoreRatingBucketBody
				{
					Stars = stars,
					Count = rating.Distribution.FirstOrDefault(bucket => bucket.Stars == stars)?.Count ?? 0
				})
				.ToList()
		};
	}

	public async Task<GetStoreReviewsResponse> GetReviews(StoreExtensionKind kind,
		string id,
		int page,
		int pageSize,
		StoreReviewSortOrder sort,
		int? rating,
		CancellationToken cancellationToken)
	{
		if (_packages.ResolveListed(kind, id) is not { } packageId)
		{
			return new GetStoreReviewsResponse { Available = false };
		}

		var result = await _platform.GetReviews(packageId,
			Math.Max(1, page),
			Math.Clamp(pageSize, 1, MaxPageSize),
			sort == StoreReviewSortOrder.Oldest ? StorePlatformReviewSort.OldestFirst : StorePlatformReviewSort.NewestFirst,
			rating is >= 1 and <= 5 ? rating : null,
			cancellationToken);
		if (!result.Success || result.Value is not { } reviews)
		{
			return new GetStoreReviewsResponse { Available = false };
		}

		return new GetStoreReviewsResponse
		{
			Available = true,
			Items = reviews.Items.Select(ToBody).ToList(),
			Page = reviews.Page,
			PageSize = reviews.PageSize,
			TotalCount = reviews.TotalCount,
			ReviewCount = reviews.ReviewCount
		};
	}

	public async Task<GetStoreOwnReviewResponse> GetOwnReview(StoreExtensionKind kind,
		string id,
		CancellationToken cancellationToken)
	{
		if (_packages.ResolveListed(kind, id) is not { } packageId)
		{
			return new GetStoreOwnReviewResponse { State = StoreReviewComposeState.Unavailable };
		}

		if (_session.Current.Status != ConnectAccountStatus.SignedIn)
		{
			return new GetStoreOwnReviewResponse { State = StoreReviewComposeState.SignedOut };
		}

		var state = await ResolveEntitlement(packageId, cancellationToken);
		if (state is StoreReviewComposeState.SignedOut or StoreReviewComposeState.Unavailable)
		{
			return new GetStoreOwnReviewResponse { State = state };
		}

		var own = await _platform.GetOwnReview(packageId, cancellationToken);
		if (!own.Success)
		{
			return new GetStoreOwnReviewResponse
			{
				State = own.Failure == StorePlatformFailure.SignInRequired
					? StoreReviewComposeState.SignedOut
					: StoreReviewComposeState.Unavailable
			};
		}

		return new GetStoreOwnReviewResponse { State = state, Review = own.Value is { } review ? ToBody(review) : null };
	}

	public async Task<StoreOwnReviewWriteResponse> PutOwnReview(StoreExtensionKind kind,
		string id,
		PutStoreOwnReviewRequest request,
		CancellationToken cancellationToken)
	{
		if (_packages.ResolveListed(kind, id) is not { } packageId)
		{
			return Failure(StorePlatformResult.Fail<bool>(StorePlatformFailure.NotFound));
		}

		if (_session.Current.Status != ConnectAccountStatus.SignedIn)
		{
			return Failure(StorePlatformResult.Fail<bool>(StorePlatformFailure.SignInRequired));
		}

		var title = Normalize(request.Title);
		var body = Normalize(request.Body);
		if (request.Rating is < 1 or > 5)
		{
			return ValidationFailure("Rating");
		}

		if (title is not null && (CodePoints(title) > MaxTitleLength || title.Any(char.IsControl)))
		{
			return ValidationFailure("Title");
		}

		if (body is not null && CodePoints(body) < MinBodyLength)
		{
			return ValidationFailure("Body");
		}

		if (body is not null && CodePoints(body) > MaxBodyLength)
		{
			var existing = await _platform.GetOwnReview(packageId, cancellationToken);
			if (!existing.Success)
			{
				return Failure(existing);
			}

			if (!string.Equals(Normalize(existing.Value?.Body), body, StringComparison.Ordinal))
			{
				return ValidationFailure("Body");
			}
		}

		var result = await _platform.PutOwnReview(packageId, request.Rating, title, body, cancellationToken);
		if (result.Failure == StorePlatformFailure.DownloadRequired && await ClaimIfInstalled(packageId, cancellationToken))
		{
			result = await _platform.PutOwnReview(packageId, request.Rating, title, body, cancellationToken);
		}

		if (!result.Success)
		{
			return Failure(result);
		}

		_ratings.TryRemove(packageId, out _);
		return new StoreOwnReviewWriteResponse { Success = true, Review = result.Value is { } review ? ToBody(review) : null };
	}

	public async Task<StoreOwnReviewWriteResponse> DeleteOwnReview(StoreExtensionKind kind,
		string id,
		CancellationToken cancellationToken)
	{
		if (_packages.ResolveListed(kind, id) is not { } packageId)
		{
			return Failure(StorePlatformResult.Fail<bool>(StorePlatformFailure.NotFound));
		}

		if (_session.Current.Status != ConnectAccountStatus.SignedIn)
		{
			return Failure(StorePlatformResult.Fail<bool>(StorePlatformFailure.SignInRequired));
		}

		var result = await _platform.DeleteOwnReview(packageId, cancellationToken);
		if (result.Failure == StorePlatformFailure.DownloadRequired && await ClaimIfInstalled(packageId, cancellationToken))
		{
			result = await _platform.DeleteOwnReview(packageId, cancellationToken);
		}

		if (!result.Success)
		{
			return Failure(result);
		}

		_ratings.TryRemove(packageId, out _);
		return new StoreOwnReviewWriteResponse { Success = true };
	}

	public async Task<StoreReportResponse> ReportEntry(StoreExtensionKind kind,
		string id,
		ReportStoreContentRequest request,
		CancellationToken cancellationToken)
	{
		var (report, refused) = PrepareReport(kind, id, request, EntryReportCategories);
		if (report is null)
		{
			return refused!;
		}

		var result = await _platform.ReportPackage(report.PackageId, report.Category, report.Detail, cancellationToken);
		return result.Failure == StorePlatformFailure.NotFound
			? ReportFailure(ReportUnavailableCode, result)
			: ReportResult(result);
	}

	public async Task<StoreReportResponse> ReportReview(StoreExtensionKind kind,
		string id,
		Guid reviewId,
		ReportStoreContentRequest request,
		CancellationToken cancellationToken)
	{
		var (report, refused) = PrepareReport(kind, id, request, ReviewReportCategories);
		if (report is null)
		{
			return refused!;
		}

		return ReportResult(
			await _platform.ReportReview(report.PackageId, reviewId, report.Category, report.Detail, cancellationToken));
	}

	public static string ErrorCode(StorePlatformFailure failure) => failure switch
	{
		StorePlatformFailure.AlreadyReported => "already_reported",
		StorePlatformFailure.RetryLater => "retry_later",
		StorePlatformFailure.SignInRequired => "sign_in_required",
		StorePlatformFailure.AccountSuspended => "account_suspended",
		StorePlatformFailure.DownloadRequired => "download_required",
		StorePlatformFailure.Forbidden => "forbidden",
		StorePlatformFailure.Moderated => "moderated",
		StorePlatformFailure.Gone => "gone",
		StorePlatformFailure.Cooldown => "cooldown",
		StorePlatformFailure.Validation => "validation",
		StorePlatformFailure.NotFound => "not_found",
		_ => "platform_unavailable"
	};

	private async Task<StoreReviewComposeState> ResolveEntitlement(string packageId, CancellationToken cancellationToken)
	{
		var entitlements = await _platform.GetEntitlements([packageId], cancellationToken);
		if (!entitlements.Success)
		{
			return entitlements.Failure == StorePlatformFailure.SignInRequired
				? StoreReviewComposeState.SignedOut
				: StoreReviewComposeState.Unavailable;
		}

		var status = entitlements.Value!.TryGetValue(packageId, out var value) ? value : StoreEntitlementStatus.Unavailable;
		return status switch
		{
			StoreEntitlementStatus.Entitled => StoreReviewComposeState.Entitled,
			StoreEntitlementStatus.NotEntitled => await ClaimIfInstalled(packageId, cancellationToken)
				? StoreReviewComposeState.Entitled
				: StoreReviewComposeState.NotEntitled,
			_ => StoreReviewComposeState.Unavailable
		};
	}

	private async Task<bool> ClaimIfInstalled(string packageId, CancellationToken cancellationToken)
	{
		if (!_packages.IsInstalled(packageId))
		{
			return false;
		}

		var claim = await _platform.ClaimEntitlements([packageId], cancellationToken);
		return claim.Success &&
			claim.Value!.TryGetValue(packageId, out var status) &&
			status is StoreEntitlementClaimStatus.Claimed or StoreEntitlementClaimStatus.AlreadyEntitled;
	}

	private (PreparedReport? Report, StoreReportResponse? Refused) PrepareReport(StoreExtensionKind kind,
		string id,
		ReportStoreContentRequest request,
		IReadOnlyList<string> categories)
	{
		if (_packages.ResolveListed(kind, id) is not { } packageId)
		{
			return Refuse(StorePlatformFailure.NotFound);
		}

		switch (_session.Current.Status)
		{
			case ConnectAccountStatus.Suspended:
				return Refuse(StorePlatformFailure.AccountSuspended);
			case not ConnectAccountStatus.SignedIn:
				return Refuse(StorePlatformFailure.SignInRequired);
		}

		var category = request.Category?.Trim() ?? string.Empty;
		if (!categories.Contains(category, StringComparer.Ordinal))
		{
			return Refuse(StorePlatformFailure.Validation, "Category");
		}

		var detail = Normalize(request.Detail);
		// The Platform's rule: nothing below 0x20 except tab, CR and LF, while DEL and C1 are accepted.
		if ((detail is null && category == OtherReportCategory) ||
			(detail is not null && (CodePoints(detail) > MaxReportDetailLength || detail.Any(IsDisallowedControl))))
		{
			return Refuse(StorePlatformFailure.Validation, "Detail");
		}

		return (new PreparedReport(packageId, category, detail), null);

		static (PreparedReport?, StoreReportResponse?) Refuse(StorePlatformFailure failure, string? field = null) =>
			(null, ReportResult(StorePlatformResult.Fail<bool>(failure, field: field)));
	}

	private static bool IsDisallowedControl(char character) =>
		character < '\u0020' && character is not ('\t' or '\r' or '\n');

	private static StoreReportResponse ReportResult(StorePlatformResult<bool> result) =>
		result.Success ? new StoreReportResponse { Success = true } : ReportFailure(ErrorCode(result.Failure), result);

	private static StoreReportResponse ReportFailure(string code, StorePlatformResult<bool> result) => new()
	{
		Success = false,
		Error = new StoreReviewWriteError
		{
			Code = code,
			Field = result.Field,
			RetryAfterSeconds = result.RetryAfter is { } retryAfter ? (int)Math.Ceiling(retryAfter.TotalSeconds) : null
		}
	};

	private static int CodePoints(string value) => value.EnumerateRunes().Count();

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static StoreOwnReviewWriteResponse ValidationFailure(string field) =>
		Failure(StorePlatformResult.Fail<bool>(StorePlatformFailure.Validation, field: field));

	private static StoreOwnReviewWriteResponse Failure<T>(StorePlatformResult<T> result) => new()
	{
		Success = false,
		Error = new StoreReviewWriteError
		{
			Code = ErrorCode(result.Failure),
			Field = result.Field,
			RetryAfterSeconds = result.RetryAfter is { } retryAfter ? (int)Math.Ceiling(retryAfter.TotalSeconds) : null
		}
	};

	private static StoreReviewBody ToBody(StorePlatformReview review) => new()
	{
		Id = review.Id,
		Rating = review.Rating,
		Title = review.Title,
		Body = review.Body,
		AuthorDisplayName = review.Author.DisplayName,
		AuthorAvatarUrl = ConnectAssetUrls.IsTrusted(review.Author.AvatarUrl)
			? $"{AvatarRoute}?src={Uri.EscapeDataString(review.Author.AvatarUrl!)}"
			: null,
		CreatedAt = review.CreatedAt,
		IsEdited = review.IsEdited,
		DownloadedBeforeReview = review.DownloadedBeforeReview
	};

	private static StoreOwnReviewBody ToBody(StorePlatformOwnReview review) => new()
	{
		Id = review.Id,
		Rating = review.Rating,
		Title = review.Title,
		Body = review.Body,
		Visibility = review.Visibility,
		ModerationReason = review.ModerationReason,
		CreatedAt = review.CreatedAt,
		UpdatedAt = review.UpdatedAt,
		IsEdited = review.IsEdited
	};

	private sealed record CachedRating(StorePlatformRating? Rating, DateTimeOffset ExpiresAt);

	private sealed record PreparedReport(string PackageId, string Category, string? Detail);
}
