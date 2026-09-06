using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Ui.Handlers;

// Registered as a singleton (see Startup.AddUiTransport override) so the availability and
// downloading dedupe state below survives across requests - each report from the bootstrapper
// arrives as its own scoped call, and there is nowhere else to remember what was last announced.
public class ReportUpdateStateRequestMessageHandler
	: IUiTransportMessageHandler<ReportUpdateStateRequest, ReportUpdateStateResponse>
{
	private const string DedupeKey = "update";

	private readonly IUserNotificationStore _store;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	private readonly object _stateLock = new();
	private string? _lastAnnouncedAvailableVersion;
	private bool _downloadRaisedForCurrentPhase;

	public ReportUpdateStateRequestMessageHandler(
		IUserNotificationStore store,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_store = store;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public async ValueTask<ReportUpdateStateResponse> Handle(
		ReportUpdateStateRequest request,
		CancellationToken cancellationToken)
	{
		switch (request.Phase)
		{
			case "available":
				await HandleAvailable(request);
				break;

			case "downloading":
				await HandleDownloading(request);
				break;

			case "downloaded":
				await HandleDownloaded(request);
				break;

			case "failed":
				await HandleFailed(request);
				break;

			case "cancelled":
				lock (_stateLock)
				{
					_downloadRaisedForCurrentPhase = false;
					// A cancelled download's version was already announced once; without
					// clearing this, a fresh "available" report for that same version would
					// hit the dedupe below and never re-raise, leaving the user with no
					// entry at all once the cancelled one is retired.
					_lastAnnouncedAvailableVersion = null;
				}

				_store.Retire(DedupeKey);
				break;

			case "upToDate":
			case "installing":
				lock (_stateLock)
				{
					_downloadRaisedForCurrentPhase = false;
				}

				_store.DismissByKey(DedupeKey);
				break;
		}

		return new ReportUpdateStateResponse { Success = true };
	}

	private async ValueTask HandleAvailable(ReportUpdateStateRequest request)
	{
		lock (_stateLock)
		{
			if (_lastAnnouncedAvailableVersion == request.Version)
			{
				return;
			}

			_lastAnnouncedAvailableVersion = request.Version;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		List<UserNotificationAction> actions =
		[
			new(UserNotificationActionKind.OpenUpdateDetails, request.Version)
		];
		if (request.CanInstall)
		{
			actions.Add(new UserNotificationAction(UserNotificationActionKind.InstallUpdate, request.Version));
		}

		actions.Add(new UserNotificationAction(UserNotificationActionKind.DismissNotification, null));

		_store.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.Update,
			Title = _localization.Resolve(AppStrings.Notifications.UpdateAvailable(version: request.Version),
					culture) ??
				request.Version,
			Message = _localization.Resolve(AppStrings.Notifications.UpdateAvailableMessage(version: request.Version),
				culture),
			Actions = actions,
			DedupeKey = DedupeKey
		});
	}

	private async ValueTask HandleDownloading(ReportUpdateStateRequest request)
	{
		bool isFirstReport;
		lock (_stateLock)
		{
			isFirstReport = !_downloadRaisedForCurrentPhase;
			_downloadRaisedForCurrentPhase = true;
		}

		var progress = new UserNotificationProgress(request.Percent ?? 0, 100);

		if (!isFirstReport)
		{
			_store.UpdateProgress(DedupeKey, progress);
			return;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		_store.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.Update,
			Title
				= _localization.Resolve(AppStrings.Notifications.UpdateDownloading(version: request.Version),
					culture) ??
				request.Version,
			Progress = progress,
			CancelKey = DedupeKey,
			Actions = [new UserNotificationAction(UserNotificationActionKind.OpenUpdateDetails, request.Version)],
			DedupeKey = DedupeKey
		});
	}

	private async ValueTask HandleDownloaded(ReportUpdateStateRequest request)
	{
		lock (_stateLock)
		{
			_downloadRaisedForCurrentPhase = false;
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		_store.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Info,
			Kind = UserNotificationKind.Update,
			Title = _localization.Resolve(AppStrings.Notifications.UpdateReady(version: request.Version), culture) ??
				request.Version,
			Actions =
			[
				new UserNotificationAction(UserNotificationActionKind.OpenUpdateDetails, request.Version),
				new UserNotificationAction(UserNotificationActionKind.InstallUpdate, request.Version),
				new UserNotificationAction(UserNotificationActionKind.DismissNotification, null)
			],
			DedupeKey = DedupeKey
		});
	}

	private async ValueTask HandleFailed(ReportUpdateStateRequest request)
	{
		lock (_stateLock)
		{
			_downloadRaisedForCurrentPhase = false;
		}

		_store.Retire(DedupeKey);

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		_store.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.Update,
			Title = _localization.Resolve(AppStrings.Notifications.UpdateFailed(version: request.Version), culture) ??
				request.Version,
			Message = request.Error,
			Actions =
			[
				new UserNotificationAction(UserNotificationActionKind.OpenUpdateDetails, request.Version),
				new UserNotificationAction(UserNotificationActionKind.DismissNotification, null)
			],
			DedupeKey = DedupeKey
		});
	}
}
