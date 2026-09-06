using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC03xx - B3: capability serialization. An unknown kind and an over-limit declared count cannot reach a
// running subject at all (PluginHostBuilder.Build rejects both), so MDC0301 and MDC0304 are regression
// guards read from the wire. MDC0303 (provider-shaped kinds declare a single "provider" capability) is
// also a regression guard - only handler code, never plugin-author code, decides a kind's local id. What
// earns this category its runtime keep is MDC0305: whether a describe result actually stays inside
// MaxMessageBytes is a function of what the author declares, and an oversize declaration is entirely
// constructible - see the misbehaving fixture's oversize-describe flag.

/// <summary>Regression guard: every declared kind, read from the wire, is one <c>CapabilityKinds</c> knows.</summary>
internal sealed class DeclaredKindsAreKnownCheck() : ConformanceCheckBase("MDC0301",
	"Every declared capability's kind is one of CapabilityKinds.All",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var capability in context.Session!.Declared)
		{
			if (!CapabilityKinds.IsKnown(capability.Kind))
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					$"Every declared kind is one of: {string.Join(", ", CapabilityKinds.All)}.",
					$"'{capability.Kind}' is not a known capability kind."));
			}
		}

		return Task.FromResult(ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("declared capabilities", context.Session.Declared.Count)
		]));
	}
}

/// <summary>Every declared version range is internally valid and was actually accepted by this host.</summary>
internal sealed class VersionRangesAreValidAndAcceptedCheck() : ConformanceCheckBase("MDC0302",
	"Every declared capability's version range is valid and overlaps this host's supported range",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var capability in context.Session!.Declared)
		{
			if (capability.VersionRange.Minimum > capability.VersionRange.Maximum)
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					"Every declared capability's versionRange has Minimum <= Maximum.",
					$"'{capability.Kind}'/'{capability.LocalId}' declared {capability.VersionRange.Minimum}-{capability.VersionRange.Maximum}."));
			}
		}

		var rejected = context.Session.Accepted.Where(result => !result.Accepted).ToList();

		if (rejected.Count > 0)
		{
			var reasons = string.Join("; ", rejected.Select(result => $"{result.Kind}: {result.RejectionReason}"));

			return Task.FromResult(ConformanceCheckResult.Fail(
				"Every declared capability's version range overlaps this host's supported range and is accepted.",
				$"{rejected.Count} declared capabilities were rejected - {reasons}."));
		}

		return Task.FromResult(ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("accepted capabilities", context.Session.Accepted.Count)
		]));
	}
}

/// <summary>
/// Regression guard: a provider-shaped kind declares exactly one capability, at its own documented local
/// id. Every provider-shaped kind but one shares <see cref="ProviderCapabilityId.LocalId" /> ("provider");
/// <c>icons</c> is the documented exception. <c>IconsCapabilityHandler</c> (<c>MacroDeck.Plugin.Hosting</c>)
/// spells this out in its own remarks: unlike every other kind, it declares metadata only, so its local id
/// is the literal <c>icon</c>, never the "provider" convention. That handler type is internal to the
/// hosting assembly - this suite has no <c>InternalsVisibleTo</c> into it and must not gain one - so
/// <see cref="IconsLocalId" /> below is this check's own copy of that documented wire value, read from the
/// SDK's own doc comment, not a rule this suite invented. Do not "fix" this back to a single shared
/// expectation: a plugin correctly declaring <c>icon</c> for <c>icons</c> would then fail a check that is
/// supposed to be a regression guard against the SDK, not against a conforming plugin.
/// </summary>
/// <remarks>
/// <c>actions</c> and <c>variables</c> are the two item-shaped kinds and are skipped outright. That
/// <c>variables</c> also answers catalog operations naming no declared id - <c>discover</c>,
/// <c>resolve</c>, <c>subscribe</c> - does not make it provider-shaped: its resource ids travel inside
/// operation arguments, while what it <em>declares</em> is still one capability per eager variable.
/// </remarks>
internal sealed class ProviderShapedKindsDeclareOneCapabilityCheck() : ConformanceCheckBase("MDC0303",
	"A provider-shaped kind declares exactly one capability, at its documented local id",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	/// <summary>
	/// <c>icons</c>' own documented local id - see this type's remarks on why it is copied here rather than
	/// referenced from <c>IconsCapabilityHandler.LocalId</c> directly.
	/// </summary>
	private const string IconsLocalId = "icon";

	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var group in context.Session!.Declared.GroupBy(capability => capability.Kind, StringComparer.Ordinal))
		{
			var isItemShaped = string.Equals(group.Key, CapabilityKinds.Actions, StringComparison.Ordinal) ||
				string.Equals(group.Key, CapabilityKinds.Variables, StringComparison.Ordinal);

			if (isItemShaped)
			{
				continue;
			}

			var expectedLocalId = string.Equals(group.Key, CapabilityKinds.Icons, StringComparison.Ordinal)
				? IconsLocalId
				: ProviderCapabilityId.LocalId;

			var members = group.ToList();

			if (members.Count != 1 || !string.Equals(members[0].LocalId, expectedLocalId, StringComparison.Ordinal))
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					$"Provider-shaped kind '{group.Key}' declares exactly one capability, at local id '{expectedLocalId}'.",
					$"'{group.Key}' declared {members.Count} capabilities: [{string.Join(", ", members.Select(capability => capability.LocalId))}]."));
			}
		}

		return Task.FromResult(ConformanceCheckResult.Pass());
	}
}

/// <summary>Regression guard: the declared capability count stays within the protocol's own ceiling.</summary>
internal sealed class DeclaredCapabilityCountIsWithinLimitCheck() : ConformanceCheckBase("MDC0304",
	"The declared capability count does not exceed MaxDeclaredCapabilities",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var count = context.Session!.Declared.Count;

		return Task.FromResult(count <= ProtocolLimits.MaxDeclaredCapabilities
			? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("declared capabilities", count)])
			: ConformanceCheckResult.Fail($"Declares at most {ProtocolLimits.MaxDeclaredCapabilities} capabilities.",
				$"Declares {count}."));
	}
}

/// <summary>
/// The one check in this category with a real, constructible violator: <c>actions/describe</c>'s reply
/// grows with every action's declared metadata, so an author who lets a description balloon (the
/// misbehaving fixture's oversize-describe flag) can push a reply past <see cref="ProtocolLimits.MaxMessageBytes" />
/// with no protocol-level guard stopping them.
/// </summary>
internal sealed class DescribeResultsRespectMessageSizeCheck() : ConformanceCheckBase("MDC0305",
	"actions/describe's reply stays within MaxMessageBytes and deserializes without loss",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (context.Session!.Declared.All(capability =>
			!string.Equals(capability.Kind, CapabilityKinds.Actions, StringComparison.Ordinal)))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the actions capability.");
		}

		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session.Actions.DescribeAsync(new CapabilityInvokeOptions
					{ CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return ConformanceCheckResult.Fail(
				$"actions/describe replies promptly, with a reply whose envelope stays within MaxMessageBytes ({ProtocolLimits.MaxMessageBytes} bytes).",
				"No reply arrived within 5 s - an oversize reply typically drops the connection rather than arriving.");
		}

		if (!outcome.Succeeded)
		{
			return ConformanceCheckResult.Fail(
				"actions/describe succeeds for a subject that declared the actions capability.",
				$"actions/describe failed: {outcome.Error?.Code} - {outcome.Error?.Message}");
		}

		var recorded = context.Host.Messages.WithCorrelationId(outcome.CorrelationId)
			.FirstOrDefault(message =>
				string.Equals(message.Envelope.Type, MessageTypes.CapabilityResult, StringComparison.Ordinal));

		if (recorded is null)
		{
			return ConformanceCheckResult.Inconclusive(
				"The recorded capability.result envelope for this invocation could not be found.");
		}

		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(recorded.Envelope).Length;

		if (bytes > ProtocolLimits.MaxMessageBytes)
		{
			return ConformanceCheckResult.Fail(
				$"The actions/describe reply's envelope stays within MaxMessageBytes ({ProtocolLimits.MaxMessageBytes} bytes).",
				$"The recorded envelope is {bytes} bytes.");
		}

		var catalog = outcome.DataAs<ActionCatalogPayload>();

		return catalog is null
			? ConformanceCheckResult.Fail(
				"actions/describe's result deserializes into ActionCatalogPayload without loss.",
				"Deserialization produced null.")
			: ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("envelope bytes", bytes),
				ConformanceCheckSupport.Observe("declared actions in catalog", catalog.Actions.Count)
			]);
	}
}

/// <summary>
/// A provider's <c>ui/describe</c> reply is what tells Macro Deck which surfaces it can open a session
/// for, so a malformed one is not caught later - the provider simply never gets asked. Nothing at the
/// protocol level constrains the surface list, which makes this a real check rather than a regression
/// guard: the kind vocabulary is deliberately open, so an empty or absent kind cannot be rejected by a
/// schema and has to be rejected here.
/// </summary>
internal sealed class UiDescribeResultIsWellFormedCheck() : ConformanceCheckBase("MDC0306",
	"ui/describe reports a surface list whose every entry names a kind",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Ui))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the ui capability.");
		}

		var outcome = await DescribeProbe.DescribeAsync(context, CapabilityKinds.Ui).ConfigureAwait(false);

		if (outcome.Failure is { } failure)
		{
			return failure;
		}

		var described = outcome.Result!.DataAs<UiDescribePayload>();

		if (described is null)
		{
			return ConformanceCheckResult.Fail("ui/describe's result carries a surfaces member and a uiModelVersion.",
				"The result did not deserialize into UiDescribePayload - a required member is missing.");
		}

		for (var index = 0; index < described.Surfaces.Count; index++)
		{
			if (string.IsNullOrWhiteSpace(described.Surfaces[index].Kind))
			{
				return ConformanceCheckResult.Fail("Every declared surface names a non-empty kind.",
					$"Surface {index} declared an empty kind.");
			}
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("declared surfaces", described.Surfaces.Count),
			ConformanceCheckSupport.Observe("ui model version", described.UiModelVersion)
		]);
	}
}

/// <summary>
/// A declared session mode decides whether Macro Deck may attach a second client to one session, and an
/// unrecognised value is enforced as exclusive rather than rejected - which means an empty one is not a
/// declaration at all, it is the provider silently accepting whatever default the host picks. Separate
/// from <see cref="UiDescribeResultIsWellFormedCheck" /> because the two fail for different reasons and
/// a suppression of one must not suppress the other.
/// </summary>
internal sealed class UiDeclaredSessionModesAreNonEmptyCheck() : ConformanceCheckBase("MDC0307",
	"Every surface ui/describe declares names a non-empty session mode",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Ui))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the ui capability.");
		}

		var outcome = await DescribeProbe.DescribeAsync(context, CapabilityKinds.Ui).ConfigureAwait(false);

		if (outcome.Failure is { } failure)
		{
			return failure;
		}

		var described = outcome.Result!.DataAs<UiDescribePayload>();

		if (described is null)
		{
			return ConformanceCheckResult.Inconclusive(
				"ui/describe's result did not deserialize - MDC0306 reports that.");
		}

		for (var index = 0; index < described.Surfaces.Count; index++)
		{
			if (string.IsNullOrWhiteSpace(described.Surfaces[index].SessionMode))
			{
				return ConformanceCheckResult.Fail("Every declared surface names a non-empty session mode.",
					$"Surface '{described.Surfaces[index].Kind}' declared an empty session mode.");
			}
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("declared session modes",
				string.Join(", ", described.Surfaces.Select(surface => surface.SessionMode)))
		]);
	}
}

/// <summary>
/// The <c>ui</c> mirror of <see cref="DescribeResultsRespectMessageSizeCheck" />, deliberately its own
/// id rather than a widening of MDC0305: a subject that declares <c>ui</c> but not <c>actions</c> would
/// otherwise move from Skip to a possible Fail under an id the conformance guide promises names one
/// rule for as long as the suite exists. A <c>ui/describe</c> reply grows with every surface and every
/// attribute a provider declares, so it can be pushed past
/// <see cref="ProtocolLimits.MaxMessageBytes" /> the same way.
/// </summary>
internal sealed class UiDescribeResultRespectsMessageSizeCheck() : ConformanceCheckBase("MDC0308",
	"ui/describe's reply stays within MaxMessageBytes and deserializes without loss",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Ui))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the ui capability.");
		}

		var outcome = await DescribeProbe.DescribeAsync(context, CapabilityKinds.Ui).ConfigureAwait(false);

		if (outcome.Failure is { } failure)
		{
			return failure;
		}

		var recorded = context.Host.Messages.WithCorrelationId(outcome.Result!.CorrelationId)
			.FirstOrDefault(message =>
				string.Equals(message.Envelope.Type, MessageTypes.CapabilityResult, StringComparison.Ordinal));

		if (recorded is null)
		{
			return ConformanceCheckResult.Inconclusive(
				"The recorded capability.result envelope for this invocation could not be found.");
		}

		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(recorded.Envelope).Length;

		if (bytes > ProtocolLimits.MaxMessageBytes)
		{
			return ConformanceCheckResult.Fail("The ui/describe reply's envelope stays within MaxMessageBytes " +
				$"({ProtocolLimits.MaxMessageBytes} bytes).",
				$"The recorded envelope is {bytes} bytes.");
		}

		var described = outcome.Result.DataAs<UiDescribePayload>();

		return described is null
			? ConformanceCheckResult.Fail("ui/describe's result deserializes into UiDescribePayload without loss.",
				"Deserialization produced null.")
			: ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("envelope bytes", bytes),
				ConformanceCheckSupport.Observe("declared surfaces", described.Surfaces.Count)
			]);
	}
}

/// <summary>
/// A state-provider action's snapshot is what a button binds its appearance to, and a state id is
/// persisted in a user's profile the moment a button adopts it - see
/// <c>MacroDeck.Sdk.Actions.IStateProviderActionDefinition.GetActionStateAsync</c>'s own remarks on why
/// this must not throw on a half-configured instance but must still answer something well-formed.
/// Nothing at the protocol level constrains what a provider hands back beyond the DTO's own shape, which
/// makes this a real check rather than a regression guard - the misbehaving fixture's own
/// <c>duplicate-state-ids</c> flag is a real, constructible violator. A <c>null</c> snapshot
/// (<see cref="ActionStateResult.HasValue" /> false) is the documented "nothing is known yet" answer and
/// always passes untouched.
/// </summary>
internal sealed class ActionStateSnapshotsAreWellFormedCheck() : ConformanceCheckBase("MDC0309",
	"Every state-provider action's state operation returns a well-formed snapshot",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var (localIds, setupFailure) = await ActionStateProbe.StateProviderLocalIdsAsync(context).ConfigureAwait(false);

		if (setupFailure is not null)
		{
			return setupFailure;
		}

		var checkedCount = 0;

		foreach (var localId in localIds)
		{
			var (result, failure) = await ActionStateProbe.GetStateAsync(context, localId).ConfigureAwait(false);

			if (failure is not null)
			{
				return failure;
			}

			if (!result!.HasValue)
			{
				// The documented "nothing is known yet" answer - always valid, nothing further to check.
				checkedCount++;
				continue;
			}

			if (result.States.Count == 0)
			{
				return ConformanceCheckResult.Fail(
					"A present snapshot's States is never empty - a provider with nothing to report returns " +
					"null instead.",
					$"'{localId}' returned a present snapshot with an empty States list.");
			}

			var seenIds = new HashSet<string>(StringComparer.Ordinal);

			foreach (var state in result.States)
			{
				if (string.IsNullOrEmpty(state.Id) || state.Label.IsEmpty)
				{
					return ConformanceCheckResult.Fail(
						"Every state in a present snapshot has a non-empty Id and a non-empty Label.",
						$"'{localId}' returned a state with Id '{state.Id}' and Label '{state.Label}'.");
				}

				if (!seenIds.Add(state.Id))
				{
					return ConformanceCheckResult.Fail("State ids are unique within one snapshot.",
						$"'{localId}' returned the state id '{state.Id}' more than once.");
				}
			}

			if (result.ActiveStateId is { Length: > 0 } activeStateId && !seenIds.Contains(activeStateId))
			{
				return ConformanceCheckResult.Fail(
					"ActiveStateId, when present, names one of the snapshot's own States.",
					$"'{localId}' returned ActiveStateId '{activeStateId}', which is not among its own States.");
			}

			checkedCount++;
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("state-provider actions checked", checkedCount)
		]);
	}
}

/// <summary>
/// The companion rule MDC0309 does not cover: every state id a provider returns is held to the same
/// declared-local-id grammar every other author-chosen id is -
/// <see cref="MacroDeckId.TryValidateLocalId" /> with <see cref="LocalIdKind.Declared" />, the identical
/// call <c>DeclaredLocalIdsAreValidCheck</c> (MDC0102) makes for a capability's own local id, so this
/// check and the host that later persists a chosen state id can never disagree about what a legal one
/// looks like. A separate id from MDC0309 rather than folded into it, so a suppression of one never hides
/// the other. A real, constructible violator - the misbehaving fixture's own <c>invalid-state-id</c> flag.
/// </summary>
internal sealed class ActionStateIdsAreValidDeclaredIdsCheck() : ConformanceCheckBase("MDC0310",
	"Every state a state-provider action returns has an id that is a valid declared-kind identifier",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var (localIds, setupFailure) = await ActionStateProbe.StateProviderLocalIdsAsync(context).ConfigureAwait(false);

		if (setupFailure is not null)
		{
			return setupFailure;
		}

		var checkedCount = 0;

		foreach (var localId in localIds)
		{
			var (result, failure) = await ActionStateProbe.GetStateAsync(context, localId).ConfigureAwait(false);

			if (failure is not null)
			{
				return failure;
			}

			if (!result!.HasValue)
			{
				continue;
			}

			foreach (var state in result.States)
			{
				if (!MacroDeckId.TryValidateLocalId(state.Id, LocalIdKind.Declared, out var error))
				{
					return ConformanceCheckResult.Fail(
						"Every state id passes MacroDeckId.TryValidateLocalId(_, LocalIdKind.Declared) - the " +
						"same grammar every other declared local id is held to, because a state id is " +
						"persisted in a user's profile once a button adopts it.",
						$"'{localId}' returned the state id '{state.Id}', which failed validation: {error}");
				}

				checkedCount++;
			}
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("state ids checked", checkedCount)
		]);
	}
}

/// <summary>
/// The variable-catalog counterpart to MDC0309/MDC0310: a real check, not a regression guard, because
/// nothing at the protocol level constrains what a provider's own <c>discover</c> handling returns - the
/// plugin-side <c>VariablesCapabilityHandler</c> in <c>MacroDeck.Plugin.Hosting</c> clamps the page size
/// and drops an invalid definition for a plugin built on this SDK, but a subject that talks the wire
/// protocol directly is under no such obligation, and this check is what holds it to the same contract
/// from the outside.
///
/// <para>
/// The precondition is the <c>describe</c> payload's own <c>supportsCatalog</c>, not a declared kind:
/// after ADR 0081 there is no separate catalog kind to look for, and the eager half of <c>variables</c>
/// says nothing about whether a catalog exists. A subject that declares <c>variables</c> and then cannot
/// answer <c>describe</c> fails here rather than skipping - "the precondition could not be read" is a
/// broken subject, not an absent feature.
/// </para>
/// </summary>
internal sealed class VariableCatalogDiscoverPageIsWellFormedCheck() : ConformanceCheckBase("MDC0311",
	"A variable provider reporting a catalog answers discover with a bounded, well-formed page",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	private static readonly IReadOnlyList<string> _knownTypes = Enum.GetNames<VariableType>();

	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Variables))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the variables capability.");
		}

		var (catalog, describeFailure) = await VariableCatalogProbe.DescribeAsync(context).ConfigureAwait(false);

		if (describeFailure is not null)
		{
			return describeFailure;
		}

		if (!catalog!.SupportsCatalog)
		{
			return ConformanceCheckResult.Skip("This subject's variable provider reports no catalog.");
		}

		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Variables
				.DiscoverAsync(options: new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return ConformanceCheckResult.Fail("variables/discover replies within the invoke timeout.",
				"No reply arrived within 5 s.");
		}

		if (!outcome.Succeeded)
		{
			return ConformanceCheckResult.Fail(
				"variables/discover succeeds for a subject whose describe reports supportsCatalog.",
				$"discover failed: {outcome.Error?.Code} - {outcome.Error?.Message}");
		}

		var page = outcome.DataAs<VariableCatalogPageResult>();

		if (page is null)
		{
			return ConformanceCheckResult.Fail(
				"variables/discover's result deserializes into VariableCatalogPageResult.",
				"Deserialization produced null.");
		}

		if (page.Items.Count > ProtocolLimits.MaxVariableCatalogPageSize)
		{
			return ConformanceCheckResult.Fail("A discover page carries at most MaxVariableCatalogPageSize " +
				$"({ProtocolLimits.MaxVariableCatalogPageSize}) items.",
				$"The page carried {page.Items.Count} items.");
		}

		foreach (var item in page.Items)
		{
			if (!_knownTypes.Contains(item.Type, StringComparer.Ordinal))
			{
				return ConformanceCheckResult.Fail("Every item's type is one of VariableType's member names.",
					$"Item '{item.Id}' declared type '{item.Type}'.");
			}

			if (item.Id is not { Length: > 0 } id)
			{
				return ConformanceCheckResult.Fail(
					"Every discover item carries its own resource id - a catalog resource is addressed by " +
					"an id, never by a name the host would have to derive.",
					$"An item named '{item.Name}' carried no id.");
			}

			if (id.Contains("::", StringComparison.Ordinal))
			{
				return ConformanceCheckResult.Fail(
					"No item id contains the '::' owner-qualifier separator - a resource id is never " +
					"allowed to smuggle another owner's namespace.",
					$"Item id '{id}' contains '::'.");
			}

			if (id.Length > MacroDeckId.MaxResourceLocalIdLength)
			{
				return ConformanceCheckResult.Fail("Every item id is at most MacroDeckId.MaxResourceLocalIdLength " +
					$"({MacroDeckId.MaxResourceLocalIdLength}) characters.",
					$"Item id '{id}' is {id.Length} characters.");
			}
		}

		if (page.ContinuationToken is { Length: 0 })
		{
			return ConformanceCheckResult.Fail(
				"A continuation token, when present, is non-empty - absent means there is no next page.",
				"The page carried an empty (but non-null) continuation token.");
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("items in page", page.Items.Count),
			ConformanceCheckSupport.Observe("continuation token present", page.ContinuationToken is not null)
		]);
	}
}

/// <summary>
/// An icon-provider action's snapshot is what a widget currently renders as its icon, and - like the
/// state snapshot MDC0309 checks - nothing at the protocol level constrains what a provider hands back
/// beyond the DTO's own shape, which makes this a real check rather than a regression guard: the
/// misbehaving fixture's own <c>inconsistent-icon-snapshot</c> flag is a real, constructible violator. A
/// <c>null</c> snapshot (<see cref="ActionIconResult.HasValue" /> false) is the documented "cannot answer
/// right now" answer and always passes untouched - see
/// <c>MacroDeck.Sdk.Actions.IIconProviderActionDefinition.GetActionIconAsync</c>'s own remarks on why that
/// falls back to the widget's configured icon rather than being treated as malformed.
/// </summary>
internal sealed class ActionIconSnapshotsAreWellFormedCheck() : ConformanceCheckBase("MDC0312",
	"Every icon-provider action's icon snapshot is internally consistent",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var (localIds, setupFailure) = await ActionIconProbe.IconProviderLocalIdsAsync(context).ConfigureAwait(false);

		if (setupFailure is not null)
		{
			return setupFailure;
		}

		var checkedCount = 0;

		foreach (var localId in localIds)
		{
			var (result, failure) = await ActionIconProbe.GetIconAsync(context, localId).ConfigureAwait(false);

			if (failure is not null)
			{
				return failure;
			}

			if (!result!.HasValue)
			{
				// The documented "cannot answer right now" answer - always valid, nothing further to check.
				checkedCount++;
				continue;
			}

			if (result.NoIcon)
			{
				if (result.Reference is not null || result.Version.Length > 0)
				{
					return ConformanceCheckResult.Fail(
						"A snapshot with NoIcon set names no Reference and carries an empty Version.",
						$"'{localId}' returned NoIcon with Reference " +
						$"'{result.Reference?.Type}:{result.Reference?.Reference}' and Version '{result.Version}'.");
				}
			}
			else
			{
				if (result.Version.Length == 0)
				{
					return ConformanceCheckResult.Fail("A snapshot that does not set NoIcon has a non-empty Version.",
						$"'{localId}' returned an empty Version without setting NoIcon.");
				}

				if (result.Reference is { } reference &&
					(string.IsNullOrEmpty(reference.Type) || string.IsNullOrEmpty(reference.Reference)))
				{
					return ConformanceCheckResult.Fail(
						"A snapshot's Reference, when present, carries a non-empty Type and Reference.",
						$"'{localId}' returned Reference Type '{reference.Type}' and Reference '{reference.Reference}'.");
				}
			}

			checkedCount++;
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("icon-provider actions checked", checkedCount)
		]);
	}
}

/// <summary>
/// The companion rule MDC0312 does not cover: a snapshot naming no <c>Reference</c> and not set
/// <c>NoIcon</c> is a promise that bytes are available, and <c>icon.content</c> is what redeems it,
/// mirroring how a music player's artwork fetch answers over the same asset-upload pipeline. A separate
/// id from MDC0312 rather than folded into it, so a suppression of one never hides the other. A real,
/// constructible violator - the misbehaving fixture's own <c>non-image-icon-media-type</c> flag, which
/// answers with a real content hash but a media type no client should ever be asked to render as an
/// image.
/// </summary>
internal sealed class ActionIconContentIsAnswerableCheck() : ConformanceCheckBase("MDC0313",
	"A snapshot naming no reference is answerable by icon.content, with AssetTooLarge the only allowed failure",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var (localIds, setupFailure) = await ActionIconProbe.IconProviderLocalIdsAsync(context).ConfigureAwait(false);

		if (setupFailure is not null)
		{
			return setupFailure;
		}

		var checkedCount = 0;

		foreach (var localId in localIds)
		{
			var (snapshot, snapshotFailure) =
				await ActionIconProbe.GetIconAsync(context, localId).ConfigureAwait(false);

			if (snapshotFailure is not null)
			{
				return snapshotFailure;
			}

			if (!snapshot!.HasValue || snapshot.NoIcon || snapshot.Reference is not null)
			{
				// Nothing for icon.content to answer for here - MDC0312 covers this snapshot's own shape.
				continue;
			}

			var (content, contentFailure) =
				await ActionIconProbe.GetIconContentAsync(context, localId, snapshot.Version).ConfigureAwait(false);

			if (contentFailure is not null)
			{
				return contentFailure;
			}

			if (content is null)
			{
				// AssetTooLarge, the one documented failure this operation may answer with instead of a
				// result - ActionIconProbe.GetIconContentAsync already turned any other failure code into
				// contentFailure above.
				checkedCount++;
				continue;
			}

			if (!content.HasValue)
			{
				// The version already moved on between the icon read and this call - a documented, valid
				// answer (IIconProviderActionDefinition.GetActionIconContentAsync's own remarks), nothing
				// further to check.
				checkedCount++;
				continue;
			}

			if (string.IsNullOrEmpty(content.ContentHash) ||
				content.MediaType is not { Length: > 0 } mediaType ||
				!mediaType.StartsWith("image/", StringComparison.Ordinal))
			{
				return ConformanceCheckResult.Fail(
					"A present icon.content result carries a non-empty ContentHash and an image/* MediaType.",
					$"'{localId}' returned ContentHash '{content.ContentHash}' and MediaType '{content.MediaType}'.");
			}

			checkedCount++;
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("icon.content operations checked", checkedCount)
		]);
	}
}

/// <summary>Runs one kind's <c>describe</c> with a local safety net, so the ui checks above express
/// "the reply did not arrive" and "the reply was an error" the same way.</summary>
internal static class DescribeProbe
{
	// actions has no provider local id to address describe with - every declared local id belongs to a
	// specific action - and every handler ignores the local id on describe anyway. See
	// ActionsTestClient's identical remark on the value itself.
	private const string ActionsDescribeLocalId = "describe";

	public static async Task<(CapabilityInvocationOutcome? Result, ConformanceCheckResult? Failure)> DescribeAsync(
		ConformanceContext context,
		string kind)
	{
		var operation = ConformanceCheckSupport.DescribeOperationFor(kind);

		if (operation is null)
		{
			return (null, ConformanceCheckResult.Inconclusive($"This suite knows no describe operation for '{kind}'."));
		}

		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.InvokeAsync(kind,
					string.Equals(kind, CapabilityKinds.Actions, StringComparison.Ordinal)
						? ActionsDescribeLocalId
						: ProviderCapabilityId.LocalId,
					operation,
					options: new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return (null,
				ConformanceCheckResult.Fail(
					$"{kind}/describe replies promptly, with a reply whose envelope stays within " +
					$"MaxMessageBytes ({ProtocolLimits.MaxMessageBytes} bytes).",
					"No reply arrived within 5 s - an oversize reply typically drops the connection rather " +
					"than arriving."));
		}

		return outcome.Succeeded
			? (outcome, null)
			: (null,
				ConformanceCheckResult.Fail($"{kind}/describe succeeds for a subject that declared that capability.",
					$"{kind}/describe failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
	}
}

/// <summary>
/// Shared plumbing for MDC0309 and MDC0310: both need the same declared, state-providing action local
/// ids and the same "invoke <c>state</c> and deserialize" step, so it lives once here rather than twice -
/// the same shape <see cref="DescribeProbe" /> gives the ui checks above.
/// </summary>
internal static class ActionStateProbe
{
	/// <summary>
	/// The local ids of every declared action reporting <see cref="ActionDescriptorDto.ProvidesState" />,
	/// or a <see cref="ConformanceCheckResult" /> for the caller to return unmodified - a
	/// <see cref="ConformanceCheckResult.Skip" /> when the subject declares no such action (including when
	/// it declares no actions at all), or a <see cref="ConformanceCheckResult.Fail" /> when
	/// <c>actions/describe</c> itself did not behave.
	/// </summary>
	public static async Task<(IReadOnlyList<string> LocalIds, ConformanceCheckResult? Failure)>
		StateProviderLocalIdsAsync(ConformanceContext context)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Actions))
		{
			return ([], ConformanceCheckResult.Skip("No declared action reports ProvidesState."));
		}

		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Actions.DescribeAsync(new CapabilityInvokeOptions
					{ CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return ([],
				ConformanceCheckResult.Fail("actions/describe replies promptly.",
					"No reply arrived within 5 s - an oversize reply typically drops the connection rather than " +
					"arriving."));
		}

		if (!outcome.Succeeded)
		{
			return ([],
				ConformanceCheckResult.Fail(
					"actions/describe succeeds for a subject that declared the actions capability.",
					$"actions/describe failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
		}

		var catalog = outcome.DataAs<ActionCatalogPayload>();

		if (catalog is null)
		{
			return ([],
				ConformanceCheckResult.Fail(
					"actions/describe's result deserializes into ActionCatalogPayload without loss.",
					"Deserialization produced null."));
		}

		var localIds = catalog.Actions.Where(action => action.ProvidesState).Select(action => action.LocalId).ToList();

		return localIds.Count == 0
			? ([], ConformanceCheckResult.Skip("No declared action reports ProvidesState."))
			: (localIds, null);
	}

	/// <summary>Invokes the <c>state</c> operation for <paramref name="localId" /> and deserializes the reply.</summary>
	public static async Task<(ActionStateResult? Result, ConformanceCheckResult? Failure)> GetStateAsync(
		ConformanceContext context,
		string localId)
	{
		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Actions.GetActionStateAsync(localId,
					options: new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return (null,
				ConformanceCheckResult.Fail($"The '{localId}' action's state operation replies promptly.",
					"No reply arrived within 5 s."));
		}

		if (!outcome.Succeeded)
		{
			return (null,
				ConformanceCheckResult.Fail(
					$"The '{localId}' action's state operation succeeds for a declared state-provider action.",
					$"state failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
		}

		var result = outcome.DataAs<ActionStateResult>();

		return result is null
			? (null,
				ConformanceCheckResult.Fail(
					$"The '{localId}' action's state operation deserializes into ActionStateResult.",
					"Deserialization produced null."))
			: (result, null);
	}
}

/// <summary>
/// Shared plumbing for MDC0312 and MDC0313: both need the same declared, icon-providing action local ids
/// and the same "invoke and deserialize" step, so it lives once here rather than twice - the same shape
/// <see cref="ActionStateProbe" /> gives the state checks above.
/// </summary>
internal static class ActionIconProbe
{
	/// <summary>
	/// The local ids of every declared action reporting <see cref="ActionDescriptorDto.ProvidesIcon" />,
	/// or a <see cref="ConformanceCheckResult" /> for the caller to return unmodified - a
	/// <see cref="ConformanceCheckResult.Skip" /> when the subject declares no such action, or a
	/// <see cref="ConformanceCheckResult.Fail" /> when <c>actions/describe</c> itself did not behave.
	/// </summary>
	public static async Task<(IReadOnlyList<string> LocalIds, ConformanceCheckResult? Failure)>
		IconProviderLocalIdsAsync(ConformanceContext context)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Actions))
		{
			return ([], ConformanceCheckResult.Skip("No declared action reports ProvidesIcon."));
		}

		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Actions.DescribeAsync(new CapabilityInvokeOptions
					{ CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return ([],
				ConformanceCheckResult.Fail("actions/describe replies promptly.",
					"No reply arrived within 5 s - an oversize reply typically drops the connection rather than " +
					"arriving."));
		}

		if (!outcome.Succeeded)
		{
			return ([],
				ConformanceCheckResult.Fail(
					"actions/describe succeeds for a subject that declared the actions capability.",
					$"actions/describe failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
		}

		var catalog = outcome.DataAs<ActionCatalogPayload>();

		if (catalog is null)
		{
			return ([],
				ConformanceCheckResult.Fail(
					"actions/describe's result deserializes into ActionCatalogPayload without loss.",
					"Deserialization produced null."));
		}

		var localIds = catalog.Actions.Where(action => action.ProvidesIcon).Select(action => action.LocalId).ToList();

		return localIds.Count == 0
			? ([], ConformanceCheckResult.Skip("No declared action reports ProvidesIcon."))
			: (localIds, null);
	}

	/// <summary>Invokes the <c>icon</c> operation for <paramref name="localId" /> and deserializes the reply.</summary>
	public static async Task<(ActionIconResult? Result, ConformanceCheckResult? Failure)> GetIconAsync(
		ConformanceContext context,
		string localId)
	{
		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Actions.GetActionIconAsync(localId,
					options: new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return (null,
				ConformanceCheckResult.Fail($"The '{localId}' action's icon operation replies promptly.",
					"No reply arrived within 5 s."));
		}

		if (!outcome.Succeeded)
		{
			return (null,
				ConformanceCheckResult.Fail(
					$"The '{localId}' action's icon operation succeeds for a declared icon-provider action.",
					$"icon failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
		}

		var result = outcome.DataAs<ActionIconResult>();

		return result is null
			? (null,
				ConformanceCheckResult.Fail(
					$"The '{localId}' action's icon operation deserializes into ActionIconResult.",
					"Deserialization produced null."))
			: (result, null);
	}

	/// <summary>
	/// Invokes the <c>icon.content</c> operation for <paramref name="localId" /> at <paramref name="version" />
	/// and deserializes the reply. Returns a <c>null</c> result with a <c>null</c> failure - not the same
	/// as a failure the caller should return - when the host answers
	/// <see cref="ProtocolErrorCodes.AssetTooLarge" />: the one documented failure this operation may
	/// report instead of a result. Any other failure becomes the returned
	/// <see cref="ConformanceCheckResult" /> for the caller to return unmodified.
	/// </summary>
	public static async Task<(ActionIconContentResult? Result, ConformanceCheckResult? Failure)> GetIconContentAsync(
		ConformanceContext context,
		string localId,
		string version)
	{
		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Actions.GetActionIconContentAsync(localId,
					version,
					options: new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return (null,
				ConformanceCheckResult.Fail($"The '{localId}' action's icon.content operation replies promptly.",
					"No reply arrived within 5 s."));
		}

		if (!outcome.Succeeded)
		{
			return string.Equals(outcome.Error?.Code, ProtocolErrorCodes.AssetTooLarge, StringComparison.Ordinal)
				? (null, null)
				: (null,
					ConformanceCheckResult.Fail(
						$"The '{localId}' action's icon.content operation succeeds, or fails specifically with " +
						"AssetTooLarge.",
						$"icon.content failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
		}

		var result = outcome.DataAs<ActionIconContentResult>();

		return result is null
			? (null,
				ConformanceCheckResult.Fail(
					$"The '{localId}' action's icon.content operation deserializes into ActionIconContentResult.",
					"Deserialization produced null."))
			: (result, null);
	}
}

/// <summary>
/// Reads a subject's <c>variables/describe</c> payload once for the checks that need it as a
/// precondition. A subject that declared the kind and then cannot answer <c>describe</c> yields a
/// failure for the caller to return unmodified, never a skip - see
/// <see cref="VariableCatalogDiscoverPageIsWellFormedCheck" />'s remarks.
/// </summary>
internal static class VariableCatalogProbe
{
	public static async Task<(VariableCatalogPayload? Catalog, ConformanceCheckResult? Failure)> DescribeAsync(
		ConformanceContext context)
	{
		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Variables
				.DescribeAsync(new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return (null,
				ConformanceCheckResult.Fail("variables/describe replies within the invoke timeout.",
					"No reply arrived within 5 s."));
		}

		if (!outcome.Succeeded)
		{
			return (null,
				ConformanceCheckResult.Fail(
					"variables/describe succeeds for a subject that declared the variables capability.",
					$"variables/describe failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
		}

		var catalog = outcome.DataAs<VariableCatalogPayload>();

		return catalog is null
			? (null,
				ConformanceCheckResult.Fail("variables/describe's result deserializes into VariableCatalogPayload.",
					"Deserialization produced null."))
			: (catalog, null);
	}

	/// <summary>The local id a definition is addressed by, or null for one that resolves to none - a
	/// template name carrying no id of its own.</summary>
	public static string? LocalIdOf(VariableDefinitionDto definition)
		=> definition.Id ?? VariableDefinitionId.FromName(definition.Name);

	public static bool IsEager(VariableDefinitionDto definition)
		=> !string.Equals(definition.Materialization, VariableMaterializations.OnDemand, StringComparison.Ordinal);
}

/// <summary>
/// A definition that declares a write capability is telling every client it may be written, and the host
/// refuses a write to anything else without ever asking the provider - so a definition carrying
/// <c>write</c> and then answering <c>set</c> with <c>NotWritable</c> (or <c>NotFound</c>, for a
/// variable it just described) leaves a control the user can drag and that silently does nothing. The
/// check writes each writable variable's own current reading back, so a conforming subject ends where it
/// started. Real violator: <c>MisbehaviorFlags.WritableVariableRefusesWrite</c>.
/// </summary>
internal sealed class WritableVariablesAcceptWritesCheck() : ConformanceCheckBase("MDC0314",
	"A variable that declares a write capability answers set with something other than NotWritable or NotFound",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	private const string NotFound = nameof(VariableWriteStatus.NotFound);

	private const string NotWritable = nameof(VariableWriteStatus.NotWritable);

	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Variables))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the variables capability.");
		}

		var (catalog, describeFailure) = await VariableCatalogProbe.DescribeAsync(context).ConfigureAwait(false);

		if (describeFailure is not null)
		{
			return describeFailure;
		}

		var writable = catalog!.Variables
			.Where(definition => definition.Write is not null)
			.Select(definition => (Definition: definition, LocalId: VariableCatalogProbe.LocalIdOf(definition)))
			.Where(pair => pair.LocalId is { Length: > 0 })
			.ToList();

		if (writable.Count == 0)
		{
			return ConformanceCheckResult.Skip("This subject declares no writable variable.");
		}

		var checkedCount = 0;
		var unreadable = 0;

		foreach (var (definition, localId) in writable)
		{
			var current = await ReadAsync(context, localId!).ConfigureAwait(false);

			if (current is null)
			{
				// The write has to carry a value the provider will accept, and its own current reading is
				// the only value this check can know is one. A variable that cannot be read right now is
				// therefore not probed rather than probed with something invented.
				unreadable++;
				continue;
			}

			var (result, failure) = await SetAsync(context, localId!, current).ConfigureAwait(false);

			if (failure is not null)
			{
				return failure;
			}

			if (string.Equals(result!.Status, NotWritable, StringComparison.Ordinal) ||
				string.Equals(result.Status, NotFound, StringComparison.Ordinal))
			{
				return ConformanceCheckResult.Fail(
					"A variable whose definition carries a write capability answers set with a status other " +
					$"than {NotWritable} or {NotFound} - a client decides whether to offer a control from the " +
					"declaration alone.",
					$"'{localId}' declares write and answered '{result.Status}'.");
			}

			checkedCount++;
		}

		return checkedCount == 0
			? ConformanceCheckResult.Inconclusive(
				$"All {unreadable} writable variable(s) reported no value, so none could be written back.")
			: ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("writable variables written", checkedCount),
				ConformanceCheckSupport.Observe("writable variables unreadable", unreadable)
			]);
	}

	private static async Task<VariableValueDto?> ReadAsync(ConformanceContext context, string localId)
	{
		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		try
		{
			var outcome = await context.Session!.Variables
				.GetAsync(localId, new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);

			var value = outcome.Succeeded ? outcome.DataAs<VariableReadingDto>()?.Value : null;

			return string.Equals(value?.Kind, VariableValueDto.Unavailable.Kind, StringComparison.Ordinal)
				? null
				: value;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	private static async Task<(VariableSetResult? Result, ConformanceCheckResult? Failure)> SetAsync(
		ConformanceContext context,
		string localId,
		VariableValueDto value)
	{
		using var safetyNet = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		CapabilityInvocationOutcome outcome;

		try
		{
			outcome = await context.Session!.Variables
				.SetAsync(localId, value, new CapabilityInvokeOptions { CancellationToken = safetyNet.Token })
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return (null,
				ConformanceCheckResult.Fail($"variables/set for '{localId}' replies within the invoke timeout.",
					"No reply arrived within 5 s."));
		}

		if (!outcome.Succeeded)
		{
			return (null,
				ConformanceCheckResult.Fail(
					$"variables/set for '{localId}' succeeds as an invocation - a refused write is a status, " +
					"not a failed operation.",
					$"set failed: {outcome.Error?.Code} - {outcome.Error?.Message}"));
		}

		var result = outcome.DataAs<VariableSetResult>();

		return result is null
			? (null,
				ConformanceCheckResult.Fail($"variables/set for '{localId}' deserializes into VariableSetResult.",
					"Deserialization produced null."))
			: (result, null);
	}
}

/// <summary>
/// The eager half of <c>variables</c> is registered and polled in full from initialization, one host-side
/// variable per entry, so it is bounded by contract at
/// <see cref="VariableLimits.MaxEagerVariablesPerProvider" /> - a set too large to enumerate belongs in
/// the catalog half instead. A plugin built on this SDK is clamped to the bound with an error line
/// rather than refused, and this is what holds a subject talking the wire protocol directly to the same
/// number.
/// </summary>
/// <remarks>
/// The wire carries no grouping by provider, so what is read here is the plugin's whole eager list. That
/// is the stricter reading of a per-provider bound and the only one a describe payload supports; a
/// plugin genuinely wanting more than the bound across several providers has already outgrown the eager
/// half.
/// </remarks>
internal sealed class EagerVariableSetIsWithinLimitCheck() : ConformanceCheckBase("MDC0315",
	"The eager variable list stays within VariableLimits.MaxEagerVariablesPerProvider",
	ConformanceCategory.CapabilitySerialization,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (!ConformanceCheckSupport.Declares(context, CapabilityKinds.Variables))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the variables capability.");
		}

		var (catalog, describeFailure) = await VariableCatalogProbe.DescribeAsync(context).ConfigureAwait(false);

		if (describeFailure is not null)
		{
			return describeFailure;
		}

		var provided = catalog!.Variables.Count(VariableCatalogProbe.IsEager);
		var declared = catalog.DeclaredVariables.Count(VariableCatalogProbe.IsEager);
		var largest = Math.Max(provided, declared);

		return largest > VariableLimits.MaxEagerVariablesPerProvider
			? ConformanceCheckResult.Fail(
				"An eager variable list carries at most VariableLimits.MaxEagerVariablesPerProvider " +
				$"({VariableLimits.MaxEagerVariablesPerProvider}) entries.",
				$"describe reported {provided} provided and {declared} declared eager variables.")
			: ConformanceCheckResult.Pass([
				ConformanceCheckSupport.Observe("eager variables provided", provided),
				ConformanceCheckSupport.Observe("eager variables declared", declared)
			]);
	}
}
