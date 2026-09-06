namespace MacroDeck.Ui.Config.Options;

/// <summary>
/// Where an option list comes from. A record rather than a static class of helpers, because the helpers return
/// one of these and a static class cannot be its own return type.
///
/// <para>
/// The load delegate is asynchronous and takes a cancellation token, which is the whole point: resolving a
/// list is a network call, and the token is how a reload cancels the request the user already moved past.
/// Everything else here is a hint the client acts on - the runtime starts and cancels loads, and does not
/// cache, expire or debounce anything.
/// </para>
/// </summary>
public sealed record UiOptionSource
{
	/// <summary>Resolves the options for a query. Faulting is allowed and becomes an error on the state that
	/// drives it, never an exception escaping into a render or a dispatch.</summary>
	public required Func<UiOptionQuery, CancellationToken, Task<UiOptionResult>> Load { get; init; }

	/// <summary>The host-side source this list is resolved from, emitted as <c>optionsSourceId</c> so a
	/// renderer can attribute the list. Absent for a source the plugin resolves itself.</summary>
	public string? SourceId { get; init; }

	/// <summary>How long the client should wait before refetching on a filter keystroke, emitted as
	/// <c>filterDebounceMs</c>. A hint only - see this type's remarks.</summary>
	public int? FilterDebounceMilliseconds { get; init; }

	/// <summary>A source backed by <paramref name="load" />.</summary>
	public static UiOptionSource From(Func<UiOptionQuery, CancellationToken, Task<UiOptionResult>> load)
	{
		ArgumentNullException.ThrowIfNull(load);

		return new UiOptionSource { Load = load };
	}

	/// <summary>A source backed by <paramref name="load" />, which returns options and nothing else.</summary>
	public static UiOptionSource From(Func<UiOptionQuery, CancellationToken, Task<IReadOnlyList<UiOption>>> load)
	{
		ArgumentNullException.ThrowIfNull(load);

		return new UiOptionSource
		{
			Load = async (query, cancellationToken) =>
				UiOptionResult.From(await load(query, cancellationToken).ConfigureAwait(false)),
		};
	}
}
