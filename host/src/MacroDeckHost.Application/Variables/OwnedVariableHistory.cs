using System.Collections.Concurrent;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Domain.Enums;
using SdkHistoryWindow = MacroDeck.Sdk.Variables.IVariableHistoryWindow;

namespace MacroDeckHost.Application.Variables;

// Ownership is checked on open and on every read, so a window opened before its variable existed never
// shows a variable of that name that someone else created later.
public sealed class OwnedVariableHistory
{
	public const int MaxWindowsPerOwner = 64;

	public const int MaxCapacity = 600;

	private readonly IVariableHistory _history;
	private readonly VariableRegistry _variables;
	private readonly ConcurrentDictionary<string, int> _openPerOwner = new(StringComparer.Ordinal);

	public OwnedVariableHistory(IVariableHistory history, VariableRegistry variables)
	{
		_history = history;
		_variables = variables;
	}

	public SdkHistoryWindow Open(string ownerId, string name, int capacity)
	{
		ArgumentException.ThrowIfNullOrEmpty(ownerId);

		if (string.IsNullOrEmpty(name) || !VariableNameSanitizer.IsValid(name) || !IsOwnedOrAbsent(ownerId, name))
		{
			throw new VariableHistoryException(VariableHistoryErrorCode.NotOwned,
				$"'{name}' is not one of the caller's own variables.");
		}

		if (_openPerOwner.AddOrUpdate(ownerId, 1, (_, count) => count + 1) > MaxWindowsPerOwner)
		{
			Release(ownerId);
			throw new VariableHistoryException(VariableHistoryErrorCode.LimitExceeded,
				"The caller holds as many variable history windows as Macro Deck allows.");
		}

		var window = _history.Open(name, Math.Clamp(capacity, 1, MaxCapacity));
		return new Window(this, ownerId, name, window);
	}

	private bool IsOwnedOrAbsent(string ownerId, string name)
	{
		return _variables.FindByName(VariableScope.Global, null, name) is not { } variable
			|| variable.Classification == VariableClassification.Integration
			&& string.Equals(variable.OwnerIntegrationId, ownerId, StringComparison.Ordinal);
	}

	private void Release(string ownerId)
	{
		_openPerOwner.AddOrUpdate(ownerId, 0, (_, count) => Math.Max(0, count - 1));
	}

	private sealed class Window : SdkHistoryWindow
	{
		private readonly OwnedVariableHistory _owner;
		private readonly string _ownerId;
		private readonly string _name;
		private readonly IVariableHistoryWindow _inner;
		private int _disposed;

		public Window(OwnedVariableHistory owner, string ownerId, string name, IVariableHistoryWindow inner)
		{
			_owner = owner;
			_ownerId = ownerId;
			_name = name;
			_inner = inner;
			_inner.Changed += OnChanged;
		}

		public event EventHandler? Changed;

		public IReadOnlyList<double?> Values
			=> _owner.IsOwnedOrAbsent(_ownerId, _name) ? [.. _inner.Values.Select(value => (double?)value)] : [];

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				_inner.Changed -= OnChanged;
				_inner.Dispose();
				_owner.Release(_ownerId);
			}

			return ValueTask.CompletedTask;
		}

		private void OnChanged(object? sender, EventArgs e)
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}
