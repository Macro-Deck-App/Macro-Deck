using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Store;

public sealed class StoreSimilarPackages
{
	public const int DefaultTake = 6;

	public const int MaxTake = 12;

	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreReviewService _reviews;

	public StoreSimilarPackages(IStoreCatalogQueryService catalogQuery, IStoreReviewService reviews)
	{
		_catalogQuery = catalogQuery;
		_reviews = reviews;
	}

	public async Task<Result<IReadOnlyList<StoreCatalogItem>, StoreCatalogError>> Find(StoreExtensionKind kind,
		string id,
		int take = DefaultTake,
		CancellationToken cancellationToken = default)
	{
		var source = _catalogQuery.Find(kind, id);
		if (!source.Success)
		{
			return Result.Fail<IReadOnlyList<StoreCatalogItem>, StoreCatalogError>(
				source.Error ?? StoreCatalogError.NotFound,
				source.ErrorMessage);
		}

		var all = AllItems();
		if (!all.Success)
		{
			return Result.Fail<IReadOnlyList<StoreCatalogItem>, StoreCatalogError>(all.Error!.Value, all.ErrorMessage);
		}

		var origin = source.Data!.Entry;
		var candidates = all.Data!
			.Where(item => item.InstallState is StoreInstallState.NotInstalled)
			.Where(item => !(item.Entry.Kind == origin.Kind &&
				string.Equals(item.Entry.Id, origin.Id, StringComparison.OrdinalIgnoreCase)))
			.Select(item => new Candidate(item,
				item.Entry.Tags.Count(tag => origin.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)),
				SamePublisher(origin, item.Entry),
				item.Entry.Kind == origin.Kind))
			.Where(candidate => candidate.SharedTags > 0 || candidate.SamePublisher || candidate.SameKind)
			.ToList();

		var installs = candidates.Count == 0
			? null
			: await StoreInstallCounts.Fetch(_reviews, candidates.Select(candidate => candidate.Item.Entry.Id),
				cancellationToken);

		IReadOnlyList<StoreCatalogItem> ordered = candidates
			.OrderByDescending(candidate => candidate.SharedTags)
			.ThenByDescending(candidate => candidate.SamePublisher)
			.ThenByDescending(candidate => candidate.SameKind)
			.ThenByDescending(candidate => installs?.GetValueOrDefault(candidate.Item.Entry.Id) ?? 0)
			.ThenBy(candidate => candidate.Item.Entry.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(candidate => candidate.Item.Entry.Kind)
			.ThenBy(candidate => candidate.Item.Entry.Id, StringComparer.Ordinal)
			.Take(Math.Clamp(take, 1, MaxTake))
			.Select(candidate => candidate.Item)
			.ToList();

		return Result.Ok<IReadOnlyList<StoreCatalogItem>, StoreCatalogError>(ordered);
	}

	private Result<List<StoreCatalogItem>, StoreCatalogError> AllItems()
	{
		var items = new List<StoreCatalogItem>();
		while (true)
		{
			var page = _catalogQuery.Query(new StoreCatalogQuery
			{
				Section = StoreCatalogSection.Name,
				Skip = items.Count,
				Take = StoreCatalogQuery.MaxTake
			});
			if (!page.Success)
			{
				return Result.Fail<List<StoreCatalogItem>, StoreCatalogError>(page.Error!.Value, page.ErrorMessage);
			}

			items.AddRange(page.Data!.Items);
			if (page.Data.Items.Count == 0 || items.Count >= page.Data.Total)
			{
				return Result.Ok<List<StoreCatalogItem>, StoreCatalogError>(items);
			}
		}
	}

	private static bool SamePublisher(StoreCatalogEntry origin, StoreCatalogEntry candidate) =>
		!string.IsNullOrWhiteSpace(origin.Publisher) &&
		!string.IsNullOrWhiteSpace(candidate.Publisher) &&
		string.Equals(origin.Publisher.Trim(), candidate.Publisher.Trim(), StringComparison.OrdinalIgnoreCase);

	private sealed record Candidate(StoreCatalogItem Item, int SharedTags, bool SamePublisher, bool SameKind);
}
