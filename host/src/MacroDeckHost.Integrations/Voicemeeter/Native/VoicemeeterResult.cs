namespace MacroDeckHost.Integrations.Voicemeeter.Native;

internal enum VoicemeeterResult
{
	Ok = 0,

	OkNotLaunched = 1,

	Error = -1,

	NoServer = -2,

	UnknownParameter = -3,

	StructureMismatch = -5,

	Unavailable = -100
}

internal static class VoicemeeterResultExtensions
{
	public static bool IsOk(this VoicemeeterResult result)
		=> result is VoicemeeterResult.Ok or VoicemeeterResult.OkNotLaunched;
}
