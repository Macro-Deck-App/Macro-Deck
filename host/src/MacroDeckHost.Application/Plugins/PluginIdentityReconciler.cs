using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Runtime;
using Serilog;

namespace MacroDeckHost.Application.Plugins;

public interface IPluginIdentityReconciler
{
	Task ReconcileAsync(CancellationToken cancellationToken);
}

public sealed class PluginIdentityReconciler : IPluginIdentityReconciler
{
	private readonly IPluginRegistrationRepository _registrationRepository;
	private readonly IPluginInstallationCatalog _catalog;
	private readonly IPluginRegistrationService _registrationService;
	private readonly ILogger _logger;

	public PluginIdentityReconciler(
		IPluginRegistrationRepository registrationRepository,
		IPluginInstallationCatalog catalog,
		IPluginRegistrationService registrationService,
		ILogger logger)
	{
		_registrationRepository = registrationRepository;
		_catalog = catalog;
		_registrationService = registrationService;
		_logger = logger.ForContext<PluginIdentityReconciler>();
	}

	public async Task ReconcileAsync(CancellationToken cancellationToken)
	{
		var nonRevoked = await _registrationRepository.GetNonRevoked();
		if (nonRevoked.Count == 0)
		{
			return;
		}

		// Same predicate PluginRegistrationService.Register (C1) and PluginInstaller.Uninstall use: a
		// plugin whose data directory survived an uninstall reports Versions.Count == 0 and must not be
		// treated as "installed" here either, or a re-enrollment after such an uninstall would find its
		// registration revoked out from under it on the very next startup.
		var installedIds = _catalog.Discover()
			.Where(plugin => plugin.Versions.Count > 0)
			.Select(plugin => plugin.PluginId)
			.ToHashSet(StringComparer.Ordinal);

		foreach (var registration in nonRevoked)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!installedIds.Contains(registration.PluginId))
			{
				continue;
			}

			// Revoke carries the full side-effect set an ad hoc revoke would have to duplicate: it
			// terminates any live session for the id, evicts the log rate-limit and compatibility state,
			// and clears the ingestor's edge-trigger memory.
			await _registrationService.Revoke(registration.PluginId);
			PluginRuntimeLog.IdentityReconciliationRevoked(_logger, registration.PluginId);
		}
	}
}
