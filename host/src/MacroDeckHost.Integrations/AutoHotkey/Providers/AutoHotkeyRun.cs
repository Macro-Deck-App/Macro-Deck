using System.Text;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Protocol;

namespace MacroDeckHost.Integrations.AutoHotkey.Providers;

internal sealed class AutoHotkeyRun
{
	private const int MaxResultLength = OutputRing.DefaultCapacity;
	private const int MaxErrorLength = 4 * 1024;

	private readonly Lock _gate = new();
	private readonly TaskCompletionSource<bool> _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly StringBuilder _errorText = new();

	private AutoHotkeyRunState _state = AutoHotkeyRunState.Starting;
	private int? _exitCode;
	private DateTimeOffset? _endedAt;
	private string? _result;
	private AutoHotkeyScriptError? _error;
	private int _notifyPending;
	private int _startedEvent;

	public AutoHotkeyRun(
		Guid id,
		string identity,
		AutoHotkeyTargetKind kind,
		Guid? scriptId,
		string displayName,
		string mainScriptPath,
		bool includesHelper,
		UserVariableContext variableContext,
		DateTimeOffset startedAt)
	{
		Id = id;
		Identity = identity;
		Kind = kind;
		ScriptId = scriptId;
		DisplayName = displayName;
		MainScriptPath = mainScriptPath;
		IncludesHelper = includesHelper;
		VariableContext = variableContext;
		StartedAt = startedAt;
	}

	public Guid Id { get; }

	public string Identity { get; }

	public AutoHotkeyTargetKind Kind { get; }

	public Guid? ScriptId { get; }

	public string DisplayName { get; }

	public string MainScriptPath { get; }

	public bool IncludesHelper { get; }

	public UserVariableContext VariableContext { get; }

	public DateTimeOffset StartedAt { get; }

	public OutputRing Output { get; } = new();

	public IAutoHotkeyProcess? Process { get; set; }

	public AutoHotkeyRunFolder? Folder { get; set; }

	public HelperChannel? Channel { get; set; }

	public bool StopRequested { get; set; }

	public bool TimedOut { get; set; }

	public bool TryClaimStartedEvent()
	{
		return Interlocked.Exchange(ref _startedEvent, 1) == 0;
	}

	public Task<bool> Loaded => _loaded.Task;

	public Task Completed => _completed.Task;

	public AutoHotkeyRunState State
	{
		get
		{
			lock (_gate)
			{
				return _state;
			}
		}
	}

	public bool IsActive => State is AutoHotkeyRunState.Starting or AutoHotkeyRunState.Running;

	public int? ExitCode
	{
		get
		{
			lock (_gate)
			{
				return _exitCode;
			}
		}
	}

	public string? Result
	{
		get
		{
			lock (_gate)
			{
				return _result;
			}
		}
	}

	public AutoHotkeyScriptError? Error
	{
		get
		{
			lock (_gate)
			{
				return _error;
			}
		}
	}

	public string ErrorText
	{
		get
		{
			lock (_gate)
			{
				return _errorText.ToString();
			}
		}
	}

	public void MarkLoaded(bool loaded)
	{
		lock (_gate)
		{
			if (loaded && _state == AutoHotkeyRunState.Starting)
			{
				_state = AutoHotkeyRunState.Running;
			}
		}

		_loaded.TrySetResult(loaded);
	}

	public void SetResult(string text)
	{
		lock (_gate)
		{
			_result = text.Length > MaxResultLength ? text[^MaxResultLength..] : text;
		}
	}

	public void SetError(AutoHotkeyScriptError error)
	{
		lock (_gate)
		{
			_error = error;
		}
	}

	public void AppendErrorText(string text)
	{
		lock (_gate)
		{
			if (_errorText.Length < MaxErrorLength)
			{
				_errorText.Append(text, 0, Math.Min(text.Length, MaxErrorLength - _errorText.Length)).Append('\n');
			}
		}
	}

	public void MarkEnded(AutoHotkeyRunState state, int? exitCode, DateTimeOffset endedAt)
	{
		lock (_gate)
		{
			_state = state;
			_exitCode = exitCode;
			_endedAt = endedAt;
		}
	}

	public void MarkCompleted()
	{
		_loaded.TrySetResult(false);
		_completed.TrySetResult();
	}

	public bool TryBeginOutputNotification()
	{
		return Interlocked.CompareExchange(ref _notifyPending, 1, 0) == 0;
	}

	public void EndOutputNotification()
	{
		Volatile.Write(ref _notifyPending, 0);
	}

	public AutoHotkeyRunSnapshot Snapshot()
	{
		lock (_gate)
		{
			return new AutoHotkeyRunSnapshot(Id,
				ScriptId,
				DisplayName,
				_state,
				StartedAt,
				_endedAt,
				_exitCode,
				_result,
				Output.Newest(),
				Output.Version,
				_error);
		}
	}
}
