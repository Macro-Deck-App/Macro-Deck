using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Icons;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public class IconMasterHashBackfillBackgroundService : HostReadyBackgroundService
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _storage;
	private readonly ILogger _logger;

	public IconMasterHashBackfillBackgroundService(
		IHostApplicationLifetime lifetime,
		IIconPackCache iconPackCache,
		IIconStorage storage,
		ILogger logger)
		: base(lifetime)
	{
		_iconPackCache = iconPackCache;
		_storage = storage;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _iconPackCache.InitializeCache();

		var pending = _iconPackCache.GetIconsMissingMasterContentHash();
		if (pending.Count == 0)
		{
			return;
		}

		_logger.Information("Backfilling master content hashes for {Count} icon(s)", pending.Count);
		var hashed = 0;
		foreach (var icon in pending)
		{
			if (stoppingToken.IsCancellationRequested)
			{
				return;
			}

			try
			{
				await using var master = _storage.OpenVariant(icon.PackId, icon.Id, IconVariants.Master);
				if (master is null)
				{
					continue;
				}

				using var hash = ContentHash.CreateIncremental();
				var buffer = new byte[81920];
				int read;
				while ((read = await master.ReadAsync(buffer, stoppingToken)) > 0)
				{
					hash.Append(buffer.AsSpan(0, read));
				}

				icon.MasterContentHash = hash.Finish();
				await _iconPackCache.UpdateIcon(icon);
				hashed++;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Failed to hash the master of icon {IconId}", icon.Id);
			}

			// Deliberately unhurried: this competes with real imports for disk, and nothing is waiting on it.
			await Task.Yield();
		}

		await _iconPackCache.FlushPendingWrites();
		_logger.Information("Backfilled master content hashes for {Count} icon(s)", hashed);
	}
}
