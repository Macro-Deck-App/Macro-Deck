using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter.Native;

internal static class VoicemeeterRemoteFactory
{
	public static IVoicemeeterRemote Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsVoicemeeterRemote();
		}

		return new NullVoicemeeterRemote(AppStrings.Integrations.Voicemeeter.Issues.NotWindowsReason());
	}
}
