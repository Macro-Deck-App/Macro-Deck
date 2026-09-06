namespace MacroDeck.Sdk;

/// <summary>
/// Implemented by integrations that ship a brand icon (e.g. the Spotify logo). The host serves the
/// bytes so the UI can display the icon next to the integration and its music player instances.
/// </summary>
public interface IIntegrationIconProvider
{
	/// <summary>MIME type of the icon, e.g. "image/svg+xml" or "image/png".</summary>
	string IconMimeType { get; }

	/// <summary>The raw icon bytes.</summary>
	byte[] GetIcon();
}
