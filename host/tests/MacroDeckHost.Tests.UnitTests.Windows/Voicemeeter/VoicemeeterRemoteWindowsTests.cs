using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Voicemeeter.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Voicemeeter;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class VoicemeeterRemoteWindowsTests
{
	[Test]
	public void Looking_for_the_installation_never_throws()
		=> Assert.DoesNotThrow(() => VoicemeeterInstallation.FindRemoteLibrary());

	[Test]
	public void The_factory_produces_a_remote_whatever_is_installed()
	{
		using var remote = VoicemeeterRemoteFactory.Create();

		if (remote.IsAvailable)
		{
			Assert.That(remote.UnavailableReason, Is.Null);
			return;
		}

		// LocalizedText is a struct, so Is.Not.Empty cannot read it: NUnit's EmptyConstraint only knows
		// strings and collections and throws on anything else.
		Assert.That(remote.UnavailableReason, Is.Not.Null);
		Assert.That(remote.UnavailableReason!.Value.IsEmpty, Is.False);
	}

	[Test]
	public void Reading_a_parameter_without_a_running_Voicemeeter_answers_a_code()
	{
		using var remote = VoicemeeterRemoteFactory.Create();
		if (!remote.IsAvailable)
		{
			Assert.Ignore("Voicemeeter is not installed on this machine.");
		}

		Assert.DoesNotThrow(() =>
		{
			remote.Login();
			remote.GetParameter("Strip[0].Gain", out float _);
			remote.IsParametersDirty();
			remote.Logout();
		});
	}
}
