namespace MacroDeckHost.Integrations;

internal static class MusicPlayerTransientFailure
{
	internal static bool IsTransientStatusCode(int statusCode) => statusCode == 429 || statusCode >= 500;

	internal static bool IsNetworkLevel(Exception ex) =>
		ex is HttpRequestException or TaskCanceledException or IOException;
}
