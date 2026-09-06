namespace MacroDeck.Ui.Dsl;

/// <summary>A constant, computed, or absent UI value.</summary>
/// <remarks><c>default(UiValue{T})</c> is the absent value.</remarks>
public readonly record struct UiValue<T>
{
	private readonly Func<UiValue<T>>? _computeOptional;
	private readonly Func<T>? _compute;
	private readonly T? _constant;
	private readonly bool _hasValue;

	internal UiValue(T? constant, Func<T>? compute, Func<UiValue<T>>? computeOptional, bool hasValue)
	{
		_constant = constant;
		_compute = compute;
		_computeOptional = computeOptional;
		_hasValue = hasValue;
	}

	/// <summary>Wraps a constant value. Use <see cref="UiValue.Of{T}"/> when <typeparamref name="T"/> is an interface.</summary>
	public static implicit operator UiValue<T>(T constant) => new(constant, null, null, true);

	internal bool IsDeclared => _hasValue || _computeOptional is not null;

	internal bool TryEvaluate(out T value)
	{
		if (_computeOptional is not null)
		{
			return _computeOptional().TryEvaluate(out value);
		}

		if (!_hasValue)
		{
			value = default!;

			return false;
		}

		value = _compute is not null ? _compute() : _constant!;

		return true;
	}
}

/// <summary>Creates <see cref="UiValue{T}"/> instances with inferred value types.</summary>
public static class UiValue
{
	/// <summary>Wraps a constant value, including interface-typed values.</summary>
	public static UiValue<T> Of<T>(T constant) => new(constant, null, null, true);

	/// <summary>Creates a value evaluated on demand.</summary>
	public static UiValue<T> From<T>(Func<T> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiValue<T>(default, compute, null, true);
	}

	/// <summary>Creates a value whose presence is decided on each evaluation.</summary>
	public static UiValue<T> Optional<T>(Func<UiValue<T>> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiValue<T>(default, null, compute, true);
	}

	/// <summary>Returns an absent value, omitting the property from the materialized node.</summary>
	public static UiValue<T> None<T>() => default;
}
