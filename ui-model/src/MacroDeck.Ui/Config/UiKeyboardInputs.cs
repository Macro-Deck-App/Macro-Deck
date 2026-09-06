using System.Text.Json;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// A recorded global hotkey. Counterpart of the existing <c>Hotkey</c> parameter type, which the editor renders
/// as its hotkey recorder.
///
/// <para>
/// The value is a <see cref="JsonElement" />: the hotkey model is a structured object the editor owns, and
/// restating its shape here would freeze a second copy of it in this package's public surface. A
/// <see cref="JsonElement" /> passes the client's own value through unchanged, which is what a value this
/// package never interprets should do. It is authored with <see cref="UiValue.Of{T}" /> or by binding a state
/// holding one.
/// </para>
/// </summary>
public sealed record UiHotkeyInput : UiInput<JsonElement>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Hotkey;
}

/// <summary>A multi-step keyboard sequence - combos, text, delays, key down and up. Counterpart of the
/// <c>KeyboardSequence</c> parameter type, which the editor renders as its sequence editor. The value is
/// structured and passed through verbatim, for the reason <see cref="UiHotkeyInput" /> gives.</summary>
public sealed record UiKeyboardSequenceInput : UiInput<JsonElement>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.KeyboardSequence;
}

/// <summary>A single key combination. Counterpart of the <c>KeyboardCombo</c> parameter type, which the editor
/// renders as its combo editor. The value is structured and passed through verbatim, for the reason
/// <see cref="UiHotkeyInput" /> gives.</summary>
public sealed record UiKeyboardComboInput : UiInput<JsonElement>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.KeyboardCombo;
}
