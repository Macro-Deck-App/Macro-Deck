namespace MacroDeck.Sdk.Devices;

/// <summary>
/// The deck surface a device should render: which profile and folder it belongs to, the effective
/// layout, and the widgets placed on it.
/// </summary>
public sealed record DeviceSurface
{
	/// <summary>
	/// The placeholder <see cref="IDeviceSession.CurrentSurface" /> holds until the host's first push: no
	/// profile, no widgets, revision 0, so it is below every surface the host pushes. A surface the host
	/// pushes for a device with no profile assigned is a separate one with a revision of 1 or more. A
	/// provider should show nothing for either.
	/// </summary>
	public static readonly DeviceSurface Empty = new()
	{
		Revision = 0, Profile = null, Folder = null, Layout = new DeviceSurfaceLayout { Rows = 0, Columns = 0 },
		Widgets = []
	};

	/// <summary>
	/// Monotonically increasing within a session: the host's first push is 1, and only the
	/// <see cref="Empty" /> placeholder is 0. A provider must drop a surface whose revision is not greater
	/// than the last one it applied - out-of-order delivery is possible over the transport.
	/// </summary>
	public required long Revision { get; init; }

	public DeviceSurfaceProfile? Profile { get; init; }

	public DeviceSurfaceFolder? Folder { get; init; }

	public required DeviceSurfaceLayout Layout { get; init; }

	public required IReadOnlyList<DeviceSurfaceWidget> Widgets { get; init; }
}
