using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Autostart;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class AutostartEntryContentTests
{
	[Test]
	public void LaunchAgentPlist_round_trips_registration()
	{
		var registration = new AutostartRegistration("/Applications/Macro Deck.app/Contents/MacOS/MacroDeck", true);

		var plist = AutostartEntryContent.BuildLaunchAgentPlist(registration);

		Assert.Multiple(() =>
		{
			Assert.That(plist, Does.Contain("<string>app.macro-deck.macrodeck</string>"));
			Assert.That(plist, Does.Contain("<key>RunAtLoad</key>"));
			Assert.That(AutostartEntryContent.ParseLaunchAgentPlist(plist), Is.EqualTo(registration));
		});
	}

	[Test]
	public void LaunchAgentPlist_escapes_xml_sensitive_paths()
	{
		var registration = new AutostartRegistration("/Users/a&b/Macro<Deck>", false);

		var plist = AutostartEntryContent.BuildLaunchAgentPlist(registration);

		Assert.Multiple(() =>
		{
			Assert.That(plist, Does.Contain("&amp;"));
			Assert.That(AutostartEntryContent.ParseLaunchAgentPlist(plist), Is.EqualTo(registration));
		});
	}

	[Test]
	public void ParseLaunchAgentPlist_rejects_foreign_content()
	{
		Assert.Multiple(() =>
		{
			Assert.That(AutostartEntryContent.ParseLaunchAgentPlist(null), Is.Null);
			Assert.That(AutostartEntryContent.ParseLaunchAgentPlist(""), Is.Null);
			Assert.That(AutostartEntryContent.ParseLaunchAgentPlist("<plist><dict></dict></plist>"), Is.Null);
		});
	}

	[Test]
	public void CommandLine_round_trips_registration()
	{
		var withMinimized = new AutostartRegistration(@"C:\Users\a\AppData\Local\MacroDeck\MacroDeck.exe", true);
		var withoutMinimized = withMinimized with { OpenMinimized = false };

		Assert.Multiple(() =>
		{
			Assert.That(AutostartEntryContent.BuildCommandLine(withMinimized),
				Is.EqualTo("\"C:\\Users\\a\\AppData\\Local\\MacroDeck\\MacroDeck.exe\" --autostart --minimized"));
			Assert.That(AutostartEntryContent.ParseCommandLine(AutostartEntryContent.BuildCommandLine(withMinimized)),
				Is.EqualTo(withMinimized));
			Assert.That(
				AutostartEntryContent.ParseCommandLine(AutostartEntryContent.BuildCommandLine(withoutMinimized)),
				Is.EqualTo(withoutMinimized));
		});
	}

	[Test]
	public void ParseCommandLine_rejects_foreign_values()
	{
		Assert.Multiple(() =>
		{
			Assert.That(AutostartEntryContent.ParseCommandLine(null), Is.Null);
			Assert.That(AutostartEntryContent.ParseCommandLine(""), Is.Null);
			Assert.That(AutostartEntryContent.ParseCommandLine("\"C:\\other\\tool.exe\" --tray"), Is.Null);
			Assert.That(AutostartEntryContent.ParseCommandLine("\"unterminated"), Is.Null);
		});
	}
}

[TestFixture]
public class FileAutostartRegistrarTests
{
	private string _directory = string.Empty;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), $"macro-deck-autostart-{Guid.NewGuid():N}");
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}
	}

	[Test]
	public void MacOs_registrar_writes_reads_and_removes_the_launch_agent()
	{
		var registrar = new MacOsAutostartRegistrar(_directory);
		var registration = new AutostartRegistration("/opt/macrodeck/MacroDeck", true);

		Assert.That(registrar.Read(), Is.Null);

		registrar.Write(registration);
		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(Path.Combine(_directory, "app.macro-deck.macrodeck.plist")), Is.True);
			Assert.That(registrar.Read(), Is.EqualTo(registration));
		});

		registrar.Remove();
		Assert.That(registrar.Read(), Is.Null);
	}

	[Test]
	public void Linux_registrar_writes_reads_and_removes_the_desktop_entry()
	{
		var registrar = new LinuxAutostartRegistrar(_directory);
		var registration = new AutostartRegistration("/usr/bin/macro-deck", false);

		registrar.Write(registration);
		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(Path.Combine(_directory, "macro-deck.desktop")), Is.True);
			Assert.That(registrar.Read(), Is.EqualTo(registration));
		});

		registrar.Remove();
		Assert.That(registrar.Read(), Is.Null);
	}
}

[TestFixture]
public class AutostartServiceTests
{
	private sealed class FakeRegistrar : IAutostartRegistrar
	{
		public AutostartRegistration? Stored { get; set; }

		public bool FailWrites { get; set; }

		public bool IsSupported => true;

		public AutostartRegistration? Read() => Stored;

		public void Write(AutostartRegistration registration)
		{
			if (FailWrites)
			{
				throw new IOException("disk full");
			}

			Stored = registration;
		}

		public void Remove() => Stored = null;
	}

	private static string ExistingExecutable()
	{
		var path = Path.Combine(Path.GetTempPath(), $"macro-deck-shell-{Guid.NewGuid():N}");
		File.WriteAllText(path, "stub");
		return path;
	}

	[Test]
	public void Unsupported_without_shell_executable()
	{
		var service = new AutostartService(new FakeRegistrar(), null);

		Assert.Multiple(() =>
		{
			Assert.That(service.GetSettings(), Is.EqualTo(new AutostartSettings(false, false, false)));
			var result = service.Update(true, false);
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AutostartError.NotSupported));
		});
	}

	[Test]
	public void Update_registers_and_unregisters_the_login_item()
	{
		var registrar = new FakeRegistrar();
		var executable = ExistingExecutable();
		var service = new AutostartService(registrar, executable);

		var enabled = service.Update(true, true);
		Assert.Multiple(() =>
		{
			Assert.That(enabled.Success, Is.True);
			Assert.That(registrar.Stored, Is.EqualTo(new AutostartRegistration(executable, true)));
			Assert.That(service.GetSettings(), Is.EqualTo(new AutostartSettings(true, true, true)));
		});

		var disabled = service.Update(false, false);
		Assert.Multiple(() =>
		{
			Assert.That(disabled.Success, Is.True);
			Assert.That(registrar.Stored, Is.Null);
			Assert.That(service.GetSettings(), Is.EqualTo(new AutostartSettings(true, false, false)));
		});

		File.Delete(executable);
	}

	[Test]
	public void Update_reports_registration_failures()
	{
		var registrar = new FakeRegistrar { FailWrites = true };
		var executable = ExistingExecutable();
		var service = new AutostartService(registrar, executable);

		var result = service.Update(true, false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AutostartError.RegistrationFailed));
		});

		File.Delete(executable);
	}

	[Test]
	public void RefreshRegistration_rewrites_a_stale_executable_path()
	{
		var registrar = new FakeRegistrar { Stored = new AutostartRegistration("/old/location/MacroDeck", true) };
		var executable = ExistingExecutable();
		var service = new AutostartService(registrar, executable);

		service.RefreshRegistration();

		Assert.That(registrar.Stored, Is.EqualTo(new AutostartRegistration(executable, true)));

		File.Delete(executable);
	}

	[Test]
	public void RefreshRegistration_leaves_missing_registrations_alone()
	{
		var registrar = new FakeRegistrar();
		var executable = ExistingExecutable();
		var service = new AutostartService(registrar, executable);

		service.RefreshRegistration();

		Assert.That(registrar.Stored, Is.Null);

		File.Delete(executable);
	}
}
