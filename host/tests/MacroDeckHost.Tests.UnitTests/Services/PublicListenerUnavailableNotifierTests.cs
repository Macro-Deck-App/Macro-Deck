using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class PublicListenerUnavailableNotifierTests
{
	private sealed class FakeRestartService : IApplicationRestartService
	{
		public RestartAvailability Availability { get; init; } = new(true, null);

		public bool RestartRequested => false;

		public Result<RestartError> Request(string reason) => Result.Ok<RestartError>();
	}

	private sealed record Fixture(UserNotificationStore Notifications, PublicListenerUnavailableNotifier Notifier);

	private static Fixture CreateFixture(bool publicListenerAvailable,
		bool restartSupported = true,
		bool overriddenByEnvironment = false,
		PublicEndpointSet? endpoints = null,
		string culture = "en")
	{
		var listenerState = new FakeHostListenerState
		{
			PublicPort = 8193,
			PublicPortOverriddenByEnvironment = overriddenByEnvironment,
			PublicEndpoints = endpoints ??
				(publicListenerAvailable
					? PublicEndpointSet.HttpOnly(8193)
					: PublicEndpointSet.HttpOnly(8193).WithoutHttp())
		};
		var notifications = new UserNotificationStore();
		var restart = new FakeRestartService
		{
			Availability = restartSupported
				? new RestartAvailability(true, null)
				: new RestartAvailability(false, "no desktop app")
		};

		return new Fixture(notifications,
			new PublicListenerUnavailableNotifier(listenerState,
				restart,
				notifications,
				new FakeLocalizationPreferences { Culture = culture },
				TestLocalization.Resolver));
	}

	/// <summary>HTTP came up on the public port, HTTPS did not - the state issue #851 reports.</summary>
	private static PublicEndpointSet HttpsThatCouldNotBeOpened()
		=> PublicEndpointSet.HttpAndHttps(8193, 8194).WithoutHttps();

	[Test]
	public async Task Nothing_is_raised_when_the_public_listener_opened_normally()
	{
		var fixture = CreateFixture(publicListenerAvailable: true);

		await fixture.Notifier.NotifyIfUnavailable();

		Assert.That(fixture.Notifications.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task A_warning_with_a_restart_button_is_raised_when_the_listener_could_not_be_opened()
	{
		var fixture = CreateFixture(publicListenerAvailable: false);

		await fixture.Notifier.NotifyIfUnavailable();

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notification.Action, Is.Not.Null);
			Assert.That(notification.Action!.Kind, Is.EqualTo(UserNotificationActionKind.RestartApplication));
		});
	}

	[Test]
	public async Task The_button_is_left_off_where_restarting_is_unsupported()
	{
		var fixture = CreateFixture(publicListenerAvailable: false, restartSupported: false);

		await fixture.Notifier.NotifyIfUnavailable();

		Assert.That(fixture.Notifications.Snapshot().Single().Action, Is.Null);
	}

	[Test]
	public async Task An_environment_override_still_warns_but_offers_no_restart()
	{
		var fixture = CreateFixture(publicListenerAvailable: false, overriddenByEnvironment: true);

		await fixture.Notifier.NotifyIfUnavailable();

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Action, Is.Null);
			Assert.That(notification.Message, Does.Contain("MACRO_DECK_PORT"));
		});
	}

	[Test]
	public async Task Calling_twice_keeps_a_single_entry()
	{
		var fixture = CreateFixture(publicListenerAvailable: false);

		await fixture.Notifier.NotifyIfUnavailable();
		await fixture.Notifier.NotifyIfUnavailable();

		Assert.That(fixture.Notifications.Snapshot(), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task An_https_listener_that_did_not_open_is_reported_while_http_still_serves()
	{
		var fixture = CreateFixture(publicListenerAvailable: true, endpoints: HttpsThatCouldNotBeOpened());

		await fixture.Notifier.NotifyIfUnavailable();

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Severity, Is.EqualTo(UserNotificationSeverity.Warning));
			Assert.That(notification.Title, Is.EqualTo("HTTPS is not running"));
			Assert.That(notification.Message, Does.Contain("reachable over HTTP"));
		});
	}

	[Test]
	public async Task The_notification_is_written_in_the_language_the_rest_of_the_interface_uses()
	{
		var fixture = CreateFixture(publicListenerAvailable: true,
			endpoints: HttpsThatCouldNotBeOpened(),
			culture: "de");

		await fixture.Notifier.NotifyIfUnavailable();

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Title, Is.EqualTo("HTTPS läuft nicht"));
			Assert.That(notification.Message, Does.Contain("über HTTP erreichbar"));
		});
	}

	[Test]
	public async Task An_unreachable_host_reports_in_that_language_too()
	{
		var fixture = CreateFixture(publicListenerAvailable: false, culture: "de");

		await fixture.Notifier.NotifyIfUnavailable();

		var notification = fixture.Notifications.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Title, Is.EqualTo("Macro Deck ist im Netzwerk nicht erreichbar"));
			Assert.That(notification.Message, Does.Contain("Netzwerkport"));
		});
	}
}
