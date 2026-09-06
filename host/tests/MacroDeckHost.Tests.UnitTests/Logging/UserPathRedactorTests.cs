using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Tests.UnitTests.Logging;

[TestFixture]
public class UserPathRedactorTests
{
	private const string WindowsHome = @"C:\Users\suchbyte";
	private const string LinuxHome = "/home/suchbyte";
	private const string MacHome = "/Users/suchbyte";

	private static UserPathRedactor For(string? home, bool ignoreCase = false)
		=> new(home, [], ignoreCase);

	[Test]
	public void Replaces_The_Current_Home_With_A_Tilde_On_Every_Platform_Shape()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(WindowsHome, ignoreCase: true)
					.Redact(@"C:\Users\suchbyte\AppData\Roaming\MacroDeck\database.db"),
				Is.EqualTo(@"~\AppData\Roaming\MacroDeck\database.db"));

			Assert.That(For(LinuxHome).Redact("/home/suchbyte/.local/share/MacroDeck/database.db"),
				Is.EqualTo("~/.local/share/MacroDeck/database.db"));

			// The space in "Application Support" must not be read as the end of the path.
			Assert.That(For(MacHome, ignoreCase: true)
					.Redact("/Users/suchbyte/Library/Application Support/MacroDeck/database.db"),
				Is.EqualTo("~/Library/Application Support/MacroDeck/database.db"));
		});
	}

	[Test]
	public void Masks_Another_Users_Home_Without_Losing_The_Root()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(WindowsHome, ignoreCase: true).Redact(@"C:\Users\other-user\Documents\file.json"),
				Is.EqualTo(@"C:\Users\<user>\Documents\file.json"));

			Assert.That(For(LinuxHome).Redact("/home/other-user/file.json"),
				Is.EqualTo("/home/<user>/file.json"));

			Assert.That(For(MacHome, ignoreCase: true).Redact("/Users/other-user/file.json"),
				Is.EqualTo("/Users/<user>/file.json"));

			Assert.That(For(WindowsHome, ignoreCase: true).Redact(@"\\fileserver\Users\other-user\share\x.json"),
				Is.EqualTo(@"\\fileserver\Users\<user>\share\x.json"));
		});
	}

	[Test]
	public void Prefers_The_Tilde_Over_The_Anonymous_Form_For_The_Current_User()
	{
		Assert.That(For(LinuxHome).Redact("/home/suchbyte/file.json"), Is.EqualTo("~/file.json"));
	}

	[Test]
	public void Does_Not_Treat_A_Sibling_Directory_As_The_Home()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(LinuxHome).Redact("/home/suchbyte2/file.json"),
				Is.EqualTo("/home/<user>/file.json"));

			Assert.That(For(WindowsHome, ignoreCase: true).Redact(@"C:\Users\suchbyte.bak\file.json"),
				Is.EqualTo(@"C:\Users\<user>\file.json"));
		});
	}

	[Test]
	public void Matches_A_Home_That_Ends_The_Message()
	{
		Assert.That(For(LinuxHome).Redact("Working directory: /home/suchbyte"),
			Is.EqualTo("Working directory: ~"));
	}

	[Test]
	public void Matches_A_Path_Embedded_In_A_Larger_Message()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(LinuxHome).Redact("Config loaded from \"/home/suchbyte/.config/md.json\"."),
				Is.EqualTo("Config loaded from \"~/.config/md.json\"."));

			Assert.That(For(LinuxHome)
					.Redact("copy /home/alice/a.json -> /home/suchbyte/b.json (from /home/alice/a.json)"),
				Is.EqualTo("copy /home/<user>/a.json -> ~/b.json (from /home/<user>/a.json)"));
		});
	}

	[Test]
	public void Matches_Regardless_Of_The_Separator_Form()
	{
		var redactor = For(WindowsHome, ignoreCase: true);

		Assert.Multiple(() =>
		{
			Assert.That(redactor.Redact("C:/Users/suchbyte/AppData/x"), Is.EqualTo("~/AppData/x"));
			Assert.That(redactor.Redact(@"C:\\Users\\suchbyte\\AppData\\x"), Is.EqualTo(@"~\\AppData\\x"));
		});
	}

	[Test]
	public void Compares_Case_Insensitively_Only_Where_The_File_System_Does()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(WindowsHome, ignoreCase: true).Redact(@"c:\users\SUCHBYTE\AppData\x"),
				Is.EqualTo(@"~\AppData\x"));

			Assert.That(For(LinuxHome).Redact("/home/SuchByte/file.json"),
				Is.EqualTo("/home/<user>/file.json"));
		});
	}

	[TestCase(@"C:\Users\Public\Documents\shared.json")]
	[TestCase(@"C:\Users\Default\NTUSER.DAT")]
	[TestCase(@"C:\Users\Default User\x.json")]
	[TestCase(@"C:\Users\All Users\Application Data\md.json")]
	[TestCase(@"c:\users\public\Documents\shared.json")]
	[TestCase("/Users/Shared/MacroDeck/plugins/weather.dll")]
	public void Leaves_Profile_Directories_That_Belong_To_No_One(string path)
	{
		Assert.That(For(WindowsHome, ignoreCase: true).Redact(path), Is.EqualTo(path));
	}

	[TestCase("/opt/suchbyte/data.db")]
	[TestCase("/var/lib/macrodeck/suchbyte/cache")]
	[TestCase(@"C:\Projects\suchbyte\build.log")]
	[TestCase("Plugin suchbyte.weather v1.2 loaded")]
	[TestCase("Author: suchbyte")]
	[TestCase("https://github.com/suchbyte/Macro-Deck")]
	[TestCase(@"C:\Program Files\MacroDeck\MacroDeck.exe")]
	[TestCase("/usr/lib/macrodeck/plugins")]
	[TestCase("/Applications/MacroDeck.app/Contents/MacOS/MacroDeck")]
	[TestCase("/tmp/md-1234/plugin.log")]
	[TestCase("/var/folders/qy/8xk_9z/T/md.log")]
	public void Leaves_Text_That_Merely_Contains_The_User_Name(string message)
	{
		Assert.That(For(LinuxHome).Redact(message), Is.EqualTo(message));
	}

	[Test]
	public void Leaves_A_Url_Route_Alone_But_Not_A_File_Uri()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(LinuxHome).Redact("Navigated to https://app.example.com/home/dashboard"),
				Is.EqualTo("Navigated to https://app.example.com/home/dashboard"));

			Assert.That(For(LinuxHome).Redact("GET https://api.example.com/users/1234abcd/playlists -> 200"),
				Is.EqualTo("GET https://api.example.com/users/1234abcd/playlists -> 200"));

			Assert.That(For(LinuxHome).Redact("Loading file:///home/other-user/plugins/manifest.json"),
				Is.EqualTo("Loading file:///home/<user>/plugins/manifest.json"));
		});
	}

	[Test]
	public void A_Url_Elsewhere_In_The_Line_Does_Not_Exempt_A_Path()
	{
		var redacted = For(LinuxHome)
			.Redact("""{"url":"https://api.example.com/v1","path":"/home/other-user/x.json"}""");

		Assert.That(redacted,
			Is.EqualTo("""{"url":"https://api.example.com/v1","path":"/home/<user>/x.json"}"""));
	}

	[Test]
	public void Only_Exempts_Shared_Profile_Names_Where_They_Are_One()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(LinuxHome).Redact("/home/shared/x.json"), Is.EqualTo("/home/<user>/x.json"));
			Assert.That(For(LinuxHome).Redact("/home/public/y.json"), Is.EqualTo("/home/<user>/y.json"));
			Assert.That(For(MacHome, ignoreCase: true).Redact("/Users/Shared/x.json"),
				Is.EqualTo("/Users/Shared/x.json"));
		});
	}

	[Test]
	public void Folds_Case_For_Ascii_Only()
	{
		Assert.That(For("/home/mänuel", ignoreCase: true).Redact("/home/MÄNUEL/x.json"),
			Is.EqualTo("/home/<user>/x.json"));
	}

	[Test]
	public void Redacts_The_Current_Home_Inside_A_File_Uri()
	{
		var redacted = For(LinuxHome).Redact("Loading file:///home/suchbyte/plugins/manifest.json");

		Assert.Multiple(() =>
		{
			Assert.That(redacted, Does.Not.Contain("suchbyte"));
			Assert.That(redacted, Does.Contain("plugins/manifest.json"));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("/")]
	[TestCase(@"\")]
	[TestCase(".")]
	[TestCase("C:")]
	public void Keeps_Masking_Foreign_Paths_When_The_Home_Is_Unusable(string? home)
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(home).Redact("/home/other-user/file.json"), Is.EqualTo("/home/<user>/file.json"));
			Assert.That(For(home).Redact("/usr/share/macrodeck/plugin.dll"),
				Is.EqualTo("/usr/share/macrodeck/plugin.dll"));
		});
	}

	[Test]
	public void Redacts_A_User_Specific_Root_That_Is_Not_Inside_The_Home()
	{
		var redactor = new UserPathRedactor(WindowsHome,
			[new KeyValuePair<string, string>(@"\\fileserver\profiles\suchbyte\AppData\Roaming", "%APPDATA%")],
			ignoreCase: true);

		var redacted = redactor.Redact(@"\\fileserver\profiles\suchbyte\AppData\Roaming\MacroDeck\database.db");

		Assert.Multiple(() =>
		{
			Assert.That(redacted, Does.Not.Contain("suchbyte"));
			Assert.That(redacted, Does.Contain(@"MacroDeck\database.db"));
		});
	}

	[Test]
	public void Is_Idempotent()
	{
		var redactor = For(WindowsHome, ignoreCase: true);
		var once = redactor.Redact(@"C:\Users\suchbyte\AppData\x.db and /home/other/y.json");

		Assert.Multiple(() =>
		{
			Assert.That(redactor.Redact(once), Is.EqualTo(once));
			Assert.That(redactor.Redact(@"~\AppData\x.db"), Is.EqualTo(@"~\AppData\x.db"));
			Assert.That(redactor.Redact(@"C:\Users\<user>\Documents"), Is.EqualTo(@"C:\Users\<user>\Documents"));
		});
	}

	[Test]
	public void Handles_Empty_Input()
	{
		Assert.Multiple(() =>
		{
			Assert.That(For(LinuxHome).Redact(string.Empty), Is.Empty);
			Assert.That(For(LinuxHome).Redact("   "), Is.EqualTo("   "));
		});
	}
}
