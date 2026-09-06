using SpotifyAPI.Web;

namespace MacroDeckHost.Integrations.Spotify;

internal static class SpotifyPremiumRequirement
{
	private static readonly string[] _markers = ["PREMIUM_REQUIRED", "premium required"];

	internal static bool IsRefusal(Exception ex)
	{
		for (var current = ex; current is not null; current = current.InnerException)
		{
			if (current is APIException { Response: { } response } api &&
				(int)response.StatusCode == 403 &&
				Mentions(response.Body as string, api.Message))
			{
				return true;
			}
		}

		return false;
	}

	private static bool Mentions(string? body, string message)
		=> _markers.Any(marker =>
			body?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true ||
			message.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
