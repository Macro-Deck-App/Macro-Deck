namespace MacroDeckHost.Integrations.Voicemeeter;

internal static class VoicemeeterEventIds
{
	public const string Connected = "connected";
	public const string Disconnected = "disconnected";
	public const string StripMuteChanged = "strip-mute-changed";
	public const string BusMuteChanged = "bus-mute-changed";
	public const string StripGainChanged = "strip-gain-changed";
	public const string BusGainChanged = "bus-gain-changed";
	public const string StripRoutingChanged = "strip-routing-changed";
	public const string MacroButtonChanged = "macro-button-changed";
}
