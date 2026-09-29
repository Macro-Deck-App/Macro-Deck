using MacroDeck.Localization;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ReportInstallationIntegrityRequestMessageHandler
	: IUiTransportMessageHandler<ReportInstallationIntegrityRequest, ReportInstallationIntegrityResponse>
{
	public const string DedupeKey = "installation.damaged";

	public const string TroubleshootingSection = "macro-deck-was-not-updated-completely";

	private const string LinuxPackage = "linuxPackage";

	private readonly IUserNotificationStore _store;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILocalizationResolver _localization;

	public ReportInstallationIntegrityRequestMessageHandler(
		IUserNotificationStore store,
		IServiceScopeFactory scopeFactory,
		ILocalizationResolver localization)
	{
		_store = store;
		_scopeFactory = scopeFactory;
		_localization = localization;
	}

	public async ValueTask<ReportInstallationIntegrityResponse> Handle(
		ReportInstallationIntegrityRequest request,
		CancellationToken cancellationToken)
	{
		if (request.Status != "damaged")
		{
			return new ReportInstallationIntegrityResponse { Accepted = true };
		}

		var culture = await ActiveLocalization.Culture(_scopeFactory);
		var packageManaged = request.InstallKind == LinuxPackage;

		List<UserNotificationAction> actions = [];
		if (!packageManaged)
		{
			actions.Add(new UserNotificationAction(UserNotificationActionKind.OpenDownloadPage, null));
		}

		actions.Add(new UserNotificationAction(UserNotificationActionKind.OpenTroubleshootingGuide,
			TroubleshootingSection));
		actions.Add(new UserNotificationAction(UserNotificationActionKind.OpenLogs, null));

		_store.RaiseIfAbsent(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Error,
			Kind = UserNotificationKind.Error,
			Title = _localization.Resolve(AppStrings.OutdatedUi.Installation.Title(), culture),
			Message = _localization.Resolve(packageManaged
					? AppStrings.Notifications.InstallationDamagedPackageMessage()
					: AppStrings.Notifications.InstallationDamagedMessage(),
				culture),
			Actions = actions,
			DedupeKey = DedupeKey
		});

		return new ReportInstallationIntegrityResponse { Accepted = true };
	}
}
