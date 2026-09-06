import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild,
} from '@angular/core';

import { ApiService } from '../../transport';
import { LocalizationService } from '../../localization';
import { UiSessionHandle, UiSessionService } from '../../services/ui-session.service';
import {
  nodeString,
  resolveLocalizedText,
  UiNode,
  UiNodeEvent,
  UiComponentEvents,
  UiComponentProperties,
  type UiComponentBox,
} from '@macro-deck/runtime';
import { ModalComponent } from '../overlay/modal/modal.component';
import { UiWidgetTreeComponent } from '../ui-render/ui-widget-tree.component';
import { UiWidgetTreeContext } from '../ui-render/ui-widget-tree-context';

const COMPLETE_EVENT = 'modal.complete';

function findNode(root: UiNode | null, nodeId: string): UiNode | null {
  if (root === null) return null;
  if (root.id === nodeId) return root;

  for (const child of root.children ?? []) {
    const found = findNode(child, nodeId);
    if (found !== null) return found;
  }
  return null;
}

@Component({
  selector: 'shared-ui-modal-host',
  standalone: true,
  imports: [ModalComponent, UiWidgetTreeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiWidgetTreeContext],
  template: `
    @if (modalId(); as id) {
      <shared-modal
        [heading]="heading()"
        [showFooter]="false"
        maxWidth="34rem"
        [style.--modal-height]="'36rem'"
        [flush]="true"
        (close)="cancel()">
        <div #content class="ui-modal-content">
          <shared-ui-widget-tree [root]="root()" [box]="box()" (nodeEvent)="onTreeEvent($event)" />
        </div>
      </shared-modal>
    }
  `,
  styles: [`
    .ui-modal-content {
      position: relative;
      width: 100%;
      height: 100%;
      flex: 1 1 0;
      min-height: 0;
      overflow: hidden;
    }
  `],
})
export class UiModalHostComponent {
  private readonly api = inject(ApiService);
  private readonly uiSessions = inject(UiSessionService);
  private readonly localization = inject(LocalizationService);
  private readonly treeContext = inject(UiWidgetTreeContext);

  private readonly content = viewChild<ElementRef<HTMLElement>>('content');

  protected readonly modalId = signal<string | null>(null);
  protected readonly box = signal<UiComponentBox>({ width: null, height: null });

  private readonly handle = signal<UiSessionHandle | null>(null);
  private readonly title = signal<unknown>(null);

  protected readonly root = computed<UiNode | null>(() => this.handle()?.root() ?? null);

  protected readonly heading = computed(() => resolveLocalizedText(this.title() as never, this.localization));

  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    const subscription = this.api.onUiModalOpened().subscribe(event => {
      // One modal at a time: the previous one is cancelled rather than stacked, so its action settles
      // instead of waiting on a dialog the user can no longer see.
      this.cancel();
      this.title.set(event.title ?? null);
      this.modalId.set(event.modalId);
      this.handle.set(this.uiSessions.open({ kind: 'modal', modalId: event.modalId }));
    });

    effect(() => {
      const element = this.content()?.nativeElement;
      if (element) this.observe(element);
    });

    inject(DestroyRef).onDestroy(() => {
      subscription.unsubscribe();
      this.resizeObserver?.disconnect();
      this.cancel();
    });
  }

  protected onTreeEvent(event: UiNodeEvent): void {
    if (event.name === COMPLETE_EVENT) {
      this.complete(event.data);
      return;
    }

    // A button carrying an answer settles the dialog with it. The client is the side that knows the
    // press happened, has to take the dialog down and owns the principal the modal is bound to, so it
    // is the side that settles - a producer doing it would need to be told all three.
    if (event.name === UiComponentEvents.Press) {
      const answer = nodeString(findNode(this.root(), event.nodeId), UiComponentProperties.Answer);
      if (answer !== undefined) {
        this.complete(answer);
        return;
      }
    }

    this.handle()?.send(event);
  }

  protected cancel(): void {
    this.settle(true, undefined);
  }

  private complete(value: unknown): void {
    this.settle(false, value);
  }

  private settle(cancelled: boolean, value: unknown): void {
    const id = this.modalId();
    if (!id) return;

    this.modalId.set(null);
    this.title.set(null);
    this.handle()?.close();
    this.handle.set(null);

    void this.api.completeUiModal({ modalId: id, cancelled, value });
  }

  // Same reason as a folder view: the widget profile has no pixel unit, so the box has to be measured.
  private observe(element: HTMLElement): void {
    // Measured straight away, not only when the observer first fires: the dialog has a size from its
    // own stylesheet the moment it is shown, and a tree that arrives before the first callback would
    // resolve every length - which is a fraction of the basis - against zero. That is a dialog that is
    // present, correctly structured and completely invisible.
    this.measure(element.clientWidth, element.clientHeight);

    this.resizeObserver?.disconnect();
    this.resizeObserver = new ResizeObserver(entries => {
      const rect = entries[0]?.contentRect;
      if (!rect) return;
      this.measure(rect.width, rect.height);
    });
    this.resizeObserver.observe(element);
  }

  private measure(width: number, height: number): void {
    this.treeContext.setBasis(Math.min(width, height));
    this.box.set({ width: width || null, height: height || null });
  }
}
