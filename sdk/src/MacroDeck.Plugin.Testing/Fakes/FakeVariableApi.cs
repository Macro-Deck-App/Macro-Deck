using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IVariableApi" /> - the plugin's own variables, plain CRUD with no apply/status
/// semantics. (That belongs to the user's variables instead; see <see cref="FakeUserVariableApi" />. The
/// two are easy to confuse from the interface names alone - see that type's remarks.)
///
/// <para>
/// <see cref="Created" /> is a log of every successful <see cref="CreateAsync" /> call, in call order,
/// unaffected by later updates or deletes - it answers "how many times did the plugin create a
/// variable", which is what acceptance tests for an init routine typically want, not "what does the
/// plugin have right now" (that is <see cref="GetAllAsync" />).
/// </para>
/// </summary>
public sealed class FakeVariableApi : IVariableApi
{
	private readonly Lock _gate = new();
	private readonly Dictionary<Guid, VariableHandle> _variables = new();
	private readonly List<VariableHandle> _created = [];
	private readonly Dictionary<string, List<double?>> _samples = new(StringComparer.Ordinal);
	private readonly List<HistoryWindow> _historyWindows = [];
	private readonly Dictionary<string, IVariableHistoryWindow> _wireWindows = new(StringComparer.Ordinal);
	private readonly List<IReadOnlyList<string>> _definitionRefreshes = [];
	private readonly List<VariableBindingRequest> _bindings = [];
	private int _refreshRequests;
	private int _declarationChanges;

	/// <summary>Every variable this fake has created, in call order, exactly as it was at creation time.</summary>
	public IReadOnlyList<VariableHandle> Created
	{
		get
		{
			lock (_gate)
			{
				return [.. _created];
			}
		}
	}

	/// <summary>
	/// Every variable created so far, with its current value - reflecting any <see cref="SetValueAsync" />
	/// since.
	/// </summary>
	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		lock (_gate)
		{
			return Task.FromResult<IReadOnlyList<VariableHandle>>([.. _variables.Values]);
		}
	}

	/// <summary>
	/// Looks up a variable by canonical name. Returns <c>null</c> when none was created with that name.
	/// </summary>
	public Task<VariableHandle?> GetByNameAsync(string name)
	{
		lock (_gate)
		{
			var match = _variables.Values.FirstOrDefault(handle
				=> string.Equals(handle.Name, name, StringComparison.Ordinal));

			return Task.FromResult(match);
		}
	}

	/// <summary>
	/// Creates and records a variable. Unlike the real host, this never fails on a name collision - two
	/// variables with the same name can coexist here, and <see cref="GetByNameAsync" /> returns whichever
	/// was created first.
	/// </summary>
	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		var handle = new VariableHandle(Guid.NewGuid(), name, type, initialValue, decimalPlaces)
		{
			DefinitionId = definitionId ?? VariableDefinitionId.FromName(name)
		};

		lock (_gate)
		{
			_variables[handle.Id] = handle;
			_created.Add(handle);
		}

		return Task.FromResult(handle);
	}

	/// <summary>
	/// Updates the value of a tracked variable. Mirrors the real host - which backs
	/// <see cref="IVariableApi" /> with a store an id can outlive - by throwing rather than silently
	/// dropping a write nobody will ever see.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="variableId" /> was never created, or was since deleted.
	/// </exception>
	public Task SetValueAsync(Guid variableId, object? value)
	{
		lock (_gate)
		{
			if (!_variables.TryGetValue(variableId, out var existing))
			{
				throw new InvalidOperationException($"Variable {variableId} does not exist; the value was not stored.");
			}

			_variables[variableId] = existing with { Value = value };
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(Guid variableId)
	{
		lock (_gate)
		{
			_variables.Remove(variableId);
		}

		return Task.CompletedTask;
	}

	/// <summary>How many times the integration asked for its declared variables to be read again through
	/// <see cref="RequestRefreshAsync" />.</summary>
	public int RefreshRequests => Volatile.Read(ref _refreshRequests);

	/// <summary>How many times the integration reported a changed set of declared variables through
	/// <see cref="NotifyDeclarationsChangedAsync" />.</summary>
	public int DeclarationChanges => Volatile.Read(ref _declarationChanges);

	/// <inheritdoc />
	public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Interlocked.Increment(ref _refreshRequests);
		return Task.CompletedTask;
	}

	/// <summary>The definition ids of every <see cref="RequestDefinitionRefreshAsync" /> call that named any,
	/// one entry per call, in call order. Such a call does not count towards <see cref="RefreshRequests" />.</summary>
	public IReadOnlyList<IReadOnlyList<string>> DefinitionRefreshRequests
	{
		get
		{
			lock (_gate)
			{
				return [.. _definitionRefreshes];
			}
		}
	}

	/// <inheritdoc />
	public Task RequestDefinitionRefreshAsync(
		IReadOnlyCollection<string> definitionIds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(definitionIds);
		cancellationToken.ThrowIfCancellationRequested();
		if (definitionIds.Count > 0)
		{
			lock (_gate)
			{
				_definitionRefreshes.Add([.. definitionIds]);
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>Every binding <see cref="BindAsync" /> recorded, in call order. A request for a resource that
	/// is already bound is not repeated here.</summary>
	public IReadOnlyList<VariableBindingRequest> Bindings
	{
		get
		{
			lock (_gate)
			{
				return [.. _bindings];
			}
		}
	}

	/// <summary>
	/// Records bindings the way Macro Deck does: a resource already bound answers
	/// <see cref="VariableBindingOutcome.AlreadyBound" />, and an invalid name rejects the whole call. Unlike Macro
	/// Deck, it creates no variables later.
	/// </summary>
	/// <exception cref="ArgumentException">A name or resource id is invalid; nothing was recorded.</exception>
	public Task<IReadOnlyList<VariableBindingOutcome>> BindAsync(
		IReadOnlyCollection<VariableBindingRequest> requests,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);
		cancellationToken.ThrowIfCancellationRequested();
		foreach (var request in requests)
		{
			if (request is null
				|| string.IsNullOrWhiteSpace(request.LocalResourceId)
				|| !VariableNames.IsValid(request.Name))
			{
				throw new ArgumentException($"'{request?.Name}' is not a valid binding.", nameof(requests));
			}
		}

		lock (_gate)
		{
			var bound = _bindings.Select(binding => binding.LocalResourceId).ToHashSet(StringComparer.Ordinal);
			var outcomes = new List<VariableBindingOutcome>(requests.Count);
			foreach (var request in requests)
			{
				if (bound.Add(request.LocalResourceId))
				{
					_bindings.Add(request);
					outcomes.Add(VariableBindingOutcome.Bound);
				}
				else
				{
					outcomes.Add(VariableBindingOutcome.AlreadyBound);
				}
			}

			return Task.FromResult<IReadOnlyList<VariableBindingOutcome>>(outcomes);
		}
	}

	/// <inheritdoc />
	public Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Interlocked.Increment(ref _declarationChanges);
		return Task.CompletedTask;
	}

	/// <summary>How many history windows opened with <see cref="OpenHistoryAsync" /> are still open.</summary>
	public int OpenHistoryWindows
	{
		get
		{
			lock (_gate)
			{
				return _historyWindows.Count;
			}
		}
	}

	/// <summary>
	/// Records one sample of <paramref name="name" />, as Macro Deck's once-per-second sampling would, and
	/// raises <see cref="IVariableHistoryWindow.Changed" /> on every window open on that name.
	/// </summary>
	public void RecordSample(string name, double? value)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		List<HistoryWindow> windows;
		lock (_gate)
		{
			if (!_samples.TryGetValue(name, out var samples))
			{
				samples = [];
				_samples[name] = samples;
			}

			samples.Add(value);
			windows = [.. _historyWindows.Where(window => string.Equals(window.Name, name, StringComparison.Ordinal))];
		}

		foreach (var window in windows)
		{
			window.RaiseChanged();
		}
	}

	/// <summary>
	/// Opens a window on the samples recorded with <see cref="RecordSample" /> for <paramref name="name" />,
	/// keeping the last <paramref name="capacity" /> of them. Every name counts as the integration's own here.
	/// </summary>
	public Task<IVariableHistoryWindow> OpenHistoryAsync(
		string name,
		int capacity,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		cancellationToken.ThrowIfCancellationRequested();

		var window = new HistoryWindow(this, name, capacity);
		lock (_gate)
		{
			_historyWindows.Add(window);
		}

		return Task.FromResult<IVariableHistoryWindow>(window);
	}

	internal async Task<string> OpenWireHistoryAsync(string name, int capacity)
	{
		var window = await OpenHistoryAsync(name, capacity).ConfigureAwait(false);
		var id = Guid.NewGuid().ToString("N");
		lock (_gate)
		{
			_wireWindows[id] = window;
		}

		return id;
	}

	internal IReadOnlyList<double?>? ReadWireHistory(string windowId)
	{
		lock (_gate)
		{
			return _wireWindows.TryGetValue(windowId, out var window) ? window.Values : null;
		}
	}

	internal async Task CloseWireHistoryAsync(string windowId)
	{
		IVariableHistoryWindow? window;
		lock (_gate)
		{
			_wireWindows.Remove(windowId, out window);
		}

		if (window is not null)
		{
			await window.DisposeAsync().ConfigureAwait(false);
		}
	}

	private IReadOnlyList<double?> Samples(string name, int capacity)
	{
		lock (_gate)
		{
			return _samples.TryGetValue(name, out var samples) ? [.. samples.TakeLast(capacity)] : [];
		}
	}

	private void Close(HistoryWindow window)
	{
		lock (_gate)
		{
			_historyWindows.Remove(window);
		}
	}

	private sealed class HistoryWindow(FakeVariableApi owner, string name, int capacity) : IVariableHistoryWindow
	{
		public string Name => name;

		public IReadOnlyList<double?> Values => owner.Samples(name, capacity);

		public event EventHandler? Changed;

		public void RaiseChanged()
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public ValueTask DisposeAsync()
		{
			owner.Close(this);
			return ValueTask.CompletedTask;
		}
	}
}
