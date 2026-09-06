using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// Compares two projected surfaces by everything except <see cref="DeviceSurface.Revision" />. This is
/// the definition of "render-relevant": a rebuild that lands on the same content is not pushed and does
/// not advance the revision, so editing a widget's flows - which the surface never carries - is
/// invisible to the device.
/// </summary>
/// <remarks>
/// Written out rather than left to record equality: the surface holds lists and a dictionary, which
/// records compare by reference, so two structurally identical rebuilds would always compare unequal.
/// </remarks>
internal static class DeviceSurfaceComparison
{
	public static bool SameContent(DeviceSurface left, DeviceSurface right)
		=> left.Profile == right.Profile &&
			left.Folder == right.Folder &&
			left.Layout == right.Layout &&
			left.Widgets.Count == right.Widgets.Count &&
			left.Widgets.Zip(right.Widgets).All(pair => SameWidget(pair.First, pair.Second));

	private static bool SameWidget(DeviceSurfaceWidget left, DeviceSurfaceWidget right)
		=> string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
			string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
			left.PositionX == right.PositionX &&
			left.PositionY == right.PositionY &&
			left.Width == right.Width &&
			left.Height == right.Height &&
			left.IsPinned == right.IsPinned &&
			string.Equals(left.StateId, right.StateId, StringComparison.Ordinal) &&
			string.Equals(left.StateLabel, right.StateLabel, StringComparison.Ordinal) &&
			left.SupportedInteractions.SequenceEqual(right.SupportedInteractions) &&
			SameAppearance(left.Appearance, right.Appearance);

	private static bool SameAppearance(DeviceSurfaceAppearance? left, DeviceSurfaceAppearance? right)
	{
		if (left is null || right is null)
		{
			return left is null && right is null;
		}

		return string.Equals(left.Label, right.Label, StringComparison.Ordinal) &&
			string.Equals(left.LabelColor, right.LabelColor, StringComparison.Ordinal) &&
			string.Equals(left.BackgroundColor, right.BackgroundColor, StringComparison.Ordinal) &&
			string.Equals(left.IconId, right.IconId, StringComparison.Ordinal) &&
			string.Equals(left.IconVersion, right.IconVersion, StringComparison.Ordinal) &&
			left.HasProviderIcon == right.HasProviderIcon &&
			string.Equals(left.IconFit, right.IconFit, StringComparison.Ordinal) &&
			left.IconZoom == right.IconZoom &&
			left.IconOffsetX == right.IconOffsetX &&
			left.IconOffsetY == right.IconOffsetY &&
			left.FontSize == right.FontSize &&
			string.Equals(left.TextAlign, right.TextAlign, StringComparison.Ordinal) &&
			string.Equals(left.LabelPosition, right.LabelPosition, StringComparison.Ordinal) &&
			SameExtra(left.Extra, right.Extra);
	}

	private static bool SameExtra(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
		=> left.Count == right.Count &&
			left.All(entry => right.TryGetValue(entry.Key, out var value) &&
				string.Equals(entry.Value, value, StringComparison.Ordinal));
}
