using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Application.Plugins.Pairing;

/// <summary>
/// Orchestrates interactive plugin pairing on top of <see cref="IPluginPairingRequestStore" />. Scoped,
/// not singleton: it depends on <see cref="IPluginRegistrationService" />, which is itself scoped
/// because it talks to the database, so a singleton here would capture that scoped dependency for the
/// lifetime of the app.
/// </summary>
public sealed class PluginPairingService : IPluginPairingService
{
	private readonly IPluginPairingRequestStore _store;
	private readonly IAppPreferenceService _preferences;
	private readonly IPluginRegistrationRepository _registrationRepository;
	private readonly IPluginRegistrationService _registrationService;
	private readonly IPluginInstallationCatalog _catalog;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginSupervisor _supervisor;
	private readonly IPluginTakeoverRegistry _takeovers;

	public PluginPairingService(
		IPluginPairingRequestStore store,
		IAppPreferenceService preferences,
		IPluginRegistrationRepository registrationRepository,
		IPluginRegistrationService registrationService,
		IPluginInstallationCatalog catalog,
		IPluginSessionRegistry sessionRegistry,
		IPluginSupervisor supervisor,
		IPluginTakeoverRegistry takeovers)
	{
		_store = store;
		_preferences = preferences;
		_registrationRepository = registrationRepository;
		_registrationService = registrationService;
		_catalog = catalog;
		_sessionRegistry = sessionRegistry;
		_supervisor = supervisor;
		_takeovers = takeovers;
	}

	public async Task<PluginPairingCreateOutcome> Create(string pluginId,
		string displayName,
		string codeChallenge,
		string codeChallengeMethod,
		PluginPairingClientInfo? client,
		bool arrivedOnPublicListener)
	{
		var developerMode = await _preferences.GetDeveloper();
		if (!developerMode.Enabled)
		{
			return PluginPairingCreateOutcome.Fail(PluginPairingCreateError.DeveloperModeDisabled);
		}

		if (!PluginId.TryValidate(pluginId, out var pluginIdError))
		{
			return PluginPairingCreateOutcome.Fail(PluginPairingCreateError.InvalidPayload, pluginIdError);
		}

		if (!string.Equals(codeChallengeMethod, PluginPairingChallengeMethods.S256, StringComparison.Ordinal))
		{
			return PluginPairingCreateOutcome.Fail(PluginPairingCreateError.InvalidPayload,
				"codeChallengeMethod must be S256.");
		}

		if (!PluginPairingCodeChallenge.IsWellFormed(codeChallenge))
		{
			return PluginPairingCreateOutcome.Fail(PluginPairingCreateError.InvalidPayload,
				"codeChallenge is malformed.");
		}

		var sanitizedDisplayName = PluginPairingClientSanitizer.SanitizeDisplayName(displayName);
		var sanitizedClient = PluginPairingClientSanitizer.Sanitize(client);

		var created = _store.Create(pluginId,
			sanitizedDisplayName,
			codeChallenge,
			sanitizedClient,
			arrivedOnPublicListener);
		if (!created.Succeeded)
		{
			return PluginPairingCreateOutcome.Fail(created.Failure switch
			{
				PluginPairingCreateFailure.DuplicateRequest => PluginPairingCreateError.DuplicateRequest,
				_ => PluginPairingCreateError.CapacityExceeded
			});
		}

		return PluginPairingCreateOutcome.Success(created.Record!, IsInstalled(pluginId));
	}

	public PluginPairingStatusOutcome Status(string requestId)
	{
		var record = _store.Find(requestId);
		if (record is null)
		{
			// An unknown or pruned id answers `expired` rather than a not-found: the plugin's polling
			// state machine only ever needs to reach a terminal status, and answering identically for
			// "never existed" and "existed, but is gone" means this endpoint cannot be used to probe
			// which request ids are live.
			return new PluginPairingStatusOutcome(PluginPairingStatuses.Expired, DateTimeOffset.UtcNow);
		}

		var status = record.State switch
		{
			PluginPairingRequestState.Pending => PluginPairingStatuses.Pending,
			PluginPairingRequestState.Approved => PluginPairingStatuses.Approved,
			PluginPairingRequestState.Redeeming => PluginPairingStatuses.Approved,
			PluginPairingRequestState.Rejected => PluginPairingStatuses.Rejected,
			// A failed redemption is terminal and not retryable - reported as rejected, the closest of
			// the four wire statuses, rather than inventing a fifth one the frozen protocol doesn't have.
			PluginPairingRequestState.Failed => PluginPairingStatuses.Rejected,
			_ => PluginPairingStatuses.Expired
		};

		return new PluginPairingStatusOutcome(status, record.ExpiresAt);
	}

	public async Task<IReadOnlyList<PluginPairingPendingItem>> Pending()
	{
		var pending = _store.Snapshot().Where(record => record.State == PluginPairingRequestState.Pending).ToList();
		var items = new List<PluginPairingPendingItem>(pending.Count);

		foreach (var record in pending)
		{
			var existing = await _registrationRepository.GetByPluginId(record.PluginId);
			var replaces = existing is { RevokedAt: null };

			items.Add(new PluginPairingPendingItem(record.RequestId,
				record.PluginId,
				record.DisplayName,
				record.Client,
				record.CreatedAt,
				record.ExpiresAt,
				replaces,
				replaces ? existing!.Origin : null,
				replaces ? existing!.CreatedAt : null,
				record.ArrivedOnPublicListener,
				IsInstalled(record.PluginId)));
		}

		return items;
	}

	public async Task<PluginPairingApproveOutcome> Approve(string requestId,
		bool replaceExistingRegistration,
		bool takeOverInstalledPlugin = false)
	{
		var record = _store.Find(requestId);
		if (record is null || record.State != PluginPairingRequestState.Pending)
		{
			return PluginPairingApproveOutcome.Fail(PluginPairingApproveError.NotFound);
		}

		var existing = await _registrationRepository.GetByPluginId(record.PluginId);

		// Replacement is an explicit, separately confirmed act: re-reading the registration here, right
		// before approving, and refusing unless the caller has already said yes to replacing it, is what
		// stops a stale UI or a mis-clicked approval from silently retiring an existing plugin's secret.
		if (existing is { RevokedAt: null } && !replaceExistingRegistration)
		{
			return PluginPairingApproveOutcome.Fail(PluginPairingApproveError.ReplacementNotConfirmed);
		}

		if (IsInstalled(record.PluginId) && !takeOverInstalledPlugin)
		{
			return PluginPairingApproveOutcome.Fail(PluginPairingApproveError.TakeoverNotConfirmed);
		}

		if (!_store.Approve(requestId, replaceExistingRegistration, takeOverInstalledPlugin))
		{
			return PluginPairingApproveOutcome.Fail(PluginPairingApproveError.NotFound);
		}

		return PluginPairingApproveOutcome.Success;
	}

	public bool Reject(string requestId) => _store.Reject(requestId);

	public async Task<PluginPairingRedeemOutcome> Redeem(string requestId, string codeVerifier)
	{
		var developerMode = await _preferences.GetDeveloper();
		if (!developerMode.Enabled)
		{
			// Developer Mode is a kill switch, not just a creation-time gate: "pairing requires an
			// explicitly enabled Developer Mode" has to hold continuously, or turning it back off would
			// not actually stop an approval a developer left outstanding from completing later.
			return PluginPairingRedeemOutcome.Fail;
		}

		var redeemed = _store.TryRedeem(requestId, codeVerifier);
		if (!redeemed.Succeeded)
		{
			return PluginPairingRedeemOutcome.Fail;
		}

		var record = redeemed.Record!;

		// All database mutation happens here, at redemption, and never at Approve. A crash, a closed
		// desktop UI, or a plugin process that exits between Approve and Redeem must not leave the host
		// owning an active registration whose secret nobody holds - that gap is exactly the unrecoverable
		// dead-end interactive pairing exists to remove. The registration only comes into being once the
		// plugin has proven, by producing the verifier, that it is still there to receive the secret.
		var result = IsInstalled(record.PluginId)
			? await RedeemTakeover(record)
			: await RegisterOrReplace(record, allowInstalledId: false);

		_store.CompleteRedemption(requestId, result.Succeeded);

		return result.Succeeded
			? PluginPairingRedeemOutcome.Success(result.Registration!.PluginId, result.PluginSecret!)
			: PluginPairingRedeemOutcome.Fail;
	}

	private async Task<PluginRegistrationResult> RedeemTakeover(PluginPairingRequestRecord record)
	{
		var pluginId = record.PluginId;
		if (!record.TakeOverInstalledPlugin)
		{
			return PluginRegistrationResult.Fail(PluginRegistrationError.PluginInstalled);
		}

		var existingTicket = record.ReplaceExistingRegistration ? _takeovers.CurrentTicket(pluginId) : null;
		var began = existingTicket is null;
		var ticket = existingTicket ?? _takeovers.Begin(pluginId);

		PluginRegistrationResult result;
		try
		{
			if (began)
			{
				await _supervisor.SuspendForTakeover(pluginId);
			}

			result = await RegisterOrReplace(record, allowInstalledId: true);
		}
		catch
		{
			if (began)
			{
				_takeovers.Finish(pluginId);
			}

			throw;
		}

		if (!result.Succeeded)
		{
			if (began)
			{
				_takeovers.Finish(pluginId);
			}

			return result;
		}

		// Developer Mode, a revoke, an uninstall or an install can all land while the credential is
		// written. Whatever raced in, no credential for an installed id may outlive its takeover.
		var registration = await _registrationRepository.GetByPluginId(pluginId);
		if (!(await _preferences.GetDeveloper()).Enabled ||
			!_takeovers.IsCurrent(pluginId, ticket) ||
			registration is not { RevokedAt: null })
		{
			await _registrationService.Revoke(pluginId);
			return PluginRegistrationResult.Fail(PluginRegistrationError.PluginInstalled);
		}

		return result;
	}

	private Task<PluginRegistrationResult> RegisterOrReplace(PluginPairingRequestRecord record, bool allowInstalledId)
		=> record.ReplaceExistingRegistration
			? _registrationService.ReplaceSecret(record.PluginId,
				record.DisplayName,
				accessTokenId: null,
				PluginRegistrationOrigins.Pairing)
			: _registrationService.Register(record.PluginId,
				record.DisplayName,
				accessTokenId: null,
				PluginRegistrationOrigins.Pairing,
				allowInstalledId);

	public async Task<IReadOnlyList<PluginPairedRegistration>> PairedRegistrations()
	{
		var registrations = await _registrationRepository.GetNonRevoked();
		var sessions = _sessionRegistry.Snapshot();
		var onlinePluginIds = sessions
			.Where(session => session.State != PluginSessionState.Dropped)
			.Select(session => session.PluginId)
			.ToHashSet(StringComparer.Ordinal);

		return registrations
			.Where(registration =>
				string.Equals(registration.Origin, PluginRegistrationOrigins.Pairing, StringComparison.Ordinal))
			.Select(registration => new PluginPairedRegistration(registration.PluginId,
				registration.DisplayName,
				registration.CreatedAt,
				registration.LastSeenAt,
				onlinePluginIds.Contains(registration.PluginId),
				_takeovers.IsActive(registration.PluginId)))
			.ToList();
	}

	private bool IsInstalled(string pluginId)
		=> _catalog.Discover().Any(plugin =>
			string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal) && plugin.Versions.Count > 0);
}
