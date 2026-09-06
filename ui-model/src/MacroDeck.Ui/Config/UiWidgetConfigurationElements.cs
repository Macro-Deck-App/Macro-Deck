using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// The root of a widget's configuration surface. It carries two regions and nothing else, because the
/// surrounding editor is Macro Deck's: the widget preview, the split layout, the visual and JSON modes,
/// scrolling, saving and navigation all stay with the application, and a widget supplies only the views.
///
/// <para>
/// Splitting the surface into named regions rather than one tree is what makes that division expressible
/// without naming a renderer. <see cref="Properties" /> is a widget's ordinary field list and
/// <see cref="Editor" /> is the room a field list would not fit in; which side of a window each lands on,
/// and whether the layout is a split or a drawer, is the renderer's to decide.
/// </para>
///
/// <para>
/// Both regions share one input-id namespace - <see cref="UiContainer" /> opens no scope - so a top-level
/// input's id is the widget data key it configures, exactly as a configuration tree's top-level input id is
/// the field key it submits. Nesting is expressed with <see cref="UiObjectInput" /> and
/// <see cref="UiArrayInput" />, which do open one.
/// </para>
/// </summary>
public sealed record UiWidgetConfiguration : UiContainer
{
	/// <summary>The widget's ordinary property configuration. Required: a widget with nothing to configure
	/// serves no configuration surface at all rather than an empty one.</summary>
	public required UiWidgetProperties Properties
	{
		get;
		init
		{
			field = value;
			Children = Compose(value, Editor);
		}
	}

	/// <summary>The widget's specialized configuration, or null when it needs none.</summary>
	public UiWidgetEditor? Editor
	{
		get;
		init
		{
			field = value;
			Children = Compose(Properties, value);
		}
	}

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.WidgetConfiguration;

	// The regions are the authored contract, but the runtime materializes a container's Children, so the two
	// named properties have to write through to it. Each recomposes from both, so an initializer setting
	// Editor before Properties ends up with the same children as one setting them the other way round.
	private static IReadOnlyList<UiElement> Compose(UiWidgetProperties? properties, UiWidgetEditor? editor)
	{
		if (properties is null)
		{
			return editor is null ? [] : [editor];
		}

		return editor is null ? [properties] : [properties, editor];
	}
}

/// <summary>A widget's ordinary property configuration, drawn wherever the renderer puts a widget's fields -
/// beside the preview in the desktop editor.</summary>
public sealed record UiWidgetProperties : UiContainer
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.WidgetProperties;
}

/// <summary>A widget's specialized configuration, given the room a property list would not fit in - the
/// Action Button's action editor is the case this exists for.</summary>
public sealed record UiWidgetEditor : UiContainer
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.WidgetEditor;
}
