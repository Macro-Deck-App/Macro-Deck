import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  HostListener,
  Input,
  OnDestroy,
  OnInit,
  Output,
  inject,
} from '@angular/core';
import { Strings } from '@macro-deck/runtime';

import { LocalizationService } from '../../../localization';

@Component({
  selector: 'shared-modal',
  standalone: true,
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './modal.component.html',
  styleUrls: ['./modal.component.scss']
})
export class ModalComponent implements OnInit, OnDestroy {
  @Input() heading = '';
  @Input() showHeader = true;
  @Input() showFooter = true;
  @Input() showClose = true;
  @Input() closeOnOverlay = true;
  @Input() maxWidth = '450px';
  @Input() size: 'small' | 'medium' | 'large' | 'full' | null = null;
  @Input() flush = false;
  @Input() zIndex: number | null = null;

  private static stack: ModalComponent[] = [];

  static get anyOpen(): boolean {
    return ModalComponent.stack.length > 0;
  }

  private static idCounter = 0;

  protected readonly headingId = `modal-heading-${ModalComponent.idCounter++}`;

  private static readonly openGraceMs = 300;

  private static readonly closeAnimationMs = 150;

  get effectiveMaxWidth(): string {
    if (this.size) {
      switch (this.size) {
        case 'small': return '450px';
        case 'medium': return '700px';
        case 'large': return '1100px';
        case 'full': return '95vw';
      }
    }
    return this.maxWidth;
  }

  get sizeClass(): string {
    return this.size ? `modal-size-${this.size}` : '';
  }

  @Output() close = new EventEmitter<void>();

  private readonly cdr = inject(ChangeDetectorRef);
  private readonly localization = inject(LocalizationService);

  protected get closeLabel(): string {
    return this.localization.translateKey(Strings.Common.Close);
  }

  protected closing = false;
  private closeTimer?: ReturnType<typeof setTimeout>;

  private openedAt = 0;
  private pressStartedOnOverlay = false;

  ngOnInit(): void {
    ModalComponent.stack.push(this);
    this.openedAt = Date.now();
  }

  ngOnDestroy(): void {
    const idx = ModalComponent.stack.lastIndexOf(this);
    if (idx >= 0) ModalComponent.stack.splice(idx, 1);
    if (this.closeTimer) {
      clearTimeout(this.closeTimer);
    }
  }

  static isAnyOpen(): boolean {
    return ModalComponent.stack.length > 0;
  }

  dismiss(then: () => void): void {
    if (this.closing) {
      then();
      return;
    }
    this.closing = true;
    this.cdr.markForCheck();
    this.closeTimer = setTimeout(() => {
      then();
      this.cdr.markForCheck();
    }, ModalComponent.closeAnimationMs);
  }

  private requestClose(): void {
    if (this.closing) return;
    this.dismiss(() => this.close.emit());
  }

  isWithinOpenGrace(): boolean {
    return Date.now() - this.openedAt < ModalComponent.openGraceMs;
  }

  private isTopmost(): boolean {
    const stack = ModalComponent.stack;
    return stack.length > 0 && stack[stack.length - 1] === this;
  }

  onOverlayPointerDown(event: Event): void {
    this.pressStartedOnOverlay = event.target === event.currentTarget;
  }

  onOverlayClick(event: MouseEvent): void {
    const startedOnOverlay = this.pressStartedOnOverlay;
    this.pressStartedOnOverlay = false;

    if (event.target !== event.currentTarget) return;
    event.stopPropagation();

    if (this.isWithinOpenGrace()) return;
    if (!startedOnOverlay) return;

    if (this.closeOnOverlay && this.isTopmost()) {
      this.requestClose();
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscapeKey(event: Event): void {
    if (!this.isTopmost()) return;
    if (!this.closeOnOverlay) return;
    event.stopImmediatePropagation();
    this.requestClose();
  }

  onCloseClick(): void {
    this.requestClose();
  }
}

export function dismissModal(modal: ModalComponent | undefined, then: () => void): void {
  if (modal) {
    modal.dismiss(then);
  } else {
    then();
  }
}
