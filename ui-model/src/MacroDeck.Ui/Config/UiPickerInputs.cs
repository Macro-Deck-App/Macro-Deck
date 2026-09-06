using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>A toggle. Counterpart of the existing <c>Boolean</c> parameter type, which the editor renders as a
/// checkbox, or as a switch when the value cannot be variable-bound. Never empty for validation: false is an
/// answer.</summary>
public sealed record UiBooleanInput : UiInput<bool>
{
	/// <summary>Draws the value as two labelled options instead of a toggle - a presentation flag, not a
	/// second node type, the same way <see cref="UiChoiceInput.Segmented" /> is for a choice. Counterpart of
	/// the original Action Button appearance form's Single state / Multi state switch: that switch is two
	/// options over a value the schema declares as a boolean, and a choice cannot carry that value, since
	/// <see cref="UiChoiceInput" /> is a <see cref="UiOptionsInput{T}" /> of <c>string</c>.</summary>
	public UiValue<bool> Segmented { get; init; }

	/// <summary>The caption for the <c>false</c> option, shown while <see cref="Segmented" /> is set.
	/// </summary>
	public UiText FalseLabel { get; init; }

	/// <summary>The caption for the <c>true</c> option, shown while <see cref="Segmented" /> is set.
	/// </summary>
	public UiText TrueLabel { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Boolean;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Segmented, Segmented);
		properties.Set(UiConfigProperties.FalseLabel, FalseLabel.Value);
		properties.Set(UiConfigProperties.TrueLabel, TrueLabel.Value);
	}
}

/// <summary>A colour. Counterpart of the <c>Color</c> parameter type, which the editor renders as its colour
/// picker and which is the one type whose reset affordance the existing schema makes opt-in.</summary>
public sealed record UiColorInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Color;
}

/// <summary>A date and time. Counterpart of the <c>DateTime</c> parameter type, which the editor renders as its
/// date-time picker over a string value.</summary>
public sealed record UiDateTimeInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.DateTime;
}

/// <summary>A file path. Counterpart of the <c>File</c> parameter type, which the editor renders as its file
/// path input with a browse affordance limited to the declared extensions.</summary>
public sealed record UiFileInput : UiInput<string>
{
	/// <summary>The extensions the browse dialog offers. Authored with <see cref="UiValue.Of{T}" />, since an
	/// interface-typed value has no implicit conversion.</summary>
	public UiValue<IReadOnlyList<string>> FileExtensions { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.File;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.FileExtensions, FileExtensions);
	}
}

/// <summary>A folder path. Counterpart of the <c>Folder</c> parameter type, which the editor renders as the
/// same path input in folder mode - so it carries no extensions.</summary>
public sealed record UiFolderInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Folder;
}

/// <summary>An icon from the icon set. Counterpart of the <c>Icon</c> parameter type, which the editor renders
/// as its widget icon control.</summary>
public sealed record UiIconInput : UiInput<string>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Icon;
}

/// <summary>
/// The same <c>icon</c> input over a typed provider reference rather than a bare icon-pack one. An icon is a
/// provider type plus an opaque reference (ADR 0022), and a value that is only the reference cannot say which
/// provider owns it - so a widget storing a provider-owned icon has no way to express it through
/// <see cref="UiIconInput" />.
///
/// <para>
/// This is a <b>widening of one primitive, not a second primitive</b>: the node type is still
/// <see cref="UiConfigPrimitives.Icon" /> and a renderer draws it with the same control. Only the value has
/// a second reading, which is why it moved <see cref="Model.Versioning.UiModelVersions.Current" /> to 4 and
/// left <see cref="Model.Versioning.UiModelVersions.Minimum" /> alone. A reader accepts both shapes and a
/// bare string reads as an icon-pack reference, so a provider built against an older package keeps working
/// and <see cref="UiIconInput" /> stays exactly as it was.
/// </para>
/// </summary>
public sealed record UiIconReferenceInput : UiInput<UiIconReference>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Icon;
}

/// <summary>An icon as the provider that owns it plus a reference only that provider interprets. The type is
/// deliberately an open string: an icon from a provider this reader does not know about is still a
/// well-formed value, and renders as no icon rather than as a malformed one.</summary>
/// <param name="Type">The icon provider, for example <c>icon-pack</c>.</param>
/// <param name="Reference">The provider-specific reference. Opaque - never assumed to be a GUID.</param>
public sealed record UiIconReference(string Type, string Reference);

/// <summary>
/// An image path. Counterpart of the <c>Image</c> parameter type, which the editor renders as the same path
/// input in image mode. A distinct type rather than a file with image extensions, because that is what the
/// existing enum has - and the editor's image mode previews the file, which its file mode does not.
/// </summary>
public sealed record UiImageInput : UiInput<string>
{
	/// <summary>The extensions the browse dialog offers, defaulting on the client to the image formats it can
	/// render.</summary>
	public UiValue<IReadOnlyList<string>> FileExtensions { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Image;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.FileExtensions, FileExtensions);
	}
}
