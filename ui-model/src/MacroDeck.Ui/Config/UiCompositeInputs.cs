using System.Text.Json;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>A map of string keys to string values. Counterpart of the existing <c>KeyValue</c> parameter type,
/// which the editor renders as its key-value editor. Authored with <see cref="UiValue.Of{T}" />, since an
/// interface-typed value has no implicit conversion.</summary>
public sealed record UiKeyValueInput : UiInput<IReadOnlyDictionary<string, string>>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.KeyValue;
}

/// <summary>
/// A fixed set of named fields. Counterpart of the existing <c>Object</c> parameter type, whose children the
/// editor renders as nested parameter rows.
///
/// <para>
/// One of the two primitives that opens an <b>input-id scope</b>: a field nested inside it is addressed as
/// <c>objectId.fieldKey</c> rather than by its bare key, so two objects can both have a <c>name</c> field
/// without their ids colliding tree-wide. Its own value is normally absent - the children carry the data - and
/// is a <see cref="JsonElement" /> for the case where a whole composed object is bound at once.
/// </para>
/// </summary>
public sealed record UiObjectInput : UiInputContainer<JsonElement>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Object;
}

/// <summary>
/// A repeated set of items. Counterpart of the existing <c>Array</c> parameter type, whose item template the
/// editor renders once per item with add and remove affordances - which is why <c>add</c> and <c>remove</c> are
/// in the event vocabulary.
///
/// <para>
/// The other primitive that opens an input-id scope. Items are produced by a
/// <see cref="UiRepeat{TItem}" /> inside it, keyed by the item's own stable key and never by its position: a
/// positional id destroys focus and in-flight edits on every mutation and makes a move meaningless.
/// </para>
/// </summary>
public sealed record UiArrayInput : UiInputContainer<JsonElement>
{
	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Array;
}
