namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The deck surface a device should render: which profile and folder it belongs to, the effective
/// layout, and the widgets placed on it.
/// </summary>
public sealed record DeviceSurface
{
	/// <summary>No profile assigned, or none resolvable. A provider should show nothing.</summary>
	public static readonly DeviceSurface Empty = new()
	{
		Revision = 1, Profile = null, Folder = null, Layout = new DeviceSurfaceLayout { Rows = 0, Columns = 0 },
		Widgets = []
	};

	/// <summary>
	/// Monotonically increasing within a session, starting at 1. A provider must drop a surface whose
	/// revision is not greater than the last one it applied - out-of-order delivery is possible over the
	/// transport.
	/// </summary>
	public required long Revision { get; init; }

	public DeviceSurfaceProfile? Profile { get; init; }

	public DeviceSurfaceFolder? Folder { get; init; }

	public required DeviceSurfaceLayout Layout { get; init; }

	public required IReadOnlyList<DeviceSurfaceWidget> Widgets { get; init; }
}
