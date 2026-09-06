using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class FolderFocusRuleChangedNotificationHandler : INotificationHandler<FolderFocusRuleChangedNotification>
{
	private readonly IFolderCache _folderCache;
	private readonly IUiTransport _uiTransport;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IApplicationFocusCoordinator _coordinator;

	public FolderFocusRuleChangedNotificationHandler(
		IFolderCache folderCache,
		IUiTransport uiTransport,
		IServiceScopeFactory scopeFactory,
		IApplicationFocusCoordinator coordinator)
	{
		_folderCache = folderCache;
		_uiTransport = uiTransport;
		_scopeFactory = scopeFactory;
		_coordinator = coordinator;
	}

	public async ValueTask Handle(FolderFocusRuleChangedNotification notification, CancellationToken cancellationToken)
	{
		await _coordinator.OnRulesChanged(cancellationToken);

		var folder = _folderCache.GetFolderById(notification.FolderId);
		if (folder is null)
		{
			return;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var deviceRepository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
		var devices = (await deviceRepository.GetAll()).ToDictionary(d => d.Id);

		var evt = new FolderFocusRuleChangedEvent
		{
			FolderId = folder.Id.ToString(),
			Rules = folder.FocusRules
				.Select(rule =>
					FolderFocusRuleDtoMapper.MapToDto(folder, rule, devices.GetValueOrDefault(rule.DeviceId)))
				.ToList()
		};

		await _uiTransport.SendToGroup(UiAdminGroups.Admin, evt, cancellationToken);
	}
}
