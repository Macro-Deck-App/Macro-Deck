namespace MacroDeckHost.Application.VideoStreams;

public enum VideoStreamError
{
	Busy,
	UnknownProvider,
	UnknownStream,
	UnknownSession,
	SessionLimitReached,
	PayloadTooLarge,
	StreamUnavailable,
	TransportNotAccepted,
	SignalingUnsupported,
	ProviderUnavailable,
	Failed
}
