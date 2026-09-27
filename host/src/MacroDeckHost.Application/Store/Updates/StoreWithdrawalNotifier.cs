using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Store.Updates;

// Raised regardless of the update notification preferences. A dismissal lasts until the host restarts,
// so a withdrawn version that is still installed is reported again after a restart.
public sealed class StoreWithdrawalNotifier : IDisposable
{
	public const string DedupeKeyPrefix = "store-withdrawn:";

	private readonly IUserNotificationStore _store;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Dictionary<string, StoreInstalledWithdrawal> _raised = new(StringComparer.Ordinal);

	public StoreWithdrawalNotifier(IUserNotificationStore store, IServiceScopeFactory scopeFactory)
	{
		_store = store;
		_scopeFactory = scopeFactory;
	}

	public async Task Notify(IReadOnlyList<StoreInstalledWithdrawal> withdrawals,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var current = new Dictionary<string, StoreInstalledWithdrawal>(StringComparer.Ordinal);
			foreach (var withdrawal in withdrawals)
			{
				current.TryAdd(DedupeKey(withdrawal), withdrawal);
			}

			foreach (var key in _raised.Keys.Where(key => !current.ContainsKey(key)).ToList())
			{
				_store.Retire(key);
				_raised.Remove(key);
			}

			foreach (var (key, withdrawal) in current)
			{
				if (_raised.TryGetValue(key, out var previous) && previous == withdrawal)
				{
					continue;
				}

				_store.Raise(await Draft(key, withdrawal));
				_raised[key] = withdrawal;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose() => _gate.Dispose();

	public static string DedupeKey(StoreInstalledWithdrawal withdrawal) =>
		$"{DedupeKeyPrefix}{withdrawal.Kind}:{withdrawal.PackageId.ToLowerInvariant()}";

	private async Task<UserNotificationDraft> Draft(string key, StoreInstalledWithdrawal withdrawal)
	{
		var lines = new List<string>();
		if (!string.IsNullOrWhiteSpace(withdrawal.Reason))
		{
			lines.Add(await ActiveLocalization.Resolve(_scopeFactory,
				AppStrings.Store.Withdrawal.Reason(reason: withdrawal.Reason)));
		}

		if (!string.IsNullOrWhiteSpace(withdrawal.Replacement))
		{
			lines.Add(await ActiveLocalization.Resolve(_scopeFactory,
				AppStrings.Store.Withdrawal.Replacement(replacement: withdrawal.Replacement)));
		}

		if (!withdrawal.Listed)
		{
			lines.Add(await ActiveLocalization.Resolve(_scopeFactory,
				AppStrings.Notifications.StoreVersionWithdrawnRemoveHint()));
		}

		return new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.Security,
			Title = await ActiveLocalization.Resolve(_scopeFactory,
				AppStrings.Notifications.StoreVersionWithdrawn(name: withdrawal.Name, version: withdrawal.InstalledVersion)),
			Message = lines.Count == 0 ? null : string.Join("\n", lines),
			Action = withdrawal.Listed
				? new UserNotificationAction(UserNotificationActionKind.OpenExtensionStore, StoreUpdateNotifier.InstalledTarget)
				: null,
			DedupeKey = key
		};
	}
}
