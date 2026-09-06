namespace MacroDeck.Sdk;

/// <summary>
/// Marks a built-in, host-managed integration that is hidden from the integrations page and whose
/// enabled state is derived automatically (via <see cref="IsActive"/>) rather than toggled or
/// persisted. Used by integrations whose availability depends on runtime state (e.g. deck
/// navigation).
/// </summary>
public interface ISystemIntegration
{
	bool IsActive { get; }
}
