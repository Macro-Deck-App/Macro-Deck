using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Reviews;

namespace MacroDeckHost.Application.Store.Testing;

public sealed class StoreTestService : IStoreTestService
{
	private readonly IStorePlatformClient _platform;
	private readonly IPluginInstallationCatalog _plugins;
	private readonly IStoreTestInstallationStore _testInstallations;
	private readonly IStoreOperationTracker _tracker;
	private readonly IStoreInstallCoordinator _coordinator;
	private readonly IStoreCatalogQueryService _catalogQuery;

	public StoreTestService(IStorePlatformClient platform,
		IPluginInstallationCatalog plugins,
		IStoreTestInstallationStore testInstallations,
		IStoreOperationTracker tracker,
		IStoreInstallCoordinator coordinator,
		IStoreCatalogQueryService catalogQuery)
	{
		_platform = platform;
		_plugins = plugins;
		_testInstallations = testInstallations;
		_tracker = tracker;
		_coordinator = coordinator;
		_catalogQuery = catalogQuery;
	}

	public async Task<StoreTestListResult> GetTests(CancellationToken cancellationToken = default)
	{
		var result = await _platform.GetTests(cancellationToken);
		if (!result.Success)
		{
			return new StoreTestListResult(result.Failure, []);
		}

		var installed = _plugins.Discover();
		return new StoreTestListResult(StorePlatformFailure.None,
		[
			.. (result.Value ?? []).Select(test =>
			{
				var installedVersion = installed
					.FirstOrDefault(plugin => string.Equals(plugin.PluginId, test.PackageId, StringComparison.OrdinalIgnoreCase))
					?.ActiveVersion?.Version;
				var record = _testInstallations.Find(test.PackageId);
				var catalog = _catalogQuery.Find(StoreExtensionKind.Plugin, test.PackageId);
				var icon = catalog.Success ? catalog.Data!.Entry.LatestRelease.Icon : null;
				return new StoreTest
				{
					Test = test,
					InstalledVersion = installedVersion,
					// A record whose version is no longer the active one was replaced by a store install or removed.
					InstalledTestBuildId = record is not null &&
						string.Equals(record.Version, installedVersion, StringComparison.Ordinal)
							? record.BuildId
							: null,
					StoreVersion = catalog.Success && catalog.Data!.InstallState is not StoreInstallState.Unsupported
						? catalog.Data.Entry.LatestVersion
						: null,
					ActiveOperationId = _tracker.FindLive(StoreExtensionKind.Plugin, test.PackageId)?.Id,
					HasIcon = icon is not null,
					IconSha256 = icon?.Sha256.ToLowerInvariant()
				};
			})
		]);
	}

	public async Task<StoreTestInstallResult> Install(string packageId,
		Guid buildId,
		bool consent,
		CancellationToken cancellationToken = default)
	{
		// What is installed is named by the Platform's answer for this account, never by the request.
		var tests = await _platform.GetTests(cancellationToken);
		if (!tests.Success)
		{
			return new StoreTestInstallResult(tests.Failure);
		}

		var test = (tests.Value ?? []).FirstOrDefault(candidate =>
			string.Equals(candidate.PackageId, packageId, StringComparison.Ordinal));
		var build = test?.Builds.FirstOrDefault(candidate => candidate.Id == buildId);
		if (test is null || build is null)
		{
			return new StoreTestInstallResult(StorePlatformFailure.NotFound);
		}

		return new StoreTestInstallResult(StorePlatformFailure.None,
			_coordinator.InstallTestBuild(test.PackageId, test.DisplayName, build, consent));
	}
}
