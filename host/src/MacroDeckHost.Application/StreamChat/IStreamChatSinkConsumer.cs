namespace MacroDeckHost.Application.StreamChat;

public interface IStreamChatSinkConsumer
{
	void UseStreamChatSink(IStreamChatSink sink);
}
