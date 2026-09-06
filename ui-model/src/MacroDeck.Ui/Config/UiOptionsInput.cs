using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Config;

/// <summary>
/// The base for an input that offers options - a choice, a dynamic choice, an autocomplete, a multiple
/// selection, a widget target. Carries the eight option properties the existing action parameter schema and its
/// dynamic-options request already have, so the five primitives do not restate them.
///
/// <para>
/// Either author the properties directly, or hand the input a <see cref="OptionsState" /> and let it drive all
/// of them. The state is the ordinary case: the options, the loading flag, the error, the custom-value flag and
/// both hints then come from one place, and each is read through its own cell, so a load landing changes only
/// the keys it actually changed.
/// </para>
/// </summary>
public abstract record UiOptionsInput<T> : UiInput<T>
{
	/// <summary>The live option list this input renders. When set, it supplies every property below that the
	/// author did not set explicitly.</summary>
	public UiOptionsState? OptionsState { get; init; }

	/// <summary>The options offered. Authored with <see cref="UiValue.Of{T}" />: an interface-typed value has
	/// no implicit conversion.</summary>
	public UiValue<IReadOnlyList<UiOption>> Options { get; init; }

	/// <summary>The host-side source the list is resolved from.</summary>
	public UiValue<string> OptionsSourceId { get; init; }

	/// <summary>Whether the list is resolved on demand rather than shipped with the node.</summary>
	public UiValue<bool> DynamicOptions { get; init; }

	/// <summary>Whether a value outside the list is accepted.</summary>
	public UiValue<bool> AllowsCustomValue { get; init; }

	/// <summary>Whether a load is running. Present only while one is - a renderer reads the key's presence, so
	/// finishing a load removes it rather than setting it to false.</summary>
	public UiValue<bool> Loading { get; init; }

	/// <summary>Why the list could not be resolved.</summary>
	public UiValue<string> Error { get; init; }

	/// <summary>How long the list stays usable, as a hint to the client's cache. Emitted, never honoured
	/// here.</summary>
	public UiValue<int> CacheSeconds { get; init; }

	/// <summary>How long the client should wait before refetching on a filter keystroke, as a hint. Emitted,
	/// never honoured here.</summary>
	public UiValue<int> FilterDebounceMilliseconds { get; init; }

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		var state = OptionsState;

		properties.Set(UiConfigProperties.Options,
			Options.IsDeclared || state is null
				? Options
				: UiValue.From(() => state.Options));

		properties.Set(UiConfigProperties.OptionsSourceId,
			OptionsSourceId.IsDeclared || state is null
				? OptionsSourceId
				: Hint(() => state.SourceId));

		properties.Set(UiConfigProperties.DynamicOptions,
			DynamicOptions.IsDeclared || state is null
				? DynamicOptions
				: UiValue.Of(true));

		properties.Set(UiConfigProperties.AllowsCustomValue,
			AllowsCustomValue.IsDeclared || state is null
				? AllowsCustomValue
				: UiValue.From(() => state.AllowsCustomValue));

		properties.Set(UiConfigProperties.Loading,
			Loading.IsDeclared || state is null
				? Loading
				: UiValue.Optional(() => state.IsLoading ? UiValue.Of(true) : UiValue.None<bool>()));

		properties.Set(UiConfigProperties.Error,
			Error.IsDeclared || state is null
				? Error
				: UiValue.Optional(() => state.Error is { } error ? UiValue.Of(error) : UiValue.None<string>()));

		properties.Set(UiConfigProperties.CacheSeconds,
			CacheSeconds.IsDeclared || state is null
				? CacheSeconds
				: Hint(() => state.CacheSeconds));

		properties.Set(UiConfigProperties.FilterDebounceMs,
			FilterDebounceMilliseconds.IsDeclared || state is null
				? FilterDebounceMilliseconds
				: Hint(() => state.FilterDebounceMilliseconds));
	}

	/// <summary>A value present only while the underlying hint is set, so an unset hint omits its key instead of
	/// writing a JSON null the renderer would have to special-case.</summary>
	private static UiValue<int> Hint(Func<int?> read)
		=> UiValue.Optional(() => read() is { } value ? UiValue.Of(value) : UiValue.None<int>());

	/// <summary>The string-valued counterpart of <see cref="Hint(Func{int?})" />.</summary>
	private static UiValue<string> Hint(Func<string?> read)
		=> UiValue.Optional(() => read() is { } value ? UiValue.Of(value) : UiValue.None<string>());
}
