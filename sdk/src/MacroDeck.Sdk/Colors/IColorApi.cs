namespace MacroDeck.Sdk.Colors;

/// <summary>
/// Resolves colour values a plugin reads from its own stored settings: a fixed colour, or a Color variable
/// reference such as <c>{{ vars.primary | color | color_darken: 20 }}</c>. Only Color variables resolve;
/// a reference to a variable of any other type, to a missing or unavailable variable or to an unknown
/// widget resolves to <c>null</c>, which means "no colour, use your own default".
/// <para>
/// A resolved colour is lowercase <c>#rrggbb</c>, or <c>#rrggbbaa</c> when it is not fully opaque.
/// Watches made through <see cref="IIntegrationContext.Colors" /> belong to the integration's current
/// initialization and are released when Macro Deck shuts it down or initializes it again. Callbacks run on
/// thread-pool threads; one that throws is logged and does not affect other watches.
/// </para>
/// </summary>
public interface IColorApi
{
	/// <summary>Resolves <paramref name="value" /> once.</summary>
	/// <param name="widgetId">The widget whose own variables shadow global ones of the same name, or
	/// <c>null</c> for global variables only.</param>
	/// <returns>The colour, or <c>null</c> when <paramref name="value" /> does not resolve to one.</returns>
	Task<string?> ResolveAsync(string value, string? widgetId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Watches what <paramref name="value" /> resolves to. <paramref name="onChanged" /> receives the current
	/// colour exactly once first, then again every time it changes, including to <c>null</c>. Dispose the
	/// result to stop watching.
	/// </summary>
	/// <exception cref="InvalidOperationException">The plugin already watches the most colours it may.</exception>
	Task<IAsyncDisposable> WatchAsync(string value,
		Func<string?, CancellationToken, Task> onChanged,
		string? widgetId = null,
		CancellationToken cancellationToken = default);
}
