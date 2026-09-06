using System.Text.Json.Serialization;

namespace MacroDeck.Ui.Config;

/// <summary>
/// Shows a field only while a sibling field holds one of <see cref="Values" />. Serialized as
/// <c>{"parameterName":"...","values":[...]}</c>, which is the shape the existing editor already reads, and
/// carries the existing semantics unchanged:
///
/// <list type="bullet">
/// <item>Exactly one sibling decides, not an expression over several.</item>
/// <item>Any one of <see cref="Values" /> is enough - the comparison is an OR.</item>
/// <item>Comparison is by string, ignoring case, so a picked option value and a hand-typed one agree.</item>
/// <item>A <see cref="ParameterName" /> naming no sibling means visible, so a typo cannot make a field
/// permanently unreachable.</item>
/// <item>A hidden field keeps its value, is still submitted, and is skipped by validation.</item>
/// </list>
///
/// <para>
/// <b>Not the same thing as <see cref="Dsl.UiWhen" /></b>, and the difference is why both exist: a
/// <see cref="Dsl.UiWhen" /> whose condition is false emits no node, so its fields are not in the tree and
/// submit nothing at all. This property emits the node and leaves the hiding to the renderer, so the value
/// survives being hidden and comes back when the sibling changes back. Use this for mutually exclusive modes
/// over the same submission, and <see cref="Dsl.UiWhen" /> for content that genuinely is not part of the
/// flow right now.
/// </para>
/// </summary>
public sealed record UiVisibleWhen
{
	/// <summary>The sibling field whose value decides visibility.</summary>
	public required string ParameterName { get; init; }

	/// <summary>The sibling values that make this field visible.</summary>
	public required IReadOnlyList<string> Values { get; init; }

	/// <summary>
	/// Reads the sibling's current value, so validation can skip a hidden field. Not serialized: the renderer
	/// resolves the sibling from the tree, and only the author can say where the value lives on this side.
	///
	/// <para>
	/// Leave it unset when nothing needs the answer here. Unset reads as visible, which is the same answer the
	/// existing editor gives for a <see cref="ParameterName" /> it cannot resolve. Reading reactive state
	/// inside it is what keeps visibility - and therefore validation - up to date, because it is invoked from
	/// the same kind of cell a value provider is.
	/// </para>
	/// </summary>
	[JsonIgnore]
	public Func<string?>? SiblingValue { get; init; }

	/// <summary>Whether the condition currently holds. Visible when <see cref="SiblingValue" /> is unset - see
	/// its remarks.</summary>
	public bool IsSatisfied()
	{
		if (SiblingValue is null)
		{
			return true;
		}

		var current = SiblingValue() ?? string.Empty;

		foreach (var value in Values)
		{
			if (string.Equals(value, current, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
