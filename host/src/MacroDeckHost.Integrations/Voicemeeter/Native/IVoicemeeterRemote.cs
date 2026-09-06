using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Voicemeeter.Native;

internal interface IVoicemeeterRemote : IDisposable
{
	bool IsAvailable { get; }

	LocalizedText? UnavailableReason { get; }

	VoicemeeterResult Login();

	void Logout();

	VoicemeeterResult RunVoicemeeter(int runType);

	VoicemeeterResult GetVoicemeeterType(out int rawType);

	VoicemeeterResult GetVoicemeeterVersion(out int packedVersion);

	bool IsParametersDirty();

	VoicemeeterResult GetParameter(string name, out float value);

	VoicemeeterResult GetParameter(string name, out string value);

	VoicemeeterResult SetParameter(string name, float value);

	VoicemeeterResult SetParameter(string name, string value);

	VoicemeeterResult RunScript(string script);

	bool IsMacroButtonDirty();

	VoicemeeterResult GetMacroButton(int button, VoicemeeterMacroButtonMode mode, out bool state);

	VoicemeeterResult SetMacroButton(int button, VoicemeeterMacroButtonMode mode, bool state);
}

internal enum VoicemeeterMacroButtonMode
{
	PushRelease = 0x00000000,

	StateOnly = 0x00000002,

	Trigger = 0x00000003
}
