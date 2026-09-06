using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class LockScreenSettingsHandlersTests
{
	private sealed class FakeAppPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}
	}

	private sealed class FakeBuildEnvironment : IBuildEnvironment
	{
		public string Version => "0.0.0-test";

		public bool IsBeta => false;

		public BuildChannel Channel => BuildChannel.Production;
	}

	private static AppPreferenceService CreateService()
		=> new(new FakeAppPreferenceRepository(), new FakeBuildEnvironment(), new FakeHostListenerState());

	[Test]
	public async Task Default_is_disabled()
	{
		var service = CreateService();
		var handler = new GetLockScreenSettingsRequestMessageHandler(service);

		var response = await handler.Handle(new GetLockScreenSettingsRequest(), CancellationToken.None);

		Assert.That(response.Enabled, Is.False);
	}

	[Test]
	public async Task Update_and_get_round_trip()
	{
		var service = CreateService();
		var updateHandler = new UpdateLockScreenSettingsRequestMessageHandler(service,
			new FakeHostLockState(),
			new RecordingMediator());
		var getHandler = new GetLockScreenSettingsRequestMessageHandler(service);

		var updated = await updateHandler.Handle(new UpdateLockScreenSettingsRequest { Enabled = true },
			CancellationToken.None);
		var reloaded = await getHandler.Handle(new GetLockScreenSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(updated.Enabled, Is.True);
			Assert.That(reloaded.Enabled, Is.True);
		});
	}

	[Test]
	public async Task Update_publishes_the_live_lock_state_event()
	{
		var service = CreateService();
		var lockState = new FakeHostLockState { IsSupported = true, IsLocked = true };
		var mediator = new RecordingMediator();
		var updateHandler = new UpdateLockScreenSettingsRequestMessageHandler(service, lockState, mediator);

		await updateHandler.Handle(new UpdateLockScreenSettingsRequest { Enabled = true }, CancellationToken.None);

		var published = mediator.Published.OfType<HostLockStateChangedNotification>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(published.LockScreenEnabled, Is.True);
			Assert.That(published.Locked, Is.True);
			Assert.That(published.Supported, Is.True);
		});
	}
}
