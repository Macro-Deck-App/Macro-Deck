using MacroDeck.Plugin.Protocol.Limits;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.Variables;

/// <summary>
/// The current catalog working set: the resource ids the host most recently told this plugin's provider
/// to watch, per <c>IVariableProvider.SubscribeAsync</c>'s "authoritative working set, not an
/// increment" contract. Registered as a singleton (see
/// <c>DependencyInjection.MacroDeckServiceCollectionExtensions</c>) so <c>VariablesCapabilityHandler</c>
/// and <c>Integrations.HostApis.RemoteVariableSink</c> always agree on what the plugin is currently
/// watching.
///
/// <para>
/// Replaced wholesale on every <c>subscribe</c>, never mutated element-wise - an atomically swapped
/// immutable snapshot is therefore the natural fit, and every read sees a consistent set with no locking.
/// </para>
/// </summary>
internal sealed class VariableSubscriptions
{
	private static readonly HashSet<string> _empty = new(StringComparer.Ordinal);

	private readonly ILogger _logger;

	private HashSet<string> _current = _empty;

	public VariableSubscriptions(ILogger logger)
	{
		_logger = logger.ForContext<VariableSubscriptions>();
	}

	/// <summary>The ids currently being watched.</summary>
	public IReadOnlySet<string> Current => _current;

	/// <summary>
	/// Replaces the working set, clamped to <see cref="ProtocolLimits.MaxVariableSubscriptions" />.
	/// Returns the clamped set that was actually stored, so the caller can report back exactly what took
	/// effect. The returned set is a copy, not the instance stored internally - handing out the same
	/// mutable <see cref="HashSet{T}"/> a caller could cast back to would let it be mutated in place,
	/// bypassing this class's own filtering.
	/// </summary>
	public IReadOnlySet<string> Replace(IReadOnlyCollection<string> ids)
	{
		ArgumentNullException.ThrowIfNull(ids);

		if (ids.Count > ProtocolLimits.MaxVariableSubscriptions)
		{
			_logger.Warning("Variable subscribe requested {Requested} ids, more than the " +
				"{Max} this plugin may watch at once; the excess ids were dropped and are unavailable " +
				"until the working set shrinks",
				ids.Count,
				ProtocolLimits.MaxVariableSubscriptions);
		}

		var clamped = new HashSet<string>(ids.Take(ProtocolLimits.MaxVariableSubscriptions),
			StringComparer.Ordinal);

		Interlocked.Exchange(ref _current, clamped);
		return new HashSet<string>(clamped, StringComparer.Ordinal);
	}
}
