using System.Collections.Concurrent;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Entities;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Variables.Files;

public sealed record WriteTicket(Guid VariableId, long Sequence, long Epoch, string Path);

public sealed record FileVariableInitialState(string Value, bool Available);

public sealed class FileVariableSynchronizer : IDisposable
{
	private readonly VariableRegistry _registry;
	private readonly IMediator _mediator;
	private readonly IVariableFileSystem _files;
	private readonly ILogger _logger;
	private readonly TimeSpan _quietPeriod;
	private readonly TimeSpan _maxDelay;
	private readonly ConcurrentDictionary<Guid, State> _states = new();
	private readonly ConcurrentDictionary<Task, byte> _work = new();

	public FileVariableSynchronizer(
		VariableRegistry registry,
		IMediator mediator,
		IVariableFileSystem files,
		ILogger logger)
		: this(registry, mediator, files, logger, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500))
	{
	}

	internal FileVariableSynchronizer(
		VariableRegistry registry,
		IMediator mediator,
		IVariableFileSystem files,
		ILogger logger,
		TimeSpan quietPeriod,
		TimeSpan maxDelay)
	{
		_registry = registry;
		_mediator = mediator;
		_files = files;
		_logger = logger;
		_quietPeriod = quietPeriod;
		_maxDelay = maxDelay;
	}

	// Registers the variable before it is in the registry. Change events that arrive before Activate
	// only mark a re-read, so nothing is applied to a variable the registry does not know yet.
	public async Task<FileVariableInitialState> AttachAsync(VariableEntity entity)
	{
		var source = entity.FileSource ?? throw new ArgumentException("The variable has no file source.", nameof(entity));

		Detach(entity.Id);
		var state = new State(entity.Id, source) { Reading = true };
		_states[entity.Id] = state;
		state.Watch = WatchSafely(state);

		var outcome = await ReadSafelyAsync(source.Path);
		if (outcome.Content is { } content &&
			VariableFileText.TryToValue(entity.Type, entity.DecimalPlaces, content, out var value))
		{
			lock (state.Gate)
			{
				state.FileValue = value;
			}

			return new FileVariableInitialState(value, true);
		}

		return new FileVariableInitialState(VariableValueSerializer.Serialize(entity.Type, null, entity.DecimalPlaces),
			false);
	}

	public void Activate(Guid id)
	{
		if (!_states.TryGetValue(id, out var state))
		{
			return;
		}

		bool readAgain;
		lock (state.Gate)
		{
			state.Reading = false;
			state.Active = true;
			readAgain = state.ReadAgain && !state.Detached;
			state.ReadAgain = false;
		}

		if (readAgain)
		{
			Track(() => ReadAndApplyAsync(state));
		}
	}

	public void Detach(Guid id)
	{
		if (!_states.TryRemove(id, out var state))
		{
			return;
		}

		IDisposable? watch;
		lock (state.Gate)
		{
			state.Detached = true;
			state.Epoch++;
			state.Queued = null;
			watch = state.Watch;
			state.Watch = null;
		}

		watch?.Dispose();
	}

	// The epoch bump drops reads of the old file that are still in flight and writes queued for it.
	public void ChangeSource(Guid id, VariableFileSource source)
	{
		if (!_states.TryGetValue(id, out var state))
		{
			return;
		}

		IDisposable? previousWatch = null;
		bool pathChanged;
		lock (state.Gate)
		{
			state.Epoch++;
			state.Completed = state.Requested;
			state.Queued = null;
			state.LastWrittenRaw = null;
			state.FileValue = null;
			state.AllowWriteBack = source.AllowWriteBack;
			pathChanged = !string.Equals(state.Path, source.Path, StringComparison.Ordinal);
			state.Path = source.Path;
			if (pathChanged)
			{
				previousWatch = state.Watch;
				state.Watch = null;
			}
		}

		if (pathChanged)
		{
			previousWatch?.Dispose();
			var watch = WatchSafely(state);
			lock (state.Gate)
			{
				if (state.Detached)
				{
					watch?.Dispose();
				}
				else
				{
					state.Watch = watch;
				}
			}
		}
	}

	public Task RefreshAsync(Guid id)
		=> _states.TryGetValue(id, out var state) ? ReadAndApplyAsync(state) : Task.CompletedTask;

	public void ScheduleRefresh(Guid id)
	{
		if (_states.TryGetValue(id, out var state))
		{
			Track(() => ReadAndApplyAsync(state));
		}
	}

	// Taken before the value is changed, under the same lock a file read holds while it applies. A read
	// that saw the file before this call is therefore either dropped or applied before the user's value.
	public WriteTicket? RequestWrite(Guid id)
	{
		if (!_states.TryGetValue(id, out var state))
		{
			return null;
		}

		lock (state.Gate)
		{
			if (state.Detached || !state.AllowWriteBack)
			{
				return null;
			}

			state.Requested++;
			return new WriteTicket(id, state.Requested, state.Epoch, state.Path);
		}
	}

	public void EnqueueWrite(WriteTicket ticket, string value)
	{
		if (!_states.TryGetValue(ticket.VariableId, out var state))
		{
			return;
		}

		bool start;
		lock (state.Gate)
		{
			if (state.Detached)
			{
				return;
			}

			state.Queued = (ticket, value);
			start = !state.Writing;
			state.Writing = true;
		}

		if (start)
		{
			Track(() => WriteQueuedAsync(state));
		}
	}

	public async Task DrainAsync(TimeSpan timeout)
	{
		var pending = Task.WhenAll(_work.Keys);
		await Task.WhenAny(pending, Task.Delay(timeout));
	}

	internal async Task WhenIdleAsync()
	{
		while (!_work.IsEmpty)
		{
			await Task.WhenAll(_work.Keys);
		}
	}

	public void Dispose()
	{
		foreach (var id in _states.Keys)
		{
			Detach(id);
		}
	}

	private void OnFileChanged(State state)
	{
		lock (state.Gate)
		{
			if (state.Detached)
			{
				return;
			}

			state.FileValue = null;
			if (!state.Active || state.Reading)
			{
				state.ReadAgain = true;
				return;
			}

			var now = DateTime.UtcNow;
			state.FirstEventAt ??= now;
			state.LastEventAt = now;
			if (state.Debouncing)
			{
				return;
			}

			state.Debouncing = true;
		}

		Track(() => DebounceThenReadAsync(state));
	}

	private async Task DebounceThenReadAsync(State state)
	{
		while (true)
		{
			await Task.Delay(_quietPeriod);
			lock (state.Gate)
			{
				if (state.Detached)
				{
					state.Debouncing = false;
					return;
				}

				var now = DateTime.UtcNow;
				if (now - state.LastEventAt >= _quietPeriod || now - state.FirstEventAt >= _maxDelay)
				{
					state.Debouncing = false;
					state.FirstEventAt = null;
					break;
				}
			}
		}

		await ReadAndApplyAsync(state);
	}

	private async Task ReadAndApplyAsync(State state)
	{
		long requested;
		long epoch;
		string path;
		lock (state.Gate)
		{
			if (state.Detached)
			{
				return;
			}

			if (state.Reading)
			{
				state.ReadAgain = true;
				return;
			}

			if (state.Completed < state.Requested)
			{
				return;
			}

			state.Reading = true;
			requested = state.Requested;
			epoch = state.Epoch;
			path = state.Path;
		}

		try
		{
			var outcome = await ReadSafelyAsync(path);
			await ApplyAsync(state, requested, epoch, outcome);
		}
		finally
		{
			bool again;
			lock (state.Gate)
			{
				state.Reading = false;
				again = state.ReadAgain && state.Active && !state.Detached;
				state.ReadAgain = false;
			}

			if (again)
			{
				Track(() => ReadAndApplyAsync(state));
			}
		}
	}

	private async Task ApplyAsync(State state, long requested, long epoch, FileReadOutcome outcome)
	{
		var entity = _registry.GetById(state.Id);
		if (entity is null)
		{
			return;
		}

		string? value = null;
		var available = outcome.Content is { } content &&
			VariableFileText.TryToValue(entity.Type, entity.DecimalPlaces, content, out value);

		var previousValue = entity.Value;
		bool wasAvailable;
		lock (state.Gate)
		{
			if (state.Detached ||
				state.Requested != requested ||
				state.Epoch != epoch ||
				state.Completed < state.Requested)
			{
				return;
			}

			wasAvailable = _registry.IsAvailable(state.Id);

			// The file still holds what we wrote: the variable already has that value, and re-parsing it
			// would drop a trailing line break the user set and report a change that did not happen.
			var ownWrite = outcome.Content is not null &&
				string.Equals(outcome.Content, state.LastWrittenRaw, StringComparison.Ordinal);
			if (!ownWrite)
			{
				state.LastWrittenRaw = null;
				state.FileValue = available ? value : null;
				if (available)
				{
					entity.Value = value!;
					entity.UpdatedAt = DateTime.UtcNow;
				}
			}

			_registry.SetAvailable(state.Id, available);
		}

		await PublishAsync(entity, previousValue, wasAvailable, available);
	}

	private async Task WriteQueuedAsync(State state)
	{
		var succeeded = false;
		while (true)
		{
			WriteTicket ticket;
			string value;
			lock (state.Gate)
			{
				if (state.Queued is not { } queued || state.Detached)
				{
					state.Writing = false;
					break;
				}

				state.Queued = null;
				(ticket, value) = queued;

				// Also skips a value the file already stands for, so setting what the file says does not
				// rewrite it in normalized form.
				if (ticket.Epoch != state.Epoch || string.Equals(value, state.FileValue, StringComparison.Ordinal))
				{
					state.Completed = Math.Max(state.Completed, ticket.Sequence);
					continue;
				}
			}

			try
			{
				await _files.WriteAsync(ticket.Path, value);
				lock (state.Gate)
				{
					// A source change during the write makes the written content foreign to the new
					// source, so the read that follows applies it instead of treating it as our echo.
					if (ticket.Epoch == state.Epoch)
					{
						state.LastWrittenRaw = value;
						state.FileValue = value;
					}

					state.Completed = Math.Max(state.Completed, ticket.Sequence);
				}

				succeeded = true;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Could not write variable {VariableId} back to {Path}", state.Id, ticket.Path);
				lock (state.Gate)
				{
					state.Completed = Math.Max(state.Completed, ticket.Sequence);
				}

				succeeded = false;
				await MarkWriteFailedAsync(state, ticket.Sequence);
			}
		}

		if (succeeded)
		{
			await ReadAndApplyAsync(state);
		}
	}

	private async Task MarkWriteFailedAsync(State state, long sequence)
	{
		var entity = _registry.GetById(state.Id);
		if (entity is null)
		{
			return;
		}

		bool wasAvailable;
		lock (state.Gate)
		{
			if (state.Detached || state.Requested != sequence)
			{
				return;
			}

			wasAvailable = _registry.IsAvailable(state.Id);
			_registry.SetAvailable(state.Id, false);
		}

		await PublishAsync(entity, entity.Value, wasAvailable, false);
	}

	private async Task PublishAsync(VariableEntity entity, string previousValue, bool wasAvailable, bool available)
	{
		if (string.Equals(entity.Value, previousValue, StringComparison.Ordinal) && wasAvailable == available)
		{
			return;
		}

		await _mediator.Publish(new VariableUpdatedNotification(entity));
		await _mediator.Publish(new VariableValueChangedNotification(entity, previousValue));
	}

	private async Task<FileReadOutcome> ReadSafelyAsync(string path)
	{
		try
		{
			return await _files.ReadAsync(path);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Debug(ex, "Could not read variable file {Path}", path);
			return FileReadOutcome.Unavailable;
		}
	}

	private IDisposable? WatchSafely(State state)
	{
		try
		{
			return _files.Watch(state.Path, () => OnFileChanged(state));
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not watch variable file {Path}", state.Path);
			return null;
		}
	}

	private void Track(Func<Task> work)
	{
		var task = Task.Run(async () =>
		{
			try
			{
				await work();
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "File variable synchronization failed");
			}
		});

		_work.TryAdd(task, 0);
		_ = task.ContinueWith(completed => _work.TryRemove(completed, out _), TaskScheduler.Default);
	}

	private sealed class State
	{
		public State(Guid id, VariableFileSource source)
		{
			Id = id;
			Path = source.Path;
			AllowWriteBack = source.AllowWriteBack;
		}

		public Guid Id { get; }
		public object Gate { get; } = new();
		public string Path { get; set; }
		public bool AllowWriteBack { get; set; }
		public long Requested { get; set; }
		public long Completed { get; set; }
		public long Epoch { get; set; }
		public string? LastWrittenRaw { get; set; }
		public string? FileValue { get; set; }
		public bool Active { get; set; }
		public bool Detached { get; set; }
		public bool Reading { get; set; }
		public bool ReadAgain { get; set; }
		public bool Debouncing { get; set; }
		public DateTime? FirstEventAt { get; set; }
		public DateTime LastEventAt { get; set; }
		public bool Writing { get; set; }
		public (WriteTicket Ticket, string Value)? Queued { get; set; }
		public IDisposable? Watch { get; set; }
	}
}
