using System.Text.RegularExpressions;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public partial class ReportInstallationIntegrityRequestMessageHandlerTests
{
	private static ReportInstallationIntegrityRequestMessageHandler CreateHandler(IUserNotificationStore store)
		=> new(store, TestLocalization.ScopeFactory, TestLocalization.Resolver);

	private static ReportInstallationIntegrityRequest Damaged(string installKind)
		=> new() { Status = "damaged", Reason = "files", Missing = 1, Modified = 2, InstallKind = installKind };

	[Test]
	public async Task A_damaged_installation_tells_the_user_to_reinstall_and_where_to_look()
	{
		var store = new UserNotificationStore();

		var response = await CreateHandler(store).Handle(Damaged("windows"), CancellationToken.None);

		var notification = store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(notification.Severity, Is.EqualTo(UserNotificationSeverity.Error));
			Assert.That(notification.Title, Is.EqualTo("Macro Deck was not updated completely"));
			Assert.That(notification.Message, Does.Contain("antivirus software"));
			Assert.That(notification.Message, Does.Contain("Download the latest version"));
			Assert.That(notification.Actions.Select(action => action.Kind), Is.EqualTo(new[]
			{
				UserNotificationActionKind.OpenDownloadPage,
				UserNotificationActionKind.OpenTroubleshootingGuide,
				UserNotificationActionKind.OpenLogs
			}));
		});
	}

	[Test]
	public async Task A_damaged_linux_package_is_pointed_at_its_package_manager_instead_of_a_download()
	{
		var store = new UserNotificationStore();

		await CreateHandler(store).Handle(Damaged("linuxPackage"), CancellationToken.None);

		var notification = store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.Message, Does.Contain("package manager"));
			Assert.That(notification.Actions.Select(action => action.Kind),
				Does.Not.Contain(UserNotificationActionKind.OpenDownloadPage));
		});
	}

	[Test]
	public async Task A_repeated_report_after_a_bootstrapper_restart_keeps_a_single_entry()
	{
		var store = new UserNotificationStore();
		var handler = CreateHandler(store);

		await handler.Handle(Damaged("macos"), CancellationToken.None);
		var first = store.Snapshot().Single();
		await handler.Handle(Damaged("macos"), CancellationToken.None);

		Assert.That(store.Snapshot().Single().Id, Is.EqualTo(first.Id));
	}

	[Test]
	public async Task A_report_that_is_not_about_damage_raises_nothing_but_is_acknowledged()
	{
		var store = new UserNotificationStore();

		var response = await CreateHandler(store)
			.Handle(new ReportInstallationIntegrityRequest { Status = "intact" }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public void The_troubleshooting_action_opens_a_section_the_user_guide_has()
	{
		var store = new UserNotificationStore();
		CreateHandler(store).Handle(Damaged("windows"), CancellationToken.None).AsTask().GetAwaiter().GetResult();
		var target = store.Snapshot().Single().Actions
			.Single(action => action.Kind == UserNotificationActionKind.OpenTroubleshootingGuide).Target;

		var slugs = File.ReadLines(TroubleshootingGuidePath())
			.Where(line => line.StartsWith("## ", StringComparison.Ordinal))
			.Select(line => Slug(line[3..]));

		Assert.That(slugs, Does.Contain(target));
	}

	private static string Slug(string heading)
		=> NotSlugCharacters().Replace(heading.Trim().ToLowerInvariant(), string.Empty).Replace(' ', '-');

	[GeneratedRegex("[^a-z0-9 -]")]
	private static partial Regex NotSlugCharacters();

	private static string TroubleshootingGuidePath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
			{
				return Path.Combine(directory.FullName, "docs", "src", "content", "docs", "guide", "troubleshooting.md");
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException("Could not locate the repository root above the test output directory.");
	}
}
