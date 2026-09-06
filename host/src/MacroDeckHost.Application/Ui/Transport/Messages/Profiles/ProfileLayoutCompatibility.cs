namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

/// <summary>Whether the profile's current grid still satisfies its <see cref="ProfileLayoutConstraint" />
/// (issue #384). Display-only: never resizes the profile or blocks anything by itself.</summary>
public class ProfileLayoutCompatibility
{
	public string Status { get; set; } = "ok"; // "ok" | "conflictingDevices" | "exceedsLayout"
	public IReadOnlyList<string> DeviceNames { get; set; } = [];
}
