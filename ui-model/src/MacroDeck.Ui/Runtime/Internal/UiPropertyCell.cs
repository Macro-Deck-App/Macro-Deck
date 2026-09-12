using System.Text.Json;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>What re-evaluating a cell did to its property key.</summary>
internal enum UiCellChange
{
	/// <summary>The value is the one already in the node - no operation, and in particular no revision
	/// advance.</summary>
	Unchanged,

	/// <summary>The key carries a new value and belongs in <c>Properties</c>.</summary>
	Set,

	/// <summary>The value became absent, so the key belongs in <c>RemovedProperties</c> - never in
	/// <c>Properties</c> with a JSON <c>null</c>, which the model defines as "explicitly null" instead.
	/// </summary>
	Removed,
}

/// <summary>
/// One <c>(node, property key)</c> pair's value: the unit dependency tracking and the property diff both
/// work in. A cell remembers the value it last produced so a re-evaluation can answer "did this key change"
/// without the node, the tree or the serializer being involved.
/// </summary>
internal abstract class UiPropertyCell : UiDependent
{
	protected UiPropertyCell(string key) => Key = key;

	/// <summary>The property key this cell feeds.</summary>
	internal string Key { get; }

	/// <summary>The node whose <c>Properties</c> this cell feeds. Assigned once, right after that node is
	/// materialized - a cell is evaluated before the node exists, since the node's properties are what the
	/// evaluation produces.</summary>
	internal UiMaterializedNode? Owner { get; private set; }

	/// <summary>Whether this cell is already in the view's dirty queue, so a state written twice before a
	/// flush queues it once.</summary>
	internal bool IsDirty { get; set; }

	/// <summary>Attaches this cell to the node it feeds.</summary>
	internal void Attach(UiMaterializedNode owner) => Owner = owner;

	/// <summary>Evaluates this cell for the first time, returning the property value or <c>null</c> when the
	/// value is absent and the key is therefore omitted from the node.</summary>
	internal abstract JsonElement? EvaluateInitial();

	/// <summary>Re-evaluates this cell and reports whether its key changed. <paramref name="value" /> is
	/// meaningful only for <see cref="UiCellChange.Set" />.</summary>
	internal abstract UiCellChange Reevaluate(out JsonElement value);

	/// <summary>Queues this cell for re-evaluation on the owning view's next flush. A released cell belongs to
	/// a node that left the tree and queues nothing.</summary>
	internal override void Invalidate()
	{
		if (IsReleased)
		{
			return;
		}

		View?.MarkDirty(this);
	}
}

/// <summary>
/// A property cell over a typed <see cref="UiValue{T}" />.
///
/// <para>
/// <b>Why change detection works the way it does.</b> The typed value is compared first, with
/// <see cref="EqualityComparer{T}.Default" /> - the cheapest comparison and the only one that is right for
/// the common case of a string, a number or a record. It can only produce false <i>negatives</i> (a composed
/// value whose type has reference equality, or an opaque <see cref="JsonElement" />), never false positives,
/// so a canonical-text comparison catches those before anything is emitted.
/// <see cref="JsonElement.Equals(object?)" /> is never used: it compares the backing document handle rather
/// than the value, so two identical values from two documents would always look different.
/// </para>
///
/// <para>
/// The remembered <see cref="JsonElement" /> keeps its backing <see cref="JsonDocument" /> reachable for as
/// long as the cell lives, which <see cref="Model.Nodes.UiNode.Properties" /> requires of every value put
/// into a node.
/// </para>
/// </summary>
internal sealed class UiPropertyCell<T> : UiPropertyCell
{
	private readonly UiValue<T> _value;
	private JsonElement _lastElement;
	private T? _lastValue;
	private bool _lastPresent;

	internal UiPropertyCell(string key, UiValue<T> value)
		: base(key)
		=> _value = value;

	internal UiValue<T> Value => _value;

	internal override JsonElement? EvaluateInitial()
	{
		Evaluate();

		return _lastPresent ? _lastElement : null;
	}

	internal override UiCellChange Reevaluate(out JsonElement value)
	{
		var previousPresent = _lastPresent;
		var previousValue = _lastValue;
		var previousElement = _lastElement;

		Evaluate();

		if (!_lastPresent)
		{
			value = default;

			return previousPresent ? UiCellChange.Removed : UiCellChange.Unchanged;
		}

		value = _lastElement;

		if (!previousPresent)
		{
			return UiCellChange.Set;
		}

		if (EqualityComparer<T>.Default.Equals(previousValue!, _lastValue!))
		{
			return UiCellChange.Unchanged;
		}

		return string.Equals(previousElement.GetRawText(), _lastElement.GetRawText(), StringComparison.Ordinal)
			? UiCellChange.Unchanged
			: UiCellChange.Set;
	}

	/// <summary>Evaluates the underlying value inside this cell's own tracking scope, so every state the
	/// provider reads is recorded against this cell and nothing else. Previous dependencies are dropped
	/// first: a provider that reads a different state this time round must not stay subscribed to the one it
	/// no longer reads.</summary>
	private void Evaluate()
	{
		ClearDependencies();

		using var scope = UiTracking.Push(View is null ? null : this);

		if (!_value.TryEvaluate(out var current))
		{
			_lastPresent = false;
			_lastValue = default;

			return;
		}

		_lastValue = current;
		_lastPresent = true;
		_lastElement = UiCanonicalJson.ToElement(current);
	}
}
