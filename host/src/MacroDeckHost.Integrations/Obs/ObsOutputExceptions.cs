namespace MacroDeckHost.Integrations.Obs;

internal sealed class ObsRequestUnsupportedException(string request)
	: Exception($"OBS does not support the request {request}");

internal sealed class ObsOutputNotFoundException(string output)
	: Exception($"OBS has no output named {output}");

internal enum ObsOutputOutcome
{
	Done,
	NotConnected,
	Unsupported,
	NotFound,
	Failed
}

internal readonly record struct ObsOutputRead(ObsOutputOutcome Outcome, bool Active);
