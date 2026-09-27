using System.Collections.Concurrent;
using MacroDeck.Localization;
using MacroDeck.Plugin.Packaging.IconPacks;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Plugins.IconPacks;

public sealed class PluginIconPackSyncOptions
{
	public TimeSpan ReadinessBound { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class PluginIconPackSync : IPluginIconPackSync, IDisposable
{
	private readonly IPluginBundledIconPackDeclarations _declarations;
	private readonly IIconPackCache _iconPackCache;
	private readonly IIconUsageScanner _usageScanner;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUserNotificationStore _notifications;
	private readonly ILocalizationResolver _localization;
	private readonly IWidgetIconInvalidator _iconInvalidator;
	private readonly RemoteIconProviderActionRegistry _iconProviders;
	private readonly IPluginSessionRegistry _sessions;
	private readonly IPluginInstallationCatalog _installationCatalog;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _timeProvider;
	private readonly PluginIconPackSyncOptions _options;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, byte> _deferred = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<Guid, bool> _publishedOwnership = new();
	private readonly ConcurrentDictionary<string, string> _pluginNames = new(StringComparer.Ordinal);
	private readonly Lock _waitLock = new();
	private Task? _readinessWait;
	private volatile bool _readinessGaveUp;
	private int _readinessFaultLogged;

	public PluginIconPackSync(IPluginBundledIconPackDeclarations declarations,
		IIconPackCache iconPackCache,
		IIconUsageScanner usageScanner,
		IServiceScopeFactory scopeFactory,
		IUserNotificationStore notifications,
		ILocalizationResolver localization,
		IWidgetIconInvalidator iconInvalidator,
		RemoteIconProviderActionRegistry iconProviders,
		IPluginSessionRegistry sessions,
		IPluginInstallationCatalog installationCatalog,
		StartupReadiness readiness,
		TimeProvider timeProvider,
		ILogger logger,
		PluginIconPackSyncOptions? options = null)
	{
		_declarations = declarations;
		_iconPackCache = iconPackCache;
		_usageScanner = usageScanner;
		_scopeFactory = scopeFactory;
		_notifications = notifications;
		_localization = localization;
		_iconInvalidator = iconInvalidator;
		_iconProviders = iconProviders;
		_sessions = sessions;
		_installationCatalog = installationCatalog;
		_readiness = readiness;
		_timeProvider = timeProvider;
		_options = options ?? new PluginIconPackSyncOptions();
		_logger = logger.ForContext<PluginIconPackSync>();

		_sessions.SessionEnded += OnSessionEnded;
		_ = RunDeferredWhenReadyAsync();
	}

	public async Task<PluginIconPackSyncResult> SyncAsync(string pluginId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);

		if (!await WaitForReadinessAsync(pluginId, cancellationToken))
		{
			return PluginIconPackSyncResult.Skipped;
		}

		var gate = _gates.GetOrAdd(pluginId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			return await SyncLockedAsync(pluginId, cancellationToken);
		}
		finally
		{
			gate.Release();
		}
	}

	public async Task<PluginIconPackSyncResult> SyncDevelopmentAsync(string pluginId,
		string sessionId,
		string pluginName,
		IReadOnlyList<DevelopmentIconPack> packs,
		CancellationToken cancellationToken)
	{
		var invalid = packs
			.Where(pack => !IsUsable(pluginId, pack.Key, () => new MemoryStream(pack.Content, writable: false)))
			.Select(pack => pack.Key)
			.ToList();
		if (invalid.Count > 0)
		{
			return new PluginIconPackSyncResult(PluginIconPackSyncStatus.Invalid, InvalidKeys: invalid);
		}

		_declarations.SetDevelopment(pluginId, sessionId, pluginName, packs);
		return await SyncAsync(pluginId, cancellationToken);
	}

	public void InstallationChanged(string pluginId) => _declarations.Invalidate(pluginId);

	public void Dispose() => _sessions.SessionEnded -= OnSessionEnded;

	private async Task<PluginIconPackSyncResult> SyncLockedAsync(string pluginId, CancellationToken cancellationToken)
	{
		var declaration = _declarations.Find(pluginId);
		var declared = declaration?.Packs ?? [];
		if (declaration is not null)
		{
			_pluginNames[pluginId] = declaration.PluginName;
		}

		var existing = new Dictionary<string, IconPackEntity>(StringComparer.Ordinal);
		foreach (var pack in StampedPacks(pluginId))
		{
			existing.TryAdd(PluginIconReferences.KeyOf(pluginId, pack.SourceId)!, pack);
		}

		IReadOnlySet<Guid>? inUse = null;
		IReadOnlySet<Guid> InUse() => inUse ??= _usageScanner.FindReferencedIconIds();

		await using var scope = _scopeFactory.CreateAsyncScope();
		var restore = scope.ServiceProvider.GetRequiredService<IIconPackRestoreService>();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var changed = false;
		var iconsChanged = false;
		var invalid = new List<string>();
		var announced = new HashSet<Guid>();

		foreach (var pack in declared)
		{
			existing.TryGetValue(pack.Key, out var current);
			if (current is not null && string.Equals(current.SourceRevision, pack.Revision, StringComparison.Ordinal))
			{
				continue;
			}

			if (!IsUsable(pluginId, pack.Key, pack.Open))
			{
				invalid.Add(pack.Key);
				continue;
			}

			var stamp = new IconPackSourceStamp(IconPackSourceType.Plugin,
				PluginIconReferences.SourceId(pluginId, pack.Key),
				pack.Revision);
			var fileName = pack.Key + PluginBundledIconPacks.FileExtension;

			await using var content = pack.Open();
			if (current is null)
			{
				var created = await restore.RestoreAsNewPack(fileName, content, cancellationToken, stamp);
				if (!created.Success)
				{
					_logger.Warning("Bundled icon pack {Key} of plugin {PluginId} could not be added: {Error}",
						pack.Key,
						pluginId,
						created.ErrorMessage ?? created.Error.ToString());
					continue;
				}

				announced.Add(created.Data!.Id);
				changed = true;
				iconsChanged = true;
				continue;
			}

			var replaced = await restore.ReplaceFromSource(current.Id, fileName, content, stamp, InUse(), cancellationToken);
			if (!replaced.Success)
			{
				_logger.Warning("Bundled icon pack {Key} of plugin {PluginId} could not be replaced: {Error}",
					pack.Key,
					pluginId,
					replaced.ErrorMessage ?? replaced.Error.ToString());
				continue;
			}

			announced.Add(current.Id);
			changed = true;
			iconsChanged |= replaced.Data!.IconsChanged;
		}

		foreach (var (key, pack) in existing)
		{
			// Deliberately kept: a pack without a revision belongs to the user until its key is declared again.
			if (declaration?.Declares(key) == true || pack.SourceRevision is null)
			{
				continue;
			}

			if (_iconPackCache.GetIconsByPackId(pack.Id).Any(icon => InUse().Contains(icon.Id)))
			{
				await KeepInUseAsync(pack, NameOf(pluginId));
				continue;
			}

			await _iconPackCache.RemovePack(pack.Id);
			await mediator.Publish(new IconPackDeletedNotification(pack.Id), cancellationToken);
			_publishedOwnership.TryRemove(pack.Id, out _);
			announced.Add(pack.Id);
			changed = true;
			iconsChanged = true;
		}

		await PublishOwnershipChangesAsync(pluginId, declaration, announced, mediator, cancellationToken);

		if (iconsChanged)
		{
			foreach (var actionId in _iconProviders.GetIconProviderActionIds(pluginId))
			{
				_iconInvalidator.Invalidate(pluginId, actionId);
			}
		}

		return invalid.Count > 0
			? new PluginIconPackSyncResult(PluginIconPackSyncStatus.Invalid, changed, invalid)
			: new PluginIconPackSyncResult(PluginIconPackSyncStatus.Synced, changed);
	}

	private List<IconPackEntity> StampedPacks(string pluginId)
		=> _iconPackCache.GetAllPacks()
			.Where(pack => pack.SourceType == IconPackSourceType.Plugin &&
				PluginIconReferences.KeyOf(pluginId, pack.SourceId) is { Length: > 0 })
			.OrderBy(pack => pack.CreatedAt)
			.ToList();

	private bool IsUsable(string pluginId, string key, Func<Stream> open)
	{
		IconPackArchiveReadResult read;
		try
		{
			using var stream = open();
			read = IconPackArchive.Read(stream);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "Bundled icon pack {Key} of plugin {PluginId} cannot be read", key, pluginId);
			return false;
		}

		if (read.Info is not { NamesAreValid: true })
		{
			_logger.Warning("Bundled icon pack {Key} of plugin {PluginId} is unusable: {Error}",
				key,
				pluginId,
				read.Error ?? "duplicate or unusable icon names " +
					string.Join(", ", read.Info!.DuplicateNames.Concat(read.Info.UnusableNames)));
			return false;
		}

		return true;
	}

	private async Task KeepInUseAsync(IconPackEntity pack, string pluginName)
	{
		pack.SourceRevision = null;
		await _iconPackCache.AddOrUpdatePack(pack);

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		_notifications.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.General,
			Title = _localization.Resolve(AppStrings.Notifications.PluginIconPackKept(), culture) ?? pack.Name,
			Message = _localization.Resolve(AppStrings.Notifications.PluginIconPackKeptMessage(pack: pack.Name, plugin: pluginName),
				culture),
			SourceId = pack.SourceId,
			SourceName = pluginName,
			Actions = [new UserNotificationAction(UserNotificationActionKind.OpenIconPacks, null)],
			DedupeKey = $"plugin-icon-pack-kept:{pack.Id}"
		});
	}

	private async Task PublishOwnershipChangesAsync(string pluginId,
		DeclaredIconPackSet? declaration,
		HashSet<Guid> announced,
		IMediator mediator,
		CancellationToken cancellationToken)
	{
		foreach (var pack in StampedPacks(pluginId))
		{
			var owned = declaration?.Declares(PluginIconReferences.KeyOf(pluginId, pack.SourceId)!) == true;
			var known = _publishedOwnership.TryGetValue(pack.Id, out var previous);
			_publishedOwnership[pack.Id] = owned;
			if (announced.Contains(pack.Id) || (known && previous == owned))
			{
				continue;
			}

			await mediator.Publish(new IconPackUpdatedNotification(pack, _iconPackCache.GetIconCount(pack.Id)),
				cancellationToken);
		}
	}

	private string NameOf(string pluginId)
		=> _pluginNames.TryGetValue(pluginId, out var name)
			? name
			: _sessions.Snapshot()
					.FirstOrDefault(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal))
					?.DisplayName ??
				pluginId;

	private async Task<bool> WaitForReadinessAsync(string pluginId, CancellationToken cancellationToken)
	{
		var ready = Task.WhenAll(_readiness.WhenCachesReady, _readiness.WhenIconPacksReady);
		if (ready.IsCompletedSuccessfully)
		{
			return true;
		}

		if (ready.IsFaulted || ready.IsCanceled)
		{
			LogReadinessFault();
			return false;
		}

		if (_readinessGaveUp)
		{
			return !Defer(pluginId, ready);
		}

		Task wait;
		lock (_waitLock)
		{
			wait = _readinessWait ??= ready.WaitAsync(_options.ReadinessBound, _timeProvider, CancellationToken.None);
		}

		try
		{
			await wait.WaitAsync(cancellationToken);
			return true;
		}
		catch (TimeoutException)
		{
			_readinessGaveUp = true;
			if (!Defer(pluginId, ready))
			{
				return true;
			}

			_logger.Warning("Icon packs were not ready within {Bound}; bundled icon packs of {PluginId} sync once they are",
				_options.ReadinessBound,
				pluginId);
			return false;
		}
		catch (Exception) when (ready.IsFaulted || ready.IsCanceled)
		{
			LogReadinessFault();
			return false;
		}
	}

	// Readiness can complete between the check and the add, after the deferred run listed its plugins, so
	// a plugin added too late proceeds at once instead of waiting for a run that already happened.
	private bool Defer(string pluginId, Task ready)
	{
		_deferred.TryAdd(pluginId, 0);
		if (!ready.IsCompletedSuccessfully)
		{
			return true;
		}

		_deferred.TryRemove(pluginId, out _);
		return false;
	}

	private void LogReadinessFault()
	{
		if (Interlocked.Exchange(ref _readinessFaultLogged, 1) == 0)
		{
			_logger.Error("Icon packs failed to initialize; bundled plugin icon packs are not synced");
		}
	}

	private async Task RunDeferredWhenReadyAsync()
	{
		try
		{
			await Task.WhenAll(_readiness.WhenCachesReady, _readiness.WhenIconPacksReady);
		}
		catch (Exception)
		{
			return;
		}

		foreach (var pluginId in _deferred.Keys)
		{
			_deferred.TryRemove(pluginId, out _);
			await RunQuietlyAsync(pluginId, () => SyncAsync(pluginId, CancellationToken.None));
		}
	}

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		if (e.Reason != PluginSessionEndReason.Pruned || !_declarations.ClearDevelopment(e.PluginId, e.SessionId))
		{
			return;
		}

		_ = RunQuietlyAsync(e.PluginId,
			() => _installationCatalog.TryResolveActive(e.PluginId, out _)
				? SyncAsync(e.PluginId, CancellationToken.None)
				: ReleaseDevelopmentAsync(e.PluginId));
	}

	private async Task<PluginIconPackSyncResult> ReleaseDevelopmentAsync(string pluginId)
	{
		if (!await WaitForReadinessAsync(pluginId, CancellationToken.None))
		{
			return PluginIconPackSyncResult.Skipped;
		}

		var gate = _gates.GetOrAdd(pluginId, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(CancellationToken.None);
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			await PublishOwnershipChangesAsync(pluginId,
				_declarations.Find(pluginId),
				[],
				scope.ServiceProvider.GetRequiredService<IMediator>(),
				CancellationToken.None);
			return new PluginIconPackSyncResult(PluginIconPackSyncStatus.Synced);
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task RunQuietlyAsync(string pluginId, Func<Task<PluginIconPackSyncResult>> sync)
	{
		try
		{
			await Task.Yield();
			await sync();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Syncing the bundled icon packs of {PluginId} failed", pluginId);
		}
	}
}
