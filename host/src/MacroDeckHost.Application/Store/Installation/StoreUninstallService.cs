using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Store.Installation;

// Nothing here downloads anything, so an uninstall deliberately runs synchronously rather than through
// StoreOperationChannel/StoreOperation, which exists for the download pipeline.
public sealed class StoreUninstallService : IStoreUninstallService
{
	private readonly IStoreInstallationStore _installations;
	private readonly IPluginInstaller _pluginInstaller;
	private readonly IIconPackService _iconPackService;
	private readonly IProfileService _profileService;
	private readonly IStoreUpdateDetector _updateDetector;
	private readonly IStoreOperationTracker _operationTracker;
	private readonly IUiTransport _transport;
	private readonly ILogger _logger;

	public StoreUninstallService(IStoreInstallationStore installations,
		IPluginInstaller pluginInstaller,
		IIconPackService iconPackService,
		IProfileService profileService,
		IStoreUpdateDetector updateDetector,
		IStoreOperationTracker operationTracker,
		IUiTransport transport,
		ILogger logger)
	{
		_installations = installations;
		_pluginInstaller = pluginInstaller;
		_iconPackService = iconPackService;
		_profileService = profileService;
		_updateDetector = updateDetector;
		_operationTracker = operationTracker;
		_transport = transport;
		_logger = logger;
	}

	public async Task<Result<StoreUninstallError>> Uninstall(StoreExtensionKind kind,
		string packageId,
		CancellationToken cancellationToken = default)
	{
		// A live install/update for the same package racing an uninstall can leave the executor
		// operating on a pack the uninstall just deleted - refuse rather than let the two interleave.
		if (_operationTracker.FindLive(kind, packageId) is not null)
		{
			return Result.Fail<StoreUninstallError>(StoreUninstallError.OperationInProgress);
		}

		var result = kind switch
		{
			StoreExtensionKind.Plugin => await UninstallPlugin(packageId, cancellationToken),
			StoreExtensionKind.IconPack => await UninstallIconPack(packageId),
			StoreExtensionKind.ProfileTemplate => await UninstallProfileTemplate(packageId),
			_ => Result.Fail<StoreUninstallError>(StoreUninstallError.Failed)
		};

		if (result.Success)
		{
			await PublishCatalogChanged(cancellationToken);
		}

		return result;
	}

	private async Task<Result<StoreUninstallError>> UninstallPlugin(string pluginId,
		CancellationToken cancellationToken)
	{
		var request = new PluginUninstallRequest { KeepData = true, Force = false };
		var result = await _pluginInstaller.Uninstall(pluginId, request, cancellationToken);
		if (result.Success)
		{
			return Result.Ok<StoreUninstallError>();
		}

		return Result.Fail(ToUninstallError(result.Error), result.ErrorMessage);
	}

	private async Task<Result<StoreUninstallError>> UninstallIconPack(string packageId)
	{
		var record = _installations.Find(StoreExtensionKind.IconPack, packageId);
		if (record is null)
		{
			return Result.Fail<StoreUninstallError>(StoreUninstallError.NotInstalled);
		}

		if (record.TargetIds.Count == 0)
		{
			_installations.Delete(StoreExtensionKind.IconPack, packageId);
			return Result.Ok<StoreUninstallError>();
		}

		// StoreIconPackOwner.Release already deletes the installation record and refreshes the update
		// state as part of a successful Delete - deleting it again here would be redundant, and deleting
		// it before Delete succeeds would strand the pack with no record to reconcile against.
		var deleted = await _iconPackService.Delete(record.TargetIds[0]);
		if (deleted.Success)
		{
			// StoreIconPackOwner.Release should already have deleted this record, but Owns() depends on
			// SourceType/SourceId/TargetIds staying in sync with the pack - if any of those drifted, the
			// pack is gone and the record would otherwise strand behind, keeping the Store reporting
			// Installed for something that no longer exists.
			if (_installations.Find(StoreExtensionKind.IconPack, packageId) is not null)
			{
				_installations.Delete(StoreExtensionKind.IconPack, packageId);
			}

			return Result.Ok<StoreUninstallError>();
		}

		if (deleted.Error == IconPackError.NotFound)
		{
			_installations.Delete(StoreExtensionKind.IconPack, packageId);
			return Result.Ok<StoreUninstallError>();
		}

		_logger.Warning("Failed to uninstall icon pack {PackageId}: {Error}", packageId, deleted.Error);
		return Result.Fail<StoreUninstallError>(StoreUninstallError.Failed, deleted.ErrorMessage);
	}

	private async Task<Result<StoreUninstallError>> UninstallProfileTemplate(string packageId)
	{
		var record = _installations.Find(StoreExtensionKind.ProfileTemplate, packageId);
		if (record is null)
		{
			return Result.Fail<StoreUninstallError>(StoreUninstallError.NotInstalled);
		}

		if (record.TargetIds.Count == 0)
		{
			_installations.Delete(StoreExtensionKind.ProfileTemplate, packageId);
			return Result.Ok<StoreUninstallError>();
		}

		var deleted = await _profileService.Delete(record.TargetIds[0]);
		if (!deleted.Success)
		{
			if (deleted.Error == ProfileError.CannotDeleteLastProfile)
			{
				return Result.Fail<StoreUninstallError>(StoreUninstallError.LastProfileProtected, deleted.ErrorMessage);
			}

			if (deleted.Error == ProfileError.NotFound)
			{
				_installations.Delete(StoreExtensionKind.ProfileTemplate, packageId);
				return Result.Ok<StoreUninstallError>();
			}

			_logger.Warning("Failed to uninstall profile template {PackageId}: {Error}", packageId, deleted.Error);
			return Result.Fail<StoreUninstallError>(StoreUninstallError.Failed, deleted.ErrorMessage);
		}

		// Nothing else prunes a ProfileTemplate installation record, so it is deleted here once the
		// profile it created is confirmed gone.
		_installations.Delete(StoreExtensionKind.ProfileTemplate, packageId);
		return Result.Ok<StoreUninstallError>();
	}

	private async Task PublishCatalogChanged(CancellationToken cancellationToken)
	{
		try
		{
			await _transport.SendToGroup(UiAdminGroups.Admin, new StoreCatalogChangedEvent(), cancellationToken);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.Warning(ex, "Failed to broadcast the store catalog change after an uninstall");
		}

		try
		{
			_updateDetector.Check();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to refresh store update state after an uninstall");
		}
	}

	private static StoreUninstallError ToUninstallError(PluginInstallError? error) => error switch
	{
		PluginInstallError.NotInstalled => StoreUninstallError.NotInstalled,
		PluginInstallError.DependencyInUse => StoreUninstallError.DependencyInUse,
		_ => StoreUninstallError.Failed
	};
}
