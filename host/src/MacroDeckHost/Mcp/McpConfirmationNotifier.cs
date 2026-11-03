using MacroDeck.Localization;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Mcp;
using MacroDeckHost.Localization;
using MacroDeckHost.Platform.Notifications;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Mcp;

public sealed class McpConfirmationNotifier : IHostedService, IDisposable
{
	private const string DedupePrefix = "mcp-confirm:";

	private readonly IMcpConfirmationStore _store;
	private readonly IUserNotificationStore _notifications;
	private readonly IPlatformNotificationService _platform;
	private readonly IUiTransport _uiTransport;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _reconcile = new(1, 1);
	private readonly HashSet<string> _shown = new(StringComparer.Ordinal);

	public McpConfirmationNotifier(
		IMcpConfirmationStore store,
		IUserNotificationStore notifications,
		IPlatformNotificationService platform,
		IUiTransport uiTransport,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization,
		ILogger logger)
	{
		_store = store;
		_notifications = notifications;
		_platform = platform;
		_uiTransport = uiTransport;
		_scopeFactory = scopeFactory;
		_localization = localization;
		_logger = logger.ForContext<McpConfirmationNotifier>();
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_store.Changed += OnChanged;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_store.Changed -= OnChanged;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_store.Changed -= OnChanged;
		_reconcile.Dispose();
	}

	// The store raises Changed on the gate's request thread and from its expiry timer; the work runs
	// detached so neither waits for the UI, the notification list or an OS notification helper.
	private void OnChanged()
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await Reconcile();
			}
			catch (Exception exception)
			{
				_logger.Warning(exception, "Publishing the pending MCP confirmations failed");
			}
		});
	}

	private async Task Reconcile()
	{
		await _reconcile.WaitAsync();
		try
		{
			var pending = _store.PendingInApp();
			var pendingIds = pending.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
			foreach (var gone in _shown.Where(id => !pendingIds.Contains(id)).ToList())
			{
				_notifications.DismissByKey(DedupePrefix + gone);
				_shown.Remove(gone);
			}

			var added = pending.Where(entry => _shown.Add(entry.Id)).ToList();
			if (added.Count > 0)
			{
				var culture = await ActiveLocalization.Culture(_scopeFactory);
				foreach (var entry in added)
				{
					Raise(entry, culture);
				}
			}

			await _uiTransport.SendToGroup(UiAdminGroups.Admin, new McpConfirmationsChangedEvent());
		}
		finally
		{
			_reconcile.Release();
		}
	}

	private void Raise(McpConfirmationEntry entry, string? culture)
	{
		var title = _localization.Resolve(AppStrings.Notifications.McpConfirmationRequested(), culture)
			?? entry.ClientName;
		var message = _localization.Resolve(AppStrings.Notifications.McpConfirmationRequestedMessage(entry.ClientName),
				culture)
			?? entry.ClientName;
		_notifications.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.Security,
			Title = title,
			Message = message,
			SourceName = entry.ClientName,
			DedupeKey = DedupePrefix + entry.Id
		});

		if (!_platform.IsSupported)
		{
			return;
		}

		_ = Task.Run(async () =>
			{
				try
				{
					await _platform.ShowAsync(title, message, CancellationToken.None);
				}
				catch (Exception exception)
				{
					_logger.Debug(exception, "Showing the MCP confirmation system notification failed");
				}
			},
			CancellationToken.None);
	}
}
