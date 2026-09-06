using MacroDeck.Localization;

namespace MacroDeck.Ui.Dsl;

/// <summary>
/// A user-facing text property: literal text, a computed one, or a <see cref="LocalizedString" /> the
/// reader resolves in the active language. <c>default(UiText)</c> is the absent value.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than <c>UiValue&lt;LocalizedText&gt;</c>, because C# applies at most one
/// user-defined conversion: <c>Label = "API key"</c> would have to go through <c>string</c> to
/// <c>LocalizedText</c> to <c>UiValue&lt;LocalizedText&gt;</c> and would simply stop compiling at every
/// existing call site. One conversion from each source type keeps literal text, computed text and a
/// localized reference all assignable directly.
/// </para>
/// </remarks>
public readonly record struct UiText
{
	private UiText(UiValue<LocalizedText> value) => Value = value;

	/// <summary>The underlying value, evaluated by the runtime when it composes node properties.</summary>
	internal UiValue<LocalizedText> Value { get; }

	/// <summary>Whether this property was set at all.</summary>
	public bool IsDeclared => Value.IsDeclared;

	/// <summary>Literal text, already in its final form.</summary>
	public static implicit operator UiText(string? literal) => new(LocalizedText.FromLiteral(literal));

	/// <summary>A reference resolved in the reader's active language.</summary>
	public static implicit operator UiText(LocalizedString localized)
		=> new(LocalizedText.FromLocalized(localized));

	/// <summary>Either shape, already wrapped.</summary>
	public static implicit operator UiText(LocalizedText text) => new(text);

	/// <summary>A computed string value, so existing <c>UiValue.From(() =&gt; …)</c> call sites keep working.</summary>
	public static implicit operator UiText(UiValue<string> value)
	{
		var captured = value;

		return new UiText(UiValue.Optional(() => captured.TryEvaluate(out var text)
			? UiValue.Of(LocalizedText.FromLiteral(text))
			: UiValue.None<LocalizedText>()));
	}

	/// <summary>Literal text, for a call site that cannot rely on the implicit conversion.</summary>
	public static UiText Of(string? literal) => literal;

	/// <summary>A reference, for a call site that cannot rely on the implicit conversion.</summary>
	public static UiText Of(LocalizedString localized) => localized;

	/// <summary>Text computed on each evaluation.</summary>
	public static UiText From(Func<string?> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiText(UiValue.From(() => LocalizedText.FromLiteral(compute())));
	}

	/// <summary>A reference computed on each evaluation.</summary>
	public static UiText FromLocalized(Func<LocalizedString> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiText(UiValue.From(() => LocalizedText.FromLocalized(compute())));
	}

	/// <summary>Text that may evaluate to absent.</summary>
	public static UiText Optional(Func<UiText> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiText(UiValue.Optional(() => compute().Value));
	}

	/// <summary>The absent value.</summary>
	public static UiText None() => default;

	/// <summary>Evaluates the property.</summary>
	/// <returns><c>false</c> when the property is absent.</returns>
	public bool TryEvaluate(out LocalizedText text) => Value.TryEvaluate(out text);
}
