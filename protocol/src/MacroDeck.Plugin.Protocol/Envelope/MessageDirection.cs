namespace MacroDeck.Plugin.Protocol.Envelope;

/// <summary>Which side of the session a message type is expected to originate from.</summary>
public enum MessageDirection
{
	PluginToHost,
	HostToPlugin,
	Bidirectional,
}
