using MacroDeck.Ui.Runtime.Internal;

namespace MacroDeck.Ui.Dsl;

/// <summary>
/// The sink an element declares its node properties into, one key at a time. Passed to
/// <see cref="UiElement.DeclareProperties" /> by the materializer.
///
/// <para>
/// A declaration is deliberately <b>not</b> a finished dictionary of values: each key becomes its own
/// <i>cell</i>, evaluated inside its own dependency-tracking scope, so the runtime knows which
/// <c>(node, property key)</c> pairs a given piece of reactive state feeds. A state write then re-evaluates
/// exactly those cells and emits one <c>set-properties</c> per affected node listing only the keys that
/// actually changed - the property whose provider was never touched is never re-invoked, and the untouched
/// part of the tree is never rebuilt or re-serialized.
/// </para>
///
/// <para>
/// An element declares the same keys in the same order on every materialization: the order is the order
/// property changes appear in a patch, and a key that appears only sometimes cannot be diffed. A value that
/// is sometimes absent is expressed with <see cref="UiValue.None{T}" />, which omits the key from the node
/// rather than dropping the cell.
/// </para>
/// </summary>
public sealed class UiPropertyDeclaration
{
	private readonly List<UiPropertyCell> _cells = [];

	/// <summary>
	/// Declares <paramref name="key" /> as backed by <paramref name="value" />. An absent value
	/// (<see cref="UiValue.None{T}" />, which <c>default(UiValue{T})</c> equals) omits the key from the
	/// materialized node instead of writing a JSON <c>null</c> - the model defines a null property value as
	/// "explicitly null", never as removed.
	/// </summary>
	/// <exception cref="UiViewException">The same key was already declared on this element.</exception>
	public void Set<T>(string key, UiValue<T> value)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);

		foreach (var cell in _cells)
		{
			if (string.Equals(cell.Key, key, StringComparison.Ordinal))
			{
				throw new UiViewException($"The property key '{key}' is declared more than once on one element.");
			}
		}

		_cells.Add(new UiPropertyCell<T>(key, value));
	}

	/// <summary>The declared cells, in declaration order. Internal: a cell is a runtime concept, not part of
	/// the authoring surface.</summary>
	internal List<UiPropertyCell> Cells => _cells;
}
