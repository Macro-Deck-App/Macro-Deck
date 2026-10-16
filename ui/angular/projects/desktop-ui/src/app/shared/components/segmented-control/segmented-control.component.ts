import { NgTemplateOutlet } from '@angular/common';
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

import { focusTabForKey } from '../../util/tab-keyboard';

export interface SegmentedOption {
  value: string;
  label?: string;
  icon?: string;
  ariaLabel?: string;
  badge?: string;
  link?: boolean;
}

@Component({
  selector: 'shared-segmented-control',
  standalone: true,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.stretch]': 'stretch',
  },
  template: `
    <div #seg class="seg" [attr.role]="tablist ? null : 'group'" [attr.aria-label]="tablist ? null : ariaLabel || null">
      <div
        class="seg-items"
        [attr.role]="tablist ? 'tablist' : null"
        [attr.aria-label]="tablist ? ariaLabel || null : null"
        (keydown)="onKeydown($event)">
        @for (option of itemOptions(); track option.value) {
          <ng-container [ngTemplateOutlet]="optionButton" [ngTemplateOutletContext]="{ $implicit: option }" />
        }
      </div>
      @for (option of linkOptions(); track option.value) {
        <ng-container [ngTemplateOutlet]="optionButton" [ngTemplateOutletContext]="{ $implicit: option }" />
      }
    </div>

    <ng-template #optionButton let-option>
      <button
        type="button"
        class="seg-option"
        [class.icon-only]="option.icon && !option.label"
        [class.active]="!option.link && option.value === value"
        [class.seg-link]="option.link"
        [attr.role]="tablist ? (option.link ? 'link' : 'tab') : null"
        [attr.id]="tablist && idPrefix && !option.link ? idPrefix + '-tab-' + option.value : null"
        [attr.aria-controls]="tablist && idPrefix && !option.link && option.value === value ? idPrefix + '-panel-' + option.value : null"
        [attr.aria-selected]="tablist && !option.link ? option.value === value : null"
        [attr.aria-pressed]="tablist ? null : option.value === value"
        [attr.tabindex]="tablist && !option.link ? tabIndexFor(option) : null"
        [attr.aria-label]="tablist && option.label ? null : (option.ariaLabel ?? option.label ?? option.value)"
        (click)="pick(option)">
        @if (option.icon) {
          <span class="icon icon-{{ option.icon }} icon-sm" aria-hidden="true"></span>
        }
        @if (option.label) {
          <span>{{ option.label }}</span>
        }
        @if (option.badge) {
          <span class="seg-badge" [attr.aria-hidden]="tablist ? null : 'true'">{{ option.badge }}</span>
        }
        @if (option.link) {
          <span class="icon icon-external-link icon-xs seg-link-icon" aria-hidden="true"></span>
        }
      </button>
    </ng-template>
  `,
  styleUrls: ['./segmented-control.component.scss'],
})
export class SegmentedControlComponent implements OnChanges, AfterViewInit {
  @Input() options: SegmentedOption[] = [];
  @Input() value: string | null = null;
  @Input() ariaLabel = '';
  @Input() stretch = false;
  @Input() tablist = false;
  @Input() idPrefix?: string;

  @Output() valueChange = new EventEmitter<string>();

  protected itemOptions(): SegmentedOption[] {
    return this.tablist ? this.options.filter(option => !option.link) : this.options;
  }

  protected linkOptions(): SegmentedOption[] {
    return this.tablist ? this.options.filter(option => option.link) : [];
  }

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

  onKeydown(event: KeyboardEvent): void {
    const seg = this.segRef?.nativeElement;
    if (!this.tablist || !seg) return;

    focusTabForKey(event, Array.from(seg.querySelectorAll<HTMLButtonElement>('.seg-items > .seg-option')));
  }

  protected tabIndexFor(option: SegmentedOption): 0 | -1 {
    if (option.value === this.value) {
      return 0;
    }

    const tabs = this.itemOptions();
    const hasActive = tabs.some(o => o.value === this.value);
    return !hasActive && tabs[0]?.value === option.value ? 0 : -1;
  }

  pick(option: SegmentedOption): void {
    if (option.link || option.value !== this.value) {
      this.valueChange.emit(option.value);
    }
  }
}
