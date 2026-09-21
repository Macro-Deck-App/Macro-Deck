using System.Collections.Concurrent;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;

namespace MacroDeckHost.Tests.UnitTests.Store.Reviews;

internal sealed class FakeStorePlatformClient : IStorePlatformClient
{
	public ConcurrentQueue<IReadOnlyList<string>> Claims { get; } = new();

	public ConcurrentQueue<IReadOnlyList<string>> RatingRequests { get; } = new();

	public List<(int Rating, string? Title, string? Body)> Puts { get; } = [];

	public int Deletes { get; private set; }

	public Func<IReadOnlyCollection<string>, StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>> ClaimResult
	{
		get;
		set;
	} = ids => StorePlatformResult.Ok<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>(
		ids.ToDictionary(id => id, _ => StoreEntitlementClaimStatus.Claimed));

	public StoreEntitlementStatus Entitlement { get; set; } = StoreEntitlementStatus.Entitled;

	public StorePlatformFailure EntitlementFailure { get; set; }

	public StorePlatformOwnReview? OwnReview { get; set; }

	public Queue<StorePlatformResult<StorePlatformOwnReview>> PutResults { get; } = new();

	public Queue<StorePlatformResult<bool>> DeleteResults { get; } = new();

	public Dictionary<string, StorePlatformRating> Ratings { get; } = new(StringComparer.Ordinal);

	public StorePlatformFailure RatingsFailure { get; set; }

	public StorePlatformReviewPage? ReviewPage { get; set; }

	public int GetOwnReviewCalls { get; private set; }

	public Task<StorePlatformResult<IReadOnlyDictionary<string, StorePlatformRating>>> GetRatings(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default)
	{
		RatingRequests.Enqueue(packageIds.ToList());
		return Task.FromResult(RatingsFailure != StorePlatformFailure.None
			? StorePlatformResult.Fail<IReadOnlyDictionary<string, StorePlatformRating>>(RatingsFailure)
			: StorePlatformResult.Ok<IReadOnlyDictionary<string, StorePlatformRating>>(
				Ratings.Where(pair => packageIds.Contains(pair.Key)).ToDictionary()));
	}

	public Task<StorePlatformResult<StorePlatformRating>> GetRating(string packageId,
		CancellationToken cancellationToken = default) =>
		Task.FromResult(Ratings.TryGetValue(packageId, out var rating)
			? StorePlatformResult.Ok(rating)
			: StorePlatformResult.Fail<StorePlatformRating>(StorePlatformFailure.Unavailable));

	public Task<StorePlatformResult<StorePlatformReviewPage>> GetReviews(string packageId,
		int page,
		int pageSize,
		StorePlatformReviewSort sort,
		int? rating,
		CancellationToken cancellationToken = default) =>
		Task.FromResult(ReviewPage is { } reviews
			? StorePlatformResult.Ok(reviews)
			: StorePlatformResult.Fail<StorePlatformReviewPage>(StorePlatformFailure.Unavailable));

	public Task<StorePlatformResult<StorePlatformOwnReview>> GetOwnReview(string packageId,
		CancellationToken cancellationToken = default)
	{
		GetOwnReviewCalls++;
		return Task.FromResult(StorePlatformResult.Ok(OwnReview));
	}

	public Task<StorePlatformResult<StorePlatformOwnReview>> PutOwnReview(string packageId,
		int rating,
		string? title,
		string? body,
		CancellationToken cancellationToken = default)
	{
		Puts.Add((rating, title, body));
		return Task.FromResult(PutResults.Count > 0
			? PutResults.Dequeue()
			: StorePlatformResult.Ok(Own(packageId, rating, title, body)));
	}

	public Task<StorePlatformResult<bool>> DeleteOwnReview(string packageId, CancellationToken cancellationToken = default)
	{
		Deletes++;
		return Task.FromResult(DeleteResults.Count > 0 ? DeleteResults.Dequeue() : StorePlatformResult.Ok(true));
	}

	public List<(string PackageId, Guid? ReviewId, string Category, string? Detail)> Reports { get; } = [];

	public StorePlatformResult<bool> ReportResult { get; set; } = StorePlatformResult.Ok(true);

	public Task<StorePlatformResult<bool>> ReportPackage(string packageId,
		string category,
		string? detail,
		CancellationToken cancellationToken = default)
	{
		Reports.Add((packageId, null, category, detail));
		return Task.FromResult(ReportResult);
	}

	public Task<StorePlatformResult<bool>> ReportReview(string packageId,
		Guid reviewId,
		string category,
		string? detail,
		CancellationToken cancellationToken = default)
	{
		Reports.Add((packageId, reviewId, category, detail));
		return Task.FromResult(ReportResult);
	}

	public Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementStatus>>> GetEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default) =>
		Task.FromResult(EntitlementFailure != StorePlatformFailure.None
			? StorePlatformResult.Fail<IReadOnlyDictionary<string, StoreEntitlementStatus>>(EntitlementFailure)
			: StorePlatformResult.Ok<IReadOnlyDictionary<string, StoreEntitlementStatus>>(
				packageIds.ToDictionary(id => id, _ => Entitlement)));

	public Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>> ClaimEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default)
	{
		Claims.Enqueue(packageIds.ToList());
		return Task.FromResult(ClaimResult(packageIds));
	}

	public List<StorePlatformTest> Tests { get; } = [];

	public StorePlatformFailure TestsFailure { get; set; }

	public StorePlatformResult<StorePlatformTestBuildDownload>? TestBuildDownload { get; set; }

	public List<(string PackageId, Guid BuildId)> TestBuildDownloadRequests { get; } = [];

	public Task<StorePlatformResult<IReadOnlyList<StorePlatformTest>>> GetTests(CancellationToken cancellationToken = default) =>
		Task.FromResult(TestsFailure != StorePlatformFailure.None
			? StorePlatformResult.Fail<IReadOnlyList<StorePlatformTest>>(TestsFailure)
			: StorePlatformResult.Ok<IReadOnlyList<StorePlatformTest>>(Tests.ToList()));

	public Task<StorePlatformResult<StorePlatformTestBuildDownload>> GetTestBuildDownload(string packageId,
		Guid buildId,
		CancellationToken cancellationToken = default)
	{
		TestBuildDownloadRequests.Add((packageId, buildId));
		return Task.FromResult(TestBuildDownload ??
			StorePlatformResult.Fail<StorePlatformTestBuildDownload>(StorePlatformFailure.NotFound));
	}

	public static StorePlatformOwnReview Own(string packageId, int rating, string? title, string? body) =>
		new(Guid.NewGuid(), packageId, rating, title, body, "Visible", false, DateTimeOffset.UnixEpoch,
			DateTimeOffset.UnixEpoch, false, null);
}

internal sealed class FakeStoreOfficialPackages : IStoreOfficialPackages
{
	public bool CatalogLoaded { get; set; } = true;

	public HashSet<string> Listed { get; } = new(StringComparer.OrdinalIgnoreCase);

	public List<string> Installed { get; set; } = [];

	public string? ResolveListed(StoreExtensionKind kind, string id) => Listed.Contains(id) ? id : null;

	public IReadOnlyList<string> ListedPackageIds(IEnumerable<string> ids) => ids.Where(Listed.Contains).ToList();

	public bool IsInstalled(string packageId) => Installed.Contains(packageId, StringComparer.OrdinalIgnoreCase);

	public IReadOnlyList<string> InstalledPackageIds() => Installed.ToList();
}
