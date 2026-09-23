using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Store;

public sealed class StoreCatalogPopularity
{
	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreReviewService _reviews;

	public StoreCatalogPopularity(IStoreCatalogQueryService catalogQuery, IStoreReviewService reviews)
	{
		_catalogQuery = catalogQuery;
		_reviews = reviews;
	}

	public async Task<Result<StoreCatalogPage, StoreCatalogError>> Query(StoreCatalogQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var matches = new List<StoreCatalogItem>();
		while (true)
		{
			var page = _catalogQuery.Query(query with
			{
				Section = StoreCatalogSection.Name,
				Skip = matches.Count,
				Take = StoreCatalogQuery.MaxTake
			});
			if (!page.Success)
			{
				return page;
			}

			matches.AddRange(page.Data!.Items);
			if (page.Data.Items.Count == 0 || matches.Count >= page.Data.Total)
			{
				break;
			}
		}

		var installs = await StoreInstallCounts.Fetch(_reviews, matches.Select(item => item.Entry.Id), cancellationToken);
		var ordered = installs is null
			? matches
			: matches.OrderByDescending(item => installs.GetValueOrDefault(item.Entry.Id)).ToList();

		return Result.Ok<StoreCatalogPage, StoreCatalogError>(new StoreCatalogPage
		{
			Items = ordered
				.Skip(Math.Max(0, query.Skip))
				.Take(Math.Clamp(query.Take, 1, StoreCatalogQuery.MaxTake))
				.ToList(),
			Total = matches.Count
		});
	}
}
