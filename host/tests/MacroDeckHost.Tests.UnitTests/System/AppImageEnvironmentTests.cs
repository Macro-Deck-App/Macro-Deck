using MacroDeckHost.Integrations.System.Application;

namespace MacroDeckHost.Tests.UnitTests.System;

public class AppImageEnvironmentTests
{
	private const string AppDir = "/tmp/.mount_MacroDabc123";

	private static Dictionary<string, string> Outside(params (string Name, string Value)[] variables)
		=> AppImageEnvironment.Outside(
			variables.ToDictionary(variable => variable.Name, variable => variable.Value),
			[AppDir]);

	[Test]
	public void The_system_path_is_what_remains_of_the_launcher_path()
	{
		var outside = Outside(("PATH",
			$"{AppDir}/usr/bin/:{AppDir}/usr/sbin/:{AppDir}/usr/games/:{AppDir}/bin/:{AppDir}/sbin/:/usr/local/bin:/usr/bin"));

		Assert.That(outside["PATH"], Is.EqualTo("/usr/local/bin:/usr/bin"));
	}

	[Test]
	public void Variables_that_only_pointed_into_the_bundle_are_removed()
	{
		var outside = Outside(
			("GTK_PATH", $"{AppDir}//usr/lib/gtk-3.0"),
			("GDK_PIXBUF_MODULE_FILE", $"{AppDir}//usr/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"),
			("PYTHONHOME", $"{AppDir}/usr/"),
			("LD_LIBRARY_PATH", $"{AppDir}/usr/lib/:{AppDir}/usr/lib/x86_64-linux-gnu/:"),
			("PYTHONPATH", $"{AppDir}/usr/share/pyshared/:"));

		Assert.That(outside, Is.Empty);
	}

	[Test]
	public void The_users_own_entries_survive_in_order()
	{
		var outside = Outside(
			("XDG_DATA_DIRS", $"{AppDir}/usr/share/:{AppDir}/usr/share:/usr/share:/home/u/.local/share/flatpak/exports/share:/usr/local/share"),
			("QT_PLUGIN_PATH", $"{AppDir}/usr/lib/qt5/plugins/:/usr/lib/qt6/plugins"));

		Assert.Multiple(() =>
		{
			Assert.That(outside["XDG_DATA_DIRS"], Is.EqualTo("/usr/share:/home/u/.local/share/flatpak/exports/share:/usr/local/share"));
			Assert.That(outside["QT_PLUGIN_PATH"], Is.EqualTo("/usr/lib/qt6/plugins"));
		});
	}

	[Test]
	public void The_hooks_literal_system_data_dir_cannot_be_told_from_the_users_and_stays()
	{
		var outside = Outside(("XDG_DATA_DIRS", $"{AppDir}/usr/share/:{AppDir}/usr/share:/usr/share:"));

		Assert.That(outside["XDG_DATA_DIRS"], Is.EqualTo("/usr/share"));
	}

	[Test]
	public void Values_the_bundle_forced_are_dropped()
	{
		var outside = Outside(("APPDIR", AppDir), ("GTK_THEME", "Adwaita:light"), ("GDK_BACKEND", "x11"));

		Assert.That(outside, Is.Empty);
	}

	[Test]
	public void Unrelated_variables_are_passed_through_unchanged()
	{
		(string, string)[] variables =
		[
			("HOME", "/home/u"),
			("DISPLAY", ":0"),
			("KDE_SESSION_VERSION", "6"),
			("XDG_CURRENT_DESKTOP", "KDE"),
			("http_proxy", "http://proxy.local:3128"),
			("APPIMAGE", "/home/u/Applications/Macro.Deck.AppImage"),
			("EMPTY", ""),
			("PATH", "/usr/bin::/bin")
		];

		var outside = Outside(variables);

		Assert.That(outside, Is.EquivalentTo(variables.ToDictionary(variable => variable.Item1, variable => variable.Item2)));
	}

	[Test]
	public void A_sibling_directory_sharing_the_prefix_is_not_the_bundle()
	{
		var outside = Outside(("PATH", $"{AppDir}X/usr/bin:{AppDir}/usr/bin:/usr/bin"));

		Assert.That(outside["PATH"], Is.EqualTo($"{AppDir}X/usr/bin:/usr/bin"));
	}

	[Test]
	public void No_opener_is_made_outside_an_AppImage()
	{
		Assert.Multiple(() =>
		{
			Assert.That(AppImageEnvironment.TryCreateOpener("https://example.com", new Dictionary<string, string> { ["PATH"] = "/usr/bin" }), Is.Null);
			Assert.That(AppImageEnvironment.TryCreateOpener("https://example.com", new Dictionary<string, string> { ["APPDIR"] = "relative/dir" }), Is.Null);
		});
	}
}
