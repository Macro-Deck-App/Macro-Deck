using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class LoggingSettingsHandlersTests
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

		public BuildChannel Channel { get; init; } = BuildChannel.Production;
	}

	private static AppPreferenceService CreateService(BuildChannel channel = BuildChannel.Production)
		=> new(new FakeAppPreferenceRepository(),
			new FakeBuildEnvironment { Channel = channel },
			new FakeHostListenerState());

	[Test]
	public async Task Get_handler_returns_the_channel_default_when_unset()
	{
		var handler = new GetLoggingSettingsRequestMessageHandler(CreateService(BuildChannel.Development));

		var response = await handler.Handle(new GetLoggingSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.MinimumLevel, Is.EqualTo(LogEntryLevel.Debug));
			Assert.That(response.DefaultMinimumLevel, Is.EqualTo(LogEntryLevel.Debug));
		});
	}

	[Test]
	public async Task Update_handler_persists_and_applies_the_level_to_the_live_state()
	{
		var service = CreateService();
		var state = new LogLevelState(LogEntryLevel.Information);
		var updateHandler = new UpdateLoggingSettingsRequestMessageHandler(service, state);
		var getHandler = new GetLoggingSettingsRequestMessageHandler(service);

		var applied = await updateHandler.Handle(
			new UpdateLoggingSettingsRequest { MinimumLevel = LogEntryLevel.Warning },
			CancellationToken.None);
		var reloaded = await getHandler.Handle(new GetLoggingSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(applied.MinimumLevel, Is.EqualTo(LogEntryLevel.Warning));
			Assert.That(reloaded.MinimumLevel, Is.EqualTo(LogEntryLevel.Warning));
			Assert.That(state.Minimum, Is.EqualTo(LogEntryLevel.Warning));
		});
	}

	[Test]
	public async Task Update_handler_without_a_level_resets_to_the_channel_default()
	{
		var service = CreateService(BuildChannel.Development);
		var state = new LogLevelState(LogEntryLevel.Information);
		var handler = new UpdateLoggingSettingsRequestMessageHandler(service, state);
		await handler.Handle(new UpdateLoggingSettingsRequest { MinimumLevel = LogEntryLevel.Fatal },
			CancellationToken.None);

		var applied = await handler.Handle(new UpdateLoggingSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(applied.MinimumLevel, Is.EqualTo(LogEntryLevel.Debug));
			Assert.That(state.Minimum, Is.EqualTo(LogEntryLevel.Debug));
		});
	}
}
