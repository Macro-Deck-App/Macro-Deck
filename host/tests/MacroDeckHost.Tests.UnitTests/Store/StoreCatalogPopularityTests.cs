using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

[TestFixture]
internal sealed class StoreCatalogPopularityTests
{
	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private FakeStoreInstallCounts _installs = null!;
	private StoreCatalogPopularity _popularity = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);

		_catalog = new StoreCatalog();
		_installs = new FakeStoreInstallCounts();
		_popularity = new StoreCatalogPopularity(new StoreCatalogQueryService(_catalog,
				new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None),
				new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None),
				new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None)),
			_installs);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public async Task Every_match_is_listed_most_installed_first_and_packages_without_installs_follow_by_name()
	{
		Seed("alpha", "bravo", "charlie", "delta", "echo");
		_installs.Counts["charlie"] = 40;
		_installs.Counts["alpha"] = 7;
		_installs.Counts["echo"] = 40;

		var page = await Popular(new StoreCatalogQuery { Section = StoreCatalogSection.Popular });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "charlie", "echo", "alpha", "bravo", "delta" }));
			Assert.That(page.Total, Is.EqualTo(5));
		});
	}

	[Test]
	public async Task A_page_of_the_popular_order_is_cut_from_the_whole_ordering()
	{
		Seed("alpha", "bravo", "charlie", "delta");
		_installs.Counts["delta"] = 9;
		_installs.Counts["bravo"] = 3;

		var page = await Popular(new StoreCatalogQuery { Section = StoreCatalogSection.Popular, Skip = 1, Take = 2 });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "bravo", "alpha" }));
			Assert.That(page.Total, Is.EqualTo(4));
		});
	}

	[Test]
	public async Task Without_install_data_from_the_platform_the_section_falls_back_to_name_order()
	{
		Seed("charlie", "alpha", "bravo");
		_installs.Available = false;

		var page = await Popular(new StoreCatalogQuery { Section = StoreCatalogSection.Popular });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "alpha", "bravo", "charlie" }));
			Assert.That(page.Total, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task A_catalog_larger_than_one_platform_request_is_still_ordered_as_a_whole()
	{
		Seed(Enumerable.Range(0, 150).Select(number => $"pkg{number:D3}").ToArray());
		_installs.Counts["pkg149"] = 5;

		var page = await Popular(new StoreCatalogQuery { Section = StoreCatalogSection.Popular, Take = 2 });

		Assert.Multiple(() =>
		{
			Assert.That(Ids(page), Is.EqualTo(new[] { "pkg149", "pkg000" }));
			Assert.That(page.Total, Is.EqualTo(150));
			Assert.That(_installs.Requests.Select(ids => ids.Count), Has.All.LessThanOrEqualTo(StorePlatformOptions.MaxIdsPerRequest));
		});
	}

	private async Task<StoreCatalogPage> Popular(StoreCatalogQuery query)
	{
		var result = await _popularity.Query(query);
		Assert.That(result.Success, Is.True);
		return result.Data!;
	}

	private void Seed(params string[] ids) =>
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries = ids.Select(id => new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.Plugin,
					Id = id,
					Name = id,
					LatestVersion = "1.0.0",
					LatestRelease = new StoreReleaseManifest
					{
						Version = "1.0.0",
						ArtifactUrl = new Uri($"https://cdn.example/{id}.bin"),
						Sha256 = new string('a', 64),
						Size = 16
					}
				})
				.ToList()
		});

	private static List<string> Ids(StoreCatalogPage page) =>
		page.Items.Select(item => item.Entry.Id).ToList();
}

internal sealed class FakeStoreInstallCounts : IStoreReviewService
{
	public bool Available { get; set; } = true;

	public Dictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

	public List<IReadOnlyCollection<string>> Requests { get; } = [];

	public Task<GetStoreInstallsResponse> GetInstalls(IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken)
	{
		Requests.Add(packageIds);
		var response = new GetStoreInstallsResponse { Available = Available };
		if (Available)
		{
			foreach (var id in packageIds.Where(Counts.ContainsKey))
			{
				response.Installs[id] = Counts[id];
			}
		}

		return Task.FromResult(response);
	}

	public Task<GetStoreRatingsResponse> GetRatings(IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<GetStoreRatingResponse> GetRating(StoreExtensionKind kind,
		string id,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<GetStoreReviewsResponse> GetReviews(StoreExtensionKind kind,
		string id,
		int page,
		int pageSize,
		StoreReviewSortOrder sort,
		int? rating,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<GetStoreOwnReviewResponse> GetOwnReview(StoreExtensionKind kind,
		string id,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<StoreOwnReviewWriteResponse> PutOwnReview(StoreExtensionKind kind,
		string id,
		PutStoreOwnReviewRequest request,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<StoreOwnReviewWriteResponse> DeleteOwnReview(StoreExtensionKind kind,
		string id,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<StoreReportResponse> ReportEntry(StoreExtensionKind kind,
		string id,
		ReportStoreContentRequest request,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<StoreReportResponse> ReportReview(StoreExtensionKind kind,
		string id,
		Guid reviewId,
		ReportStoreContentRequest request,
		CancellationToken cancellationToken) => throw new NotSupportedException();

	public Task<GetStoreCreatorGuidelinesResponse> GetCreatorGuidelines(CancellationToken cancellationToken) =>
		throw new NotSupportedException();
}
