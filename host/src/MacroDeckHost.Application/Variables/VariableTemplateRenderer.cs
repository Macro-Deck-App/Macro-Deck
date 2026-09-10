using System.Collections.Concurrent;
using System.Globalization;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace MacroDeckHost.Application.Variables;

// The "vars" namespace. A name the registry does not know resolves to VariableTemplateValue.Unknown
// rather than to nothing, so "vars.typo.state.is_not_available" answers the same as the isNotAvailable
// condition operator does. The stub renders as nothing and is falsy, so every template that only ever
// reads the value is unaffected.
internal sealed class VariablesScriptObject : ScriptObject
{
	public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
	{
		if (base.TryGetValue(context, span, member, out value))
		{
			return true;
		}

		value = VariableTemplateValue.Unknown;
		return true;
	}
}

public sealed class VariableContext
{
	private static readonly IReadOnlyDictionary<string, object?> _noEventParameters
		= new Dictionary<string, object?>(StringComparer.Ordinal);

	private static readonly IReadOnlyDictionary<string, object?> _noInputs
		= new Dictionary<string, object?>(StringComparer.Ordinal);

	private readonly IReadOnlyDictionary<string, VariableEntity> _byName;
	private readonly IReadOnlyDictionary<string, object?> _eventParameters;
	private readonly IReadOnlyDictionary<string, object?> _inputs;

	internal VariableContext(
		IReadOnlyDictionary<string, VariableEntity> byName,
		ScriptObject scriptObject,
		IReadOnlyDictionary<string, object?>? eventParameters = null,
		IReadOnlyDictionary<string, object?>? inputs = null)
	{
		_byName = byName;
		_eventParameters = eventParameters ?? _noEventParameters;
		_inputs = inputs ?? _noInputs;
		ScribanObject = scriptObject;
	}

	internal ScriptObject ScribanObject { get; }

	public bool TryResolve(string name, out VariableEntity variable) => _byName.TryGetValue(name, out variable!);

	public bool TryResolveEventParameter(string name, out object? value)
		=> _eventParameters.TryGetValue(name, out value);

	public bool TryResolveInput(string name, out object? value) => _inputs.TryGetValue(name, out value);

	public VariableContext WithEvent(IReadOnlyDictionary<string, object?> eventParameters)
	{
		var eventObject = new ScriptObject();
		foreach (var parameter in eventParameters)
		{
			eventObject[parameter.Key] = parameter.Value;
		}

		var root = new ScriptObject { ["vars"] = ScribanObject["vars"], ["event"] = eventObject };
		return new VariableContext(_byName, root, eventParameters, _inputs);
	}

	/// <summary>
	/// Overlays a script run's inputs onto the <c>vars</c> namespace, shadowing a global of the same name
	/// for that run only.
	/// </summary>
	public VariableContext WithInputs(IReadOnlyDictionary<string, object?> inputs)
	{
		// A new ScriptObject, never an edit in place: the existing one is shared with the context this was
		// derived from - including the Empty singleton - so overlaying onto it would leak into every run.
		var vars = new VariablesScriptObject();
		if (ScribanObject["vars"] is ScriptObject existing)
		{
			foreach (var entry in existing)
			{
				vars[entry.Key] = entry.Value;
			}
		}

		foreach (var input in inputs)
		{
			vars[input.Key] = input.Value;
		}

		var root = new ScriptObject { ["vars"] = vars, ["event"] = ScribanObject["event"] };
		return new VariableContext(_byName, root, _eventParameters, inputs);
	}

	public static VariableContext Empty { get; } = new(new Dictionary<string, VariableEntity>(StringComparer.Ordinal),
		new ScriptObject { ["vars"] = new VariablesScriptObject(), ["event"] = new ScriptObject() });
}

public class VariableTemplateRenderer : IVariableTemplateRenderer
{
	public const string UnavailablePlaceholder = "n/v";

	private static readonly ParserOptions _liquidParserOptions = new() { LiquidFunctionsToScriban = true };

	private readonly VariableRegistry _registry;
	private readonly ConcurrentDictionary<string, ParsedTemplate> _templateCache = new();
	private readonly ConcurrentDictionary<(VariableScope Scope, string? ScopeRefId), RenderMemo> _memo = new();

	public VariableTemplateRenderer(VariableRegistry registry)
	{
		_registry = registry;
	}

	public Task<string> RenderAsync(string templateText, VariableScope contextScope, string? contextScopeRefId)
		=> Task.FromResult(Render(templateText, contextScope, contextScopeRefId));

	public string Render(string templateText, VariableScope contextScope, string? contextScopeRefId)
	{
		if (!ContainsLiquid(templateText))
		{
			return templateText;
		}

		var parsed = Parse(templateText);
		if (parsed.Names is null)
		{
			return Render(parsed.Template, BuildContext(contextScope, contextScopeRefId));
		}

		var snapshot = CaptureSnapshot(parsed.Names, contextScope, contextScopeRefId);
		var key = (contextScope, contextScopeRefId);

		if (_memo.TryGetValue(key, out var memo) &&
			string.Equals(memo.TemplateText, templateText, StringComparison.Ordinal) &&
			memo.Snapshot.SequenceEqual(snapshot))
		{
			return memo.Output;
		}

		var output = RenderSnapshot(parsed.Template, snapshot);
		_memo[key] = new RenderMemo(templateText, snapshot, output);
		return output;
	}

	internal IReadOnlyList<VariableSnapshot> CaptureSnapshot(
		IReadOnlyCollection<string> names,
		VariableScope contextScope,
		string? contextScopeRefId)
	{
		var readsLocals = contextScope != VariableScope.Global && !string.IsNullOrEmpty(contextScopeRefId);
		var snapshot = new List<VariableSnapshot>(names.Count);

		foreach (var name in names)
		{
			var entity = (readsLocals ? _registry.FindByName(contextScope, contextScopeRefId, name) : null) ??
				_registry.FindByName(VariableScope.Global, null, name);

			snapshot.Add(entity is null
				? new VariableSnapshot(name, null, false)
				: new VariableSnapshot(name, Detach(entity), _registry.IsAvailable(entity.Id)));
		}

		return snapshot;
	}

	internal string RenderSnapshot(string templateText, IReadOnlyList<VariableSnapshot> snapshot)
		=> RenderSnapshot(Parse(templateText).Template, snapshot);

	private static string RenderSnapshot(Template template, IReadOnlyList<VariableSnapshot> snapshot)
	{
		var vars = new VariablesScriptObject();
		var resolvable = new Dictionary<string, VariableEntity>(StringComparer.Ordinal);

		// Rendered from the detached copies only, never the live entities writers mutate in place: the memo
		// then stores text and snapshot that describe the same data, so an equal snapshot means equal text.
		foreach (var entry in snapshot)
		{
			if (entry.Variable is not { } variable)
			{
				continue;
			}

			vars[entry.Name] = EntryOf(variable, entry.IsAvailable);
			if (entry.IsAvailable)
			{
				resolvable[entry.Name] = variable;
			}
		}

		var root = new ScriptObject { ["vars"] = vars, ["event"] = new ScriptObject() };
		return Render(template, new VariableContext(resolvable, root));
	}

	private ParsedTemplate Parse(string templateText)
		=> _templateCache.GetOrAdd(templateText, text =>
		{
			var template = Template.ParseLiquid(text, null, _liquidParserOptions);
			return new ParsedTemplate(template, TemplateVariableAccess.ReadNames(template));
		});

	private static VariableEntity Detach(VariableEntity live) => new()
	{
		Id = live.Id,
		Name = live.Name,
		Scope = live.Scope,
		ScopeRefId = live.ScopeRefId,
		Type = live.Type,
		Classification = live.Classification,
		Value = live.Value,
		DecimalPlaces = live.DecimalPlaces,
		Unit = live.Unit,
		SemanticKind = live.SemanticKind,
		Attributes = live.Attributes is { } attributes
			? new Dictionary<string, string>(attributes, StringComparer.Ordinal)
			: null,
		Min = live.Min,
		Max = live.Max,
		Step = live.Step,
	};

	public Task<VariableContext> CreateContextAsync(VariableScope contextScope, string? contextScopeRefId)
		=> Task.FromResult(BuildContext(contextScope, contextScopeRefId));

	private VariableContext BuildContext(VariableScope contextScope, string? contextScopeRefId)
	{
		var globals = _registry.GetByScope(VariableScope.Global, null);
		var locals = contextScope != VariableScope.Global && !string.IsNullOrEmpty(contextScopeRefId)
			? _registry.GetByScope(contextScope, contextScopeRefId)
			: [];

		var byName = new Dictionary<string, VariableEntity>(StringComparer.Ordinal);
		foreach (var v in globals)
		{
			byName[v.Name] = v;
		}

		foreach (var v in locals)
		{
			byName[v.Name] = v;
		}

		var vars = new VariablesScriptObject();

		// An unavailable variable renders as the placeholder but does not resolve. The two paths differ
		// deliberately: a template is text, so "n/v" is the honest thing to show, while a condition is a
		// decision - comparing against a value the host knows is stale is how an automation fires on a
		// battery level from an unplugged device. Unresolvable is the same answer a name nobody declared
		// gets, which is the behaviour a condition already handles.
		var resolvable = new Dictionary<string, VariableEntity>(StringComparer.Ordinal);
		foreach (var kv in byName)
		{
			var isAvailable = _registry.IsAvailable(kv.Value.Id);
			vars[kv.Key] = EntryOf(kv.Value, isAvailable);
			if (isAvailable)
			{
				resolvable[kv.Key] = kv.Value;
			}
		}

		var root = new ScriptObject { ["vars"] = vars, ["event"] = new ScriptObject() };
		return new VariableContext(resolvable, root);
	}

	public string Render(string templateText, VariableContext context)
	{
		if (!ContainsLiquid(templateText))
		{
			return templateText;
		}

		return Render(Parse(templateText).Template, context);
	}

	private static string Render(Template template, VariableContext context)
	{
		var templateContext = new VariableTemplateContext();

		templateContext.PushGlobal(TemplateFilters.CreateScope());
		templateContext.PushGlobal(context.ScribanObject);
		return template.Render(templateContext);
	}

	// Wrapped on the unavailable branch too: a variable's unit does not stop existing because its provider is
	// momentarily quiet, so vars.x.unit still resolves while vars.x reads "n/v".
	private static VariableTemplateValue EntryOf(VariableEntity variable, bool isAvailable)
		=> isAvailable
			? Wrap(variable, FormatForTemplate(variable), isAvailable: true)
			: Wrap(variable, UnavailablePlaceholder, isAvailable: false);

	private sealed record ParsedTemplate(Template Template, IReadOnlyCollection<string>? Names);

	private sealed record RenderMemo(string TemplateText, IReadOnlyList<VariableSnapshot> Snapshot, string Output);

	internal sealed record VariableSnapshot(string Name, VariableEntity? Variable, bool IsAvailable)
	{
		public bool Equals(VariableSnapshot? other)
			=> other is not null &&
				string.Equals(Name, other.Name, StringComparison.Ordinal) &&
				IsAvailable == other.IsAvailable &&
				RendersAlike(Variable, other.Variable);

		public override int GetHashCode() => HashCode.Combine(Name, IsAvailable, Variable?.Value);

		private static bool RendersAlike(VariableEntity? left, VariableEntity? right)
			=> left is null || right is null
				? left is null && right is null
				: left.Id == right.Id &&
				left.Type == right.Type &&
				string.Equals(left.Value, right.Value, StringComparison.Ordinal) &&
				left.DecimalPlaces == right.DecimalPlaces &&
				string.Equals(left.Unit, right.Unit, StringComparison.Ordinal) &&
				string.Equals(left.SemanticKind, right.SemanticKind, StringComparison.Ordinal) &&
				Nullable.Equals(left.Min, right.Min) &&
				Nullable.Equals(left.Max, right.Max) &&
				Nullable.Equals(left.Step, right.Step) &&
				SameAttributes(left.Attributes, right.Attributes);

		private static bool SameAttributes(
			IReadOnlyDictionary<string, string>? left,
			IReadOnlyDictionary<string, string>? right)
			=> left is null || right is null
				? left is null && right is null
				: left.Count == right.Count &&
				left.All(pair => right.TryGetValue(pair.Key, out var value) &&
					string.Equals(value, pair.Value, StringComparison.Ordinal));
	}

	public static bool ContainsLiquid(string? template)
	{
		return !string.IsNullOrEmpty(template) &&
			(template.Contains("{{", StringComparison.Ordinal) || template.Contains("{%", StringComparison.Ordinal));
	}

	// Every variable is wrapped, because "vars.x.state" has to answer on a plain user variable too. The
	// container stays transparent - it delegates any member it does not own back to the accessor Scriban
	// would have used on the bare value - and allocates its attribute dictionary only when there are
	// attributes, so an unattributed variable pays for one small object rather than a dictionary.
	private static VariableTemplateValue Wrap(VariableEntity entity, object value, bool isAvailable)
		=> new(value, entity, isAvailable);

	private static object FormatForTemplate(VariableEntity v)
	{
		return v.Type switch
		{
			VariableType.Text => v.Value,
			VariableType.Numeric => decimal.TryParse(v.Value,
				NumberStyles.Number,
				CultureInfo.InvariantCulture,
				out var d)
				? v.DecimalPlaces.HasValue
					? decimal.Parse(d.ToString("F" + v.DecimalPlaces.Value, CultureInfo.InvariantCulture),
						NumberStyles.Number,
						CultureInfo.InvariantCulture)
					: d
				: v.Value,
			VariableType.Boolean => string.Equals(v.Value, "true", StringComparison.OrdinalIgnoreCase),
			_ => v.Value
		};
	}
}
