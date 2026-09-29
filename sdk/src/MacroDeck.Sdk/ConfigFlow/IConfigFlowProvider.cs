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

	/// <summary>
	/// Whether the integration needs a configuration before it can run. Defaults to <c>true</c>: the
	/// integration starts disabled, is shown as needing setup, and turning it on opens the flow.
	/// </summary>
	/// <remarks>
	/// Return <c>false</c> when every setting the flow collects has a default. The integration then runs
	/// with no config entry (<see cref="IIntegrationConfig.GetEntriesAsync" /> returns an empty list, so
	/// use the defaults), is never shown as needing setup, and the flow stays available on its page for
	/// changing the settings. Saving the flow enables and reinitializes the integration; removing the
	/// entry keeps it running on the defaults. Read when the provider is discovered, so it must be
	/// side-effect free. Hosts up to 3.0.0-beta.14 ignore it and treat every flow as required.
	/// </remarks>
	bool RequiresConfiguration => true;
}
