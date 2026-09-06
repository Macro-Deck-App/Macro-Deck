import {
  ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, model, signal, untracked,
} from '@angular/core';

import { FormsModule } from '@angular/forms';

import { IpcFolderView, UiConfigEntryPoints, UiConfigEvents, UiNode, UiNodeEvent, WIDGET_GRID_VIEW_ID, resolveLocalizedText } from '@macro-deck/runtime';
import { FolderViewService, LocalizationService, TranslatePipe, UiSessionHandle, UiSessionService } from '@shared';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { UiTreeComponent } from '../../ui-render/ui-tree.component';

@Component({
  selector: 'app-folder-view-picker',
  standalone: true,
  imports: [FormsModule, SelectComponent, TranslatePipe, UiTreeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './folder-view-picker.component.html',
  styleUrls: ['./folder-view-picker.component.scss'],
})
export class FolderViewPickerComponent {
  readonly viewId = model<string>(WIDGET_GRID_VIEW_ID);

  readonly configuration = model<string | null>(null);

  readonly folderId = input<string | null>(null);

  private readonly folderViews = inject(FolderViewService);
  private readonly localization = inject(LocalizationService);
  private readonly uiSessions = inject(UiSessionService);

  protected readonly views = this.folderViews.folderViews;

  protected readonly selected = computed(() => this.folderViews.find(this.viewId()));

  protected readonly showConfiguration = computed(() => this.selected()?.hasConfiguration === true);

  protected readonly root = computed<UiNode | null>(() => this.session()?.root() ?? null);

  private readonly session = signal<UiSessionHandle | null>(null);
  private values: Record<string, unknown> = {};
  private openedViewId: string | null = null;

  constructor() {
    effect(() => void this.folderViews.load());

    effect(() => {
      const viewId = this.viewId();
      const canConfigure = this.showConfiguration();

      if (!canConfigure) {
        this.closeSession();
        return;
      }

      // untracked so the session body's own reads - the stored configuration, the catalog entry - do not
      // become dependencies of the effect that opens it.
      if (viewId !== this.openedViewId) untracked(() => this.openSession(viewId));
    });

    inject(DestroyRef).onDestroy(() => this.closeSession());
  }

  protected readonly options = computed<SelectOption[]>(() =>
    this.views().map(view => ({ value: view.id, label: this.label(view) })));

  protected description(view: IpcFolderView): string {
    return resolveLocalizedText(view.description, this.localization);
  }

  protected onSelect(value: string): void {
    if (value === this.viewId()) return;

    // A configuration belongs to the view that wrote it, so switching views starts from nothing rather
    // than handing the next provider a shape it has never seen.
    this.values = {};
    this.configuration.set(null);
    this.viewId.set(value);
  }

  private label(view: IpcFolderView): string {
    return resolveLocalizedText(view.name, this.localization) || view.id;
  }

  protected onNodeEvent(event: UiNodeEvent): void {
    if (event.name === UiConfigEvents.Change) {
      this.values = { ...this.values, [event.nodeId]: event.data };
      this.configuration.set(JSON.stringify(this.values));
    }

    this.session()?.send(event);
  }

  private openSession(viewId: string): void {
    this.closeSession();
    this.openedViewId = viewId;

    const integrationId = this.selected()?.providerId ?? '';
    if (!integrationId) return;

    this.values = this.parseConfiguration(this.configuration());

    this.session.set(this.uiSessions.open({
      kind: 'config',
      entryPoint: UiConfigEntryPoints.FolderViewConfig,
      integrationId,
      folderId: this.folderId() ?? undefined,
      folderViewId: viewId,
      folderViewConfiguration: this.configuration() ?? undefined,
      // Negotiated per view rather than per integration would need a second round trip for a value the
      // provider already agreed to when it declared the ui capability; 0 means "whatever the host speaks".
      configUiModelVersion: 0,
    }));
  }

  private closeSession(): void {
    this.session()?.close();
    this.session.set(null);
    this.openedViewId = null;
  }

  private parseConfiguration(json: string | null): Record<string, unknown> {
    if (!json) return {};

    try {
      const parsed: unknown = JSON.parse(json);
      return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
        ? parsed as Record<string, unknown>
        : {};
    } catch {
      return {};
    }
  }
}
