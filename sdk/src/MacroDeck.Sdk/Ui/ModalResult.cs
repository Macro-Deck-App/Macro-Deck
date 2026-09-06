namespace MacroDeck.Sdk.Ui;

/// <summary>
/// How a modal ended. <see cref="Cancelled" /> is the distinction an action needs before it uses
/// <see cref="Value" />: a user who dismissed the dialog decided nothing, and continuing as though they had
/// is the mistake this type exists to prevent.
/// </summary>
/// <remarks>
/// Everything that is not an explicit completion is a cancellation - the user dismissing the dialog, the
/// client disconnecting, the flow being cancelled, the session faulting, and the run reaching its time
/// limit. An action therefore never has to distinguish "cancelled" from "never answered", and awaiting a
/// modal cannot hang.
/// </remarks>
/// <typeparam name="T">What the modal's completion carries.</typeparam>
public sealed record ModalResult<T>
{
	/// <summary>Whether the modal ended without the user completing it.</summary>
	public required bool Cancelled { get; init; }

	/// <summary>What the completion carried. Always <c>default</c> when <see cref="Cancelled" />.</summary>
	public T? Value { get; init; }
}

/// <summary>Factories for <see cref="ModalResult{T}" />, which cannot declare them itself.</summary>
public static class ModalResult
{
	/// <summary>A completion carrying <paramref name="value" />.</summary>
	public static ModalResult<T> FromValue<T>(T? value) => new() { Cancelled = false, Value = value };

	/// <summary>A cancellation, whatever caused it.</summary>
	public static ModalResult<T> FromCancellation<T>() => new() { Cancelled = true };
}
