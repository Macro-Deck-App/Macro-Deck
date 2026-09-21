using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.ActionButton;

/// <summary>
/// Builds the Action Button widget's <c>widget-config</c> tree from its stored data - see ADR 0050 and
/// issue #791. The hard part named by that issue is the action editor: it is reached by naming
/// <see cref="UiActionsListEditor" /> rather than rebuilt from primitives, exactly like every other widget's
/// <c>flows</c> region. The state mapping is the same move: it names <see cref="UiStateMappingEditorInput" />
/// rather than exposing its rules as a raw array a renderer with no dedicated editor would have to draw as a
/// list of condition builders.
/// </summary>
internal static class ActionButtonWidgetConfigView
{
	// The icon-display preview frames the icon at the widget's own aspect ratio so drag/zoom feels right on
	// the real button shape. Widget geometry (grid width/height) is not part of a config surface's inputs
	// today, so this uses the Action Button's usual square footprint rather than the placed instance's
	// actual size - the same approximation the original editor made for a folder's inherited spacing.
	public static UiElement Build(JsonElement data,
		double aspectRatio,
		IIntegrationRegistry integrations,
		IFontCatalog fonts,
		WidgetStateOption? liveState,
		ActionButtonConfigContext context)
	{
		ArgumentNullException.ThrowIfNull(integrations);
		ArgumentNullException.ThrowIfNull(fonts);
		ArgumentNullException.ThrowIfNull(context);

		var root = data.ValueKind == JsonValueKind.Object
			? JsonNode.Parse(data.GetRawText()) as JsonObject ?? new JsonObject()
			: new JsonObject();
		var model = ActionButtonStateModel.Read(root);

		// ---- font catalog helpers - back the transient family selector (issue #837) ----------------------
		//
		// Family is not a stored key - only fontFaceId is - so offering it as its own control needs a
		// transient input (UiInput.Transient): it renders and raises change like any other input, but the
		// client's draft composer skips it entirely, and its own write here only ever resolves a real
		// fontFaceId. The session runs in-process beside the catalogue, so deriving the family from a face
		// id, and the faces a family actually ships, are both plain reads.

		var fontFamilies = fonts.GetFaces()
			.Select(face => face.Family)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
			.ToList();

		string FamilyOf(string faceId)
			=> string.IsNullOrEmpty(faceId)
				? string.Empty
				: fonts.GetFaces().FirstOrDefault(face => string.Equals(face.FaceId, faceId, StringComparison.Ordinal))
					?.Family ??
				string.Empty;

		List<FontFaceInfo> FacesForFamily(string family)
			=> string.IsNullOrEmpty(family)
				? []
				: fonts.GetFaces().Where(face => string.Equals(face.Family, family, StringComparison.Ordinal)).ToList();

		// ---- Persisted cells - every one of these is a real widget-data key -----------------------------

		var stateMode = new UiState<bool>(model.StateMode);
		var states = new UiState<List<ActionButtonStateEntry>>(model.States.ToList());
		var mappingRules =
			new UiState<List<ActionButtonStateMappingRule>>(model.StateMapping?.Rules.ToList() ?? []);
		var fallbackStateId = new UiState<string>(model.StateMapping?.FallbackStateId ?? string.Empty);
		var stateProvider = new UiState<ActionButtonStateProvider?>(model.StateProvider);
		var iconProviderState = new UiState<ActionButtonIconProvider?>(model.IconProvider);
		var manualBackup = new UiState<ActionButtonManualStateBackup?>(model.ManualStateBackup);
		var activeStoredStateId = new UiState<string>(model.ActiveStateId ?? string.Empty);
		var cycleStatesOnPress = new UiState<bool>(model.CycleStatesOnPress);

		// The state the widget is actually showing right now (issue #837) - resolved once, host-side, when
		// the config session opens (ActionButtonWidgetUiProvider), the same source WidgetStateService itself
		// resolves a rendered button's live face from. Not necessarily the one being edited above, and never
		// re-resolved for the life of this session - a config editor is a short-lived, single-shot surface,
		// not a live view of the running button.
		var liveStateId = new UiState<string>(liveState?.Id ?? string.Empty);
		var liveStateLabel = new UiState<string>(liveState is null ? string.Empty : context.Resolve(liveState.Label));

		var label = new UiState<string>(WidgetConfigJson.ReadString(data, "label") ?? string.Empty);
		var icon = new UiState<UiIconReference>(ReadIcon(data)!);
		var iconDisplayData = WidgetConfigJson.ReadObject(data, "iconDisplay");
		var iconFit = new UiState<string>(WidgetConfigJson.ReadString(iconDisplayData, "fit") ?? "contain");
		var iconZoom = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "zoom") ?? 100);
		var iconOffsetX = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "offsetX") ?? 0);
		var iconOffsetY = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "offsetY") ?? 0);
		var iconOpacity = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "opacity") ?? 100);
		var iconColor = new UiState<string>(WidgetConfigJson.ReadString(data, "iconColor") ?? string.Empty);
		var backgroundColor =
			new UiState<string>(WidgetConfigJson.ReadString(data, "backgroundColor") ?? string.Empty);
		var labelColor = new UiState<string>(WidgetConfigJson.ReadString(data, "labelColor") ?? string.Empty);
		var fontFaceId = new UiState<string>(WidgetConfigJson.ReadString(data, "fontFaceId") ?? string.Empty);
		var fontSize = new UiState<double>(WidgetConfigJson.ReadDouble(data, "fontSize") ?? 14);
		var textAlign = new UiState<string>(WidgetConfigJson.ReadString(data, "textAlign") ?? "center");
		var labelPosition = new UiState<string>(WidgetConfigJson.ReadString(data, "labelPosition") ?? "center");
		var rootBorder = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(rootBorder, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(rootBorder, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		// ---- Local, session-only UI state - never bound to a widget-data key ----------------------------

		var pendingOffer = new UiState<ProviderOffer?>(null);
		var offerUseState = new UiState<bool>(true);
		var offerUseIcon = new UiState<bool>(true);
		var pendingSwitch = new UiState<ProviderSwitch?>(null);
		var pendingStop = new UiState<string?>(null);
		var stateNotices = new UiState<IReadOnlyList<ProviderNotice>>([]);
		var iconNotices = new UiState<IReadOnlyList<ProviderNotice>>([]);
		var iconHasSnapshot = new UiAsyncState<bool?>(async cancellationToken =>
			{
				if (iconProviderState.Peek() is not { } provider ||
					FindBlock(provider.BlockId) is not { } block)
				{
					return null;
				}

				using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
					context.SessionToken);
				var result = await context.Probe
					.ProbeIconAsync(block.IntegrationId, block.ActionId, block.Parameters, linked.Token)
					.ConfigureAwait(false);

				return result.Outcome == ActionProbeOutcome.Ok ? result.Snapshot is not null : null;
			},
			null);

		// The snapshot and hand-offs are only touched inside a dispatch; the generations and offerInFlight are
		// also read and written by settle continuations, hence Volatile and Interlocked.
		var knownBlocks = BlocksById(flows.Peek());
		ActionButtonFlowBlockInfo? handoffOffer = null;
		ActionButtonFlowBlockInfo? handoffRefresh = null;
		string? offerInFlight = null;
		var handoffIconReload = false;
		var iconGeneration = 0;
		var offerGeneration = 0;
		var refreshGeneration = 0;


		// ---- mutation helpers ----------------------------------------------------------------------------

		List<ActionButtonStateEntry> DefaultStatePair()
		{
			JsonObject Appearance(string background)
			{
				var appearance = new JsonObject();

				if (!string.IsNullOrEmpty(label.Value))
				{
					appearance["label"] = label.Value;
				}

				appearance["backgroundColor"] = background;

				return appearance;
			}

			return
			[
				new ActionButtonStateEntry(ActionButtonStateModel.DefaultOffStateId, "Off", Appearance("#ef4444")),
				new ActionButtonStateEntry(ActionButtonStateModel.DefaultOnStateId, "On", Appearance("#16a34a")),
			];
		}

		void EnableStateMode()
		{
			stateMode.Value = true;

			// Turning state mode off and on again must not lose the appearances: a dormant states list
			// is kept in the stored data, so it is read back here rather than reseeded. Only a button
			// that has genuinely never had states gets the default pair.
			if (states.Value.Count == 0)
			{
				states.Value = DefaultStatePair();
				activeStoredStateId.Value = ActionButtonStateModel.DefaultOffStateId;
			}

			// Each state owns its background from here on, so the single-state cell is cleared. Note this
			// clears the cell, not the stored key: the root appearance leaves the tree in state mode, and
			// composition only writes the keys the tree names, so a key whose node is gone keeps its stored
			// value. Naming it from a node that stays behind is not open either - that node would have to
			// take over the id the departing one still holds, which a patch cannot express in one step.
			backgroundColor.Value = string.Empty;
		}

		// Which state's appearance the properties form is currently showing - the state row's own choice
		// bound to activeStateId, falling back to the first state whenever the stored id names none of the
		// current states (unset, or the one it named was just removed). This is exactly the fallback
		// WidgetStateService itself uses to resolve a live face when no provider or mapping governs, so the
		// editor and the rendered button never disagree about what "no explicit choice yet" means.
		string EffectiveSelectedStateId()
			=> states.Value.Any(s => s.Id == activeStoredStateId.Value)
				? activeStoredStateId.Value
				: states.Value.Count > 0
					? states.Value[0].Id
					: string.Empty;

		void StashManualBackup()
		{
			manualBackup.Value = new ActionButtonManualStateBackup(states.Value.ToList(),
				mappingRules.Value.Count > 0
					? new ActionButtonStateMapping(mappingRules.Value.ToList(), fallbackStateId.Value)
					: null,
				activeStoredStateId.Value);
		}

		void RestoreManualStates(bool alsoDisableStateMode)
		{
			var backup = manualBackup.Value;

			states.Value = backup is { States.Count: > 0 } ? backup.States.ToList() : DefaultStatePair();
			mappingRules.Value = backup?.StateMapping?.Rules.ToList() ?? [];
			fallbackStateId.Value = backup?.StateMapping?.FallbackStateId ?? string.Empty;
			activeStoredStateId.Value = backup?.ActiveStateId ?? string.Empty;
			stateProvider.Value = null;
			manualBackup.Value = null;

			if (alsoDisableStateMode)
			{
				stateMode.Value = false;
			}
		}

		void ApplyEnableStateProvider(ActionButtonFlowBlockInfo block, ActionStateSnapshot snapshot)
		{
			var switching = stateProvider.Value is not null;

			if (!switching)
			{
				StashManualBackup();
				mappingRules.Value = [];
				fallbackStateId.Value = string.Empty;
			}

			var data = new JsonObject
			{
				["stateMode"] = true,
				["states"] = JsonNode.Parse(SerializeStates(states.Value).GetRawText()),
				// Written before adoption: AdoptProviderStates only fills the cache of a provider it can see.
				["stateProvider"] = ProviderJson(new ActionButtonStateProvider(block.Id,
					block.IntegrationId,
					block.ActionId,
					ActionLabelOf(block),
					[])),
			};
			ActionButtonStateJson.AdoptProviderStates(data, snapshot.States, context.Localization, context.Culture);
			var adopted = ActionButtonStateModel.Read(data);

			states.Value = adopted.States.ToList();
			stateProvider.Value = adopted.StateProvider;
			stateMode.Value = true;

			if (!switching || states.Value.All(s => s.Id != activeStoredStateId.Value))
			{
				activeStoredStateId.Value = states.Value.Count > 0 ? states.Value[0].Id : string.Empty;
			}
		}

		void AdoptIconProvider(ActionButtonFlowBlockInfo block)
		{
			iconProviderState.Value = new ActionButtonIconProvider(block.Id,
				block.IntegrationId,
				block.ActionId,
				ActionLabelOf(block));
			iconHasSnapshot.Reload();
		}

		void ClearIconProvider()
		{
			iconProviderState.Value = null;
			iconHasSnapshot.Reload();
		}

		void RemoveSelectedState()
		{
			var removedId = EffectiveSelectedStateId();

			if (states.Value.Count <= 1 || string.IsNullOrEmpty(removedId))
			{
				return;
			}

			states.Value = states.Value.Where(s => s.Id != removedId).ToList();
			mappingRules.Value = mappingRules.Value.Where(r => r.StateId != removedId).ToList();

			if (fallbackStateId.Value == removedId)
			{
				fallbackStateId.Value = states.Value.Count > 0 ? states.Value[0].Id : string.Empty;
			}

			if (activeStoredStateId.Value == removedId)
			{
				activeStoredStateId.Value = string.Empty;
			}
		}

		ActionButtonFlowBlockInfo? FindBlock(string blockId)
			=> ActionButtonFlowBlocks.Enumerate(flows.Peek()).FirstOrDefault(b => b.Id == blockId);

		bool IsStateCapable(ActionButtonFlowBlockInfo block)
			=> !block.Disabled && context.Probe.IsStateProvider(block.IntegrationId, block.ActionId);

		bool IsIconCapable(ActionButtonFlowBlockInfo block)
			=> !block.Disabled && context.Probe.IsIconProvider(block.IntegrationId, block.ActionId);

		string ActionLabelOf(ActionButtonFlowBlockInfo block)
			=> block.Label.Length > 0
				? block.Label
				: integrations.FindAction(block.IntegrationId, block.ActionId) is { } action
					? context.Resolve(action.Name)
					: block.ActionId;

		string IntegrationNameOf(string? integrationId)
			=> integrations.Integrations.FirstOrDefault(i => i.Id == integrationId) is { } integration
				? context.Resolve(integration.Name)
				: integrationId ?? string.Empty;

		string StateNamesOf(ActionStateSnapshot snapshot)
			=> string.Join(", ", snapshot.States.Select(state => context.Resolve(state.Label)));

		void Notify(UiState<IReadOnlyList<ProviderNotice>> target, string severity, params LocalizedString[] texts)
			=> target.Value = [.. texts.Select(text => new ProviderNotice(Guid.NewGuid().ToString("N"), text, severity))];

		void NotifyProbeFailure(ActionProbeOutcome outcome)
			=> Notify(stateNotices,
				"warning",
				outcome == ActionProbeOutcome.Timeout
					? AppStrings.Errors.Actions.ProviderTimeout()
					: AppStrings.Widgets.Editor.ProviderProbeFailed());

		void RepairMissingProviders(IReadOnlyDictionary<string, ActionButtonFlowBlockInfo> blocks)
		{
			if (stateProvider.Value is { } provider && !blocks.ContainsKey(provider.BlockId))
			{
				var hadBackup = manualBackup.Value is not null;

				if (hadBackup)
				{
					RestoreManualStates(false);
					Notify(stateNotices,
						"info",
						AppStrings.Widgets.Editor.ProviderActionRemoved(),
						AppStrings.Widgets.Editor.PreviousStateRestored());
				}
				else
				{
					stateProvider.Value = null;
					Notify(stateNotices, "info", AppStrings.Widgets.Editor.ProviderActionRemoved());
				}
			}

			if (iconProviderState.Value is { } iconProvider && !blocks.ContainsKey(iconProvider.BlockId))
			{
				ClearIconProvider();
				Notify(iconNotices, "info", AppStrings.Widgets.Editor.IconProviderActionRemoved());
			}

			if (pendingOffer.Value is { } offer && !blocks.ContainsKey(offer.Block.Id))
			{
				pendingOffer.Value = null;
			}

			if (pendingSwitch.Value is { } request && !blocks.ContainsKey(request.Block.Id))
			{
				pendingSwitch.Value = null;
			}
		}

		void OnFlowsChanged()
		{
			var current = BlocksById(flows.Value);

			RepairMissingProviders(current);

			var added = current.Values
				.Where(b => !knownBlocks.ContainsKey(b.Id) && (IsStateCapable(b) || IsIconCapable(b)))
				.ToList();

			if (added.Count > 0)
			{
				handoffOffer = added[^1];
				Interlocked.Increment(ref offerGeneration);
			}

			var offeredId = pendingOffer.Value?.Block.Id ?? Volatile.Read(ref offerInFlight);

			if (handoffOffer is null &&
				offeredId is not null &&
				current.TryGetValue(offeredId, out var offered) &&
				knownBlocks.TryGetValue(offeredId, out var offeredBefore) &&
				offered.ParametersSignature != offeredBefore.ParametersSignature)
			{
				handoffOffer = offered;
				Interlocked.Increment(ref offerGeneration);
				pendingOffer.Value = null;
			}

			if (iconProviderState.Value is { } iconProvider &&
				current.TryGetValue(iconProvider.BlockId, out var iconNow) &&
				knownBlocks.TryGetValue(iconProvider.BlockId, out var iconBefore) &&
				iconNow.ParametersSignature != iconBefore.ParametersSignature)
			{
				handoffIconReload = true;
				Interlocked.Increment(ref iconGeneration);
			}

			if (stateProvider.Value is { } provider &&
				current.TryGetValue(provider.BlockId, out var now) &&
				knownBlocks.TryGetValue(provider.BlockId, out var before) &&
				now.ParametersSignature != before.ParametersSignature)
			{
				handoffRefresh = now;
				Interlocked.Increment(ref refreshGeneration);
			}

			knownBlocks = current;
		}

		async Task SettleAndProbeAsync()
		{
			// Read before the first await: the next dispatch may already overwrite the hand-off.
			var offerBlock = handoffOffer;
			var refreshBlock = handoffRefresh;
			var offerAt = Volatile.Read(ref offerGeneration);
			var refreshAt = Volatile.Read(ref refreshGeneration);
			var iconReload = handoffIconReload;
			var iconAt = Volatile.Read(ref iconGeneration);
			handoffOffer = null;
			handoffRefresh = null;
			handoffIconReload = false;

			if (offerBlock is not null)
			{
				Volatile.Write(ref offerInFlight, offerBlock.Id);
			}

			if (offerBlock is null && refreshBlock is null && !iconReload)
			{
				return;
			}

			try
			{
				await Task.Delay(ActionButtonConfigContext.SettleDelay, context.TimeProvider, context.SessionToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (iconReload && Volatile.Read(ref iconGeneration) == iconAt)
			{
				iconHasSnapshot.Reload();
			}

			if (refreshBlock is not null)
			{
				await RefreshProvidedStatesAsync(refreshBlock, refreshAt).ConfigureAwait(false);
			}

			if (offerBlock is not null)
			{
				await OfferAsync(offerBlock, offerAt).ConfigureAwait(false);

				if (Volatile.Read(ref offerGeneration) == offerAt)
				{
					Volatile.Write(ref offerInFlight, null);
				}
			}
		}

		async Task RefreshProvidedStatesAsync(ActionButtonFlowBlockInfo block, int generation)
		{
			if (Volatile.Read(ref refreshGeneration) != generation)
			{
				return;
			}

			var result = await context.Probe
				.ProbeStatesAsync(block.IntegrationId, block.ActionId, block.Parameters, context.SessionToken)
				.ConfigureAwait(false);

			using var batch = context.Batch();

			if (Volatile.Read(ref refreshGeneration) != generation ||
				stateProvider.Peek()?.BlockId != block.Id ||
				FindBlock(block.Id) is not { } current ||
				current.ParametersSignature != block.ParametersSignature ||
				context.SessionToken.IsCancellationRequested)
			{
				return;
			}

			switch (result)
			{
				case { Outcome: ActionProbeOutcome.Ok, Snapshot: { States.Count: > 0 } refreshed }:
					ApplyEnableStateProvider(current, refreshed);
					break;
				case { Outcome: ActionProbeOutcome.Ok }:
					Notify(stateNotices, "warning", AppStrings.Widgets.Editor.ProviderNoStatesYet());
					break;
				default:
					NotifyProbeFailure(result.Outcome);
					break;
			}
		}

		async Task OfferAsync(ActionButtonFlowBlockInfo block, int generation)
		{
			ActionStateSnapshot? snapshot = null;

			if (IsStateCapable(block) && stateProvider.Peek() is null)
			{
				var result = await context.Probe
					.ProbeStatesAsync(block.IntegrationId, block.ActionId, block.Parameters, context.SessionToken)
					.ConfigureAwait(false);

				if (result is { Outcome: ActionProbeOutcome.Ok, Snapshot: { States.Count: > 0 } probed })
				{
					snapshot = probed;
				}
			}

			var iconAnswers = false;

			if (IsIconCapable(block) && iconProviderState.Peek() is null)
			{
				var result = await context.Probe
					.ProbeIconAsync(block.IntegrationId, block.ActionId, block.Parameters, context.SessionToken)
					.ConfigureAwait(false);

				iconAnswers = result is { Outcome: ActionProbeOutcome.Ok, Snapshot: not null };
			}

			using var batch = context.Batch();

			if (Volatile.Read(ref offerGeneration) != generation ||
				context.SessionToken.IsCancellationRequested ||
				FindBlock(block.Id) is not { Disabled: false } current ||
				current.ParametersSignature != block.ParametersSignature)
			{
				return;
			}

			if (stateProvider.Peek() is not null)
			{
				snapshot = null;
			}

			var icon = iconAnswers && iconProviderState.Peek() is null && IsIconCapable(current);

			if (snapshot is null && !icon)
			{
				return;
			}

			offerUseState.Value = snapshot is not null;
			offerUseIcon.Value = icon;
			pendingOffer.Value = new ProviderOffer(current, snapshot, icon);
		}

		void AcceptOffer(bool useState, bool useIcon)
		{
			if (pendingOffer.Value is not { } offer)
			{
				return;
			}

			pendingOffer.Value = null;

			if (useState && offer.States is { } snapshot && stateProvider.Value is null)
			{
				ApplyEnableStateProvider(offer.Block, snapshot);
			}

			if (useIcon && offer.Icon && iconProviderState.Value is null)
			{
				AdoptIconProvider(offer.Block);
			}
		}

		async Task OnProvideAsync(UiEventData data)
		{
			if (data.Raw is not { ValueKind: JsonValueKind.Object } payload ||
				WidgetConfigJson.ReadString(payload, "capability") is not { } capability ||
				WidgetConfigJson.ReadString(payload, "blockId") is not { } blockId ||
				!payload.TryGetProperty("enabled", out var enabledElement) ||
				enabledElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
			{
				return;
			}

			var enabled = enabledElement.GetBoolean();
			var block = FindBlock(blockId);

			if (capability == "icon")
			{
				if (!enabled)
				{
					if (iconProviderState.Value?.BlockId == blockId)
					{
						pendingStop.Value = "icon";
					}

					return;
				}

				if (block is null || !IsIconCapable(block) || iconProviderState.Value?.BlockId == blockId)
				{
					return;
				}

				if (iconProviderState.Value is not null)
				{
					pendingSwitch.Value = new ProviderSwitch("icon", block, null);

					return;
				}

				AdoptIconProvider(block);

				return;
			}

			if (capability != "state")
			{
				return;
			}

			if (!enabled)
			{
				if (stateProvider.Value?.BlockId == blockId)
				{
					pendingStop.Value = "state";
				}

				return;
			}

			if (block is null || !IsStateCapable(block) || stateProvider.Value?.BlockId == blockId)
			{
				return;
			}

			var result = await context.Probe
				.ProbeStatesAsync(block.IntegrationId, block.ActionId, block.Parameters, context.SessionToken)
				.ConfigureAwait(false);

			using var batch = context.Batch();

			if (context.SessionToken.IsCancellationRequested ||
				FindBlock(blockId) is not { Disabled: false } current ||
				current.ParametersSignature != block.ParametersSignature ||
				stateProvider.Peek()?.BlockId == blockId)
			{
				return;
			}

			switch (result)
			{
				case { Outcome: ActionProbeOutcome.Ok, Snapshot: { States.Count: > 0 } offered }
					when stateProvider.Peek() is not null:
					pendingSwitch.Value = new ProviderSwitch("state", current, offered);
					break;
				case { Outcome: ActionProbeOutcome.Ok, Snapshot: { States.Count: > 0 } adopted }:
					ApplyEnableStateProvider(current, adopted);
					break;
				case { Outcome: ActionProbeOutcome.Ok }:
					Notify(stateNotices, "warning", AppStrings.Widgets.Editor.ProviderReturnedNoStates());
					break;
				default:
					NotifyProbeFailure(result.Outcome);
					break;
			}
		}

		void ConfirmSwitch()
		{
			if (pendingSwitch.Value is not { } request)
			{
				return;
			}

			pendingSwitch.Value = null;

			if (request is { Capability: "state", States: { } snapshot })
			{
				ApplyEnableStateProvider(request.Block, snapshot);
			}
			else if (request.Capability == "icon")
			{
				AdoptIconProvider(request.Block);
			}
		}

		RepairMissingProviders(knownBlocks);

		if (iconProviderState.Peek() is not null)
		{
			iconHasSnapshot.Reload();
		}

		// ---- appearance tabs - shared shape between the root and every per-state appearance -------------
		//
		// Label/Background/Border, exactly as the original appearance form's own segmented tabs split them.
		// Headings are chrome and live one level up (see the STATE/APPEARANCE UiHeadings in the assembly
		// below and inside BuildStateItem) - this builds only the tab strip and its three panels, so it can
		// serve both the root fields and a per-state UiObjectInput's fields, which have different bindings
		// but the identical shape.

		// The tab strip is chrome, so its key is the caller's to choose: chrome inside an array item shares
		// the enclosing structural scope, which would make the root strip and a state's strip one id.
		UiElement BuildAppearanceTabs(
			string key,
			UiBinding<string> labelBinding,
			UiBinding<string> fontFaceIdBinding,
			Func<string> currentFontFaceId,
			Action<string> setFontFaceId,
			UiBinding<double> fontSizeBinding,
			UiBinding<string> textAlignBinding,
			UiBinding<string> labelPositionBinding,
			UiBinding<string> labelColorBinding,
			UiBinding<string> backgroundColorBinding,
			UiBinding<UiIconReference> iconBinding,
			UiBinding<UiIconDisplay> iconDisplayBinding,
			UiBinding<string> iconColorBinding,
			UiObjectInput borderNode,
			Func<UiIconReference> currentIcon,
			Func<string> currentBackgroundColor,
			Func<string> currentIconColor)
			=> new UiTabs
			{
				Key = key,
				Children =
				[
					new UiTab
					{
						Key = "label-tab",
						Label = AppStrings.Widgets.Editor.Label(),
						Children =
						[
							new UiStringInput
							{
								Key = "label",
								Label = AppStrings.Widgets.Editor.Label(),
								Placeholder = AppStrings.Widgets.Editor.ButtonLabelPlaceholder(),
								// The one field on this form that takes variables, so LiteralOnly stays unset -
								// the renderer offers the {{ }} helper exactly where the original editor did.
								Multiline = true,
								Binding = labelBinding,
							},
							// Font, full width on its own line, then Style and Size sharing the next line about
							// 2:1 (issue #837) - exactly the original appearance form's linked Font/Style
							// selects plus its Size field, none of which this profile's vocabulary names as one
							// control. Family is not itself a stored key - only fontFaceId is - so it needs a
							// transient input (UiInput.Transient): it renders and raises change like every other
							// control, but the client's draft composer skips it entirely, and picking a family
							// here only ever resolves a real fontFaceId (that family's first face), mirroring
							// WidgetFontFaceControlComponent's own onFamilyChange. Style then offers only the
							// faces the selected family actually ships, recomputed reactively from fontFaceId
							// since family is derived from it rather than stored beside it.
							new UiChoiceInput
							{
								Key = "fontFamily",
								Transient = true,
								Label = AppStrings.Widgets.Appearance.Font.FontLabel(),
								// A face missing from the catalogue reports its raw id, so the Inherited option
								// still differs from it and choosing it actually clears the override.
								Binding = Bind.Custom(() =>
									{
										var faceId = currentFontFaceId();
										var family = FamilyOf(faceId);

										return family.Length == 0 ? faceId : family;
									},
									family =>
									{
										var faces = FacesForFamily(family);
										setFontFaceId(faces.Count > 0 ? faces[0].FaceId : string.Empty);
									}),
								Options = UiValue.From(() => (IReadOnlyList<UiOption>)
								[
									new UiOption
									{
										Value = string.Empty, Label = AppStrings.Forms.InheritableSetting.Inherited(),
									},
									.. fontFamilies.Select(family => UiOption.Of(family, family)),
								]),
							},
							new UiConfigStack
							{
								Key = "label-appearance-row",
								Direction = "horizontal",
								Wrap = false,
								Children =
								[
									new UiChoiceInput
									{
										Key = "fontFaceId",
										Label = AppStrings.Widgets.Appearance.Font.StyleLabel(),
										Binding = fontFaceIdBinding,
										Disabled = UiValue.From(() =>
											FacesForFamily(FamilyOf(currentFontFaceId())).Count == 0),
										Options = UiValue.From(() =>
											(IReadOnlyList<UiOption>)FacesForFamily(FamilyOf(currentFontFaceId()))
												.Select(face => UiOption.Of(face.FaceId, face.StyleName))
												.ToList()),
										RowWeight = 2,
									},
									new UiNumberInput
									{
										Key = "fontSize",
										Label = AppStrings.Widgets.Appearance.Font.SizeLabel(),
										Binding = fontSizeBinding,
										Min = 1,
										Max = 100,
										RowWeight = 1,
									},
								],
							},
							// Align and Position, one non-wrapping line, each a segmented choice whose options
							// carry an icon instead of text (issue #837) - the original editor's own icon
							// button groups (icon names recovered from the pre-#791
							// WidgetFontAppearanceControlComponent).
							new UiConfigStack
							{
								Key = "label-alignment-row",
								Direction = "horizontal",
								Wrap = false,
								Children =
								[
									new UiChoiceInput
									{
										Key = "textAlign",
										Segmented = true,
										Label = AppStrings.Widgets.Appearance.Font.AlignLabel(),
										Binding = textAlignBinding,
										RowWeight = 1,
										Options = UiValue.Of<IReadOnlyList<UiOption>>([
											UiOption.Of("left", AppStrings.Widgets.Appearance.Font.AlignLeft())
												with
												{
													Icon = "align-left"
												},
											UiOption.Of("center", AppStrings.Widgets.Appearance.Font.AlignCenter())
												with
												{
													Icon = "align-center"
												},
											UiOption.Of("right", AppStrings.Widgets.Appearance.Font.AlignRight())
												with
												{
													Icon = "align-right"
												},
										]),
									},
									new UiChoiceInput
									{
										Key = "labelPosition",
										Segmented = true,
										Label = AppStrings.Widgets.Appearance.Font.PositionLabel(),
										Binding = labelPositionBinding,
										RowWeight = 1,
										Options = UiValue.Of<IReadOnlyList<UiOption>>([
											UiOption.Of("top", AppStrings.Widgets.Appearance.Font.PositionTop())
												with
												{
													Icon = "align-top"
												},
											UiOption.Of("center", AppStrings.Widgets.Appearance.Font.PositionCenter())
												with
												{
													Icon = "align-middle"
												},
											UiOption.Of("bottom", AppStrings.Widgets.Appearance.Font.PositionBottom())
												with
												{
													Icon = "align-bottom"
												},
										]),
									},
								],
							},
							new UiColorInput
							{
								Key = "labelColor",
								Label = AppStrings.Widgets.Editor.LabelColor(),
								Binding = labelColorBinding,
								// A reset affordance ahead of the swatches (issue #837), returning to unset -
								// which is the button's own real default (a computed contrast colour), not a
								// literal one this tree would have to know - see "keep an unset colour unset".
								SupportsReset = true,
								DefaultValue = string.Empty,
							},
						],
					},
					new UiTab
					{
						Key = "background-tab",
						Label = AppStrings.Widgets.Editor.BackgroundTab(),
						Children =
						[
							new UiColorInput
							{
								Key = "backgroundColor",
								Label = AppStrings.Widgets.Editor.BackgroundColor(),
								Binding = backgroundColorBinding,
								// Same unset-means-default rule as the label colour above (issue #896): the
								// button's own default background is a theme surface, not a literal colour.
								SupportsReset = true,
								DefaultValue = string.Empty,
							},
							new UiIconReferenceInput
							{
								Key = "icon",
								Label = AppStrings.Widgets.Editor.Icon(),
								Binding = iconBinding,
								Disabled = UiValue.From(() => iconProviderState.Value is not null),
							},
							new UiWhen
							{
								Key = "icon-color-when",
								Condition = () => iconProviderState.Value is null && currentIcon() is not null,
								Content = () => new UiColorInput
								{
									Key = "iconColor",
									Label = AppStrings.Widgets.Editor.IconColor(),
									Binding = iconColorBinding,
									SupportsReset = true,
									DefaultValue = string.Empty,
								},
							},
							new UiWhen
							{
								Key = "icon-display-when",
								// Only while an icon is actually set - the original editor's own preview needs
								// something to frame, and offering the drag/zoom control over nothing invites a
								// gesture with no visible effect.
								Condition = () => iconProviderState.Value is null && currentIcon() is not null,
								Content = () => new UiIconDisplayInput
								{
									Key = "iconDisplay",
									Label = AppStrings.Widgets.Editor.IconDisplay(),
									Binding = iconDisplayBinding,
									Icon = UiValue.From(currentIcon),
									AspectRatio = aspectRatio,
									Background = UiValue.From(currentBackgroundColor),
									Tint = UiValue.From(currentIconColor),
								},
							},
						],
					},
					new UiTab
					{
						Key = "border-tab", Label = AppStrings.Widgets.Editor.Border(), Children = [borderNode]
					},
				],
			};

		// ---- state row - the compact "which state am I editing" controls --------------------------------
		//
		UiElement BuildStateRow() => new UiConfigStack
		{
			Key = "state-row",
			Direction = "horizontal",
			Wrap = false,
			Children =
			[
				new UiChoiceInput
				{
					Key = "activeStateId",
					HideLabel = true,
					RowWeight = 1,
					Label = UiText.FromLocalized(() =>
						states.Value.FirstOrDefault(s => s.Id == EffectiveSelectedStateId()) is { } selected
							? AppStrings.Widgets.Editor.StateBeingEdited(name: selected.Label)
							: AppStrings.Widgets.Editor.NoStates()),
					Binding = Bind.Custom(EffectiveSelectedStateId, value => activeStoredStateId.Value = value),
					// The state the button is on right now is marked in the list itself, so opening it
					// answers "which one is it doing" without reading anything else.
					Options = UiValue.From(()
						=> (IReadOnlyList<UiOption>)states.Value
							.Select(s => s.Id == liveStateId.Value
								? UiOption.Of(s.Id, s.Label) with { Badge = AppStrings.Widgets.Editor.Live() }
								: UiOption.Of(s.Id, s.Label))
							.ToList()),
				},
				new UiWhen
				{
					Key = "addState-when",
					Condition = () => stateProvider.Value is null && states.Value.Count < ActionButtonStateModel.MaxStates,
					Content = () => new UiConfigButton
					{
						Key = "addState",
						Label = AppStrings.Widgets.Editor.AddState(),
						Icon = "plus",
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Activate,
								() =>
								{
									var id = Guid.NewGuid().ToString("N");
									var newState = new ActionButtonStateEntry(id, NextStateLabel(states.Value), null);
									states.Value = [.. states.Value, newState];
									activeStoredStateId.Value = id;
								}),
						],
					},
				},
				new UiWhen
				{
					Key = "manageState-when",
					Condition = () => stateProvider.Value is null,
					Content = () => new UiConfigMenu
					{
						Key = "manageState",
						Label = AppStrings.Widgets.Editor.ManageThisState(),
						Icon = "dots-vertical",
						Children = ManageStateEntries(string.Empty),
						Fallback = new UiConfigStack
						{
							Key = "manageState-fallback",
							Direction = "horizontal",
							Wrap = false,
							Children = ManageStateEntries("-fallback"),
						},
					},
				},
			],
		};

		UiElement[] ManageStateEntries(string keySuffix) =>
		[
			new UiConfigButton
			{
				Key = $"renameState{keySuffix}",
				Label = MacroDeckStrings.Common.Rename(),
				Icon = "pencil",
				ConfirmTitle = MacroDeckStrings.Common.Rename(),
				ConfirmLabel = MacroDeckStrings.Common.Save(),
				PromptValue = UiValue.From(() =>
					states.Value.FirstOrDefault(s => s.Id == EffectiveSelectedStateId())?.Label ?? string.Empty),
				Placeholder = AppStrings.Widgets.Editor.StateNamePlaceholder(),
				Events = [UiEventHandler.On(UiConfigEvents.Activate, data => RenameSelectedState(data))],
			},
			new UiWhen
			{
				Key = $"deleteState-when{keySuffix}",
				Condition = () => states.Value.Count > 1,
				Content = () => new UiConfigButton
				{
					Key = $"deleteState{keySuffix}",
					Label = MacroDeckStrings.Common.Delete(),
					Icon = "trash",
					ConfirmTitle = AppStrings.Widgets.Editor.RemoveStateHeading(),
					ConfirmMessage = UiText.FromLocalized(() =>
						IsReferencedByMapping(EffectiveSelectedStateId())
							? AppStrings.Widgets.Editor.RemoveStateConfirm(
								name: states.Value.FirstOrDefault(s => s.Id == EffectiveSelectedStateId())?.Label ??
									string.Empty)
							: AppStrings.Dialogs.Confirm.DefaultMessage()),
					ConfirmLabel = MacroDeckStrings.Common.Remove(),
					ConfirmDanger = true,
					Events = [UiEventHandler.On(UiConfigEvents.Activate, RemoveSelectedState)],
				},
			},
		];

		bool IsReferencedByMapping(string stateId)
			=> fallbackStateId.Value == stateId || mappingRules.Value.Any(rule => rule.StateId == stateId);

		void RenameSelectedState(UiEventData data)
		{
			var stateId = EffectiveSelectedStateId();

			if (data.TryGetString(out var name) && name.Trim() is { Length: > 0 } trimmed && stateId.Length > 0)
			{
				states.Value = states.Value.Select(s => s.Id == stateId ? s with { Label = trimmed } : s).ToList();
			}
		}

		// The live state line (issue #837): "Currently <state>" under the state row, naming the state the
		// widget is actually showing right now - a small green dot (UiProse's Severity, "success") ahead
		// of otherwise-muted text. liveState is resolved host-side once, when the config session opens
		// (ActionButtonWidgetUiProvider), the same source WidgetStateService itself resolves a rendered
		// button's live face from.
		//
		// Only while that state is not the one being edited: the state list already badges the live one,
		// so with the two the same the line would say a second time what the row above it just said. The
		// case it exists for is the common one - styling one state while the button sits on another.
		UiElement BuildLiveStateLine() => new UiWhen
		{
			Key = "live-state-when",
			Condition = () =>
				!string.IsNullOrEmpty(liveStateId.Value) &&
				liveStateId.Value != EffectiveSelectedStateId() &&
				(states.Value.Count == 0 || states.Value.Any(s => s.Id == liveStateId.Value)),
			Content = () => new UiProse
			{
				Key = "live-state",
				Severity = "success",
				Text = UiText.FromLocalized(() => AppStrings.Widgets.Editor.CurrentlyState(name: liveStateLabel.Value)),
			},
		};

		// Says why Add went away (issue #673), since UiConfigButton has no disabled state to show instead.
		// At or above the limit, not exactly on it: a button predating the limit can hold far more.
		UiElement BuildStateLimitLine() => new UiWhen
		{
			Key = "state-limit-when",
			Condition = () => stateProvider.Value is null && states.Value.Count >= ActionButtonStateModel.MaxStates,
			Content = () => new UiProse
			{
				Key = "state-limit",
				Severity = "warning",
				Text = UiText.FromLocalized(()
					=> AppStrings.Widgets.Editor.MaxStatesReached(max: ActionButtonStateModel.MaxStates)),
			},
		};

		// ---- states array --------------------------------------------------------------------------------
		//
		// Every state contributes an item so the client's array reconciliation never drops one it is not
		// currently touching (config-draft.util.ts's reconcileArray/composeConfigDraft both rebuild the
		// array from exactly the item nodes present in the tree) - only the selected item additionally
		// carries the rename field and the appearance form, both scoped under states.<id> so their ids
		// compose as states.<id>.label and states.<id>.appearance.*.

		UiElement BuildStateItem(ActionButtonStateEntry state, string _)
			=> new UiObjectInput
			{
				Key = state.Id,
				Children =
				[
					new UiWhen
					{
						Key = $"selected-when-{state.Id}",
						// Structural, not VisibleWhen: which state is selected is the state row's own choice,
						// not a question about another field's rendered value.
						Condition = () => EffectiveSelectedStateId() == state.Id,
						Content = () => new UiFragment
						{
							Key = "selected",
							Children =
							[
								// Every chrome key below carries this state's id, and the root section's
								// counterparts are keyed apart again: chrome inside an array item composes
								// its id from the enclosing structural scope, not from the item, so two
								// items both keyed "appearance-heading" are one id. Only the selected item
								// draws any of it, so they never show together - but a patch inserts
								// before it removes, and adding a state (or switching state mode) puts the
								// arriving item's chrome in the tree while the departing one's is still
								// there, which is a duplicate id the renderer rejects the whole patch for.
								new UiHeading
								{
									Key = $"state-appearance-heading-{state.Id}",
									Text = AppStrings.Widgets.Editor.Appearance(),
								},
								BuildIconProviderSection(keySuffix: $"-{state.Id}"),
								BuildStateAppearance(state.Id),
							],
						},
					},
				],
			};

		UiObjectInput BuildStateAppearance(string stateId)
		{
			var iconBinding = StateIconBinding(states, stateId);
			var iconDisplayBinding = StateIconDisplayBinding(states, stateId);

			return new UiObjectInput
			{
				Key = "appearance",
				Children =
				[
					BuildAppearanceTabs(key: $"state-appearance-tabs-{stateId}",
						labelBinding: StateStringBinding(states, stateId, "label"),
						fontFaceIdBinding: StateStringBinding(states, stateId, "fontFaceId"),
						currentFontFaceId: () =>
							ReadAppearanceString(states.Value, stateId, "fontFaceId") ?? string.Empty,
						setFontFaceId: value =>
							MutateAppearance(states,
								stateId,
								appearance =>
								{
									if (value.Length == 0)
									{
										appearance.Remove("fontFaceId");
									}
									else
									{
										appearance["fontFaceId"] = value;
									}
								}),
						fontSizeBinding: StateDoubleBinding(states, stateId, "fontSize", 14),
						textAlignBinding: StateStringBinding(states, stateId, "textAlign", fallback: "center"),
						labelPositionBinding: StateStringBinding(states, stateId, "labelPosition", fallback: "center"),
						labelColorBinding: StateStringBinding(states, stateId, "labelColor"),
						backgroundColorBinding: StateStringBinding(states, stateId, "backgroundColor"),
						iconBinding: iconBinding,
						iconDisplayBinding: iconDisplayBinding,
						iconColorBinding: StateStringBinding(states, stateId, "iconColor"),
						// "off" is the style a state with no border of its own has, and naming it here is what
						// keeps the control from rendering with nothing selected - see StateStringBinding.
						borderNode: WidgetConfigFragments.Border(
							StateStringBinding(states, stateId, "border", "style", fallback: "off"),
							StateStringBinding(states, stateId, "border", "color")),
						currentIcon: () => ReadStateIcon(states.Value, stateId)!,
						currentBackgroundColor: () =>
							ReadAppearanceString(states.Value, stateId, "backgroundColor") ?? string.Empty,
						currentIconColor: () =>
							ReadAppearanceString(states.Value, stateId, "iconColor") ?? string.Empty),
				],
			};
		}

		UiElement BuildStatesArray() => new UiWhen
		{
			Key = "states-when",
			Condition = () => stateMode.Value,
			Content = () => new UiArrayInput
			{
				Key = "states",
				Binding = Bind.Custom(() => SerializeStates(states.Value),
					value => states.Value = DeserializeStates(value)),
				Children =
				[
					new UiRepeat<ActionButtonStateEntry>
					{
						Key = "stateItems",
						Items = UiValue.From(() => (IReadOnlyList<ActionButtonStateEntry>)states.Value),
						KeySelector = s => s.Id,
						Template = BuildStateItem,
					},
				],
			},
		};

		// ---- state row and states array's shared visibility ----------------------------------------------

		UiElement BuildStateRowSection() => new UiWhen
		{
			Key = "state-row-when",
			Condition = () => stateMode.Value,
			Content = () => new UiFragment
			{
				Key = "state-row-group",
				Children =
				[
					BuildStateRow(), BuildStateLimitLine(), BuildLiveStateLine()
				],
			},
		};

		// ---- state mapping ---------------------------------------------------------------------------------
		//
		// Named rather than rebuilt from primitives (issue #837), exactly like UiActionsListEditor names the
		// action-flow editor: the value is the whole mapping object, and the states it may route to travel
		// as this node's own States property since the node - not a client service - is what knows them.

		UiElement BuildStateMappingSection() => new UiWhen
		{
			Key = "stateMapping-when",
			Condition = () => stateMode.Value && stateProvider.Value is null,
			Content = () => new UiStateMappingEditorInput
			{
				Key = "stateMapping",
				Binding = Bind.Custom(() => SerializeMapping(mappingRules.Value, fallbackStateId.Value),
					value =>
					{
						var (rules, fallback) = DeserializeMapping(value);
						mappingRules.Value = rules;
						fallbackStateId.Value = fallback;
					}),
				States = UiValue.From(()
					=> (IReadOnlyList<UiOption>)states.Value.Select(s => UiOption.Of(s.Id, s.Label)).ToList()),
			},
		};

		// ---- cycle states on press ---------------------------------------------------------------------
		//
		// Offered exactly while this button's own state is the only thing that could move it: a provider
		// or a mapping is authoritative, and a press advance is refused for them anyway, so a toggle
		// promising otherwise would be a lie rather than a disabled control.

		UiElement BuildCycleStatesSection() => new UiWhen
		{
			Key = "cycleStatesOnPress-when",
			Condition = () => stateMode.Value && stateProvider.Value is null && mappingRules.Value.Count == 0,
			Content = () => new UiBooleanInput
			{
				Key = "cycleStatesOnPress",
				Label = AppStrings.Widgets.Editor.CycleStatesOnPress(),
				Description = AppStrings.Widgets.Editor.CycleStatesOnPressHint(),
				Binding = Bind.To(cycleStatesOnPress),
			},
		};

		// ---- state/icon provider banners --------------------------------------------------------------------

		// Each branch is its own UiWhen: a structural scope's Content only re-runs when its own Condition
		// changes, so a choice made inside Content would never react to the state it reads.
		UiElement BuildNotices(string key, UiState<IReadOnlyList<ProviderNotice>> notices) => new UiWhen
		{
			Key = $"{key}-when",
			Condition = () => notices.Value.Count > 0,
			Content = () => new UiFragment
			{
				Key = key,
				Children =
				[
					new UiRepeat<ProviderNotice>
					{
						Key = $"{key}-items",
						Items = UiValue.From(() => notices.Value),
						KeySelector = notice => notice.Id,
						Template = (notice, _) => new UiProse
						{
							Key = $"{key}-{notice.Id}", Severity = notice.Severity, Text = notice.Text,
						},
					},
					new UiConfigButton
					{
						Key = $"{key}-dismiss",
						Label = AppStrings.Feedback.Dismiss(),
						Events = [UiEventHandler.On(UiConfigEvents.Activate, () => notices.Value = [])],
					},
				],
			},
		};

		UiText ProviderName(string? actionLabel)
			=> string.IsNullOrEmpty(actionLabel) ? AppStrings.Widgets.Editor.AnAction() : actionLabel;

		UiStatus ProviderStatus(string key, string icon, UiText label, UiText value, UiText sentence, UiConfigButton stop)
			=> new()
			{
				Key = $"{key}Status",
				Icon = icon,
				Label = label,
				Value = value,
				Children = [stop],
				Fallback = new UiConfigStack
				{
					Key = $"{key}StatusFallback",
					Children =
					[
						new UiProse { Key = $"{key}StatusFallbackText", Text = sentence },
						stop with { Key = $"{stop.Key}Fallback", Icon = default },
					],
				},
			};

		UiElement BuildStateProviderSection() => new UiFragment
		{
			Key = "stateProvider-group",
			Children =
			[
				BuildNotices("stateProviderNotices", stateNotices),
				new UiWhen
				{
					Key = "stateProviderActive-when",
					Condition = () => stateMode.Value && stateProvider.Value is not null,
					Content = () => ProviderStatus("stateProvider",
						"zap",
						AppStrings.Widgets.Editor.ProvidedByPrefix(),
						UiText.Optional(() => ProviderName(stateProvider.Value?.ActionLabel)),
						UiText.FromLocalized(() => AppStrings.Widgets.Editor.StateProvidedBy(
							integration: IntegrationNameOf(stateProvider.Value?.IntegrationId),
							action: stateProvider.Value?.ActionLabel ?? string.Empty)),
						new UiConfigButton
						{
							Key = "removeStateProvider",
							Label = AppStrings.Widgets.Editor.StopUsingProviderAction(),
							Icon = "x",
							ConfirmTitle = AppStrings.Widgets.Editor.StopUsingProviderHeading(),
							ConfirmMessage = AppStrings.Widgets.Editor.RestoreManualStatesMessage(),
							ConfirmLabel = AppStrings.Widgets.Editor.StopUsingProviderConfirm(),
							ConfirmDanger = true,
							Events = [UiEventHandler.On(UiConfigEvents.Activate, () => RestoreManualStates(false))],
						}),
				},
			],
		};

		// keySuffix keys this section apart per call site (a state item passes its state's id, the root
		// appearance section passes nothing) - see BuildStateItem for why an array item's chrome has to.
		UiElement BuildIconProviderSection(string keySuffix = "") => new UiFragment
		{
			Key = $"iconProvider-group{keySuffix}",
			Children =
			[
				BuildNotices($"iconProviderNotices{keySuffix}", iconNotices),
				new UiWhen
				{
					Key = $"iconProviderActive-when{keySuffix}",
					Condition = () => iconProviderState.Value is not null,
					Content = () => new UiFragment
					{
						Key = $"iconProviderActive{keySuffix}",
						Children =
						[
							ProviderStatus($"iconProvider{keySuffix}",
								"image",
								AppStrings.Widgets.Editor.ProvidedByPrefix(),
								UiText.Optional(() => ProviderName(iconProviderState.Value?.ActionLabel)),
								UiText.FromLocalized(() => AppStrings.Widgets.Editor.IconProvidedByPrefix(
									integration: IntegrationNameOf(iconProviderState.Value?.IntegrationId),
									action: iconProviderState.Value?.ActionLabel ?? string.Empty)),
								new UiConfigButton
								{
									Key = $"removeIconProvider{keySuffix}",
									Label = AppStrings.Widgets.Editor.StopUsingIconProviderAction(),
									Icon = "x",
									ConfirmTitle = AppStrings.Widgets.Editor.StopUsingIconProviderHeading(),
									ConfirmMessage = AppStrings.Widgets.Editor.StopUsingIconProviderMessage(),
									ConfirmLabel = AppStrings.Widgets.Editor.StopUsingIconProviderConfirm(),
									ConfirmDanger = true,
									Events = [UiEventHandler.On(UiConfigEvents.Activate, ClearIconProvider)],
								}),
							new UiWhen
							{
								Key = $"iconProviderNoIcon-when{keySuffix}",
								Condition = () => iconHasSnapshot.Value == false,
								Content = () => new UiProse
								{
									Key = $"iconProviderNoIcon{keySuffix}",
									Severity = "warning",
									Text = AppStrings.Widgets.Editor.IconProviderNoIconYet(),
								},
							},
							new UiProse
							{
								Key = $"iconProviderPreviewNote{keySuffix}",
								Text = AppStrings.Widgets.Editor.IconProviderPreviewNote(),
							},
						],
					},
				},
			],
		};

		UiConfigButton AnswerButton(string key, UiText label, Action answer, bool danger = false) => new()
		{
			Key = key,
			Label = label,
			ConfirmDanger = danger,
			Events = [UiEventHandler.On(UiConfigEvents.Activate, answer)],
		};

		UiElement Dialog(string key, Func<bool> open, Func<UiText> title, Func<UiText> text, Action cancel,
			Func<IReadOnlyList<UiElement>> children)
			=> new UiWhen
			{
				Key = $"{key}-when",
				Condition = open,
				Content = () => new UiConfigDialog
				{
					Key = key,
					Title = title(),
					Text = text(),
					Events = [UiEventHandler.On(UiConfigEvents.Cancel, cancel)],
					Children = [.. children()],
				},
			};

		UiElement BuildDialogs() => new UiFragment
		{
			Key = "dialogs",
			Children =
			[
				Dialog("stateProviderOffer",
					() => pendingOffer.Value is { States: not null, Icon: false },
					() => stateMode.Value
						? AppStrings.Widgets.Editor.ProviderOwnStatesHeading()
						: AppStrings.Widgets.Editor.ProviderMultiStatesHeading(),
					() => stateMode.Value
						? AppStrings.Widgets.Editor.OfferProviderMessage(action: ActionLabelOf(pendingOffer.Value!.Block),
							names: StateNamesOf(pendingOffer.Value!.States!))
						: AppStrings.Widgets.Editor.EnableMultiStateMessage(action: ActionLabelOf(pendingOffer.Value!.Block),
							names: StateNamesOf(pendingOffer.Value!.States!)),
					() => pendingOffer.Value = null,
					() =>
					[
						AnswerButton("declineStateProvider", AppStrings.Feedback.Dismiss(), () => pendingOffer.Value = null),
						AnswerButton("acceptStateProvider",
							stateMode.Value
								? AppStrings.Widgets.Editor.UseProvidedStatesConfirm()
								: AppStrings.Widgets.Editor.TurnOnMultiStateConfirm(),
							() => AcceptOffer(true, false)),
					]),
				Dialog("combinedProviderOffer",
					() => pendingOffer.Value is { States: not null, Icon: true },
					() => AppStrings.Widgets.Editor.CombinedProviderHeading(),
					() => AppStrings.Widgets.Editor.CombinedProviderMessage(action: ActionLabelOf(pendingOffer.Value!.Block)),
					() => pendingOffer.Value = null,
					() =>
					[
						new UiBooleanInput
						{
							Key = "combinedProviderUseState",
							Transient = true,
							Label = AppStrings.Widgets.Editor.CombinedProviderStateCheckboxLabel(
								names: StateNamesOf(pendingOffer.Value!.States!)),
							Binding = Bind.To(offerUseState),
						},
						new UiBooleanInput
						{
							Key = "combinedProviderUseIcon",
							Transient = true,
							Label = AppStrings.Widgets.Editor.CombinedProviderIconCheckboxLabel(),
							Binding = Bind.To(offerUseIcon),
						},
						AnswerButton("declineCombinedProvider", AppStrings.Feedback.Dismiss(), () => pendingOffer.Value = null),
						AnswerButton("acceptCombinedProvider",
							AppStrings.Widgets.Editor.UseSelectedProvidersConfirm(),
							() => AcceptOffer(offerUseState.Value, offerUseIcon.Value)),
					]),
				Dialog("iconProviderOffer",
					() => pendingOffer.Value is { States: null, Icon: true },
					() => AppStrings.Dialogs.Confirm.DefaultHeading(),
					() => AppStrings.Widgets.Editor.OfferIconProviderMessage(action: ActionLabelOf(pendingOffer.Value!.Block)),
					() => pendingOffer.Value = null,
					() =>
					[
						AnswerButton("declineIconProvider", AppStrings.Feedback.Dismiss(), () => pendingOffer.Value = null),
						AnswerButton("acceptIconProvider", AppStrings.Widgets.Editor.UseProvidedIconConfirm(),
							() => AcceptOffer(false, true)),
					]),
				Dialog("providerSwitch",
					() => pendingSwitch.Value is not null,
					() => AppStrings.Dialogs.Confirm.DefaultHeading(),
					() => pendingSwitch.Value?.Capability == "icon"
						? AppStrings.Widgets.Editor.SwitchIconProviderMessage()
						: AppStrings.Widgets.Editor.SwitchProviderMessage(),
					() => pendingSwitch.Value = null,
					() =>
					[
						AnswerButton("providerSwitchDismiss", MacroDeckStrings.Common.Cancel(), () => pendingSwitch.Value = null),
						AnswerButton("providerSwitchConfirm", AppStrings.Widgets.Editor.SwitchProviderConfirm(), ConfirmSwitch),
					]),
				Dialog("stopStateProvider",
					() => pendingStop.Value == "state",
					() => AppStrings.Widgets.Editor.StopUsingProviderHeading(),
					() => AppStrings.Widgets.Editor.RestoreManualStatesMessage(),
					() => pendingStop.Value = null,
					() =>
					[
						AnswerButton("stopStateProviderCancel", MacroDeckStrings.Common.Cancel(), () => pendingStop.Value = null),
						AnswerButton("stopStateProviderConfirm",
							AppStrings.Widgets.Editor.StopUsingProviderConfirm(),
							() =>
							{
								pendingStop.Value = null;
								RestoreManualStates(false);
							},
							danger: true),
					]),
				Dialog("stopIconProvider",
					() => pendingStop.Value == "icon",
					() => AppStrings.Widgets.Editor.StopUsingIconProviderHeading(),
					() => AppStrings.Widgets.Editor.StopUsingIconProviderMessage(),
					() => pendingStop.Value = null,
					() =>
					[
						AnswerButton("stopIconProviderCancel", MacroDeckStrings.Common.Cancel(), () => pendingStop.Value = null),
						AnswerButton("stopIconProviderConfirm",
							AppStrings.Widgets.Editor.StopUsingIconProviderConfirm(),
							() =>
							{
								pendingStop.Value = null;
								ClearIconProvider();
							},
							danger: true),
					]),
			],
		};

		// ---- root (stateMode off) appearance ------------------------------------------------------------

		UiElement BuildRootAppearance() => new UiWhen
		{
			Key = "rootAppearance-when",
			Condition = () => !stateMode.Value,
			Content = () => new UiFragment
			{
				Key = "rootAppearance",
				Children =
				[
					new UiHeading { Key = "appearance-heading", Text = AppStrings.Widgets.Editor.Appearance() },
					BuildIconProviderSection(),
					BuildRootAppearanceTabs(),
				],
			},
		};

		UiElement BuildRootAppearanceTabs() => BuildAppearanceTabs(key: "appearance-tabs",
			labelBinding: Bind.To(label),
			fontFaceIdBinding: Bind.To(fontFaceId),
			currentFontFaceId: () => fontFaceId.Value,
			setFontFaceId: value => fontFaceId.Value = value,
			fontSizeBinding: Bind.To(fontSize),
			textAlignBinding: Bind.To(textAlign),
			labelPositionBinding: Bind.To(labelPosition),
			labelColorBinding: Bind.To(labelColor),
			backgroundColorBinding: Bind.To(backgroundColor),
			iconBinding: Bind.To(icon),
			iconDisplayBinding: Bind.Custom(() => new UiIconDisplay(iconFit.Value,
					iconZoom.Value,
					iconOffsetX.Value,
					iconOffsetY.Value,
					iconOpacity.Value),
				value =>
				{
					if (value.Fit is not null)
					{
						iconFit.Value = value.Fit;
					}

					if (value.Zoom is not null)
					{
						iconZoom.Value = value.Zoom.Value;
					}

					if (value.OffsetX is not null)
					{
						iconOffsetX.Value = value.OffsetX.Value;
					}

					if (value.OffsetY is not null)
					{
						iconOffsetY.Value = value.OffsetY.Value;
					}

					if (value.Opacity is not null)
					{
						iconOpacity.Value = value.Opacity.Value;
					}
				}),
			iconColorBinding: Bind.To(iconColor),
			borderNode: WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
			currentIcon: () => icon.Value!,
			currentBackgroundColor: () => backgroundColor.Value,
			currentIconColor: () => iconColor.Value);

		// Host-owned keys no control edits: a bound composite is authoritative in the client's draft, and a
		// sibling value stateMode never takes keeps it out of sight while it is still submitted.
		UiObjectInput PersistedNode(string key, Func<JsonNode?> read) => new()
		{
			Key = key,
			Binding = Bind.ReadOnly(UiValue.From(() => ToElement(read()))),
			VisibleWhen = new UiVisibleWhen { ParameterName = "stateMode", Values = ["persisted-only"] },
		};

		// ---- assembly -------------------------------------------------------------------------------------

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "state-heading", Text = AppStrings.Widgets.Editor.State() },
					new UiBooleanInput
					{
						Key = "stateMode",
						Segmented = true,
						FalseLabel = AppStrings.Widgets.Editor.SingleState(),
						TrueLabel = AppStrings.Widgets.Editor.MultiState(),
						Binding = Bind.Custom(() => stateMode.Value,
							enabled =>
							{
								if (enabled)
								{
									EnableStateMode();
								}
								else if (stateProvider.Value is not null)
								{
									RestoreManualStates(true);
								}
								else
								{
									stateMode.Value = false;
								}
							}),
					},
					PersistedNode("stateProvider", () => ProviderJson(stateProvider.Value)),
					PersistedNode("manualStateBackup", () => BackupJson(manualBackup.Value)),
					PersistedNode("iconProvider", () => IconProviderJson(iconProviderState.Value)),
					BuildDialogs(),
					BuildStateProviderSection(),
					BuildStateRowSection(),
					BuildStateMappingSection(),
					BuildCycleStatesSection(),
					BuildStatesArray(),
					BuildRootAppearance(),
				],
			},
			Editor = new UiWidgetEditor
			{
				Key = "editor",
				Children =
				[
					new UiActionsListEditor
					{
						Key = "flows",
						Binding = Bind.To(flows),
						// A placed widget's own actions can be run against it from the editor.
						CanRun = true,
						OffersStateProvider = true,
						OffersIconProvider = true,
						StateProviderBlockId = UiValue.From(() => stateProvider.Value?.BlockId ?? string.Empty),
						IconProviderBlockId = UiValue.From(() => iconProviderState.Value?.BlockId ?? string.Empty),
						// The synchronous diff must run before the settle step that consumes its hand-off.
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Change, OnFlowsChanged),
							UiEventHandler.OnAsync(UiConfigEvents.Change, _ => SettleAndProbeAsync()),
							UiEventHandler.OnAsync(UiConfigEvents.Provide, (data, _) => OnProvideAsync(data)),
						],
						// The draft state list: a "Set Button State" block inside these flows resolves its
						// options from stored widget data, which has not seen a state added in this session
						// until the widget is saved.
						States = UiValue.From(() => stateMode.Value
							? (IReadOnlyList<UiOption>)states.Value.Select(s => UiOption.Of(s.Id, s.Label)).ToList()
							: []),
						Triggers = UiValue.From(() =>
						{
							List<string> triggers =
							[
								WidgetTriggerTypes.ShortPress,
								WidgetTriggerTypes.LongPress,
								WidgetTriggerTypes.TouchStart,
								WidgetTriggerTypes.TouchEnd,
								WidgetTriggerTypes.DoublePress,
							];

							if (stateMode.Value)
							{
								triggers.Add(WidgetTriggerTypes.StateChange);
							}

							return (IReadOnlyList<string>)triggers;
						}),
					},
				],
			},
		};
	}

	// ---- bindings into the states list ---------------------------------------------------------------------

	/// <param name="fallback">What the control shows while the state stores nothing for this field. An
	/// enumerated control with no matching option renders with nothing selected, which reads as broken
	/// rather than as "unset" - so a field with an effective default names it here.</param>
	private static UiBinding<string> StateStringBinding(
		UiState<List<ActionButtonStateEntry>> states,
		string stateId,
		string field,
		string? nestedField = null,
		string fallback = "")
		=> Bind.Custom(() => ReadAppearanceString(states.Value, stateId, field, nestedField) ?? fallback,
			value => MutateAppearance(states,
				stateId,
				appearance =>
				{
					if (nestedField is null)
					{
						appearance[field] = value;

						return;
					}

					var nested = (JsonObject?)appearance[field] as JsonObject ?? new JsonObject();
					nested[nestedField] = value;
					appearance[field] = nested;
				}));

	private static UiBinding<double> StateDoubleBinding(
		UiState<List<ActionButtonStateEntry>> states,
		string stateId,
		string field,
		double fallback)
		=> Bind.Custom(() =>
			{
				var appearance = states.Value.FirstOrDefault(s => s.Id == stateId)?.Appearance;

				return appearance?[field] is JsonValue value && value.TryGetValue<double>(out var d) ? d : fallback;
			},
			value => MutateAppearance(states, stateId, appearance => appearance[field] = value));

	private static UiIconReference? ReadStateIcon(IReadOnlyList<ActionButtonStateEntry> states, string stateId)
	{
		var appearance = states.FirstOrDefault(s => s.Id == stateId)?.Appearance;
		var reference = WidgetIconReference.Read(appearance?["icon"], null);

		return reference is { } value ? new UiIconReference(value.Type, value.Reference) : null;
	}

	private static UiBinding<UiIconReference> StateIconBinding(UiState<List<ActionButtonStateEntry>> states,
		string stateId)
		=> Bind.Custom(() => ReadStateIcon(states.Value, stateId)!,
			value => MutateAppearance(states,
				stateId,
				appearance =>
				{
					if (value is null)
					{
						appearance.Remove("icon");

						return;
					}

					appearance["icon"] = new JsonObject { ["type"] = value.Type, ["reference"] = value.Reference };
				}));

	/// <summary>The per-state counterpart of the root <c>iconDisplay</c> binding built inline in
	/// <see cref="Build" />: one value carrying the whole framing object, read with the same fallbacks a
	/// freshly-created state would show and written field-by-field so a value that leaves a field out never
	/// clobbers what is already there - see <see cref="UiIconDisplay" />'s own remarks.</summary>
	private static UiBinding<UiIconDisplay> StateIconDisplayBinding(UiState<List<ActionButtonStateEntry>> states,
		string stateId)
	{
		double? Numeric(string field)
		{
			var display = states.Value.FirstOrDefault(s => s.Id == stateId)?.Appearance?["iconDisplay"] as JsonObject;

			return display?[field] is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;
		}

		return Bind.Custom(() => new UiIconDisplay(
				ReadAppearanceString(states.Value, stateId, "iconDisplay", "fit") ?? "contain",
				Numeric("zoom") ?? 100,
				Numeric("offsetX") ?? 0,
				Numeric("offsetY") ?? 0,
				Numeric("opacity") ?? 100),
			value => MutateIconDisplay(states,
				stateId,
				display =>
				{
					if (value.Fit is not null)
					{
						display["fit"] = value.Fit;
					}

					if (value.Zoom is not null)
					{
						display["zoom"] = value.Zoom.Value;
					}

					if (value.OffsetX is not null)
					{
						display["offsetX"] = value.OffsetX.Value;
					}

					if (value.OffsetY is not null)
					{
						display["offsetY"] = value.OffsetY.Value;
					}

					if (value.Opacity is not null)
					{
						display["opacity"] = value.Opacity.Value;
					}
				}));
	}

	private static void MutateIconDisplay(UiState<List<ActionButtonStateEntry>> states,
		string stateId,
		Action<JsonObject> mutate)
		=> MutateAppearance(states,
			stateId,
			appearance =>
			{
				var display = (JsonObject?)appearance["iconDisplay"] as JsonObject ?? new JsonObject();
				mutate(display);
				appearance["iconDisplay"] = display;
			});

	private static string? ReadAppearanceString(
		IReadOnlyList<ActionButtonStateEntry> states,
		string stateId,
		string field,
		string? nestedField = null)
	{
		var appearance = states.FirstOrDefault(s => s.Id == stateId)?.Appearance;

		if (nestedField is null)
		{
			return appearance?[field] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
		}

		return appearance?[field] is JsonObject nested &&
			nested[nestedField] is JsonValue nestedValue &&
			nestedValue.TryGetValue<string>(out var nestedString)
				? nestedString
				: null;
	}

	private static void MutateAppearance(UiState<List<ActionButtonStateEntry>> states,
		string stateId,
		Action<JsonObject> mutate)
	{
		states.Value = states.Value.Select(s =>
		{
			if (s.Id != stateId)
			{
				return s;
			}

			var appearance = (JsonObject?)s.Appearance?.DeepClone() ?? new JsonObject();
			mutate(appearance);

			return s with { Appearance = appearance };
		}).ToList();
	}

	// ---- state-mapping-editor value (de)serialization ---------------------------------------------------

	private static JsonElement SerializeMapping(IReadOnlyList<ActionButtonStateMappingRule> rules,
		string? fallbackStateId)
	{
		var array = new JsonArray();

		foreach (var rule in rules)
		{
			array.Add(new JsonObject
			{
				["id"] = rule.Id,
				["stateId"] = rule.StateId,
				["when"] = rule.When.ValueKind != JsonValueKind.Undefined
					? JsonNode.Parse(rule.When.GetRawText())
					: new JsonObject(),
			});
		}

		var mapping = new JsonObject { ["rules"] = array, ["fallbackStateId"] = fallbackStateId ?? string.Empty };

		return JsonSerializer.Deserialize<JsonElement>(mapping.ToJsonString());
	}

	private static (List<ActionButtonStateMappingRule> Rules, string FallbackStateId) DeserializeMapping(
		JsonElement value)
	{
		var rules = new List<ActionButtonStateMappingRule>();

		if (value.ValueKind != JsonValueKind.Object)
		{
			return (rules, string.Empty);
		}

		if (value.TryGetProperty("rules", out var rulesElement) && rulesElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in rulesElement.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object)
				{
					continue;
				}

				var id = WidgetConfigJson.ReadString(item, "id") ?? Guid.NewGuid().ToString("N");
				var stateId = WidgetConfigJson.ReadString(item, "stateId") ?? string.Empty;
				var when = item.TryGetProperty("when", out var whenElement)
					? whenElement
					: JsonSerializer.Deserialize<JsonElement>("{}");

				rules.Add(new ActionButtonStateMappingRule(id, stateId, when));
			}
		}

		return (rules, WidgetConfigJson.ReadString(value, "fallbackStateId") ?? string.Empty);
	}

	// ---- states array (de)serialization for the whole-array binding (drag reorder) ------------------------

	private static JsonElement SerializeStates(IReadOnlyList<ActionButtonStateEntry> entries)
	{
		var array = new JsonArray();

		foreach (var entry in entries)
		{
			var obj = new JsonObject { ["id"] = entry.Id, ["label"] = entry.Label };

			if (entry.Appearance is { Count: > 0 } appearance)
			{
				obj["appearance"] = appearance.DeepClone();
			}

			array.Add(obj);
		}

		return JsonSerializer.Deserialize<JsonElement>(array.ToJsonString());
	}

	private static List<ActionButtonStateEntry> DeserializeStates(JsonElement value)
	{
		var result = new List<ActionButtonStateEntry>();

		if (value.ValueKind != JsonValueKind.Array)
		{
			return result;
		}

		foreach (var item in value.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var id = WidgetConfigJson.ReadString(item, "id");

			if (string.IsNullOrEmpty(id))
			{
				continue;
			}

			var stateLabel = WidgetConfigJson.ReadString(item, "label") ?? id;
			var appearance = item.TryGetProperty("appearance", out var appearanceElement) &&
				appearanceElement.ValueKind == JsonValueKind.Object
					? JsonNode.Parse(appearanceElement.GetRawText()) as JsonObject
					: null;

			result.Add(new ActionButtonStateEntry(id, stateLabel, appearance));
		}

		return result;
	}

	private static string NextStateLabel(List<ActionButtonStateEntry> states)
	{
		var existingLabels = new HashSet<string>(states.Select(s => s.Label), StringComparer.Ordinal);
		var n = states.Count + 1;
		var candidate = $"State {n}";

		while (existingLabels.Contains(candidate))
		{
			n++;
			candidate = $"State {n}";
		}

		return candidate;
	}

	// Imported or hand-edited flows can repeat a block id; the first one wins, as ActionFlowJson.TryFindBlock does.
	private static Dictionary<string, ActionButtonFlowBlockInfo> BlocksById(JsonElement flows)
	{
		var blocks = new Dictionary<string, ActionButtonFlowBlockInfo>(StringComparer.Ordinal);

		foreach (var block in ActionButtonFlowBlocks.Enumerate(flows))
		{
			blocks.TryAdd(block.Id, block);
		}

		return blocks;
	}

	private static JsonElement ToElement(JsonNode? node)
		=> node is null
			? JsonSerializer.SerializeToElement<object?>(null)
			: JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());

	private static JsonObject? ProviderJson(ActionButtonStateProvider? provider)
		=> provider is null
			? null
			: new JsonObject
			{
				["blockId"] = provider.BlockId,
				["integrationId"] = provider.IntegrationId,
				["actionId"] = provider.ActionId,
				["actionLabel"] = provider.ActionLabel,
				["states"] = new JsonArray(provider.States
					.Select(state => (JsonNode)new JsonObject { ["id"] = state.Id, ["label"] = state.Label })
					.ToArray()),
			};

	private static JsonObject? IconProviderJson(ActionButtonIconProvider? provider)
		=> provider is null
			? null
			: new JsonObject
			{
				["blockId"] = provider.BlockId,
				["integrationId"] = provider.IntegrationId,
				["actionId"] = provider.ActionId,
				["actionLabel"] = provider.ActionLabel,
			};

	private static JsonObject? BackupJson(ActionButtonManualStateBackup? backup)
	{
		if (backup is null)
		{
			return null;
		}

		var result = new JsonObject { ["states"] = JsonNode.Parse(SerializeStates(backup.States).GetRawText()) };

		if (backup.StateMapping is { } mapping)
		{
			result["stateMapping"] = JsonNode.Parse(SerializeMapping(mapping.Rules, mapping.FallbackStateId).GetRawText());
		}

		if (backup.ActiveStateId is { Length: > 0 } activeStateId)
		{
			result["activeStateId"] = activeStateId;
		}

		return result;
	}

	private sealed record ProviderNotice(string Id, LocalizedString Text, string Severity);

	private sealed record ProviderOffer(ActionButtonFlowBlockInfo Block, ActionStateSnapshot? States, bool Icon);

	private sealed record ProviderSwitch(string Capability, ActionButtonFlowBlockInfo Block, ActionStateSnapshot? States);

	private static UiIconReference? ReadIcon(JsonElement data)
	{
		var iconNode = data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty("icon", out var iconElement) &&
			iconElement.ValueKind == JsonValueKind.Object
				? JsonNode.Parse(iconElement.GetRawText())
				: null;

		var legacyIconId = WidgetConfigJson.ReadString(data, "iconId");
		var reference = WidgetIconReference.Read(iconNode, legacyIconId);

		return reference is { } value ? new UiIconReference(value.Type, value.Reference) : null;
	}
}
