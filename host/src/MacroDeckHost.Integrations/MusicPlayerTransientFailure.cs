namespace MacroDeckHost.Integrations;

internal static class MusicPlayerTransientFailure
{
	internal static bool IsTransientStatusCode(int statusCode)
	{
		return statusCode == 429 || statusCode >= 500;
	}

	internal static bool IsNetworkLevel(Exception ex)
	{
		return ex is HttpRequestException or TaskCanceledException or IOException;
	}
}
