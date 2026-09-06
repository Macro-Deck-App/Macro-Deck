using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter.Native;

internal sealed class NullVoicemeeterRemote : IVoicemeeterRemote
{
	public NullVoicemeeterRemote(LocalizedText reason)
	{
		UnavailableReason = reason;
	}

	public bool IsAvailable => false;

	public LocalizedText? UnavailableReason { get; }

	public VoicemeeterResult Login() => VoicemeeterResult.Unavailable;

	public void Logout()
	{
	}

	public VoicemeeterResult RunVoicemeeter(int runType) => VoicemeeterResult.Unavailable;

	public VoicemeeterResult GetVoicemeeterType(out int rawType)
	{
		rawType = 0;
		return VoicemeeterResult.Unavailable;
	}

	public VoicemeeterResult GetVoicemeeterVersion(out int packedVersion)
	{
		packedVersion = 0;
		return VoicemeeterResult.Unavailable;
	}

	public bool IsParametersDirty() => false;

	public VoicemeeterResult GetParameter(string name, out float value)
	{
		value = 0f;
		return VoicemeeterResult.Unavailable;
	}

	public VoicemeeterResult GetParameter(string name, out string value)
	{
		value = string.Empty;
		return VoicemeeterResult.Unavailable;
	}

	public VoicemeeterResult SetParameter(string name, float value) => VoicemeeterResult.Unavailable;

	public VoicemeeterResult SetParameter(string name, string value) => VoicemeeterResult.Unavailable;

	public VoicemeeterResult RunScript(string script) => VoicemeeterResult.Unavailable;

	public bool IsMacroButtonDirty() => false;

	public VoicemeeterResult GetMacroButton(int button, VoicemeeterMacroButtonMode mode, out bool state)
	{
		state = false;
		return VoicemeeterResult.Unavailable;
	}

	public VoicemeeterResult SetMacroButton(int button, VoicemeeterMacroButtonMode mode, bool state)
		=> VoicemeeterResult.Unavailable;

	public void Dispose()
	{
	}
}
