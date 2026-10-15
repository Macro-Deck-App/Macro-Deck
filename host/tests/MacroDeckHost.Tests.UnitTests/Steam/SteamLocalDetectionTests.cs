using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamLocalDetectionTests
{
	private SteamFolders _folders = null!;

	[SetUp]
	public void SetUp()
	{
		_folders = new SteamFolders();
	}

	[TearDown]
	public void TearDown()
	{
		_folders.Dispose();
	}

	[Test]
	public void Fully_installed_games_are_listed_with_their_names_and_last_played_time()
	{
		var steam = _folders.Make("Steam");
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2", lastPlayed: 1_700_000_000);
		SteamFolders.WriteManifest(steam, 220, "Half-Life 2");

		var games = new SteamLibrary(new FakeTimeProvider()).Scan(steam, []);

		Assert.Multiple(() =>
		{
			Assert.That(games.Select(game => game.Name), Is.EqualTo(new[] { "Counter-Strike 2", "Half-Life 2" }));
			Assert.That(games[0].LastPlayed, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)));
			Assert.That(games[1].LastPlayed, Is.Null);
		});
	}

	[Test]
	public void Games_that_are_still_downloading_are_not_listed()
	{
		var steam = _folders.Make("Steam");
		SteamFolders.WriteManifest(steam, 730, "Installed");
		SteamFolders.WriteManifest(steam, 440, "Downloading", 1026);

		var games = new SteamLibrary(new FakeTimeProvider()).Scan(steam, []);

		Assert.That(games.Select(game => game.AppId), Is.EqualTo(new[] { 730 }));
	}

	[TestCase(228980, "Steamworks Common Redistributables")]
	[TestCase(1493710, "Proton Experimental")]
	[TestCase(2348590, "Proton 8.0")]
	[TestCase(1628350, "Steam Linux Runtime 3.0 (sniper)")]
	public void Compatibility_tools_and_runtimes_are_not_games(int appId, string name)
	{
		var steam = _folders.Make("Steam");
		SteamFolders.WriteManifest(steam, appId, name);
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");

		var games = new SteamLibrary(new FakeTimeProvider()).Scan(steam, []);

		Assert.That(games.Select(game => game.AppId), Is.EqualTo(new[] { 730 }));
	}

	[Test]
	public void Games_in_additional_library_folders_listed_by_steam_are_found()
	{
		var steam = _folders.Make("Steam");
		var second = _folders.Make("SteamLibrary");
		SteamFolders.WriteLibraryFolders(steam, second);
		SteamFolders.WriteManifest(steam, 730, "On the main drive");
		SteamFolders.WriteManifest(second, 220, "On the second drive");

		var games = new SteamLibrary(new FakeTimeProvider()).Scan(steam, []);

		Assert.That(games.Select(game => game.Name), Is.EqualTo(new[] { "On the main drive", "On the second drive" }));
	}

	[Test]
	public void A_library_folder_on_an_unmounted_drive_is_skipped_without_losing_the_others()
	{
		var steam = _folders.Make("Steam");
		SteamFolders.WriteLibraryFolders(steam, Path.Combine(_folders.Root, "NotMounted"));
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");

		var games = new SteamLibrary(new FakeTimeProvider()).Scan(steam, []);

		Assert.That(games.Select(game => game.AppId), Is.EqualTo(new[] { 730 }));
	}

	[Test]
	public void A_folder_the_user_added_is_scanned_whether_given_as_library_or_as_steamapps()
	{
		var steam = _folders.Make("Steam");
		var libraryA = _folders.Make("A");
		var libraryB = _folders.Make("B");
		SteamFolders.WriteManifest(libraryA, 1, "From A");
		SteamFolders.WriteManifest(libraryB, 2, "From B");

		var games = new SteamLibrary(new FakeTimeProvider())
			.Scan(steam, [libraryA, Path.Combine(libraryB, "steamapps")]);

		Assert.That(games.Select(game => game.Name), Is.EqualTo(new[] { "From A", "From B" }));
	}

	[Test]
	public void The_same_game_in_two_folders_is_listed_once()
	{
		var steam = _folders.Make("Steam");
		var other = _folders.Make("Other");
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");
		SteamFolders.WriteManifest(other, 730, "Counter-Strike 2");

		var games = new SteamLibrary(new FakeTimeProvider()).Scan(steam, [other]);

		Assert.That(games, Has.Count.EqualTo(1));
	}

	[Test]
	public void A_newly_installed_game_appears_on_the_next_scan_without_waiting_for_the_age_limit()
	{
		var steam = _folders.Make("Steam");
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");
		var library = new SteamLibrary(new FakeTimeProvider());
		Assert.That(library.Scan(steam, []), Has.Count.EqualTo(1));

		SteamFolders.WriteManifest(steam, 220, "Half-Life 2");
		Directory.SetLastWriteTimeUtc(Path.Combine(steam, "steamapps"), DateTime.UtcNow.AddSeconds(5));

		Assert.That(library.Scan(steam, []), Has.Count.EqualTo(2));
	}

	[Test]
	public void An_unchanged_library_returns_the_same_list_instance()
	{
		var steam = _folders.Make("Steam");
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");
		var time = new FakeTimeProvider();
		var library = new SteamLibrary(time);

		var first = library.Scan(steam, []);
		time.Now = time.Now.AddSeconds(61);

		Assert.That(library.Scan(steam, []), Is.SameAs(first));
	}

	[Test]
	public void A_corrupt_manifest_is_ignored()
	{
		var steam = _folders.Make("Steam");
		File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_9.acf"), "\"AppState\" {");
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");

		Assert.That(new SteamLibrary(new FakeTimeProvider()).Scan(steam, []).Select(game => game.AppId),
			Is.EqualTo(new[] { 730 }));
	}

	[Test]
	public void The_macos_install_is_found_under_application_support()
	{
		var steam = Path.Combine(_folders.Root, "Library", "Application Support", "Steam", "steamapps");
		Directory.CreateDirectory(steam);
		var platform = new FakeSteamPlatform { Os = SteamOs.MacOs, HomeDirectory = _folders.Root };

		Assert.That(SteamInstallLocator.Find(platform), Is.EqualTo(Path.GetDirectoryName(steam)));
	}

	[TestCase(".steam/steam")]
	[TestCase(".local/share/Steam")]
	[TestCase(".var/app/com.valvesoftware.Steam/.local/share/Steam")]
	[TestCase("snap/steam/common/.local/share/Steam")]
	public void The_linux_install_is_found_in_each_known_location(string relative)
	{
		var steam = Path.Combine(_folders.Root, relative.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
		var platform = new FakeSteamPlatform { Os = SteamOs.Linux, HomeDirectory = _folders.Root };

		Assert.That(SteamInstallLocator.Find(platform), Is.EqualTo(steam));
	}

	[Test]
	public void The_windows_install_follows_the_registry_path_with_forward_slashes()
	{
		var steam = _folders.Make("Steam");
		var platform = new FakeSteamPlatform
		{
			Os = SteamOs.Windows,
			HomeDirectory = _folders.Root,
			RegistrySteamPath = steam.Replace('\\', '/')
		};

		Assert.That(SteamInstallLocator.Find(platform), Is.EqualTo(Path.GetFullPath(steam)));
	}

	[Test]
	public void A_malformed_registry_path_is_ignored_instead_of_breaking_detection()
	{
		var steam = _folders.Make("Steam");
		var platform = new FakeSteamPlatform
		{
			Os = SteamOs.Windows,
			HomeDirectory = _folders.Root,
			RegistrySteamPath = "C:\\bad\0path",
			ProgramFilesDirectories = [_folders.Root]
		};

		Assert.That(SteamInstallLocator.Find(platform), Is.EqualTo(steam));
	}

	[Test]
	public void No_install_is_reported_when_no_candidate_has_a_library()
	{
		var platform = new FakeSteamPlatform { Os = SteamOs.Linux, HomeDirectory = _folders.Root };

		Assert.That(SteamInstallLocator.Find(platform), Is.Null);
	}

	[Test]
	public void A_games_manifest_with_an_unsupported_app_id_is_ignored()
	{
		var steam = _folders.Make("Steam");
		File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_x.acf"),
			"\"AppState\" { \"appid\" \"abc\" \"name\" \"X\" \"StateFlags\" \"4\" }");

		Assert.That(new SteamLibrary(new FakeTimeProvider()).Scan(steam, []), Is.Empty);
	}

	[Test]
	public void Running_game_reads_the_windows_registry_value()
	{
		var platform = new FakeSteamPlatform { Os = SteamOs.Windows, RegistryRunningAppId = 730 };

		Assert.That(SteamRunningApp.Read(platform, null), Is.EqualTo(730));
	}

	[Test]
	public void A_running_app_id_of_zero_means_nothing_is_running()
	{
		var platform = new FakeSteamPlatform { Os = SteamOs.Windows, RegistryRunningAppId = 0 };

		Assert.That(SteamRunningApp.Read(platform, null), Is.Null);
	}

	[Test]
	public void A_stale_registry_value_is_ignored_when_steam_is_not_running()
	{
		var platform = new FakeSteamPlatform
			{ Os = SteamOs.Windows, RegistryRunningAppId = 730, SteamProcessRunning = false };

		Assert.That(SteamRunningApp.Read(platform, null), Is.Null);
	}

	[Test]
	public void Running_game_on_linux_comes_from_registry_vdf()
	{
		var home = _folders.Root;
		Directory.CreateDirectory(Path.Combine(home, ".steam"));
		File.WriteAllText(Path.Combine(home, ".steam", "registry.vdf"),
			"\"Registry\" { \"HKCU\" { \"Software\" { \"Valve\" { \"Steam\" { \"RunningAppID\" \"440\" } } } } }");
		var platform = new FakeSteamPlatform { Os = SteamOs.Linux, HomeDirectory = home };

		Assert.That(SteamRunningApp.Read(platform, null), Is.EqualTo(440));
	}

	[Test]
	public void The_linux_process_fallback_is_used_only_when_registry_vdf_shows_nothing()
	{
		var home = _folders.Root;
		Directory.CreateDirectory(Path.Combine(home, ".steam"));
		File.WriteAllText(Path.Combine(home, ".steam", "registry.vdf"),
			"\"Registry\" { \"HKCU\" { \"Software\" { \"Valve\" { \"Steam\" { \"RunningAppID\" \"0\" } } } } }");
		var platform = new FakeSteamPlatform { Os = SteamOs.Linux, HomeDirectory = home, LaunchedAppIds = { 620 } };

		Assert.That(SteamRunningApp.Read(platform, null), Is.EqualTo(620));

		File.WriteAllText(Path.Combine(home, ".steam", "registry.vdf"),
			"\"Registry\" { \"HKCU\" { \"Software\" { \"Valve\" { \"Steam\" { \"RunningAppID\" \"440\" } } } } }");

		Assert.That(SteamRunningApp.Read(platform, null), Is.EqualTo(440));
	}

	[Test]
	public void The_linux_process_fallback_is_not_used_when_steam_is_not_running()
	{
		var platform = new FakeSteamPlatform
		{
			Os = SteamOs.Linux,
			HomeDirectory = _folders.Root,
			SteamProcessRunning = false,
			LaunchedAppIds = { 620 }
		};

		Assert.That(SteamRunningApp.Read(platform, null), Is.Null);
	}

	[Test]
	public void The_reaper_command_line_names_the_launched_app()
	{
		const string commandLine
			= "/home/u/.steam/ubuntu12_32/reaper\0SteamLaunch\0AppId=620\0--\0/path/proton\0waitforexitandrun";

		Assert.That(SteamRunningApp.TryReadReaperAppId(commandLine), Is.EqualTo(620));
	}

	[Test]
	public void A_command_line_that_is_not_steams_launcher_is_not_mistaken_for_one()
	{
		Assert.That(SteamRunningApp.TryReadReaperAppId("/usr/bin/some-tool\0AppId=620"), Is.Null);
	}
}
