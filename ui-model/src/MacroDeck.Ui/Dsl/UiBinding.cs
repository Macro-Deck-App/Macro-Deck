using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Dsl;

/// <summary>A readable or writable binding used by UI inputs.</summary>
public readonly record struct UiBinding<T>
{
	private readonly UiValue<T> _value;
	private readonly Action<T>? _set;

	private UiBinding(UiValue<T> value, Action<T>? set)
	{
		_value = value;
		_set = set;
	}

	internal static UiBinding<T> Create(UiValue<T> value, Action<T>? set) => new(value, set);

	internal UiValue<T> Value => _value;

	internal bool CanWrite => _set is not null;

	internal void Write(T value)
	{
		if (_set is null)
		{
			throw new InvalidOperationException("A read-only binding rejects a write.");
		}

		_set(value);
	}
}

/// <summary>Creates UI bindings.</summary>
public static class Bind
{
	/// <summary>Creates a two-way binding to state.</summary>
	public static UiBinding<T> To<T>(UiState<T> state)
	{
		ArgumentNullException.ThrowIfNull(state);

		return UiBinding<T>.Create(UiValue.From(() => state.Value), value => state.Value = value);
	}

	/// <summary>Creates a binding that rejects writes.</summary>
	public static UiBinding<T> ReadOnly<T>(UiValue<T> value) => UiBinding<T>.Create(value, null);

	/// <summary>Creates a binding backed by custom read and write callbacks.</summary>
	public static UiBinding<T> Custom<T>(Func<T> get, Action<T> set)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);

		return UiBinding<T>.Create(UiValue.From(get), set);
	}
}
