using System.Text;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeckHost.Application.Plugins.Pairing;

/// <summary>
/// Sanitises the self-reported strings on a pairing request before they are ever stored or shown to a
/// user. <see cref="PluginPairingRequest.DisplayName" /> and the client's executable path are rendered
/// verbatim in the desktop approval prompt, so an attacker-chosen value must not be able to impersonate
/// Macro Deck's own chrome (control characters, unbounded length) while a human is deciding whether to
/// trust it.
/// </summary>
public static class PluginPairingClientSanitizer
{
	public const int MaxDisplayNameLength = 100;

	public const int MaxExecutablePathLength = 260;

	public static string SanitizeDisplayName(string displayName)
		=> Truncate(StripControlCharacters(displayName.Trim()), MaxDisplayNameLength);

	public static PluginPairingClientInfo? Sanitize(PluginPairingClientInfo? client)
	{
		if (client is null)
		{
			return null;
		}

		return client with
		{
			ExecutablePath = client.ExecutablePath is { } path
				? Truncate(StripControlCharacters(path.Trim()), MaxExecutablePathLength)
				: null,
			SdkVersion = client.SdkVersion is { } sdkVersion ? StripControlCharacters(sdkVersion.Trim()) : null
		};
	}

	private static string Truncate(string value, int maxLength)
		=> value.Length <= maxLength ? value : value[..maxLength];

	private static string StripControlCharacters(string value)
	{
		StringBuilder? builder = null;

		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];
			var isPrintable = !char.IsControl(c);

			if (!isPrintable && builder is null)
			{
				builder = new StringBuilder(value, 0, i, value.Length);
			}

			if (isPrintable)
			{
				builder?.Append(c);
			}
		}

		return builder?.ToString() ?? value;
	}
}
