namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// Implemented by integrations that can be set up through a multi-step config flow
/// (Home-Assistant style). The host calls <see cref="CreateConfigFlow"/> once per setup
/// session.
/// </summary>
public interface IConfigFlowProvider
{
	IConfigFlow CreateConfigFlow();

	/// <summary>
	/// Whether more than one configuration may be created. Defaults to <c>true</c>; integrations that
	/// only ever make sense with a single configuration (e.g. a single Spotify account) return
	/// <c>false</c> so the host and UI prevent adding a second one.
	/// </summary>
	bool AllowsMultipleConfigurations => true;
}
