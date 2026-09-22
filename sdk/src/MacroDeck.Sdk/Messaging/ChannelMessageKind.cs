namespace MacroDeck.Sdk.Messaging;

public enum ChannelMessageKind
{
	/// <summary>Delivered to every matching subscription. Nobody answers.</summary>
	Event = 0,

	/// <summary>Delivered to the topic's one handler, which answers when it finished.</summary>
	Command = 1,

	/// <summary>Delivered to the topic's one handler, which answers with a reply.</summary>
	Request = 2
}
