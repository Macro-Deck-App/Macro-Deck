using System.Text.RegularExpressions;

namespace MacroDeckHost.Tests.UnitTests.Architecture;

// IUiTransport.Send reaches every UI connection, client-scope deck devices included. Anything that only the
// desktop may see goes to UiAdminGroups.Admin instead, so a new broadcasting file is a scope decision: it is
// added here only once what it sends is safe for every client.
public partial class UiBroadcastScopeTests
{
	private static readonly string[] _clientSafeBroadcasters =
	[
		"MacroDeckHost.Application/Deck/DeckNavigator.cs",
		"MacroDeckHost.Application/Events/Handlers/AdbSettingsChangedUiRefreshHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/AppearanceChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/ColorVariableChangedHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/DeveloperModeChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/FolderCreatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/FolderDeletedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/FolderUpdatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/FolderViewCatalogChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/FoldersReorderedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/HostLockStateChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/IconDeletedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/IconImportProgressNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/IconPackCreatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/IconPackDeletedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/IconPackUpdatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/IconUpdatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/IconsAddedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/LocalizationCatalogChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/LocalizationCultureChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/ProfileCreatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/ProfileDeletedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/ProfileUpdatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/ProfilesReorderedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/ScreenSaverCatalogChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetCreatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetDeletedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetPositionsUpdatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetTypeCatalogChangedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetUpdatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetsCreatedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetsDeletedNotificationHandler.cs",
		"MacroDeckHost.Application/Events/Handlers/WidgetsUpdatedNotificationHandler.cs",
		"MacroDeckHost.Application/Icons/IconAppearanceService.cs",
		"MacroDeckHost.Application/Integrations/ConfigFlow/OAuthCallbackCoordinator.cs",
		"MacroDeckHost.Application/Rendering/WidgetStatePublisher.cs",
		"MacroDeckHost.Application/Ui/Handlers/UpdateWidgetStateRequestMessageHandler.cs",
		"MacroDeckHost.Application/VideoStreams/VideoStreamUiPushHandler.cs",
		"MacroDeckHost.Infrastructure/BackgroundServices/AdbBackgroundService.cs",
		"MacroDeckHost.Infrastructure/BackgroundServices/IntegrationIssueBroadcastBackgroundService.cs",
		"MacroDeckHost.Infrastructure/BackgroundServices/MusicPlayerStateBroadcastBackgroundService.cs",
		"MacroDeckHost.Infrastructure/BackgroundServices/NativeUsbBackgroundService.cs",
		"MacroDeckHost.Infrastructure/BackgroundServices/NotificationBroadcastBackgroundService.cs",
		"MacroDeckHost.Infrastructure/BackgroundServices/WeatherStateBroadcastBackgroundService.cs",
		"MacroDeckHost/Connect/ConnectSessionNotifier.cs"
	];

	[Test]
	public void Only_reviewed_files_broadcast_to_every_ui_connection()
	{
		var sourceRoot = Path.Combine(RepositoryRoot(), "host", "src");
		var broadcasters = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj"))
			.Where(path =>
			{
				var source = File.ReadAllText(path);
				return source.Contains("IUiTransport", StringComparison.Ordinal) && BroadcastCall().IsMatch(source);
			})
			.Select(path => Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(broadcasters.Except(_clientSafeBroadcasters),
				Is.Empty,
				"broadcasts to every connection without a client-safety review; use UiAdminGroups.Admin or add it");
			Assert.That(_clientSafeBroadcasters.Except(broadcasters),
				Is.Empty,
				"no longer broadcasts; remove it from the list");
		});
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
	}

	[GeneratedRegex(@"(?<![\w.])(_uiTransport|_transport|transport|_ui|ui)\.Send\(")]
	private static partial Regex BroadcastCall();
}
