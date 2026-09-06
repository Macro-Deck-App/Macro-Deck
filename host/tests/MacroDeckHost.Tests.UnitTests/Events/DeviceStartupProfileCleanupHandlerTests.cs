using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Device = MacroDeckHost.Application.Ui.Transport.Messages.Devices.Device;

namespace MacroDeckHost.Tests.UnitTests.Events;

[TestFixture]
public class DeviceStartupProfileCleanupHandlerTests
{
	private RecordingDeviceService _deviceService = null!;
	private ServiceProvider _provider = null!;
	private DeviceStartupProfileCleanupHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_deviceService = new RecordingDeviceService();

		var services = new ServiceCollection();
		services.AddScoped<IDeviceService>(_ => _deviceService);
		_provider = services.BuildServiceProvider();

		_handler = new DeviceStartupProfileCleanupHandler(_provider.GetRequiredService<IServiceScopeFactory>());
	}

	[TearDown]
	public void TearDown() => _provider.Dispose();

	[Test]
	public async Task Handle_ProfileDeleted_ClearsAssignmentsForThatProfile()
	{
		var profileId = Guid.NewGuid();

		await _handler.Handle(new ProfileDeletedNotification(profileId), CancellationToken.None);

		Assert.That(_deviceService.ClearedProfileIds, Is.EqualTo(new[] { profileId.ToString() }));
	}

	private sealed class RecordingDeviceService : IDeviceService
	{
		public List<string> ClearedProfileIds { get; } = [];

		public Task ClearStartupProfileAssignments(string profileId)
		{
			ClearedProfileIds.Add(profileId);
			return Task.CompletedTask;
		}

		public Task<DeviceRegistrationResult> RegisterOrReuse(DeviceRegistration registration, DateTime now)
			=> throw new NotSupportedException();

		public Task PurgeStale(DateTime now) => throw new NotSupportedException();

		public Task<IReadOnlyList<Device>> GetAll() => throw new NotSupportedException();

		public Task<Device> ToDto(DeviceEntity device) => throw new NotSupportedException();

		public Task<Result<DeviceEntity, DeviceError>> Rename(Guid id, string name)
			=> throw new NotSupportedException();

		public Task<Result<DeviceError>> LogoutDevice(Guid id) => throw new NotSupportedException();

		public Task<Result<DeviceError>> RemoveDevice(Guid id) => throw new NotSupportedException();

		public Task<Result<DeviceEntity, DeviceError>> SetStartupProfile(Guid id, string? profileId)
			=> throw new NotSupportedException();

		public Task<string?> ResolveStartupProfileId(Guid deviceId) => throw new NotSupportedException();

		public Task<Result<DeviceError>> OpenProfileOnDevice(Guid id, string profileId)
			=> throw new NotSupportedException();
	}
}
