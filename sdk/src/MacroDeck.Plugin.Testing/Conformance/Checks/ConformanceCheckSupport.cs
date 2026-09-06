using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Reconnection;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

/// <summary>
/// Small pieces several checks in this namespace share - never protocol logic of its own, only plumbing
/// around the public <c>MacroDeck.Plugin.Testing</c> surface every check already has access to through
/// <see cref="ConformanceContext" />.
/// </summary>
internal static class ConformanceCheckSupport
{
	/// <summary>The local ids of every declared <c>actions</c> capability, in declaration order.</summary>
	public static IReadOnlyList<string> DeclaredActionLocalIds(ConformanceContext context)
		=> context.Session is null
			? []
			:
			[
				.. context.Session.Declared.Where(capability =>
						string.Equals(capability.Kind, CapabilityKinds.Actions, StringComparison.Ordinal))
					.Select(capability => capability.LocalId)
			];

	/// <summary>
	/// The wire operation name for "describe" under <paramref name="kind" /> - every one of the ten
	/// capability kinds declares one, always named identically today, but resolved through
	/// <see cref="CapabilityOperations" />'s own per-kind constants rather than the literal string, so a
	/// future kind that named its own describe operation differently would not silently break this. Null
	/// for a kind this list does not (yet) know.
	/// </summary>
	public static string? DescribeOperationFor(string kind) => kind switch
	{
		_ when string.Equals(kind, CapabilityKinds.Actions, StringComparison.Ordinal) => CapabilityOperations.Actions
			.Describe,
		_ when string.Equals(kind, CapabilityKinds.Variables, StringComparison.Ordinal) => CapabilityOperations
			.Variables.Describe,
		_ when string.Equals(kind, CapabilityKinds.Events, StringComparison.Ordinal) => CapabilityOperations.Events
			.Describe,
		_ when string.Equals(kind, CapabilityKinds.Icons, StringComparison.Ordinal) => CapabilityOperations.Icons
			.Describe,
		_ when string.Equals(kind, CapabilityKinds.ConfigFlow, StringComparison.Ordinal) => CapabilityOperations
			.ConfigFlow.Describe,
		_ when string.Equals(kind, CapabilityKinds.MusicPlayer, StringComparison.Ordinal) => CapabilityOperations
			.MusicPlayer.Describe,
		_ when string.Equals(kind, CapabilityKinds.Weather, StringComparison.Ordinal) => CapabilityOperations.Weather
			.Describe,
		_ when string.Equals(kind, CapabilityKinds.VirtualProfiles, StringComparison.Ordinal) => CapabilityOperations
			.VirtualProfiles.Describe,
		_ when string.Equals(kind, CapabilityKinds.Issues, StringComparison.Ordinal) => CapabilityOperations.Issues
			.Describe,
		_ when string.Equals(kind, CapabilityKinds.Ui, StringComparison.Ordinal) => CapabilityOperations.Ui.Describe,
		_ when string.Equals(kind, CapabilityKinds.WidgetTypeProvider, StringComparison.Ordinal) =>
			CapabilityOperations.WidgetTypeProvider.Describe,
		_ => null
	};

	/// <summary>Whether the subject declared any capability of <paramref name="kind" />.</summary>
	public static bool Declares(ConformanceContext context, string kind)
		=> context.Session is not null &&
			context.Session.Declared.Any(capability =>
				string.Equals(capability.Kind, kind, StringComparison.Ordinal));

	/// <summary>
	/// Invokes an action, treating <paramref name="options" />'s own <see cref="CapabilityInvokeOptions.CancellationToken" />
	/// firing as "no reply arrived" rather than letting the exception propagate - the shape every check that
	/// probes a declared action with a local safety-net budget needs.
	/// </summary>
	public static async Task<CapabilityInvocationOutcome?> TryExecuteAsync(
		ConformanceContext context,
		string localId,
		CapabilityInvokeOptions options)
	{
		try
		{
			return await context.Session!.Actions.ExecuteAsync(localId, options: options).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	/// <summary>Polls <paramref name="plugin" />'s health until <paramref name="until" /> is satisfied or <paramref name="timeout" /> elapses, returning whatever was last observed either way.</summary>
	public static async Task<PluginHealthReport> WaitForHealthAsync(
		PluginUnderTest plugin,
		Func<PluginHealthReport, bool> until,
		TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		var last = await plugin.ProbeHealthAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

		while (!until(last) && DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
			last = await plugin.ProbeHealthAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
		}

		return last;
	}

	/// <summary>How many <c>capability.result</c> messages carry <paramref name="correlationId" />.</summary>
	public static int ReplyCount(MacroDeckTestHost host, string correlationId)
		=> host.Messages.WithCorrelationId(correlationId)
			.Count(message =>
				string.Equals(message.Envelope.Type, MessageTypes.CapabilityResult, StringComparison.Ordinal));

	/// <summary>
	/// How many messages the plugin itself sent (as opposed to the host) carrying <paramref name="correlationId" />,
	/// of any message type. Unlike <see cref="ReplyCount" />, not restricted to <c>capability.result</c> - the
	/// right measure for "the plugin said nothing at all about this correlation", where <see cref="ProtocolMessageLog.WithCorrelationId" />
	/// alone would also count the host's own outbound message that carried the same id (a <c>capability.cancel</c>
	/// the host itself just sent, in particular).
	/// </summary>
	public static int MessagesFromPluginWithCorrelationId(MacroDeckTestHost host, string correlationId)
		=> host.Messages.WithCorrelationId(correlationId)
			.Count(message => message.Direction == ProtocolMessageDirection.FromPlugin);

	/// <summary>Shorthand for building one <see cref="ConformanceObservation" />.</summary>
	public static ConformanceObservation Observe(string label, object? detail)
		=> new() { Label = label, Detail = detail?.ToString() ?? "(none)" };

	/// <summary>
	/// Awaits <paramref name="operation" />, and while it is still pending, repeatedly advances
	/// <paramref name="clock" /> so a reconnect attempt scheduled against it can actually fire.
	///
	/// <para>
	/// The SDK's own reconnect loop (<c>PluginConnectionHostedService.RunAsync</c>) backs off with
	/// <c>Task.Delay(delay, timeProvider)</c>, where <c>delay</c> comes from <see cref="ReconnectPolicy.DelayFor" />.
	/// For an in-process subject, whose <see cref="TimeProvider" /> is the <see cref="ManualTimeProvider" />
	/// <see cref="ConformanceSubjectHandle.Clock" /> exposes, that delay never elapses on its own - the clock
	/// only moves when something calls <see cref="ManualTimeProvider.Advance" />. Determinism is the reason a
	/// manual clock was injected in the first place, not a reason to sleep through it in real time, so this
	/// pumps it instead of waiting real seconds for a frozen timer that will never fire unassisted.
	/// </para>
	///
	/// <para>
	/// <paramref name="clock" /> is null for an executable or artifact subject - see
	/// <see cref="ConformanceSubjectHandle.Clock" />'s own remarks on why only an in-process subject ever gets
	/// one - in which case this simply awaits <paramref name="operation" /> unmodified, identical to calling
	/// it directly.
	/// </para>
	/// </summary>
	/// <remarks>
	/// Each pump advances the clock by <see cref="ReconnectPolicy.DelayFor" />'s own full-jitter upper bound
	/// for reconnect attempt 1 (jitter sample <c>1.0</c>) - derived from the real policy, never a magic
	/// constant. Every check that calls this causes exactly one pending reconnect (one disconnect, or one
	/// gate release, followed by a single successful retry), so attempt 1's own upper bound always exceeds
	/// whatever delay the plugin actually sampled for it; advances simply accumulate on the clock across
	/// iterations if an earlier one landed before the timer existed to observe it. <paramref name="operation" />'s
	/// own timeout remains the only bound on how long this can run - a subject that genuinely never
	/// reconnects still fails the caller's own wait the same way it always did, just without burning real
	/// time to get there.
	/// </remarks>
	public static async Task<T> WaitPumpingClockAsync<T>(ManualTimeProvider? clock, Func<Task<T>> operation)
	{
		ArgumentNullException.ThrowIfNull(operation);
		var task = operation();

		if (clock is null)
		{
			return await task.ConfigureAwait(false);
		}

		var step = ReconnectPolicy.DelayFor(1, 1.0);

		while (!task.IsCompleted)
		{
			clock.Advance(step);
			await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(25))).ConfigureAwait(false);
		}

		return await task.ConfigureAwait(false);
	}
}
