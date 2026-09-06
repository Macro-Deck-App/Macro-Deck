using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
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
///
/// <para>
/// The state machinery (state list, state mapping, state/icon providers) is genuinely new among the six
/// built-in widgets, so it gets more code than the other five combined - but it is still ordinary
/// configuration: every field is a real schema key, states are addressed by their own stable
/// <see cref="ActionButtonStateEntry.Id" /> and never by position (a <see cref="UiRepeat{TItem}" /> keyed
/// that way, per ADR 0050), and the two provider-adoption "offers" are a <see cref="UiBanner" /> plus
/// <see cref="UiConfigButton" />s rather than a modal dialog - the Angular editor's confirmation dialogs are
/// one renderer's way of asking, not part of the contract.
/// </para>
///
/// <para>
/// The state row (which state is being edited, adding one, deleting it) is rebuilt from existing
/// primitives instead: a dropdown-plus-menu picker is not a control this profile names, so a
/// <see cref="UiChoiceInput" /> bound to the schema's own <c>activeStateId</c>, plus an add and a delete
/// <see cref="UiConfigButton" />, are the honest equivalent. The rename field for the selected state has to
/// stay nested inside that state's own <c>states</c> array item rather than sit beside those buttons: the
/// client patches a widget's draft data by the tree position of the node an event fired on
/// (<c>config-draft.util.ts</c>), so a control addressing <c>states.&lt;id&gt;.label</c> has to actually be
/// declared under that scope. The same reasoning is why every state still contributes an item to the array
/// even though only the selected one carries a rename field and an appearance subtree: the client rebuilds
/// the array from exactly the item nodes present in the tree, so an item missing from it is an item silently
/// dropped from the saved data, not merely one this renderer chose not to draw.
/// </para>
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
		WidgetStateOption? liveState)
	{
		ArgumentNullException.ThrowIfNull(integrations);
		ArgumentNullException.ThrowIfNull(fonts);

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
		var liveStateLabel = new UiState<string>(liveState?.Label.Literal ?? string.Empty);

		var label = new UiState<string>(WidgetConfigJson.ReadString(data, "label") ?? string.Empty);
		var icon = new UiState<UiIconReference>(ReadIcon(data)!);
		var iconDisplayData = WidgetConfigJson.ReadObject(data, "iconDisplay");
		var iconFit = new UiState<string>(WidgetConfigJson.ReadString(iconDisplayData, "fit") ?? "contain");
		var iconZoom = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "zoom") ?? 100);
		var iconOffsetX = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "offsetX") ?? 0);
		var iconOffsetY = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "offsetY") ?? 0);
		var iconOpacity = new UiState<double>(WidgetConfigJson.ReadDouble(iconDisplayData, "opacity") ?? 100);
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
		//
		// Declining a provider/icon offer is remembered for the life of this session only - a fresh
		// session (reopening the editor) asks again, which is the same "re-offer" behaviour a fresh
		// mount of the Angular editor already has.
		var declinedStateBlocks = new UiState<HashSet<string>>(new HashSet<string>(StringComparer.Ordinal));
		var declinedIconBlocks = new UiState<HashSet<string>>(new HashSet<string>(StringComparer.Ordinal));

		// Whether the state row's "Manage this state" menu is open (issue #837): the DSL has no dropdown-menu
		// chrome, so this is the disclosure fallback the issue itself allows - it reveals the rename field
		// (nested inside the selected state's own array item, below) and the delete action together, closing
		// again on a second click of the same button.
		var stateManageOpen = new UiState<bool>(false);

		// ---- mutation helpers ----------------------------------------------------------------------------

		// The Off/On pair a button starts with the first time it enters state mode. "off" deliberately
		// carries no background of its own and falls through to the reader's accent, while "on" gets a
		// colour, so a button that has just gained states already reads as two visibly different faces.
		// Both inherit the caption the button had, so gaining states never blanks its label.
		List<ActionButtonStateEntry> DefaultStatePair()
		{
			JsonObject Appearance(string? background)
			{
				var appearance = new JsonObject();

				if (!string.IsNullOrEmpty(label.Value))
				{
					appearance["label"] = label.Value;
				}

				if (background is not null)
				{
					appearance["backgroundColor"] = background;
				}

				return appearance;
			}

			return
			[
				new ActionButtonStateEntry(ActionButtonStateModel.DefaultOffStateId, "Off", Appearance(null)),
				new ActionButtonStateEntry(ActionButtonStateModel.DefaultOnStateId, "On", Appearance("#ef4444")),
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
			StashManualBackup();
			states.Value = AdoptProvidedStates(states.Value, snapshot.States);
			mappingRules.Value = [];
			fallbackStateId.Value = string.Empty;
			stateProvider.Value = new ActionButtonStateProvider(block.Id,
				block.IntegrationId,
				block.ActionId,
				block.Label,
				snapshot.States.Select(s => new ActionButtonStateProviderOption(s.Id, DisplayLabel(s.Label))).ToList());
			stateMode.Value = true;
			// Not written while a provider is authoritative - the host resolves the live one instead.
			activeStoredStateId.Value = string.Empty;
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

		ActionButtonFlowBlockInfo? FindEligibleStateBlock()
		{
			if (stateProvider.Value is not null)
			{
				return null;
			}

			foreach (var block in ActionButtonFlowBlocks.Enumerate(flows.Value))
			{
				if (declinedStateBlocks.Value.Contains(block.Id) || !integrations.IsEnabled(block.IntegrationId))
				{
					continue;
				}

				if (integrations.FindAction(block.IntegrationId, block.ActionId) is IStateProviderActionDefinition)
				{
					return block;
				}
			}

			return null;
		}

		ActionButtonFlowBlockInfo? FindEligibleIconBlock()
		{
			if (iconProviderState.Value is not null)
			{
				return null;
			}

			foreach (var block in ActionButtonFlowBlocks.Enumerate(flows.Value))
			{
				if (declinedIconBlocks.Value.Contains(block.Id) || !integrations.IsEnabled(block.IntegrationId))
				{
					continue;
				}

				if (integrations.FindAction(block.IntegrationId, block.ActionId) is IIconProviderActionDefinition)
				{
					return block;
				}
			}

			return null;
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
			UiObjectInput borderNode,
			Func<UiIconReference> currentIcon,
			Func<string> currentBackgroundColor)
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
								Placeholder = AppStrings.Widgets.Appearance.Font.Default(),
								Binding = Bind.Custom(() => FamilyOf(currentFontFaceId()),
									family =>
									{
										var faces = FacesForFamily(family);
										setFontFaceId(faces.Count > 0 ? faces[0].FaceId : string.Empty);
									}),
								Options = UiValue.From(() => (IReadOnlyList<UiOption>)fontFamilies
									.Select(family => UiOption.Of(family, family))
									.ToList()),
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
										Placeholder = AppStrings.Widgets.Appearance.Font.Default(),
										Binding = fontFaceIdBinding,
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
							new UiWhen
							{
								Key = "icon-when",
								Condition = () => iconProviderState.Value is null,
								Content = () => new UiIconReferenceInput
								{
									Key = "icon", Label = AppStrings.Widgets.Editor.Icon(), Binding = iconBinding,
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
		// Reproduces the original dropdown-plus-menu picker from existing primitives (issue #837): a
		// choice bound to the schema's own activeStateId - the field WidgetStateService itself resolves a
		// manual live face from, so picking a state to style here also makes it the one the button shows
		// while no provider or mapping governs, exactly like the original picker's own live/edited
		// distinction collapses once there is only one selection to make - plus an Add button and a third
		// control standing in for the original's own "Manage this state" dropdown menu (rename and
		// delete). The DSL names no dropdown-menu chrome, so this is the disclosure fallback the issue
		// itself allows: the button toggles stateManageOpen, which reveals the rename field - nested on
		// the selected item inside the states array below, not here (a client patches a widget's draft
		// data structurally, per config-draft.util.ts, matching a changed node to its data path by tree
		// position, so a rename control has to live inside that state's own array item for an edit to land
		// on states.<id>.label rather than on some unrelated key of its own) - and the delete action,
		// built by BuildManageStateDisclosure below.
		// A single non-wrapping line (issue #837): the picker takes the remaining share once the two
		// buttons claim theirs, and its own caption is suppressed because the STATE heading immediately
		// above this row already names it - the picker's dynamic label (which state is being edited, or
		// that there are none yet) still carries the accessible name, just as an aria-label rather than a
		// visible caption.
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
				new UiConfigButton
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
				new UiConfigButton
				{
					Key = "manageState",
					Label = AppStrings.Widgets.Editor.ManageThisState(),
					Icon = "dots-vertical",
					Events =
					[
						UiEventHandler.On(UiConfigEvents.Activate, () => stateManageOpen.Value = !stateManageOpen.Value)
					],
				},
			],
		};

		// The "Manage this state" disclosure's own content (issue #837): only Delete lives here - Rename is
		// the state item's own label field, nested inside the states array for the structural reason
		// BuildStateRow's remarks give, and stateManageOpen also gates that field's visibility (see
		// BuildStateItem). A stateful button keeps at least one state, so Delete stays hidden rather than
		// disabled below two - UiConfigButton carries no Disabled property of its own (that lives on
		// UiInput).
		UiElement BuildManageStateDisclosure() => new UiWhen
		{
			Key = "manageState-when",
			Condition = () => stateManageOpen.Value,
			Content = () => new UiWhen
			{
				Key = "deleteState-when",
				Condition = () => states.Value.Count > 1,
				Content = () => new UiConfigButton
				{
					Key = "deleteState",
					Label = MacroDeckStrings.Common.Delete(),
					Icon = "trash",
					Events =
					[
						UiEventHandler.On(UiConfigEvents.Activate,
							() =>
							{
								RemoveSelectedState();
								stateManageOpen.Value = false;
							}),
					],
				},
			},
		};

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
				!string.IsNullOrEmpty(liveStateId.Value) && liveStateId.Value != EffectiveSelectedStateId(),
			Content = () => new UiProse
			{
				Key = "live-state",
				Severity = "success",
				Text = UiText.FromLocalized(() => AppStrings.Widgets.Editor.CurrentlyState(name: liveStateLabel.Value)),
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
								// Rename lives here, nested inside this state's own array item, rather than in
								// the state row above - see BuildStateRow's remarks for why a control has to
								// sit under states.<id> to land on states.<id>.label. Revealed by the row's
								// "Manage this state" button rather than always shown (issue #837): the
								// original's own menu-driven rename, not a permanent field.
								new UiWhen
								{
									Key = "rename-when",
									Condition = () => stateManageOpen.Value,
									Content = () => new UiStringInput
									{
										Key = "label",
										Placeholder = AppStrings.Widgets.Editor.StateNamePlaceholder(),
										Binding = StateLabelBinding(states, state.Id),
									},
								},
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
							MutateAppearance(states, stateId, appearance => appearance["fontFaceId"] = value),
						fontSizeBinding: StateDoubleBinding(states, stateId, "fontSize", 14),
						textAlignBinding: StateStringBinding(states, stateId, "textAlign", fallback: "center"),
						labelPositionBinding: StateStringBinding(states, stateId, "labelPosition", fallback: "center"),
						labelColorBinding: StateStringBinding(states, stateId, "labelColor"),
						backgroundColorBinding: StateStringBinding(states, stateId, "backgroundColor"),
						iconBinding: iconBinding,
						iconDisplayBinding: iconDisplayBinding,
						// "off" is the style a state with no border of its own has, and naming it here is what
						// keeps the control from rendering with nothing selected - see StateStringBinding.
						borderNode: WidgetConfigFragments.Border(
							StateStringBinding(states, stateId, "border", "style", fallback: "off"),
							StateStringBinding(states, stateId, "border", "color")),
						currentIcon: () => ReadStateIcon(states.Value, stateId)!,
						currentBackgroundColor: () =>
							ReadAppearanceString(states.Value, stateId, "backgroundColor") ?? string.Empty),
				],
			};
		}

		UiElement BuildStatesArray() => new UiWhen
		{
			Key = "states-when",
			Condition = () => stateMode.Value && stateProvider.Value is null,
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
			Condition = () => stateMode.Value && stateProvider.Value is null,
			Content = () => new UiFragment
			{
				Key = "state-row-group",
				Children = [BuildStateRow(), BuildManageStateDisclosure(), BuildLiveStateLine()],
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

		// Two sibling UiWhens rather than one UiWhen branching inside Content(): a structural scope's
		// Content only re-runs when its own Condition's boolean result changes, so a decision that lives
		// entirely inside Content (as an earlier draft of this method had it) never re-evaluates when the
		// state it reads changes but the outer Condition does not - the active/offer choice has to be the
		// Condition itself for each branch to react on its own.
		UiElement BuildStateProviderSection() => new UiFragment
		{
			Key = "stateProvider-group",
			Children =
			[
				new UiWhen
				{
					Key = "stateProviderActive-when",
					Condition = () => stateMode.Value && stateProvider.Value is not null,
					Content = () =>
					{
						var provider = stateProvider.Value!;

						return new UiFragment
						{
							Key = "stateProviderActive",
							Children =
							[
								new UiBanner
								{
									Key = "stateProviderBanner",
									Severity = "info",
									// No state-specific "controlled by" wording exists with placeholders - the
									// icon provider's carries the same "an action governs this" meaning and
									// already has both.
									Text = AppStrings.Widgets.Editor.IconProvidedByPrefix(
										integration: provider.IntegrationId ?? string.Empty,
										action: provider.ActionLabel ?? string.Empty),
								},
								new UiConfigButton
								{
									Key = "removeStateProvider",
									Label = AppStrings.Widgets.Editor.StopUsingProviderAction(),
									Events =
									[
										UiEventHandler.On(UiConfigEvents.Activate, () => RestoreManualStates(false))
									],
								},
							],
						};
					},
				},
				new UiWhen
				{
					Key = "stateProviderOffer-when",
					Condition = () =>
						stateMode.Value && stateProvider.Value is null && FindEligibleStateBlock() is not null,
					Content = () =>
					{
						var block = FindEligibleStateBlock()!;

						return new UiFragment
						{
							Key = "stateProviderOffer",
							Children =
							[
								new UiBanner
								{
									Key = "stateProviderOfferBanner",
									Severity = "info",
									Text = AppStrings.Widgets.Editor.ProviderMultiStatesHeading(),
								},
								new UiConfigButton
								{
									Key = "acceptStateProvider",
									Label = AppStrings.Widgets.Editor.UseProvidedStatesConfirm(),
									Events =
									[
										UiEventHandler.OnAsync(UiConfigEvents.Activate,
											async (_, cancellationToken) =>
											{
												if (integrations.FindAction(block.IntegrationId, block.ActionId) is not
													IStateProviderActionDefinition stateProviderAction)
												{
													return;
												}

												var snapshot = await stateProviderAction
													.GetActionStateAsync(block.Parameters, cancellationToken)
													.ConfigureAwait(false);

												if (snapshot is not { States.Count: > 0 })
												{
													return;
												}

												ApplyEnableStateProvider(block, snapshot);
											}),
									],
								},
								new UiConfigButton
								{
									Key = "declineStateProvider",
									Label = AppStrings.Feedback.Dismiss(),
									Events =
									[
										UiEventHandler.On(UiConfigEvents.Activate,
											() => declinedStateBlocks.Value = [.. declinedStateBlocks.Value, block.Id]),
									],
								},
							],
						};
					},
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
				new UiWhen
				{
					Key = $"iconProviderActive-when{keySuffix}",
					Condition = () => iconProviderState.Value is not null,
					Content = () =>
					{
						var iconProviderValue = iconProviderState.Value!;

						return new UiFragment
						{
							Key = $"iconProviderActive{keySuffix}",
							Children =
							[
								new UiBanner
								{
									Key = $"iconProviderBanner{keySuffix}",
									Severity = "info",
									Text = AppStrings.Widgets.Editor.IconProvidedByPrefix(
										integration: iconProviderValue.IntegrationId ?? string.Empty,
										action: iconProviderValue.ActionLabel ?? string.Empty),
								},
								new UiConfigButton
								{
									Key = $"removeIconProvider{keySuffix}",
									Label = AppStrings.Widgets.Editor.StopUsingIconProviderAction(),
									Events =
									[
										UiEventHandler.On(UiConfigEvents.Activate, () => iconProviderState.Value = null)
									],
								},
							],
						};
					},
				},
				new UiWhen
				{
					Key = $"iconProviderOffer-when{keySuffix}",
					Condition = () => iconProviderState.Value is null && FindEligibleIconBlock() is not null,
					Content = () =>
					{
						var block = FindEligibleIconBlock()!;

						return new UiFragment
						{
							Key = $"iconProviderOffer{keySuffix}",
							Children =
							[
								new UiBanner
								{
									Key = $"iconProviderOfferBanner{keySuffix}",
									Severity = "info",
									Text = AppStrings.Widgets.Editor.OfferIconProviderMessage(action: block.Label),
								},
								new UiConfigButton
								{
									Key = $"acceptIconProvider{keySuffix}",
									Label = AppStrings.Widgets.Editor.UseProvidedStatesConfirm(),
									Events =
									[
										UiEventHandler.On(UiConfigEvents.Activate,
											() =>
											{
												iconProviderState.Value =
													new ActionButtonIconProvider(block.Id,
														block.IntegrationId,
														block.ActionId,
														block.Label);
											}),
									],
								},
								new UiConfigButton
								{
									Key = $"declineIconProvider{keySuffix}",
									Label = AppStrings.Feedback.Dismiss(),
									Events =
									[
										UiEventHandler.On(UiConfigEvents.Activate,
											() => declinedIconBlocks.Value = [.. declinedIconBlocks.Value, block.Id]),
									],
								},
							],
						};
					},
				},
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
			borderNode: WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
			currentIcon: () => icon.Value!,
			currentBackgroundColor: () => backgroundColor.Value);

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
								else
								{
									stateMode.Value = false;
								}
							}),
					},
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

	private static UiBinding<string> StateLabelBinding(UiState<List<ActionButtonStateEntry>> states, string stateId)
		=> Bind.Custom(() => states.Value.FirstOrDefault(s => s.Id == stateId)?.Label ?? string.Empty,
			value => states.Value = states.Value.Select(s => s.Id == stateId ? s with { Label = value } : s).ToList());

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

	// ---- provider adoption helpers --------------------------------------------------------------------------

	/// <summary>Re-adopts a provider's declared state set: a state id that already exists keeps the
	/// appearance the user configured for it (a provider never overwrites hand-authored styling - see
	/// <see cref="ActionStateAppearance" />), and a newly-declared id seeds from the provider's own default
	/// appearance. A previously-adopted id the provider no longer declares is dropped.</summary>
	private static List<ActionButtonStateEntry> AdoptProvidedStates(
		IReadOnlyList<ActionButtonStateEntry> existing,
		IReadOnlyList<ActionStateDefinition> declared)
	{
		var byId = existing.ToDictionary(s => s.Id, StringComparer.Ordinal);
		var result = new List<ActionButtonStateEntry>(declared.Count);

		foreach (var definition in declared)
		{
			if (byId.TryGetValue(definition.Id, out var current))
			{
				result.Add(current with { Label = DisplayLabel(definition.Label) });

				continue;
			}

			JsonObject? appearance = null;

			if (definition.DefaultAppearance is { } defaultAppearance)
			{
				appearance = [];

				if (defaultAppearance.Label is { } appearanceLabel)
				{
					appearance["label"] = appearanceLabel;
				}

				if (defaultAppearance.BackgroundColor is { } backgroundColor)
				{
					appearance["backgroundColor"] = backgroundColor;
				}

				if (defaultAppearance.LabelColor is { } labelColor)
				{
					appearance["labelColor"] = labelColor;
				}

				if (defaultAppearance.IconId is { } iconId)
				{
					appearance["icon"] = WidgetIconReference.IconPack(iconId).ToJson();
				}
			}

			result.Add(new ActionButtonStateEntry(definition.Id, DisplayLabel(definition.Label), appearance));
		}

		return result;
	}

	/// <summary>A best-effort plain string for a provider-declared label: the literal text when the action
	/// declared one directly, which is the overwhelming common case, and the id otherwise - this
	/// configuration session has no per-viewer culture to resolve a <see cref="LocalizedText" /> reference
	/// against, unlike a rendered widget view.</summary>
	private static string DisplayLabel(LocalizedText text) => text.Literal ?? string.Empty;

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
