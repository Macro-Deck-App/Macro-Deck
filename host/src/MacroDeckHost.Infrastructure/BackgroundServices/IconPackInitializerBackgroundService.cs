using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Domain.Entities;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public class IconPackInitializerBackgroundService : HostReadyBackgroundService
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IStoreInstallationReconciler _installationReconciler;
	private readonly IMacroDeckPaths _paths;
	private readonly ILogger _logger;

	public IconPackInitializerBackgroundService(
		IHostApplicationLifetime lifetime,
		IIconPackCache iconPackCache,
		IStoreInstallationReconciler installationReconciler,
		IMacroDeckPaths paths,
		ILogger logger)
		: base(lifetime)
	{
		_iconPackCache = iconPackCache;
		_installationReconciler = installationReconciler;
		_paths = paths;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		SweepLegacyDirectories();

		await _iconPackCache.InitializeCache();

		// Must run after InitializeCache(): the reconciler resolves each record's TargetIds against the
		// cache, and calling it any earlier would see every record as orphaned and prune installs that
		// are actually present.
		_installationReconciler.PruneOrphanedIconPackRecords();

		if (_iconPackCache.GetDefaultPack() is null)
		{
			_logger.Information("Creating default 'Imported Icons' pack");
			await _iconPackCache.AddOrUpdatePack(new IconPackEntity
			{
				Id = Guid.CreateVersion7(),
				Name = "Imported Icons",
				Description = "User imported icons",
				IsDefault = true,
				CreatedAt = DateTime.UtcNow
			});
		}
	}

	private void SweepLegacyDirectories()
	{
		try
		{
			foreach (var directory in Directory.EnumerateDirectories(_paths.IconsDirectory))
			{
				if (string.Equals(directory, _paths.IconPacksDirectory, StringComparison.Ordinal) ||
					string.Equals(directory, _paths.IconStagingDirectory, StringComparison.Ordinal))
				{
					continue;
				}

				_logger.Information("Removing legacy icon directory {Directory}", directory);
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to sweep legacy icon directories");
		}
	}
}
