using MacroDeckHost.Application.Devices;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class DeviceStartupProfileCleanupHandler : INotificationHandler<ProfileDeletedNotification>
{
	private readonly IServiceScopeFactory _scopeFactory;

	public DeviceStartupProfileCleanupHandler(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public async ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var deviceService = scope.ServiceProvider.GetRequiredService<IDeviceService>();

		await deviceService.ClearStartupProfileAssignments(notification.ProfileId.ToString());
	}
}
