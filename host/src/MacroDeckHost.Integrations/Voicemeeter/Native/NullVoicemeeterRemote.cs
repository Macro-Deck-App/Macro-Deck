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

	public VoicemeeterResult Login()
	{
		return VoicemeeterResult.Unavailable;
	}

	public void Logout()
	{
	}

	public VoicemeeterResult RunVoicemeeter(int runType)
	{
		return VoicemeeterResult.Unavailable;
	}

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

	public bool IsParametersDirty()
	{
		return false;
	}

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

	public VoicemeeterResult SetParameter(string name, float value)
	{
		return VoicemeeterResult.Unavailable;
	}

	public VoicemeeterResult SetParameter(string name, string value)
	{
		return VoicemeeterResult.Unavailable;
	}

	public VoicemeeterResult RunScript(string script)
	{
		return VoicemeeterResult.Unavailable;
	}

	public bool IsMacroButtonDirty()
	{
		return false;
	}

	public VoicemeeterResult GetMacroButton(int button, VoicemeeterMacroButtonMode mode, out bool state)
	{
		state = false;
		return VoicemeeterResult.Unavailable;
	}

	public VoicemeeterResult SetMacroButton(int button, VoicemeeterMacroButtonMode mode, bool state)
	{
		return VoicemeeterResult.Unavailable;
	}

	public void Dispose()
	{
	}
}
