namespace MacroDeckHost.Application.Deck;

/// <summary>
/// The origin client id a device session acts under. The <c>device:</c> prefix is reserved: a client id
/// that starts with it is never a WebSocket connection, which is what lets navigation raised by a device
/// press route back to that device alone instead of to a transport group.
/// </summary>
public static class DeviceOrigin
{
	public const string Prefix = "device:";

	public static string For(Guid deviceId) => $"{Prefix}{deviceId:D}";

	public static bool TryParse(string? originClientId, out Guid deviceId)
	{
		deviceId = Guid.Empty;
		return originClientId is not null &&
			originClientId.StartsWith(Prefix, StringComparison.Ordinal) &&
			Guid.TryParse(originClientId[Prefix.Length..], out deviceId);
	}
}
