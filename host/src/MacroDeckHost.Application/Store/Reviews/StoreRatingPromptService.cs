using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Store.Reviews;

public interface IStoreRatingPromptService
{
	Task<GetStoreRatingPromptResponse> GetPrompt(CancellationToken cancellationToken);

	Task MarkShown(StoreExtensionKind kind, string id, CancellationToken cancellationToken);
}

public sealed class StoreRatingPromptService : IStoreRatingPromptService, IDisposable
{
	public const string HistoryKey = "storeRatingPrompt.history";
	public static readonly TimeSpan MinimumInstallAge = TimeSpan.FromDays(7);
	public static readonly TimeSpan PromptInterval = TimeSpan.FromDays(5);
	public const int MaxReviewLookups = 5;

	private readonly IStoreOfficialPackages _packages;
	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreInstallationStore _installations;
	private readonly IStoreReviewService _reviews;
	private readonly IConnectSessionService _session;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public StoreRatingPromptService(IStoreOfficialPackages packages,
		IStoreCatalogQueryService catalogQuery,
		IStoreInstallationStore installations,
		IStoreReviewService reviews,
		IConnectSessionService session,
		IServiceScopeFactory scopeFactory,
		TimeProvider timeProvider)
	{
		_packages = packages;
		_catalogQuery = catalogQuery;
		_installations = installations;
		_reviews = reviews;
		_session = session;
		_scopeFactory = scopeFactory;
		_timeProvider = timeProvider;
	}

	public async Task<GetStoreRatingPromptResponse> GetPrompt(CancellationToken cancellationToken)
	{
		if (!_packages.CatalogLoaded)
		{
			return new GetStoreRatingPromptResponse { Ready = false };
		}

		var response = new GetStoreRatingPromptResponse { Ready = true };
		if (_session.Current.Status != ConnectAccountStatus.SignedIn)
		{
			return response;
		}

		using (var scope = _scopeFactory.CreateScope())
		{
			var extensions = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetExtensions();
			if (!extensions.StoreEnabled || !extensions.AskForRatings)
			{
				return response;
			}
		}

		var now = _timeProvider.GetUtcNow();
		var history = await ReadHistory();
		if (history.Count > 0 && now - history.Values.Max() < PromptInterval)
		{
			return response;
		}

		var lookups = 0;
		foreach (var item in Candidates(now, history))
		{
			if (lookups++ >= MaxReviewLookups)
			{
				break;
			}

			var own = await _reviews.GetOwnReview(item.Entry.Kind, item.Entry.Id, cancellationToken);
			if (own.State == StoreReviewComposeState.Entitled && own.Review is null)
			{
				response.Candidate = new StoreRatingPromptCandidateBody
				{
					Kind = item.Entry.Kind,
					Id = item.Entry.Id,
					Name = item.Entry.Name,
					HasIcon = item.Entry.LatestRelease.Icon is not null,
					IconSha256 = item.Entry.LatestRelease.Icon?.Sha256.ToLowerInvariant()
				};
				return response;
			}

			if (own.State is StoreReviewComposeState.SignedOut or StoreReviewComposeState.Unavailable)
			{
				break;
			}
		}

		return response;
	}

	public async Task MarkShown(StoreExtensionKind kind, string id, CancellationToken cancellationToken)
	{
		if (_packages.ResolveListed(kind, id) is not { } packageId || !_packages.IsInstalled(packageId))
		{
			return;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var now = _timeProvider.GetUtcNow();
			var installed = _packages.InstalledPackageIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
			var history = (await ReadHistory())
				.Where(entry => installed.Contains(entry.Key))
				.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
			history[packageId] = now;
			await WriteHistory(history);
		}
		finally
		{
			_gate.Release();
		}
	}

	private IEnumerable<StoreCatalogItem> Candidates(DateTimeOffset now, Dictionary<string, DateTimeOffset> history)
	{
		var eligible = new List<(StoreCatalogItem Item, DateTimeOffset InstalledAt)>();
		foreach (var item in _catalogQuery.Installed())
		{
			if (item.Withdrawal is not null ||
				item.InstalledTestBuild is not null ||
				_packages.ResolveListed(item.Entry.Kind, item.Entry.Id) is null ||
				_installations.Find(item.Entry.Kind, item.Entry.Id) is not { } record ||
				!StoreRegistryOptions.IsOfficial(record.Origin) ||
				now - record.InstalledAt < MinimumInstallAge)
			{
				continue;
			}

			eligible.Add((item, record.InstalledAt));
		}

		return eligible
			.OrderBy(candidate => history.TryGetValue(candidate.Item.Entry.Id, out var shown) ? shown : DateTimeOffset.MinValue)
			.ThenBy(candidate => candidate.InstalledAt)
			.Select(candidate => candidate.Item);
	}

	private async Task<Dictionary<string, DateTimeOffset>> ReadHistory()
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var stored = (await repository.GetByKey(HistoryKey))?.Value;
		var history = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(stored))
		{
			return history;
		}

		try
		{
			var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stored);
			foreach (var (id, value) in parsed ?? [])
			{
				if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var shown))
				{
					history[id] = shown;
				}
			}
		}
		catch (JsonException)
		{
		}

		return history;
	}

	private async Task WriteHistory(Dictionary<string, DateTimeOffset> history)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var serialized = JsonSerializer.Serialize(history.ToDictionary(entry => entry.Key,
			entry => entry.Value.ToString("O", CultureInfo.InvariantCulture)));
		await repository.SetValue(HistoryKey, serialized);
	}

	public void Dispose() => _gate.Dispose();
}
