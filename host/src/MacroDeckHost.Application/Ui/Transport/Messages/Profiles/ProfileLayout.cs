namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class ProfileLayout
{
	public int Rows { get; set; }
	public int Columns { get; set; }
	public bool RowsLocked { get; set; }
	public bool ColumnsLocked { get; set; }

	/// <summary>The grid constraint the devices claiming this profile place on it, or null when no
	/// claiming device declares one (issue #384). Additive: absent from the wire format for clients
	/// that predate it.</summary>
	public ProfileLayoutConstraint? Constraint { get; set; }

	/// <summary>Whether the profile's current grid still satisfies <see cref="Constraint" />, or null
	/// alongside a null <see cref="Constraint" />.</summary>
	public ProfileLayoutCompatibility? Compatibility { get; set; }
}
