namespace MacroDeck.Sdk.Devices;

/// <summary>
/// One of the labels an Action Button shows besides its primary one - see
/// <see cref="DeviceSurfaceAppearance.ExtraLabels" />. Every value is already resolved.
/// </summary>
public sealed record DeviceSurfaceLabel
{
	/// <summary>The label's text, already resolved - variables and templates expanded. Null or empty
	/// draws nothing.</summary>
	public string? Text { get; init; }

	public string? Color { get; init; }

	/// <summary>Font size as a percentage of the widget's smaller dimension. Null means the default.</summary>
	public double? FontSize { get; init; }

	/// <summary>Where the label sits across the button's content box, in percent: <c>0</c> is flush left,
	/// <c>50</c> centred and <c>100</c> flush right, so the label never leaves the box.</summary>
	public double X { get; init; } = 50;

	/// <summary>The same down the content box: <c>0</c> is flush top and <c>100</c> flush bottom.</summary>
	public double Y { get; init; } = 50;
}
