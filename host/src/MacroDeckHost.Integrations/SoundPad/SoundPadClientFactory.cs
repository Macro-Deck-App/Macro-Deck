namespace MacroDeckHost.Integrations.SoundPad;

internal static class SoundPadClientFactory
{
	public static ISoundPadClient Create()
		=> OperatingSystem.IsWindows()
			? new SoundPadPipeClient()
			: throw new PlatformNotSupportedException("SoundPad remote control is only available on Windows.");
}
