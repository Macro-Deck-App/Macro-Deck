using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class DeviceChangedNotificationHandler : INotificationHandler<DeviceChangedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IServiceScopeFactory _scopeFactory;

	public DeviceChangedNotificationHandler(IUiTransport uiTransport, IServiceScopeFactory scopeFactory)
	{
		_uiTransport = uiTransport;
		_scopeFactory = scopeFactory;
	}

	public async ValueTask Handle(DeviceChangedNotification notification, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var deviceRepository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
		var deviceService = scope.ServiceProvider.GetRequiredService<IDeviceService>();

		var device = await deviceRepository.GetById(notification.DeviceId);
		if (device is null)
		{
			return;
		}

		var dto = await deviceService.ToDto(device);
		await _uiTransport.SendToGroup(UiAdminGroups.Admin, new DeviceChangedEvent { Device = dto }, cancellationToken);
	}
}
