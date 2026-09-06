using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.System;

public class GetLockStateRequestMessageHandlerTests
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

	[Test]
	public async Task Reports_the_three_facts_independently()
	{
		var repository = new FakeAppPreferenceRepository();
		var preferences = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());
		await preferences.SetLockScreen(false);
		var lockState = new FakeHostLockState { IsSupported = true, IsLocked = true };
		var handler = new GetLockStateRequestMessageHandler(lockState, preferences);

		var response = await handler.Handle(new GetLockStateRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Locked, Is.True);
			Assert.That(response.LockScreenEnabled, Is.False);
			Assert.That(response.Supported, Is.True);
		});
	}

	[Test]
	public async Task Unsupported_platform_reports_not_locked_and_not_supported()
	{
		var repository = new FakeAppPreferenceRepository();
		var preferences = new AppPreferenceService(repository, new FakeBuildEnvironment(), new FakeHostListenerState());
		var lockState = new FakeHostLockState { IsSupported = false, IsLocked = false };
		var handler = new GetLockStateRequestMessageHandler(lockState, preferences);

		var response = await handler.Handle(new GetLockStateRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Locked, Is.False);
			Assert.That(response.Supported, Is.False);
		});
	}
}
