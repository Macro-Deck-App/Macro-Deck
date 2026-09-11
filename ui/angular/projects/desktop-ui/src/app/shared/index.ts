// Angular adapter - host connection
export { ApiService, type ConnectionState, type WidgetTypeInfo } from './transport/api.service';
export { HOST_URL_RESOLVER } from './transport/host-url';

// Angular adapter - application state and host-backed services
export { ActionExecutionService } from './services/action-execution.service';
export { AUTH_REQUIRED_SCOPE, AuthService, type AuthState } from './services/auth.service';
export {
  DECK_DRAG_THRESHOLD_PX,
  type DeckDragMode,
  DeckDragService,
} from './services/deck-drag.service';
export { DeckMarqueeService } from './services/deck-marquee.service';
export { CLIENT_TYPE, DeviceIdentityService } from './services/device-identity.service';
export { DismissibleHintService } from './services/dismissible-hint.service';
export { FolderViewService } from './services/folder-view.service';
export { FolderService } from './services/folder.service';
export { IconImageService } from './services/icon-image.service';
export { IconPrefetchService } from './services/icon-prefetch.service';
export { KeyRingService } from './services/key-ring.service';
export { ProfileService } from './services/profile.service';
export { DEFAULT_ACCENT_COLOR, ThemeService, UiFontService } from './services/theme.service';
export { ToastService } from './services/toast.service';
export {
  type UiSessionHandle,
  type UiSessionOpenRequest,
  type UiSessionRejection,
  UiSessionService,
} from './services/ui-session.service';
export { VariableService } from './services/variable.service';
export { WidgetClipboardService } from './services/widget-clipboard.service';
export { WidgetRegistryService } from './services/widget-registry.service';
export { WidgetTypeCatalogService } from './services/widget-type-catalog.service';

// Angular adapter - components
export { LoginFormComponent } from './components/auth/login-form/login-form.component';
export { ButtonGroupComponent } from './components/button/button-group.component';
export { ButtonComponent } from './components/button/button.component';
export { ErrorBannerComponent } from './components/feedback/error-banner/error-banner.component';
export { ToastHostComponent } from './components/feedback/toast-host/toast-host.component';
export { FolderViewHostComponent } from './components/folder-view/folder-view-host.component';
export { CheckboxComponent } from './components/forms/checkbox/checkbox.component';
export { InputComponent } from './components/forms/input/input.component';
export { ToggleSwitchComponent } from './components/forms/toggle-switch/toggle-switch.component';
export {
  ContextMenuComponent,
  type ContextMenuItem,
} from './components/overlay/context-menu/context-menu.component';
export { ModalComponent, dismissModal } from './components/overlay/modal/modal.component';
export { NoticeModalComponent } from './components/overlay/notice-modal/notice-modal.component';
export {
  OverlayPanelComponent,
} from './components/overlay/overlay-panel/overlay-panel.component';
export {
  SegmentedControlComponent,
  type SegmentedOption,
} from './components/segmented-control/segmented-control.component';
export { SettingsRowComponent } from './components/settings/settings-row/settings-row.component';
export {
  SettingsSectionComponent,
} from './components/settings/settings-section/settings-section.component';
export { UiModalHostComponent } from './components/ui-modal/ui-modal-host.component';
export { UiNodeEventBus } from './components/ui-render/ui-node-event-bus';
export { UiWidgetNodeComponent } from './components/ui-render/ui-widget-node.component';
export {
  UiWidgetResourceBaseUrl,
} from './components/ui-render/ui-widget-resource-base-url';
export { UiWidgetTreeContext } from './components/ui-render/ui-widget-tree-context';
export { UiWidgetTreeComponent } from './components/ui-render/ui-widget-tree.component';
export { WidgetBorderOverlayComponent } from './components/widget-border-overlay/widget-border-overlay.component';
export {
  type WidgetContextMenuAction,
  WidgetContextMenuComponent,
  type WidgetContextMenuMode,
} from './components/widget-context-menu/widget-context-menu.component';
export { WidgetGhostComponent } from './components/widget-ghost/widget-ghost.component';
export { WidgetGridComponent } from './components/widget-grid/widget-grid.component';
export {
  UiTreeWidgetComponent,
} from './components/widget-types/ui-tree-widget/ui-tree-widget.component';

// Angular adapter - localization
export { LocalizationService } from './localization/localization.service';
export { LocalizedTextPipe } from './localization/localized-text.pipe';
export { type LocalizationKey, TranslatePipe } from './localization/translate.pipe';

// Angular adapter - version checking
export {
  provideVersionCheck,
} from './version/version-check';

// Angular adapter - widget registration contract
export {
  type IWidgetComponent,
  type IWidgetEditorComponent,
} from './widget-definition.interface';
