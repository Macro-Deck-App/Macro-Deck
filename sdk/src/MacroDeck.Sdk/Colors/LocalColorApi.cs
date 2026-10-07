namespace MacroDeck.Sdk.Colors;

// What a plugin gets when Macro Deck cannot resolve colours for it: a fixed colour still resolves, a
// variable reference has nothing to resolve against and reads as no colour.
internal sealed class LocalColorApi : IColorApi
{
	public static readonly LocalColorApi Instance = new();

	public Task<string?> ResolveAsync(string value, string? widgetId = null, CancellationToken cancellationToken = default)
		=> Task.FromResult(Resolve(value));

	public Task<IAsyncDisposable> WatchAsync(string value,
		Func<string?, CancellationToken, Task> onChanged,
		string? widgetId = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(onChanged);

		var resolved = Resolve(value);
		_ = Task.Run(async () =>
			{
				try
				{
					await onChanged(resolved, CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception exception) when (exception is not OutOfMemoryException)
				{
					Serilog.Log.Warning(exception, "A colour watch callback failed");
				}
			},
			CancellationToken.None);

		return Task.FromResult<IAsyncDisposable>(NoWatch.Instance);
	}

	public static string? Resolve(string? value) => ColorGrammar.IsReference(value) ? null : ColorGrammar.CanonicalLiteral(value);

	private sealed class NoWatch : IAsyncDisposable
	{
		public static readonly NoWatch Instance = new();

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
