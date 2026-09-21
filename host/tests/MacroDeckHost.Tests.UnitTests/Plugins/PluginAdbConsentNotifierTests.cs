using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginAdbConsentNotifierTests
{
	private const string PluginId = "com.example.android";

	[Test]
	public async Task Installing_a_plugin_that_declares_adb_while_plugins_may_not_use_it_asks_the_user()
	{
		var world = new World(allowPlugins: false);

		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", declaresAdb: true, previouslyDeclaredAdb: false);

		var notification = world.Store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.SourceId, Is.EqualTo(PluginId));
			Assert.That(notification.Actions.Select(action => action.Kind),
				Does.Contain(UserNotificationActionKind.EnablePluginAdb));
			Assert.That(notification.Message, Does.Contain("Android Tools"));
		});
	}

	[Test]
	public async Task Installing_a_plugin_that_declares_adb_while_adb_is_off_asks_to_turn_it_on()
	{
		var world = new World(allowPlugins: true, adbEnabled: false);

		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", declaresAdb: true, previouslyDeclaredAdb: false);

		var notification = world.Store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Actions.Select(action => action.Kind),
				Does.Contain(UserNotificationActionKind.EnablePluginAdb));
			Assert.That(notification.Message, Does.Contain("ADB is turned off"));
		});
	}

	[Test]
	public async Task Allowing_plugins_while_adb_stays_off_keeps_the_question()
	{
		var world = new World(allowPlugins: false, adbEnabled: false);
		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", true, false);
		var handler = new PluginAdbConsentDismissHandler(world.Notifier);

		await handler.Handle(new AdbSettingsChangedNotification(new AdbSettings(false, null, true, null, AllowPlugins: true)),
			CancellationToken.None);
		var whileAdbOff = world.Store.Snapshot().Count;
		await handler.Handle(new AdbSettingsChangedNotification(new AdbSettings(true, null, true, null, AllowPlugins: true)),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(whileAdbOff, Is.EqualTo(1));
			Assert.That(world.Store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_plugin_refused_adb_asks_once_per_run_even_after_the_user_dismissed_the_question()
	{
		var world = new World(allowPlugins: false);

		await world.Notifier.AskAfterRefusalAsync(PluginId, "Android Tools");
		world.Store.Dismiss(world.Store.Snapshot().Single().Id);
		await world.Notifier.AskAfterRefusalAsync(PluginId, "Android Tools");

		Assert.That(world.Store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task A_plugin_that_needs_adb_while_adb_is_on_but_missing_offers_to_set_it_up()
	{
		var adbManager = new FakeAdbManager { Status = AdbStatus.Disabled with { Enabled = true } };
		var world = new World(allowPlugins: true, adbEnabled: true, adbManager);

		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", declaresAdb: true, previouslyDeclaredAdb: false);

		Assert.That(world.Store.Snapshot().Single().Message, Does.Contain("cannot find adb"));
	}

	[Test]
	public async Task Nothing_is_asked_while_adb_is_on_found_and_plugins_are_allowed()
	{
		var adbManager = new FakeAdbManager
		{
			Status = AdbStatus.Disabled with { Enabled = true, ResolvedExecutablePath = "/usr/local/bin/adb" }
		};
		var world = new World(allowPlugins: true, adbEnabled: true, adbManager);

		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", declaresAdb: true, previouslyDeclaredAdb: false);

		Assert.That(world.Store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Uninstalling_the_plugin_takes_its_question_away()
	{
		var world = new World(allowPlugins: false);
		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", true, false);

		world.Notifier.Dismiss(PluginId);

		Assert.That(world.Store.Snapshot(), Is.Empty);
	}

	[TestCase(true, false, false)]
	[TestCase(false, true, false)]
	[TestCase(false, false, true)]
	public async Task Nothing_is_asked_when_there_is_nothing_to_decide(bool allowPlugins,
		bool previouslyDeclaredAdb,
		bool declaresNothing)
	{
		var world = new World(allowPlugins);

		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", !declaresNothing, previouslyDeclaredAdb);

		Assert.That(world.Store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Allowing_plugins_clears_the_question_and_a_later_install_asks_again()
	{
		var world = new World(allowPlugins: false);
		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", true, false);

		world.Notifier.DismissAll();
		var afterAllowing = world.Store.Snapshot().Count;
		await world.Notifier.NotifyIfNeededAsync(PluginId, "Android Tools", true, false);

		Assert.Multiple(() =>
		{
			Assert.That(afterAllowing, Is.Zero);
			Assert.That(world.Store.Snapshot(), Has.Count.EqualTo(1));
		});
	}

	private sealed class World
	{
		public World(bool allowPlugins, bool adbEnabled = true, IAdbManager? adbManager = null)
		{
			var preferences = new FakeAdbPreferenceService
			{
				AdbSettings = new AdbSettings(adbEnabled, null, true, null, AllowPlugins: allowPlugins)
			};
			var services = new ServiceCollection();
			services.AddScoped<IAppPreferenceService>(_ => preferences);
			Notifier = new PluginAdbConsentNotifier(Store,
				services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
				TestLocalization.Resolver,
				adbManager);
		}

		public UserNotificationStore Store { get; } = new();

		public PluginAdbConsentNotifier Notifier { get; }
	}
}
