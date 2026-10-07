using System.Collections.Concurrent;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Variables.Templates;

public sealed record TemplateEvaluation(string? Value, VariableTemplateError? Error, IReadOnlySet<string> Reads);

public sealed class TemplateVariableSynchronizer : IDisposable
{
	private readonly VariableRegistry _registry;
	private readonly IMediator _mediator;
	private readonly VariableTemplateRenderer _renderer;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly SemaphoreSlim _signal = new(0, 1);
	private readonly ConcurrentDictionary<Guid, IReadOnlySet<string>> _reads = new();
	private readonly ConcurrentDictionary<Guid, string> _names = new();
	private readonly Lock _pendingLock = new();
	private HashSet<string> _pending = new(StringComparer.Ordinal);
	private int _signalled;

	public TemplateVariableSynchronizer(VariableRegistry registry, IMediator mediator)
	{
		_registry = registry;
		_mediator = mediator;
		_renderer = new VariableTemplateRenderer(registry);
	}

	internal bool HasPendingChanges
	{
		get
		{
			lock (_pendingLock)
			{
				return _pending.Count > 0;
			}
		}
	}

	public string? FindParseError(string template) => _renderer.FindParseError(template);

	public void NoteChanged(VariableEntity entity)
	{
		var previous = _names.TryGetValue(entity.Id, out var known) ? known : null;
		_names[entity.Id] = entity.Name;
		Enqueue(entity.Name, previous);
	}

	public void NoteDeleted(VariableEntity entity)
	{
		_names.TryRemove(entity.Id, out var previous);
		_reads.TryRemove(entity.Id, out _);
		Enqueue(entity.Name, previous);
	}

	public void Forget(Guid id) => _reads.TryRemove(id, out _);

	public async Task WaitForChangesAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
			throw new OperationCanceledException(cancellationToken);
		}

		Interlocked.Exchange(ref _signalled, 0);
	}

	// Renders a variable that is not in the registry yet and records its dependencies. The caller adds it
	// to the registry, whose Created notification then wakes every template that already reads its name.
	public async Task<TemplateEvaluation> AttachAsync(VariableEntity entity)
	{
		var template = entity.TemplateSource?.Template ??
			throw new ArgumentException("The variable has no template source.", nameof(entity));

		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			var evaluation = Evaluate(entity, template);
			if (evaluation.Error?.Code != VariableTemplateError.CircularReference)
			{
				_reads[entity.Id] = evaluation.Reads;
				_names[entity.Id] = entity.Name;
				entity.Value = evaluation.Value ??
					VariableValueSerializer.Serialize(entity.Type, null, entity.DecimalPlaces);
				entity.TemplateError = evaluation.Error;
			}

			return evaluation;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<bool> ChangeAsync(Guid id, VariableTemplateSource? source, int? decimalPlaces = null)
	{
		Publication? publication;
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			var entity = _registry.GetById(id);
			if (entity?.TemplateSource is null)
			{
				return true;
			}

			var previousDecimalPlaces = entity.DecimalPlaces;
			if (decimalPlaces.HasValue && entity.Type == VariableType.Numeric)
			{
				entity.DecimalPlaces = decimalPlaces;
			}

			var evaluation = Evaluate(entity, source?.Template ?? entity.TemplateSource.Template);
			if (source is not null && evaluation.Error?.Code == VariableTemplateError.CircularReference)
			{
				entity.DecimalPlaces = previousDecimalPlaces;
				return false;
			}

			if (source is not null)
			{
				entity.TemplateSource = source;
			}

			publication = Apply(entity, evaluation);
			if (publication is null && entity.DecimalPlaces != previousDecimalPlaces)
			{
				publication = new Publication(entity, entity.Value, false);
			}
		}
		finally
		{
			_gate.Release();
		}

		await PublishAsync(publication).ConfigureAwait(false);
		return true;
	}

	// Startup: nothing is published, so restoring a value is not reported as a change on every start.
	public async Task InitializeAsync()
	{
		foreach (var variable in _registry.GetAll())
		{
			_names[variable.Id] = variable.Name;
		}

		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			RunPass(_registry.GetAll().Where(variable => variable.TemplateSource is not null).ToList());
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task DrainAsync()
	{
		HashSet<string> changed;
		lock (_pendingLock)
		{
			changed = _pending;
			_pending = new HashSet<string>(StringComparer.Ordinal);
		}

		if (changed.Count == 0 || _reads.IsEmpty)
		{
			return;
		}

		List<Publication?> publications;
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			var templates = _registry.GetAll().Where(variable => variable.TemplateSource is not null).ToList();
			publications = RunPass(Affected(templates, changed));
		}
		finally
		{
			_gate.Release();
		}

		foreach (var publication in publications)
		{
			await PublishAsync(publication).ConfigureAwait(false);
		}
	}

	public TemplateEvaluation Preview(
		string template,
		VariableType type,
		int? decimalPlaces,
		VariableScope scope,
		string? scopeRefId,
		Guid? variableId,
		string? name)
	{
		if (FindParseError(template) is { } parseError)
		{
			return new TemplateEvaluation(null,
				new VariableTemplateError(VariableTemplateError.RenderFailed, parseError),
				new HashSet<string>());
		}

		var probe = new VariableEntity
		{
			Id = variableId ?? Guid.CreateVersion7(),
			Name = (variableId is { } id ? _registry.GetById(id)?.Name : null) ?? name ?? string.Empty,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = type,
			Classification = VariableClassification.User,
			DecimalPlaces = type == VariableType.Numeric ? decimalPlaces : null,
		};

		return Evaluate(probe, template, new VariableTemplateRenderer(_registry));
	}

	public bool WouldCloseCycle(VariableEntity variable, string name, string template)
	{
		var probe = variable.CopyWithValue(variable.Value);
		probe.Name = name;
		return Evaluate(probe, template, new VariableTemplateRenderer(_registry)).Error?.Code ==
			VariableTemplateError.CircularReference;
	}

	public void Dispose()
	{
		_gate.Dispose();
		_signal.Dispose();
	}

	private void Enqueue(string name, string? previousName)
	{
		lock (_pendingLock)
		{
			_pending.Add(name);
			if (previousName is not null)
			{
				_pending.Add(previousName);
			}
		}

		if (Interlocked.Exchange(ref _signalled, 1) == 0)
		{
			try
			{
				_signal.Release();
			}
			catch (ObjectDisposedException)
			{
			}
			catch (SemaphoreFullException)
			{
			}
		}
	}

	private List<VariableEntity> Affected(List<VariableEntity> templates, HashSet<string> changed)
	{
		var affected = new Dictionary<Guid, VariableEntity>();
		var frontier = new Queue<VariableEntity>();

		foreach (var template in templates)
		{
			// A variable's own publish names it, and that must not wake it again.
			var triggered = !_reads.TryGetValue(template.Id, out var reads) ||
				reads.Any(name => changed.Contains(name) &&
					!string.Equals(name, template.Name, StringComparison.Ordinal));
			if (triggered && affected.TryAdd(template.Id, template))
			{
				frontier.Enqueue(template);
			}
		}

		while (frontier.TryDequeue(out var upstream))
		{
			foreach (var template in templates)
			{
				if (!affected.ContainsKey(template.Id) && DependsOn(template, upstream))
				{
					affected[template.Id] = template;
					frontier.Enqueue(template);
				}
			}
		}

		return affected.Values.ToList();
	}

	private List<Publication?> RunPass(List<VariableEntity> affected)
	{
		var publications = new List<Publication?>();
		if (affected.Count == 0)
		{
			return publications;
		}

		foreach (var variable in affected)
		{
			var reads = new HashSet<string>(StringComparer.Ordinal);
			try
			{
				_renderer.RenderRecording(variable.TemplateSource!.Template,
					variable.Scope,
					variable.ScopeRefId,
					reads);
			}
			catch (Exception)
			{
			}

			_reads[variable.Id] = reads;
		}

		var cyclic = CycleMembers(affected);
		foreach (var variable in affected.Where(variable => cyclic.Contains(variable.Id)))
		{
			publications.Add(Apply(variable,
				new TemplateEvaluation(null,
					new VariableTemplateError(VariableTemplateError.CircularReference),
					_reads[variable.Id])));
		}

		foreach (var variable in TopologicalOrder(affected.Where(variable => !cyclic.Contains(variable.Id)).ToList()))
		{
			publications.Add(Apply(variable, Evaluate(variable, variable.TemplateSource!.Template)));
		}

		return publications;
	}

	private TemplateEvaluation Evaluate(VariableEntity variable, string template)
		=> Evaluate(variable, template, _renderer);

	private TemplateEvaluation Evaluate(VariableEntity variable, string template, VariableTemplateRenderer renderer)
	{
		var reads = new HashSet<string>(StringComparer.Ordinal);
		string rendered;
		try
		{
			rendered = renderer.RenderRecording(template, variable.Scope, variable.ScopeRefId, reads);
		}
		catch (Exception ex)
		{
			return new TemplateEvaluation(null,
				new VariableTemplateError(VariableTemplateError.RenderFailed, ex.Message),
				reads);
		}

		if (ClosesCycle(variable, reads))
		{
			return new TemplateEvaluation(null,
				new VariableTemplateError(VariableTemplateError.CircularReference),
				reads);
		}

		var (value, error) = TemplateVariableValue.Convert(variable.Type, variable.DecimalPlaces, rendered);
		return new TemplateEvaluation(value, error, reads);
	}

	private Publication? Apply(VariableEntity variable, TemplateEvaluation evaluation)
	{
		_reads[variable.Id] = evaluation.Reads;

		var previousValue = variable.Value;
		var wasAvailable = _registry.IsAvailable(variable.Id);
		var previousError = variable.TemplateError;
		var available = evaluation.Error is null;

		if (available)
		{
			variable.Value = evaluation.Value!;
		}

		variable.TemplateError = evaluation.Error;

		var valueChanged = !string.Equals(previousValue, variable.Value, StringComparison.Ordinal) ||
			wasAvailable != available;
		var errorChanged = !Equals(previousError, evaluation.Error);
		if (!valueChanged && !errorChanged)
		{
			return null;
		}

		variable.UpdatedAt = DateTime.UtcNow;
		_registry.SetAvailable(variable.Id, available);
		return new Publication(variable, previousValue, valueChanged);
	}

	private async Task PublishAsync(Publication? publication)
	{
		if (publication is null || _registry.GetById(publication.Variable.Id) is null)
		{
			return;
		}

		await _mediator.Publish(new VariableUpdatedNotification(publication.Variable)).ConfigureAwait(false);
		if (publication.ValueChanged)
		{
			var changed = new VariableValueChangedNotification(publication.Variable, publication.PreviousValue);
			await _mediator.Publish(changed).ConfigureAwait(false);
		}
	}

	private VariableEntity? Resolve(VariableScope scope, string? scopeRefId, string name)
	{
		var local = scope != VariableScope.Global && !string.IsNullOrEmpty(scopeRefId)
			? _registry.FindByName(scope, scopeRefId, name)
			: null;
		return local ?? _registry.FindByName(VariableScope.Global, null, name);
	}

	private bool DependsOn(VariableEntity reader, VariableEntity upstream)
		=> _reads.TryGetValue(reader.Id, out var reads) &&
			reads.Any(name => Resolve(reader.Scope, reader.ScopeRefId, name)?.Id == upstream.Id);

	// The variable may be unregistered or about to be renamed, so its own name is matched by visibility
	// rather than through the registry, and its registered old name no longer counts as itself.
	private bool ClosesCycle(VariableEntity self, IReadOnlySet<string> selfReads)
	{
		var visited = new HashSet<Guid>();
		var stack = new Stack<(VariableScope Scope, string? ScopeRefId, IReadOnlySet<string> Reads)>();
		stack.Push((self.Scope, self.ScopeRefId, selfReads));

		while (stack.TryPop(out var frame))
		{
			foreach (var name in frame.Reads)
			{
				if (string.Equals(name, self.Name, StringComparison.Ordinal) && Sees(frame, self))
				{
					return true;
				}

				var target = Resolve(frame.Scope, frame.ScopeRefId, name);
				if (target is null || target.Id == self.Id)
				{
					continue;
				}

				if (target.TemplateSource is not null &&
					visited.Add(target.Id) &&
					_reads.TryGetValue(target.Id, out var targetReads))
				{
					stack.Push((target.Scope, target.ScopeRefId, targetReads));
				}
			}
		}

		return false;
	}

	private bool Sees((VariableScope Scope, string? ScopeRefId, IReadOnlySet<string> Reads) frame, VariableEntity self)
	{
		if (self.Scope != VariableScope.Global)
		{
			return frame.Scope == self.Scope && frame.ScopeRefId == self.ScopeRefId;
		}

		var local = frame.Scope != VariableScope.Global && !string.IsNullOrEmpty(frame.ScopeRefId)
			? _registry.FindByName(frame.Scope, frame.ScopeRefId, self.Name)
			: null;
		return local is null || local.Id == self.Id;
	}

	private HashSet<Guid> CycleMembers(List<VariableEntity> nodes)
	{
		var index = 0;
		var indices = new Dictionary<Guid, int>();
		var lowLinks = new Dictionary<Guid, int>();
		var onStack = new HashSet<Guid>();
		var stack = new Stack<VariableEntity>();
		var members = new HashSet<Guid>();

		void Connect(VariableEntity node)
		{
			indices[node.Id] = lowLinks[node.Id] = index++;
			stack.Push(node);
			onStack.Add(node.Id);

			foreach (var reader in nodes.Where(reader => DependsOn(reader, node)))
			{
				if (!indices.TryGetValue(reader.Id, out var readerIndex))
				{
					Connect(reader);
					lowLinks[node.Id] = Math.Min(lowLinks[node.Id], lowLinks[reader.Id]);
				}
				else if (onStack.Contains(reader.Id))
				{
					lowLinks[node.Id] = Math.Min(lowLinks[node.Id], readerIndex);
				}
			}

			if (lowLinks[node.Id] != indices[node.Id])
			{
				return;
			}

			var component = new List<VariableEntity>();
			VariableEntity popped;
			do
			{
				popped = stack.Pop();
				onStack.Remove(popped.Id);
				component.Add(popped);
			} while (popped.Id != node.Id);

			if (component.Count > 1 || DependsOn(node, node))
			{
				members.UnionWith(component.Select(member => member.Id));
			}
		}

		foreach (var node in nodes.Where(node => !indices.ContainsKey(node.Id)))
		{
			Connect(node);
		}

		return members;
	}

	private List<VariableEntity> TopologicalOrder(List<VariableEntity> nodes)
	{
		var inDegree = nodes.ToDictionary(node => node.Id,
			node => nodes.Count(upstream => upstream.Id != node.Id && DependsOn(node, upstream)));
		var ready = new Queue<VariableEntity>(nodes.Where(node => inDegree[node.Id] == 0));
		var order = new List<VariableEntity>(nodes.Count);

		while (ready.TryDequeue(out var node))
		{
			order.Add(node);
			foreach (var reader in nodes.Where(reader => reader.Id != node.Id && DependsOn(reader, node)))
			{
				if (--inDegree[reader.Id] == 0)
				{
					ready.Enqueue(reader);
				}
			}
		}

		return order;
	}

	private sealed record Publication(VariableEntity Variable, string PreviousValue, bool ValueChanged);
}
