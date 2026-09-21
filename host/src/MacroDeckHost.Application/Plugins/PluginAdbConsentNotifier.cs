using System.Collections.Concurrent;
using MacroDeck.Localization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Plugins;

public interface IPluginAdbConsentNotifier
{
	Task NotifyIfNeededAsync(string pluginId,
		string pluginName,
		bool declaresAdb,
		bool previouslyDeclaredAdb,
		CancellationToken cancellationToken = default);

	Task AskAfterRefusalAsync(string pluginId, string pluginName, CancellationToken cancellationToken = default);

	void Dismiss(string pluginId);

	void DismissAll();
}

public sealed class PluginAdbConsentNotifier(
	IUserNotificationStore store,
	IServiceScopeFactory scopeFactory,
	ILocalizationResolver localization,
	IAdbManager? adbManager = null) : IPluginAdbConsentNotifier
{
	private readonly ConcurrentDictionary<string, byte> _raisedKeys = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, byte> _askedAfterRefusal = new(StringComparer.Ordinal);

	public async Task NotifyIfNeededAsync(string pluginId,
		string pluginName,
		bool declaresAdb,
		bool previouslyDeclaredAdb,
		CancellationToken cancellationToken = default)
	{
		if (!declaresAdb || previouslyDeclaredAdb)
		{
			return;
		}

		await using var scope = scopeFactory.CreateAsyncScope();
		var settings = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetAdb();
		var adbMissing = adbManager?.Status is { Supported: true, ResolvedExecutablePath: null };
		if (settings.Enabled && settings.AllowPlugins && !adbMissing)
		{
			return;
		}

		var culture = await ActiveLocalization.Culture(scopeFactory);
		var dedupeKey = KeyFor(pluginId);
		_raisedKeys[dedupeKey] = 0;

		store.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.Security,
			Title = localization.Resolve(AppStrings.Notifications.PluginAdbAccessRequested(), culture) ?? pluginName,
			Message = localization.Resolve(!settings.Enabled
					? AppStrings.Notifications.PluginAdbAccessRequestedAdbOffMessage(name: pluginName)
					: !settings.AllowPlugins
						? AppStrings.Notifications.PluginAdbAccessRequestedMessage(name: pluginName)
						: AppStrings.Notifications.PluginAdbAccessRequestedAdbMissingMessage(name: pluginName),
				culture),
			SourceId = pluginId,
			SourceName = pluginName,
			Actions =
			[
				new UserNotificationAction(UserNotificationActionKind.EnablePluginAdb, null),
				new UserNotificationAction(UserNotificationActionKind.DismissNotification, null)
			],
			DedupeKey = dedupeKey
		});
	}

	public Task AskAfterRefusalAsync(string pluginId, string pluginName, CancellationToken cancellationToken = default)
		=> _askedAfterRefusal.TryAdd(pluginId, 0)
			? NotifyIfNeededAsync(pluginId, pluginName, declaresAdb: true, previouslyDeclaredAdb: false, cancellationToken)
			: Task.CompletedTask;

	public void Dismiss(string pluginId)
	{
		var key = KeyFor(pluginId);
		store.DismissByKey(key);
		_raisedKeys.TryRemove(key, out _);
		_askedAfterRefusal.TryRemove(pluginId, out _);
	}

	// DismissByKey rather than Retire: a retired key stays reserved and would never ask again.
	public void DismissAll()
	{
		foreach (var key in _raisedKeys.Keys)
		{
			store.DismissByKey(key);
			_raisedKeys.TryRemove(key, out _);
		}

		_askedAfterRefusal.Clear();
	}

	private static string KeyFor(string pluginId) => $"plugin-adb-access:{pluginId}";
}
