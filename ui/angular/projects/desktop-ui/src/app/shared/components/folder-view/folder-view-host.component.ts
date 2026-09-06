import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output,
  signal, untracked, viewChild,
} from '@angular/core';

import { ApiService, ConnectionState } from '../../transport';
import { UiSessionHandle, UiSessionService } from '../../services/ui-session.service';
import { UiNode, UiNodeEvent, type UiComponentBox } from '@macro-deck/runtime';
import { TranslatePipe } from '../../localization';
import { FolderViewService } from '../../services/folder-view.service';
import { UiWidgetTreeComponent } from '../ui-render/ui-widget-tree.component';
import { UiWidgetTreeContext } from '../ui-render/ui-widget-tree-context';

const NAVIGATION_HIDDEN = 'hidden';

// The back button is deliberately not the provider's to draw or wire: it always drives Macro Deck's own
// navigation, and a view asking to hide it still gets one whenever it is the only way out - which is
// what keeps a broken plugin view from trapping the user inside it.
@Component({
  selector: 'shared-folder-view-host',
  standalone: true,
  imports: [UiWidgetTreeComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiWidgetTreeContext],
  templateUrl: './folder-view-host.component.html',
  styleUrls: ['./folder-view-host.component.scss'],
})
export class FolderViewHostComponent {
  readonly folderId = input.required<string>();

  readonly viewId = input<string>('');

  readonly canGoBack = input(false);

  readonly showRecoveryActions = input(true);

  readonly back = output<void>();

  readonly openIntegrations = output<void>();

  readonly changeView = output<void>();

  private readonly uiSessions = inject(UiSessionService);
  private readonly api = inject(ApiService);
  private readonly treeContext = inject(UiWidgetTreeContext);
  private readonly folderViews = inject(FolderViewService);

  private readonly content = viewChild<ElementRef<HTMLElement>>('content');

  private readonly handle = signal<UiSessionHandle | null>(null);

  protected readonly box = signal<UiComponentBox>({ width: null, height: null });

  protected readonly root = computed<UiNode | null>(() => this.handle()?.root() ?? null);

  protected readonly unavailable = computed(() => (this.handle()?.rejection() ?? null) !== null);

  protected readonly loading = computed(() => !this.unavailable() && this.root() === null);

  protected readonly showBack = computed(() => this.canGoBack() &&
    (this.unavailable() || this.navigation() !== NAVIGATION_HIDDEN));

  private readonly navigation = computed(() => this.folderViews.find(this.viewId())?.navigation ?? '');

  private openedFolderId: string | null = null;
  private reconnectBaseline: ConnectionState | null = null;
  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    effect(() => {
      const folderId = this.folderId();
      if (folderId !== this.openedFolderId) this.open(folderId);
    });

    effect(() => {
      const state = this.api.connectionStateSignal();
      if (this.reconnectBaseline === null) {
        this.reconnectBaseline = state;
        return;
      }
      // A dropped session is gone on the host too, so reconnecting has to reopen rather than resync.
      // untracked: reading the folder id here would make a folder change re-run the reconnect effect too.
      if (state === 'connected' && this.reconnectBaseline !== 'connected') {
        untracked(() => this.open(this.folderId()));
      }
      this.reconnectBaseline = state;
    });

    effect(() => {
      const element = this.content()?.nativeElement;
      if (element) this.observe(element);
    });

    inject(DestroyRef).onDestroy(() => {
      this.resizeObserver?.disconnect();
      this.close();
    });
  }

  protected onTreeEvent(event: UiNodeEvent): void {
    this.handle()?.send(event);
  }

  protected onBack(): void {
    this.back.emit();
  }

  protected onOpenIntegrations(): void {
    this.openIntegrations.emit();
  }

  protected onChangeView(): void {
    this.changeView.emit();
  }

  private open(folderId: string): void {
    this.close();
    this.openedFolderId = folderId;
    if (!folderId) return;

    this.handle.set(this.uiSessions.open({ kind: 'folder', folderId }));
  }

  private close(): void {
    this.handle()?.close();
    this.handle.set(null);
  }

  // The widget profile expresses every length as a fraction of the box's smaller side, so a folder view
  // has to be told its box the same way a widget tile is - there is no pixel unit to fall back on.
  private observe(element: HTMLElement): void {
    this.resizeObserver?.disconnect();
    this.resizeObserver = new ResizeObserver(entries => {
      const rect = entries[0]?.contentRect;
      if (!rect) return;
      this.treeContext.setBasis(Math.min(rect.width, rect.height));
      this.box.set({ width: rect.width || null, height: rect.height || null });
    });
    this.resizeObserver.observe(element);
  }
}
