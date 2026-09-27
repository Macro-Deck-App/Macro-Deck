namespace MacroDeckHost.Application.Store.Reviews;

public interface IStorePlatformClient
{
	Task<StorePlatformResult<IReadOnlyDictionary<string, StorePlatformRating>>> GetRatings(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<IReadOnlyDictionary<string, StorePlatformInstalls>>> GetInstalls(
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

	Task<StorePlatformResult<bool>> ReportPackage(string packageId,
		string category,
		string? detail,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<bool>> ReportReview(string packageId,
		Guid reviewId,
		string category,
		string? detail,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementStatus>>> GetEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>> ClaimEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<IReadOnlyList<StorePlatformTest>>> GetTests(CancellationToken cancellationToken = default);

	Task<StorePlatformResult<StorePlatformTestBuildDownload>> GetTestBuildDownload(string packageId,
		Guid buildId,
		CancellationToken cancellationToken = default);

	Task<StorePlatformResult<string>> GetCreatorGuidelines(CancellationToken cancellationToken = default);
}
