using MacroDeck.Sdk.Ui;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

/// <summary>
/// A UI provider serving surfaces on behalf of a built-in integration whose own assembly cannot reach
/// the host's UI layer.
/// </summary>
/// <remarks>
/// Built-in integrations are written against the SDK exactly as a plugin is, so their assembly sits below
/// the host's UI resources and cannot register the icons or build the trees a dialog needs. This is the
/// same split <see cref="IBuiltInWidgetUiProvider" /> already makes for widgets, and it resolves under the
/// integration's own id, so a modal an integration opens reaches it without the integration itself having
/// to implement <see cref="IUiProvider" />.
/// </remarks>
public interface IBuiltInIntegrationUiProvider : IUiProvider
{
	/// <summary>The integration this provider serves surfaces for.</summary>
	string IntegrationId { get; }
}
