using System.Globalization;
using MacroDeckHost.Domain.Entities;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace MacroDeckHost.Application.Variables;

// What "vars.x" resolves to when the variable carries attributes: the scalar value, with the attributes
// reachable as members on the same reference.
//
// A plain object rather than a ScriptObject subclass, because ScriptObject seals ToString() and this type
// has to render as its value. It deliberately implements neither IFormattable, IConvertible, IEnumerable
// nor IDictionary: every one of those is a branch Scriban takes *before* asking the context to convert, so
// implementing one would decide a conversion behind VariableTemplateContext's back.
//
// The custom binary and unary operations are what keep comparisons honest. Scriban dispatches a binary
// operator on the CLR type of its operands, so without them "vars.cpu > 12" against 12.5 would be answered
// by whatever the container looks like rather than by the number it holds.
public sealed class VariableTemplateValue : IScriptObject, IScriptCustomBinaryOperation, IScriptCustomUnaryOperation
{
	/// <summary>The one member name the host reserves on a variable reference; see <see cref="State"/>.</summary>
	public const string StateMemberName = "state";

	// Allocated only for a variable that actually declares attributes. Every variable is wrapped now - a
	// plain user variable has to answer "state" too - so the container itself must stay cheap.
	private readonly Dictionary<string, object?>? _members;

	private readonly bool _isAvailable;
	private readonly bool _isEmpty;

	private ScriptObject? _state;

	/// <summary>
	/// A name the registry does not know. It renders as nothing and stays falsy, exactly as an absent
	/// <c>vars</c> entry did, but it can still answer <c>state</c> - so a template and a condition give the
	/// same answer for an undeclared name instead of only agreeing about declared ones.
	/// </summary>
	public static VariableTemplateValue Unknown { get; } = new();

	private VariableTemplateValue()
	{
		Value = null;
		_isAvailable = false;
		_isEmpty = false;
	}

	public VariableTemplateValue(object? value, VariableEntity entity, bool isAvailable)
	{
		Value = value;
		_isAvailable = isAvailable;

		// Emptiness is a property of what the reference renders as, measured with the same three shapes
		// ToString() uses, so "state.is_empty" and "{{ vars.x }}" can never disagree. Gated on
		// availability, which is what keeps the unavailable branch's placeholder from ever being measured.
		_isEmpty = isAvailable && Render(value).Length == 0;

		if (!HasAttributes(entity))
		{
			return;
		}

		_members = new Dictionary<string, object?>(StringComparer.Ordinal);

		if (entity.Unit is not null)
		{
			_members["unit"] = entity.Unit;
		}

		if (entity.SemanticKind is not null)
		{
			_members["semantic_kind"] = entity.SemanticKind;
		}

		if (entity.DecimalPlaces is { } decimalPlaces)
		{
			_members["decimal_places"] = decimalPlaces;
		}

		if (entity.Min is { } min)
		{
			_members["min"] = min;
		}

		if (entity.Max is { } max)
		{
			_members["max"] = max;
		}

		if (entity.Step is { } step)
		{
			_members["step"] = step;
		}

		if (entity.Attributes is null)
		{
			return;
		}

		foreach (var attribute in entity.Attributes)
		{
			// TryAdd, not an assignment: the open map is not allowed to shadow an attribute the host
			// computed, and the host does not interpret what is left.
			_members.TryAdd(attribute.Key, attribute.Value);
		}
	}

	public object? Value { get; }

	/// <summary>
	/// The four state predicates behind <c>vars.x.state</c>, mirroring the condition operators of the same
	/// names. <c>is_empty</c> and <c>is_not_empty</c> are deliberately not negations of each other: a
	/// variable that did not resolve is neither, only unavailable. Built on first access, because most
	/// templates never ask.
	/// </summary>
	private ScriptObject State => _state ??= BuildState();

	private ScriptObject BuildState()
	{
		var state = new ScriptObject
		{
			["is_available"] = _isAvailable,
			["is_not_available"] = !_isAvailable,
			["is_empty"] = _isEmpty,
			["is_not_empty"] = _isAvailable && !_isEmpty
		};

		// Read-only because Unknown is a process-wide singleton: a template must not be able to write
		// through one render's state object into every later render's.
		state.IsReadOnly = true;
		return state;
	}

	/// <summary>Whether wrapping <paramref name="entity"/> would expose anything at all.</summary>
	public static bool HasAttributes(VariableEntity entity)
		=> entity.Unit is not null ||
			entity.SemanticKind is not null ||
			entity.DecimalPlaces is not null ||
			entity.Min is not null ||
			entity.Max is not null ||
			entity.Step is not null ||
			entity.Attributes is { Count: > 0 };

	public static object? Unwrap(object? value) => value is VariableTemplateValue wrapped ? wrapped.Value : value;

	// Mirrors Scriban's own ObjectToStringImpl for the three shapes a variable value can take, so a
	// container that reaches a rendering path without going through VariableTemplateContext still prints
	// what the bare value would have printed.
	public override string ToString() => Render(Value);

	private static string Render(object? value) => value switch
	{
		null => string.Empty,
		string text => text,
		bool flag => flag ? "true" : "false",
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? string.Empty
	};

	public int Count => (_members?.Count ?? 0) + 1;

	public bool IsReadOnly
	{
		get => true;
		set { }
	}

	public IEnumerable<string> GetMembers()
		=> _members is null ? [StateMemberName] : _members.Keys.Prepend(StateMemberName);

	public bool Contains(string member)
		=> member == StateMemberName || (_members?.ContainsKey(member) ?? false);

	// The reserved name is answered ahead of the attribute map rather than merely inserted before it, so
	// the open plugin map cannot reach it at all - see the TryAdd note in the constructor.
	//
	// The delegation at the end is what keeps the container transparent. Scriban asks an IScriptObject for
	// a member before it falls back to a member accessor, so without it wrapping would swallow everything
	// the accessor serves on the bare value - a string's "size" among them.
	public bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
	{
		if (member == StateMemberName)
		{
			value = State;
			return true;
		}

		if (_members is not null && _members.TryGetValue(member, out value))
		{
			return true;
		}

		if (Value is null)
		{
			value = null;
			return false;
		}

		return context.GetMemberAccessor(Value).TryGetValue(context, span, Value, member, out value);
	}

	public bool CanWrite(string member) => false;

	public bool TrySetValue(TemplateContext context, SourceSpan span, string member, object? value, bool readOnly)
		=> false;

	public bool Remove(string member) => false;

	public void SetReadOnly(string member, bool readOnly)
	{
	}

	public IScriptObject Clone(bool deep) => this;

	public bool TryEvaluate(
		TemplateContext context,
		SourceSpan span,
		ScriptBinaryOperator op,
		SourceSpan leftSpan,
		object? leftValue,
		SourceSpan rightSpan,
		object? rightValue,
		out object? result)
	{
		result = ScriptBinaryExpression.Evaluate(context,
			span,
			op,
			leftSpan,
			Unwrap(leftValue),
			rightSpan,
			Unwrap(rightValue));

		return true;
	}

	public bool TryEvaluate(
		TemplateContext context,
		SourceSpan span,
		ScriptUnaryOperator op,
		object rightValue,
		out object result)
	{
		result = ScriptUnaryExpression.Evaluate(context, span, op, Unwrap(rightValue))!;
		return true;
	}
}
