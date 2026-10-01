namespace MacroDeckHost.Application.VideoStreams;

public enum VideoStreamRelayAcquisition
{
	Acquired,
	NotFound,
	Busy
}

public interface IVideoStreamRelay
{
	string Arm(string sessionId, Uri upstream, string transport);

	void Suspend(string sessionId);

	void Revoke(string sessionId);

	VideoStreamRelayAcquisition Acquire(string token, out VideoStreamRelayLease? lease);
}
