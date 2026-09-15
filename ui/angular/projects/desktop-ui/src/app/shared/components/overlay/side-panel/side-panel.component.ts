import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  Injector,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';

import { ModalComponent } from '../modal/modal.component';
import { OverlayPanelComponent } from '../overlay-panel/overlay-panel.component';

@Component({
  selector: 'shared-side-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './side-panel.component.html',
  styleUrls: ['./side-panel.component.scss'],
})
export class SidePanelComponent {
  readonly isOpen = input(false);
  readonly edge = input<'start' | 'end'>('start');
  readonly heading = input.required<string>();
  readonly closeLabel = input.required<string>();

  readonly closed = output<void>();

  private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');
  private readonly injector = inject(Injector);
  private opener: HTMLElement | null = null;

  constructor() {
    effect(() => {
      if (this.isOpen()) {
        this.opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      } else {
        this.returnFocus();
      }
    });
  }

  // Stops the key so Escape handlers behind the scrim do not act on it too; that only works while this
  // listener is registered before theirs, as it is for a panel mounted ahead of the content it covers.
  @HostListener('document:keydown.escape', ['$event'])
  protected onEscape(event: Event): void {
    if (!this.isOpen()) return;
    if (ModalComponent.isAnyOpen() || OverlayPanelComponent.isAnyOpen()) return;
    event.stopImmediatePropagation();
    this.closed.emit();
  }

  private returnFocus(): void {
    const opener = this.opener;
    this.opener = null;
    if (!opener) return;

    // After render, so a modal opened in the same pass is already on the stack and keeps the focus.
    afterNextRender(() => {
      if (ModalComponent.isAnyOpen()) return;
      if (!this.panel().nativeElement.contains(document.activeElement)) return;
      if (opener === document.body || !opener.isConnected) return;
      opener.focus();
    }, { injector: this.injector });
  }
}
