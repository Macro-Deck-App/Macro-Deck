namespace MacroDeckHost.Integrations.SoundPad.Protocol;

internal static class SoundPadClientFactory
{
	public static ISoundPadClient Create()
	{
		return OperatingSystem.IsWindows()
			? new SoundPadPipeClient()
			: throw new PlatformNotSupportedException("SoundPad remote control is only available on Windows.");
	}
}
