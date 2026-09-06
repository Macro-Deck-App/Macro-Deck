using System.Text.Json;

namespace MacroDeck.Ui.Dsl;

/// <summary>
/// What an element does when a client raises one of its events. An element lists the handlers it wants on
/// <see cref="UiElement.Events" />; the names it lists are also what the node advertises under its
/// <c>events</c> property, so a renderer knows which affordances to offer without a second declaration.
///
/// <para>
/// A handler is either synchronous or asynchronous, never both. A synchronous handler runs to completion
/// inside <see cref="Runtime.UiView.Dispatch" />, so everything it writes lands in that dispatch's single
/// patch. An asynchronous handler is started there and awaited by
/// <see cref="Runtime.UiView.WhenIdleAsync" />: dispatch itself stays synchronous, because a client's event
/// has to be accepted or rejected before the work it triggers can be finished. A handler that faults - either
/// kind - surfaces on <see cref="Runtime.UiView.HandlerFaulted" /> instead of escaping into the caller or
/// going unobserved.
/// </para>
///
/// <para>
/// <b>Declining is not faulting.</b> A handler that returns a <see cref="UiEventOutcome" /> can refuse the
/// event - a step whose fields are not filled in yet - and that refusal is a rejected
/// <see cref="Runtime.UiDispatchResult" /> carrying the handler's own reason, with
/// <see cref="Runtime.UiView.HandlerFaulted" /> <b>not</b> raised. A handler that throws is rejected
/// <i>and</i> reported as a fault. Both paths exist because they mean different things to whoever is
/// listening: "the plugin declined this input" is an ordinary answer a renderer shows the user, while "the
/// plugin's code is broken" is something a host logs as an error. Collapsing them would make a failed
/// validation indistinguishable from a bug. The <see cref="Action" />-shaped factories below always accept,
/// which is why they keep their meaning unchanged.
/// </para>
///
/// <para>
/// A <c>change</c> handler is optional on a bound input: the binding is the write path, so an input with a
/// writable <see cref="UiInput{T}.Binding" /> accepts <c>change</c> without one. Register a handler when
/// something beyond the bound value has to happen.
/// </para>
/// </summary>
// CA1711 reserves the EventHandler suffix for delegates so a type is not mistaken for one. Here the suffix is
// the whole point: this is the handler an element registers, and the delegate it wraps is an implementation
// detail an author never names. Calling it anything else would make the authoring surface read worse to avoid a
// confusion nobody has.
#pragma warning disable CA1711
public sealed record UiEventHandler
#pragma warning restore CA1711
{
	private UiEventHandler(
		string name,
		Func<UiEventData, UiEventOutcome>? synchronous,
		Func<UiEventData, CancellationToken, Task<UiEventOutcome>>? asynchronous)
	{
		Name = name;
		Synchronous = synchronous;
		Asynchronous = asynchronous;
	}

	/// <summary>The event name this handler answers to - one of the names
	/// <see cref="Config.UiConfigEvents" /> ships, or a newer one a newer renderer sends.</summary>
	public string Name { get; }

	/// <summary>The synchronous body, or <c>null</c> when this handler is asynchronous. A body registered
	/// through an <see cref="Action" />-shaped factory is wrapped to answer
	/// <see cref="UiEventOutcome.Accepted" />.</summary>
	internal Func<UiEventData, UiEventOutcome>? Synchronous { get; }

	/// <summary>The asynchronous body, or <c>null</c> when this handler is synchronous.</summary>
	internal Func<UiEventData, CancellationToken, Task<UiEventOutcome>>? Asynchronous { get; }

	/// <summary>A synchronous handler that ignores the event's payload and always accepts it.</summary>
	public static UiEventHandler On(string name, Action handler)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(handler);

		return new UiEventHandler(name,
			_ =>
			{
				handler();

				return UiEventOutcome.Accepted;
			},
			null);
	}

	/// <summary>A synchronous handler that reads the event's payload and always accepts it.</summary>
	public static UiEventHandler On(string name, Action<UiEventData> handler)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(handler);

		return new UiEventHandler(name,
			data =>
			{
				handler(data);

				return UiEventOutcome.Accepted;
			},
			null);
	}

	/// <summary>
	/// A synchronous handler that decides whether it accepts the event. Returning
	/// <see cref="UiEventOutcome.Rejected" /> makes the dispatch a rejection carrying that reason, without
	/// raising <see cref="Runtime.UiView.HandlerFaulted" /> - see this type's remarks. Whatever the handler wrote
	/// before declining is still flushed, so the validation message the user needs arrives with the refusal.
	/// </summary>
	public static UiEventHandler On(string name, Func<UiEventData, UiEventOutcome> handler)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(handler);

		return new UiEventHandler(name, handler, null);
	}

	/// <summary>An asynchronous handler that ignores the event's payload and always accepts it.</summary>
	public static UiEventHandler OnAsync(string name, Func<CancellationToken, Task> handler)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(handler);

		return new UiEventHandler(name,
			null,
			async (_, cancellationToken) =>
			{
				await handler(cancellationToken).ConfigureAwait(false);

				return UiEventOutcome.Accepted;
			});
	}

	/// <summary>An asynchronous handler that reads the event's payload and always accepts it.</summary>
	public static UiEventHandler OnAsync(string name, Func<UiEventData, CancellationToken, Task> handler)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(handler);

		return new UiEventHandler(name,
			null,
			async (data, cancellationToken) =>
			{
				await handler(data, cancellationToken).ConfigureAwait(false);

				return UiEventOutcome.Accepted;
			});
	}

	/// <summary>
	/// An asynchronous handler that decides whether it accepts the event.
	///
	/// <para>
	/// The outcome cannot reach the dispatch that started the handler: dispatch answers the client before the
	/// work it triggered can finish, which is the contract that keeps an event from blocking on a network call.
	/// A rejection from here is therefore observable only through what the handler wrote - a banner, a
	/// validation message - and the dispatch itself was already accepted. Use the synchronous overload when the
	/// refusal has to be the dispatch's answer.
	/// </para>
	/// </summary>
	public static UiEventHandler OnAsync(
		string name,
		Func<UiEventData, CancellationToken, Task<UiEventOutcome>> handler)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(handler);

		return new UiEventHandler(name, null, handler);
	}
}

/// <summary>
/// Whether a handler accepted the event it was given, and why not when it did not.
///
/// <para>
/// <c>default(UiEventOutcome)</c> is <see cref="Accepted" />, so a handler that falls off the end of a branch
/// accepts rather than landing in an undefined third state. Declining is deliberate and carries a reason the
/// user can read, because a refusal with nothing to say is a dead end.
/// </para>
///
/// <para>
/// Distinct from <see cref="Runtime.UiDispatchResult" />, which is what the <i>view</i> answers and also covers
/// an event no node knows about. This is only what one handler decided.
/// </para>
/// </summary>
public readonly record struct UiEventOutcome
{
	private UiEventOutcome(string? rejectionReason) => RejectionReason = rejectionReason;

	/// <summary>The handler accepted the event. Equal to <c>default(UiEventOutcome)</c>.</summary>
	public static UiEventOutcome Accepted => default;

	/// <summary>Why the handler declined, or <c>null</c> when it accepted.</summary>
	public string? RejectionReason { get; }

	/// <summary>Whether the handler accepted the event.</summary>
	public bool IsAccepted => RejectionReason is null;

	/// <summary>The handler declined the event, for <paramref name="reason" /> - which becomes the rejected
	/// dispatch's <see cref="Runtime.UiDispatchResult.Reason" />.</summary>
	public static UiEventOutcome Rejected(string reason)
	{
		ArgumentException.ThrowIfNullOrEmpty(reason);

		return new UiEventOutcome(reason);
	}
}

/// <summary>
/// A dispatched event's payload, as the client sent it. The accessors are all <c>TryGet</c> shaped on
/// purpose: the model treats a property or payload value as unvalidated data and requires a
/// <see cref="JsonElement.ValueKind" /> check before a typed getter, because a value declared as a number can
/// arrive as a JSON string. A handler that reads the wrong kind therefore gets <c>false</c> rather than an
/// exception it would have to catch.
/// </summary>
public readonly record struct UiEventData
{
	internal UiEventData(JsonElement? raw) => Raw = raw;

	/// <summary>The payload verbatim, or <c>null</c> when the event carried none.</summary>
	public JsonElement? Raw { get; }

	/// <summary>Reads the payload as a JSON string.</summary>
	public bool TryGetString(out string value)
	{
		if (Raw is { ValueKind: JsonValueKind.String } element)
		{
			value = element.GetString()!;

			return true;
		}

		value = string.Empty;

		return false;
	}

	/// <summary>Reads the payload as a JSON boolean.</summary>
	public bool TryGetBoolean(out bool value)
	{
		if (Raw is { } element && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
		{
			value = element.GetBoolean();

			return true;
		}

		value = false;

		return false;
	}

	/// <summary>Reads the payload as a JSON number.</summary>
	public bool TryGetDouble(out double value)
	{
		if (Raw is { ValueKind: JsonValueKind.Number } element && element.TryGetDouble(out value))
		{
			return true;
		}

		value = 0;

		return false;
	}
}
