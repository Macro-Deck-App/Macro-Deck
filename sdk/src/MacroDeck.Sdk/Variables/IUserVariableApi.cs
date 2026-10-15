using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.Variables;

/// <summary>Writes variables by name, on behalf of a plugin acting for the user.</summary>
public interface IUserVariableApi
{
	/// <summary>
	/// Applies an operation to a variable. Widget-scoped variables take precedence when <paramref name="ownerWidgetId"/> is set.
	///
	/// <para>
	/// <see cref="UserVariableOperation.Set" /> reaches any variable whose owner accepts a write - a user
	/// variable, or a provider variable whose definition declares a write capability - and is refused with
	/// <see cref="UserVariableWriteStatus.NotEditable" /> for the rest. A user variable that reads its value
	/// from a file without write-back, or renders it from a template, is among the rest, for every operation.
	/// The read-modify-write operations (<see cref="UserVariableOperation.Add" />,
	/// <see cref="UserVariableOperation.Toggle" />, <see cref="UserVariableOperation.Append" />) stay
	/// user-variable-only: they compute from the value the host last saw, which for a provider variable is a
	/// reading the owner may already have moved on from, so an increment against one would silently be lost.
	/// </para>
	/// </summary>
	Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a user variable. Without <paramref name="ownerWidgetId"/> the variable is global; with one it
	/// belongs to that widget and shadows a global of the same name in that widget's context. The host owns
	/// the widget check, so a widget id that does not exist is refused rather than creating an orphan.
	/// </summary>
	/// <remarks>
	/// Default-implemented so an existing implementation of this interface keeps compiling and running; it
	/// reports <see cref="UserVariableCreateStatus.NotSupported" /> rather than pretending to have created
	/// anything.
	/// </remarks>
	Task<UserVariableCreateResult> CreateAsync(
		string name,
		string? ownerWidgetId,
		VariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(UserVariableCreateResult.Failed(UserVariableCreateStatus.NotSupported,
			"This host does not support creating user variables."));
	}

	/// <summary>
	/// Applies an operation to the variable <paramref name="name" /> as the running flow sees it: a variable
	/// local to the script or automation in <paramref name="context" /> wins, then one local to its owner
	/// widget, then a global. The operations and refusals are those of <see cref="ApplyAsync" />.
	/// </summary>
	/// <remarks>
	/// Default-implemented so an existing implementation keeps compiling: it delegates to
	/// <see cref="ApplyAsync" /> with <see cref="UserVariableContext.OwnerWidgetId" />, so script and
	/// automation locals are not consulted there. A host that predates script and automation variables
	/// behaves the same way.
	/// </remarks>
	Task<UserVariableWriteResult> ApplyInContextAsync(
		string name,
		UserVariableContext context,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		return ApplyAsync(name, context.OwnerWidgetId, operation, value, cancellationToken);
	}

	/// <summary>
	/// Creates a user variable in the most local scope <paramref name="context" /> names: its script, else
	/// its automation, else its owner widget, else global. A script or automation that does not exist is
	/// refused with <see cref="UserVariableCreateStatus.UnknownScript" /> or
	/// <see cref="UserVariableCreateStatus.UnknownAutomation" />, a missing widget as in
	/// <see cref="CreateAsync" />.
	/// </summary>
	/// <remarks>
	/// Default-implemented so an existing implementation keeps compiling: it delegates to
	/// <see cref="CreateAsync" /> with <see cref="UserVariableContext.OwnerWidgetId" />, ignoring the script
	/// and automation.
	/// </remarks>
	Task<UserVariableCreateResult> CreateInContextAsync(
		string name,
		UserVariableContext context,
		VariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		return CreateAsync(name, context.OwnerWidgetId, type, initialValue, decimalPlaces, cancellationToken);
	}
}

/// <summary>
/// Where a running flow stands, for resolving and creating user variables by name. Every member is
/// optional; an empty context means global.
/// </summary>
/// <param name="OwnerWidgetId">The widget the flow runs on, as in <c>ActionExecutionContext.OwnerWidgetId</c>.</param>
/// <param name="ScriptId">The script the flow belongs to, as in <c>ActionExecutionContext.ScriptId</c>.</param>
/// <param name="AutomationId">The automation the flow belongs to, as in <c>ActionExecutionContext.AutomationId</c>.</param>
public sealed record UserVariableContext(
	string? OwnerWidgetId = null,
	string? ScriptId = null,
	string? AutomationId = null)
{
	/// <summary>The context of the action step that is executing.</summary>
	public static UserVariableContext From(ActionExecutionContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		return new UserVariableContext(context.OwnerWidgetId, context.ScriptId, context.AutomationId);
	}
}

/// <summary>Operations supported for user variables.</summary>
public enum UserVariableOperation
{
	Set = 0,

	/// <summary>Adds to a numeric value using the variable's configured precision.</summary>
	Add = 1,

	Toggle = 2,

	Append = 3
}

/// <summary>Result status of a variable write.</summary>
public enum UserVariableWriteStatus
{
	Applied = 0,
	NotFound = 1,

	/// <summary>The variable's owner does not accept this operation - a provider variable that declares no
	/// write capability, a user variable that reads from a file without write-back or renders a template,
	/// or any non-user variable under a read-modify-write operation.</summary>
	NotEditable = 2,

	InvalidValue = 3,

	/// <summary>The owner accepts writes but could not take this one right now - disconnected or busy. A
	/// later retry may succeed, which is what separates it from a plain failure.</summary>
	Unavailable = 4
}

/// <summary>The outcome of <see cref="IUserVariableApi.ApplyAsync" />.</summary>
public sealed record UserVariableWriteResult(UserVariableWriteStatus Status, string? Message = null)
{
	public static UserVariableWriteResult Applied()
	{
		return new UserVariableWriteResult(UserVariableWriteStatus.Applied);
	}

	public static UserVariableWriteResult Failed(UserVariableWriteStatus status, string message)
	{
		return new UserVariableWriteResult(status, message);
	}
}

/// <summary>Result status of a user-variable creation.</summary>
public enum UserVariableCreateStatus
{
	Created = 0,

	/// <summary>A variable of that name already exists in the same scope.</summary>
	AlreadyExists = 1,

	/// <summary>The name cannot be turned into a valid variable name.</summary>
	InvalidName = 2,

	/// <summary>The initial value does not match the declared type, or the decimal places are out of range.</summary>
	InvalidValue = 3,

	/// <summary>No widget with the given id exists.</summary>
	UnknownWidget = 4,

	/// <summary>The host does not implement creation.</summary>
	NotSupported = 5,

	/// <summary>The host could not be reached, so whether anything was created is unknown.</summary>
	Unavailable = 6,

	/// <summary>No script with the given id exists.</summary>
	UnknownScript = 7,

	/// <summary>No automation with the given id exists.</summary>
	UnknownAutomation = 8
}

/// <summary>The outcome of <see cref="IUserVariableApi.CreateAsync" />.</summary>
public sealed record UserVariableCreateResult(UserVariableCreateStatus Status, string? Message = null)
{
	public static UserVariableCreateResult Created()
	{
		return new UserVariableCreateResult(UserVariableCreateStatus.Created);
	}

	public static UserVariableCreateResult Failed(UserVariableCreateStatus status, string message)
	{
		return new UserVariableCreateResult(status, message);
	}
}
