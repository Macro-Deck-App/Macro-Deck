namespace MacroDeckHost.Application.VideoStreams;

public enum VideoStreamError
{
	Busy,
	UnknownProvider,
	UnknownStream,
	UnknownSession,
	SessionLimitReached,
	StreamUnavailable,
	TransportNotAccepted,
	ProviderUnavailable,
	Failed
}
