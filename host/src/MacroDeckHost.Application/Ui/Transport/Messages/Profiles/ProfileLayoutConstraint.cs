namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

/// <summary>Wire form of <c>DeviceGridConstraint</c> (issue #384): what the devices claiming this
/// profile say the grid is allowed to be.</summary>
public class ProfileLayoutConstraint
{
	public int Rows { get; set; }
	public int Columns { get; set; }
	public int MinRows { get; set; }
	public int MaxRows { get; set; }
	public int MinColumns { get; set; }
	public int MaxColumns { get; set; }
	public bool RowsLocked { get; set; }
	public bool ColumnsLocked { get; set; }
	public string? LayoutId { get; set; }
	public string? LayoutName { get; set; }
	public string? DeviceName { get; set; }

	/// <summary>False when the claiming surface ignores the profile's widget spacing. Not a lock: the
	/// value is not fixed, it simply changes nothing there.</summary>
	public bool WidgetSpacingHonoured { get; set; } = true;

	/// <summary>False when the claiming surface ignores the profile's corner radius.</summary>
	public bool CornerRadiusHonoured { get; set; } = true;

	/// <summary>False when the claiming surface cannot render a folder view at all, so Macro Deck does not
	/// offer one for this profile. Unlike the two above this is not about fidelity - see the SDK's
	/// <c>LayoutVisualCapabilities.CustomFolderViews</c>.</summary>
	public bool CustomFolderViewsSupported { get; set; } = true;
}
