namespace MacroDeck.Ui.Dsl;

/// <summary>
/// One position of one pointer, as the pointer family of <see cref="Components.UiComponentEvents" /> reports
/// it. Positions are relative to the node's top-left corner in fractions of the widget basis.
/// </summary>
public readonly struct UiPointerSample
{
	/// <summary>The pointer's id: an opaque integer that stays the same from its
	/// <see cref="Components.UiComponentEvents.PointerDown" /> to its <see cref="Components.UiComponentEvents.PointerUp" />
	/// and is not reused by the reader for another pointer.</summary>
	public int Id { get; init; }

	/// <summary>The horizontal position, growing right.</summary>
	public double X { get; init; }

	/// <summary>The vertical position, growing down.</summary>
	public double Y { get; init; }

	/// <summary>Whole milliseconds since the first pointer of the current contact went down on the node.</summary>
	public double TimeMs { get; init; }
}

/// <summary>The payload of <see cref="Components.UiComponentEvents.PointerDown" />.</summary>
public readonly struct UiPointerDown
{
	/// <summary>Where and when the pointer went down.</summary>
	public UiPointerSample Sample { get; init; }

	/// <summary>The node's width in fractions of the widget basis.</summary>
	public double Width { get; init; }

	/// <summary>The node's height in fractions of the widget basis.</summary>
	public double Height { get; init; }
}

/// <summary>The payload of <see cref="Components.UiComponentEvents.PointerUp" />.</summary>
public readonly struct UiPointerUp
{
	/// <summary>Where and when the pointer lifted.</summary>
	public UiPointerSample Sample { get; init; }

	/// <summary>Whether the platform cancelled the pointer instead of the finger lifting.</summary>
	public bool Cancelled { get; init; }
}
