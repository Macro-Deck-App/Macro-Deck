namespace MacroDeckHost.Application.StreamStats;

public interface IStreamStatsSinkConsumer
{
	void UseStreamStatsSink(IStreamStatsSink sink);
}
