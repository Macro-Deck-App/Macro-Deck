using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Config.Options;

/// <summary>
/// The live option list behind a selection control: the current query, the options the last load produced,
/// whether one is running, and the error the last one reported. Composes
/// <see cref="UiAsyncState{T}" /> rather than extending it, because the query is a configuration concept and
/// the asynchronous state deliberately knows nothing about it.
///
/// <para>
/// Every read here is a tracked read, so a node reading <see cref="Options" /> re-renders when a load lands and
/// a node reading <see cref="IsLoading" /> is enough to gate a busy indicator.
/// </para>
///
/// <para>
/// <b>Nothing here has a clock.</b> Changing the filter records it and does not fetch; the caller decides when
/// to <see cref="Reload" />, and <see cref="UiOptionSource.FilterDebounceMilliseconds" /> is emitted for the
/// client to honour. Same for <see cref="CacheSeconds" />: reported, never enforced. A debounce or a cache
/// window here would be a timer the runtime owns and a test could only observe by waiting.
/// </para>
/// </summary>
public sealed class UiOptionsState
{
	private readonly UiAsyncState<UiOptionResult> _state;
	private readonly UiOptionSource _source;

	// A leaf lock over the query alone: it is read on whichever thread the load starts on and written on
	// whichever thread dispatched the filter change, and every write is a read-modify-write of the record.
	private readonly Lock _sync = new();
	private UiOptionQuery _query = new();

	/// <summary>Creates a state backed by <paramref name="source" />. No load is started - see
	/// <see cref="Reload" />.</summary>
	public UiOptionsState(UiOptionSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
		_state = new UiAsyncState<UiOptionResult>(cancellationToken => source.Load(Query, cancellationToken),
			new UiOptionResult());
	}

	/// <summary>What the next load will ask for: whatever was set last, from whichever thread set it.
	/// </summary>
	public UiOptionQuery Query
	{
		get
		{
			lock (_sync)
			{
				return _query;
			}
		}
	}

	/// <summary>The host-side source id, emitted as <c>optionsSourceId</c>.</summary>
	public string? SourceId => _source.SourceId;

	/// <summary>The filter debounce hint, emitted as <c>filterDebounceMs</c>.</summary>
	public int? FilterDebounceMilliseconds => _source.FilterDebounceMilliseconds;

	/// <summary>The options the last successful load produced, in source order. A tracked read.</summary>
	public IReadOnlyList<UiOption> Options => _state.Value.Options;

	/// <summary>Whether a value outside <see cref="Options" /> is accepted, as the last load reported. A
	/// tracked read.</summary>
	public bool AllowsCustomValue => _state.Value.AllowsCustomValue;

	/// <summary>The cache hint the last load reported. A tracked read.</summary>
	public int? CacheSeconds => _state.Value.CacheSeconds;

	/// <summary>Whether a load is running. A tracked read.</summary>
	public bool IsLoading => _state.IsLoading;

	/// <summary>
	/// Why the last load did not produce options: the error it reported, or the message of the exception it
	/// threw. A tracked read, and <c>null</c> once a later load succeeds.
	/// </summary>
	public string? Error
	{
		get
		{
			// Both are read on every evaluation, so a cell reading this depends on both and re-evaluates
			// whichever of the two the next load changes.
			var fault = _state.Error;
			var result = _state.Value;

			return fault?.Message ?? result.Error;
		}
	}

	/// <summary>Records the filter the next load asks for. Does not fetch - see this type's remarks.</summary>
	public void SetFilter(string? filter)
	{
		lock (_sync)
		{
			_query = _query with { Filter = filter };
		}
	}

	/// <summary>Records the sibling values the next load asks for. Does not fetch.</summary>
	public void SetValues(IReadOnlyDictionary<string, string> values)
	{
		ArgumentNullException.ThrowIfNull(values);

		lock (_sync)
		{
			_query = _query with { Values = values };
		}
	}

	/// <summary>Starts a load for the current <see cref="Query" />, cancelling the one in flight so a stale
	/// response cannot overwrite a newer one.</summary>
	public void Reload() => _state.Reload();
}
