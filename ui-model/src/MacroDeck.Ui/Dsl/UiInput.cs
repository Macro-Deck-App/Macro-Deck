using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Validation;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Dsl;

/// <summary>Base type for value-bearing UI inputs.</summary>
/// <remarks>Input IDs are submitted field names. Nested input containers open an input-ID scope.</remarks>
public abstract record UiInput<T> : UiElement, IUiInputElement
{
	/// <summary>Binding used to read and, when writable, update the value.</summary>
	public UiBinding<T> Binding { get; init; }

	public UiText Label { get; init; }

	/// <summary>Whether <see cref="Label" /> is left unrendered as a caption because the field's surroundings
	/// already name it - a heading above a lone field, or a control it is paired with in a row. The value
	/// itself is still authored and still carries the field's accessible name; only the visible caption is
	/// suppressed.</summary>
	public UiValue<bool> HideLabel { get; init; }

	public UiText Description { get; init; }

	public UiText Placeholder { get; init; }

	public UiValue<T> DefaultValue { get; init; }

	public UiValue<bool> Required { get; init; }

	public UiValue<bool> Disabled { get; init; }

	public UiValue<bool> LiteralOnly { get; init; }

	public UiValue<bool> SupportsReset { get; init; }

	/// <summary>
	/// Whether this input is operated but contributes nothing to the stored configuration: it renders, it
	/// raises its own events, but the client's draft composer (<c>config-draft.util.ts</c>) skips it
	/// entirely, so no key is ever written for it. For a value derived from another stored key rather than
	/// stored itself - the Action Button font family, which the client offers as its own control even
	/// though only <c>fontFaceId</c> is a real key (issue #837).
	/// </summary>
	public UiValue<bool> Transient { get; init; }

	/// <summary>Validation regular expression. Invalid patterns are ignored.</summary>
	public UiValue<string> ValidationRegex { get; init; }

	public UiValue<int> MaxLength { get; init; }

	/// <summary>Renderer-level visibility. Hidden fields keep their value and remain submitted.</summary>
	public UiValue<UiVisibleWhen> VisibleWhen { get; init; }

	/// <summary>Explicit validation state. When absent, declared constraints determine it.</summary>
	public UiValue<bool> Invalid { get; init; }

	/// <summary>Explicit validation message. When absent, declared constraints determine it.</summary>
	public UiText ValidationMessage { get; init; }

	/// <summary>Additional validation rules evaluated after built-in constraints.</summary>
	public IReadOnlyList<UiValidationRule> ValidationRules { get; init; } = [];

	protected internal override IReadOnlyList<string> DeclaredEvents
	{
		get
		{
			var names = new List<string>(base.DeclaredEvents);

			if (Binding.CanWrite && !names.Contains(ChangeEvent, StringComparer.Ordinal))
			{
				names.Insert(0, ChangeEvent);
			}

			return names;
		}
	}

	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(ValueProperty, Binding.Value);
		properties.Set(LabelProperty, Label.Value);
		properties.Set(HideLabelProperty, HideLabel);
		properties.Set(DescriptionProperty, Description.Value);
		properties.Set(PlaceholderProperty, Placeholder.Value);
		properties.Set(DefaultValueProperty, DefaultValue);
		properties.Set(RequiredProperty, Required);
		properties.Set(DisabledProperty, Disabled);
		properties.Set(LiteralOnlyProperty, LiteralOnly);
		properties.Set(SupportsResetProperty, SupportsReset);
		properties.Set(TransientProperty, Transient);
		properties.Set(ValidationRegexProperty, ValidationRegex);
		properties.Set(MaxLengthProperty, MaxLength);
		properties.Set(VisibleWhenProperty, VisibleWhen);

		if (!HasValidation)
		{
			return;
		}

		properties.Set(InvalidProperty,
			Invalid.IsDeclared
				? Invalid
				: UiValue.Optional(() => Validate().IsValid ? UiValue.None<bool>() : UiValue.Of(true)));

		properties.Set(ValidationMessageProperty,
			ValidationMessage.IsDeclared
				? ValidationMessage.Value
				: UiValue.Optional(() => Validate() is { IsValid: false, Message: { } message }
					? UiValue.Of(message)
					: UiValue.None<LocalizedText>()));
	}

	protected internal virtual UiValue<double> MinConstraint => default;

	protected internal virtual UiValue<double> MaxConstraint => default;

	private UiValidationOutcome Validate()
	{
		var visible = !VisibleWhen.TryEvaluate(out var visibleWhen) || visibleWhen is null || visibleWhen.IsSatisfied();

		return new UiValidation
		{
			Type = Type,
			Name = Key,
			Label = Label.TryEvaluate(out var label) ? label : null,
			Value = Binding.Value.TryEvaluate(out var value) ? value : null,
			Visible = visible,
			Required = Required.TryEvaluate(out var required) && required,
			ValidationRegex = ValidationRegex.TryEvaluate(out var pattern) ? pattern : null,
			MaxLength = MaxLength.TryEvaluate(out var maxLength) ? maxLength : null,
			Min = MinConstraint.TryEvaluate(out var min) ? min : null,
			Max = MaxConstraint.TryEvaluate(out var max) ? max : null,
			Rules = ValidationRules
		}.Validate();
	}

	private bool HasValidation
		=> Invalid.IsDeclared
			|| ValidationMessage.IsDeclared
			|| Required.IsDeclared
			|| ValidationRegex.IsDeclared
			|| MaxLength.IsDeclared
			|| MinConstraint.IsDeclared
			|| MaxConstraint.IsDeclared
			|| ValidationRules.Count > 0;

	bool IUiInputElement.CanWriteValue => Binding.CanWrite;

	bool IUiInputElement.TryWriteValue(JsonElement? data, out string? rejection)
	{
		if (!Binding.CanWrite)
		{
			rejection = "The binding is read-only, so the value cannot be written.";

			return false;
		}

		if (data is not { } element || element.ValueKind is JsonValueKind.Undefined)
		{
			rejection = "A change event carries the new value as its data, which was absent.";

			return false;
		}

		if (element.ValueKind is JsonValueKind.Null && default(T) is not null)
		{
			rejection = $"A JSON null cannot be written to a '{typeof(T).Name}' value.";

			return false;
		}

		T decoded;

		try
		{
			decoded = element.Deserialize<T>(UiCanonicalJson.Options)!;
		}
		catch (JsonException)
		{
			rejection = $"A JSON {element.ValueKind} payload cannot be written to a '{typeof(T).Name}' value.";

			return false;
		}
		catch (NotSupportedException)
		{
			rejection = $"A '{typeof(T).Name}' value cannot be decoded from an event payload.";

			return false;
		}

		if (!AcceptsValue(decoded, out rejection))
		{
			return false;
		}

		Binding.Write(decoded);
		rejection = null;

		return true;
	}

	protected private virtual bool AcceptsValue(T? value, out string? rejection)
	{
		rejection = null;

		return true;
	}

	private const string ValueProperty = "value";

	private const string LabelProperty = "label";
	private const string HideLabelProperty = "hideLabel";
	private const string DescriptionProperty = "description";
	private const string PlaceholderProperty = "placeholder";
	private const string DefaultValueProperty = "defaultValue";
	private const string RequiredProperty = "required";
	private const string DisabledProperty = "disabled";
	private const string LiteralOnlyProperty = "literalOnly";
	private const string SupportsResetProperty = "supportsReset";
	private const string TransientProperty = "transient";
	private const string ValidationRegexProperty = "validationRegex";
	private const string MaxLengthProperty = "maxLength";
	private const string VisibleWhenProperty = "visibleWhen";
	private const string InvalidProperty = "invalid";
	private const string ValidationMessageProperty = "validationMessage";
	private const string ChangeEvent = "change";
}

/// <summary>An input that groups children and opens an input-ID scope.</summary>
public abstract record UiInputContainer<T> : UiInput<T>, IUiInputContainerElement
{
	public IReadOnlyList<UiElement> Children
	{
		get;
		init => field = UiElementLists.DropNullElements(value);
	} = [];
}

internal interface IUiInputElement
{
	bool CanWriteValue { get; }

	bool TryWriteValue(JsonElement? data, out string? rejection);
}

internal interface IUiInputContainerElement : IUiInputElement
{
	IReadOnlyList<UiElement> Children { get; }
}
