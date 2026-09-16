namespace MacroDeckHost.Application.Store.Reviews;

public interface IStorePlatformClient
{
	Task<StorePlatformResult<IReadOnlyDictionary<string, StorePlatformRating>>> GetRatings(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<StorePlatformRating>> GetRating(string packageId,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<StorePlatformReviewPage>> GetReviews(string packageId,
		int page,
		int pageSize,
		StorePlatformReviewSort sort,
		int? rating,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<StorePlatformOwnReview>> GetOwnReview(string packageId,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<StorePlatformOwnReview>> PutOwnReview(string packageId,
		int rating,
		string? title,
		string? body,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<bool>> DeleteOwnReview(string packageId, CancellationToken cancellationToken = default);

	Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementStatus>>> GetEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>> ClaimEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default);
}
