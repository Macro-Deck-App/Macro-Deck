using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class NetworkRestartNotifierTests
{
	private const int ActivePort = BuildConfig.DefaultPublicPort;

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

	private sealed class FakeRestartService : IApplicationRestartService
	{
		public RestartAvailability Availability { get; init; } = new(true, null);

		public bool RestartRequested => false;

		public Result<RestartError> Request(string reason) => Result.Ok<RestartError>();
	}

	private sealed record Fixture(
		IAppPreferenceService Preferences,
		UserNotificationStore Notifications,
		NetworkRestartNotifier Notifier);

	private static Fixture CreateFixture(bool restartSupported = true,
		bool overriddenByEnvironment = false,
		bool publicListenerAvailable = true)
	{
		// A host left on its defaults serves HTTP and HTTPS side by side, so that is what the listener has
		// to report. An HTTP-only fake would read as a pending TLS change on every one of these cases.
		var listenerState = new FakeHostListenerState
		{
			PublicPort = ActivePort,
			PublicPortOverriddenByEnvironment = overriddenByEnvironment,
			PublicListenerAvailable = publicListenerAvailable,
			PublicEndpoints = publicListenerAvailable
				? PublicEndpointSet.HttpAndHttps(ActivePort, BuildConfig.DefaultPublicHttpsPort)
				: PublicEndpointSet.HttpOnly(ActivePort).WithoutHttp()
		};
		var preferences = new AppPreferenceService(new FakeAppPreferenceRepository(),
			new FakeBuildEnvironment(),
			listenerState);
		var notifications = new UserNotificationStore();
		var restart = new FakeRestartService
		{
			Availability = restartSupported
				? new RestartAvailability(true, null)
				: new RestartAvailability(false, "no desktop app")
		};

		return new Fixture(preferences,
			notifications,
			new NetworkRestartNotifier(preferences, restart, notifications, TestLocalization.Resolver));
	}

	[Test]
	public async Task Nothing_is_raised_while_the_configured_port_is_the_active_one()
	{
		var fixture = CreateFixture();

		await fixture.Notifier.Sync();

		Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
	}

	// This notifier stays about pending port changes only. An unopened public listener (issue #515) is
	// a separate notification: saying it here would read "listens on port 8193 until it restarts on
	// port 8193".
	[Test]
	public async Task An_unopened_public_listener_raises_nothing_here_while_the_port_is_unchanged()
	{
		var fixture = CreateFixture(publicListenerAvailable: false);

		await fixture.Notifier.Sync();

		Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task A_pending_port_is_raised_with_a_restart_button()
	{
		var fixture = CreateFixture();
		await fixture.Preferences.SetNetwork(9100);

		await fixture.Notifier.Sync();

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Title, Does.Contain("Restart"));
			Assert.That(notification.Message, Is.Not.Empty);
			Assert.That(notification.Action, Is.Not.Null);
			Assert.That(notification.Action!.Kind, Is.EqualTo(UserNotificationActionKind.RestartApplication));
			Assert.That(notification.Action.Target, Is.Null);
		});
	}

	[Test]
	public async Task The_button_is_left_off_where_restarting_is_unsupported()
	{
		var fixture = CreateFixture(restartSupported: false);
		await fixture.Preferences.SetNetwork(9100);

		await fixture.Notifier.Sync();

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Action, Is.Null);
			Assert.That(notification.Message, Is.Not.Empty);
		});
	}

	[Test]
	public async Task An_environment_override_raises_nothing()
	{
		var fixture = CreateFixture(overriddenByEnvironment: true);
		await fixture.Preferences.SetNetwork(9100);

		await fixture.Notifier.Sync();

		Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Setting_the_port_back_clears_the_notification()
	{
		var fixture = CreateFixture();
		await fixture.Preferences.SetNetwork(9100);
		await fixture.Notifier.Sync();

		await fixture.Preferences.SetNetwork(ActivePort);
		await fixture.Notifier.Sync();

		Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
	}

	// Syncing again on the next start must not stack a second row up.
	[Test]
	public async Task Repeated_syncs_keep_a_single_entry()
	{
		var fixture = CreateFixture();
		await fixture.Preferences.SetNetwork(9100);

		await fixture.Notifier.Sync();
		await fixture.Notifier.Sync();

		Assert.That(fixture.Notifications.Snapshot(), Has.Count.EqualTo(1));
	}
}
