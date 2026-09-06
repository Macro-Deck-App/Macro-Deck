using MacroDeckHost.Integrations.Voicemeeter.Native;

namespace MacroDeckHost.Tests.UnitTests.Voicemeeter;

[TestFixture]
internal sealed class VoicemeeterInstallationTests
{
	[TestCase(@"""C:\Program Files (x86)\VB\Voicemeeter\voicemeeterpro.exe"" -uninstall",
		@"C:\Program Files (x86)\VB\Voicemeeter\voicemeeterpro.exe")]
	[TestCase(@"""C:\Program Files (x86)\VB\Voicemeeter\voicemeeter.exe""",
		@"C:\Program Files (x86)\VB\Voicemeeter\voicemeeter.exe")]
	[TestCase(@"C:\VB\Voicemeeter\voicemeeter8x64.exe -uninstall",
		@"C:\VB\Voicemeeter\voicemeeter8x64.exe")]
	[TestCase(@"  C:\VB\Voicemeeter\voicemeeter.exe  ", @"C:\VB\Voicemeeter\voicemeeter.exe")]
	public void The_executable_is_pulled_out_of_an_uninstall_string(string uninstallString, string expected)
		=> Assert.That(VoicemeeterInstallation.ExtractExecutablePath(uninstallString), Is.EqualTo(expected));

	[Test]
	public void An_unrecognised_uninstall_string_is_left_alone()
		=> Assert.That(VoicemeeterInstallation.ExtractExecutablePath(@"C:\VB\Voicemeeter"),
			Is.EqualTo(@"C:\VB\Voicemeeter"));

	[Test]
	public void An_unterminated_quote_does_not_throw()
		=> Assert.That(VoicemeeterInstallation.ExtractExecutablePath(@"""C:\VB\voicemeeter.exe"),
			Is.EqualTo(@"C:\VB\voicemeeter.exe"));
}
