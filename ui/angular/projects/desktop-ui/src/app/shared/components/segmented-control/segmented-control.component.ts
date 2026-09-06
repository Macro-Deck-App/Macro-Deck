import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
} from '@angular/core';

import { scrollActiveIntoView } from '@macro-deck/runtime';

export interface SegmentedOption {
  value: string;
  label?: string;
  icon?: string;
  ariaLabel?: string;
}

@Component({
  selector: 'shared-segmented-control',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.stretch]': 'stretch',
  },
  template: `
    <div #seg class="seg" role="group" [attr.aria-label]="ariaLabel || null">
      @for (option of options; track option.value) {
        <button
          type="button"
          class="seg-option"
          [class.icon-only]="option.icon && !option.label"
          [class.active]="option.value === value"
          [attr.aria-pressed]="option.value === value"
          [attr.aria-label]="option.ariaLabel ?? option.label ?? option.value"
          (click)="pick(option)">
          @if (option.icon) {
            <span class="icon icon-{{ option.icon }} icon-sm" aria-hidden="true"></span>
          }
          @if (option.label) {
            <span>{{ option.label }}</span>
          }
        </button>
      }
    </div>
  `,
  styleUrls: ['./segmented-control.component.scss'],
})
export class SegmentedControlComponent implements OnChanges, AfterViewInit {
  @Input() options: SegmentedOption[] = [];
  @Input() value: string | null = null;
  @Input() ariaLabel = '';
  @Input() stretch = false;

  @Output() valueChange = new EventEmitter<string>();

  @ViewChild('seg') private segRef?: ElementRef<HTMLElement>;

  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resizeObserver?.disconnect());
  }

  ngAfterViewInit(): void {
    const element = this.segRef?.nativeElement;
    if (!element) return;

    this.resizeObserver = new ResizeObserver(() => this.ensureActiveVisible());
    this.resizeObserver.observe(element);
    this.ensureActiveVisible();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['value'] || changes['options']) {
      queueMicrotask(() => this.ensureActiveVisible());
    }
  }

  ensureActiveVisible(): void {
    const seg = this.segRef?.nativeElement;
    const active = seg?.querySelector<HTMLElement>('.seg-option.active');
    if (!seg || !active) return;

    scrollActiveIntoView(seg, active);
  }

  pick(option: SegmentedOption): void {
    if (option.value !== this.value) {
      this.valueChange.emit(option.value);
    }
  }
}
