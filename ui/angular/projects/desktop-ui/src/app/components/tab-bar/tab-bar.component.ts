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
import { AppStrings, scrollActiveIntoView } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { TabItem } from './tab-bar.model';

@Component({
  selector: 'shared-tab-bar',
  standalone: true,
  imports: [NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #bar class="tab-bar" role="tablist" (keydown)="onKeydown($event)">
      @for (item of items; track item.id) {
        @if (item.removable) {
          <!-- A button cannot nest another interactive element, so the tab and its "×" are siblings
               wrapped in a presentational span rather than the "×" living inside the tab button. -->
          <span class="tab-item-wrap" role="presentation" [class.active]="activeId === item.id">
            <ng-container [ngTemplateOutlet]="tabButton" [ngTemplateOutletContext]="{ $implicit: item }" />
            <button
              type="button"
              class="tab-remove-btn"
              [attr.tabindex]="tabIndexFor(item)"
              [attr.aria-label]="removeLabel(item)"
              [title]="removeLabel(item)"
              (click)="removeItem($event, item)">
              <span class="icon icon-x icon-xs" aria-hidden="true"></span>
            </button>
          </span>
        } @else {
          <ng-container [ngTemplateOutlet]="tabButton" [ngTemplateOutletContext]="{ $implicit: item }" />
        }
      }
    </div>

    <ng-template #tabButton let-item>
      <button
        type="button"
        role="tab"
        class="tab-item"
        [attr.id]="idPrefix ? idPrefix + '-tab-' + item.id : null"
        [attr.aria-controls]="idPrefix && activeId === item.id ? idPrefix + '-panel-' + item.id : null"
        [class.active]="activeId === item.id"
        [attr.aria-selected]="activeId === item.id"
        [attr.tabindex]="tabIndexFor(item)"
        (click)="select(item.id)">
        <span class="tab-label">{{ item.label }}</span>
        @if (item.badge !== undefined && item.badge !== null && item.badge !== 0) {
          <span class="tab-badge">{{ item.badge }}</span>
        }
        @if (item.dot) {
          <span class="tab-dot" aria-hidden="true"></span>
        }
      </button>
    </ng-template>
  `,
  styleUrls: ['./tab-bar.component.scss'],
})
export class TabBarComponent implements OnChanges, AfterViewInit {
  @Input() items: TabItem[] = [];
  @Input() activeId = '';

  @Input() idPrefix?: string;

  @Output() activeIdChange = new EventEmitter<string>();

  @Output() itemRemove = new EventEmitter<string>();

  @ViewChild('bar') private barRef?: ElementRef<HTMLElement>;

  private readonly localization = inject(LocalizationService);

  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resizeObserver?.disconnect());
  }

  ngAfterViewInit(): void {
    const element = this.barRef?.nativeElement;
    if (!element) return;

    this.resizeObserver = new ResizeObserver(() => this.ensureActiveVisible());
    this.resizeObserver.observe(element);
    this.ensureActiveVisible();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['activeId'] || changes['items']) {
      queueMicrotask(() => this.ensureActiveVisible());
    }
  }

  protected tabIndexFor(item: TabItem): 0 | -1 {
    if (this.activeId === item.id) {
      return 0;
    }

    const hasActive = this.items.some(i => i.id === this.activeId);
    return !hasActive && this.items[0]?.id === item.id ? 0 : -1;
  }

  select(id: string): void {
    this.activeIdChange.emit(id);
  }

  protected removeLabel(item: TabItem): string {
    return this.localization.translateKey(AppStrings.Forms.TabBar.RemoveTab, { label: item.label });
  }

  removeItem(event: MouseEvent, item: TabItem): void {
    event.stopPropagation();
    this.itemRemove.emit(item.id);
  }

  onKeydown(event: KeyboardEvent): void {
    const key = event.key;
    if (key !== 'ArrowLeft' && key !== 'ArrowRight' && key !== 'Home' && key !== 'End') {
      return;
    }

    const bar = this.barRef?.nativeElement;
    const buttons = bar ? Array.from(bar.querySelectorAll<HTMLButtonElement>('.tab-item')) : [];
    if (buttons.length === 0) {
      return;
    }

    const currentIndex = buttons.indexOf(document.activeElement as HTMLButtonElement);
    let nextIndex: number;
    switch (key) {
      case 'ArrowLeft':
        nextIndex = currentIndex <= 0 ? buttons.length - 1 : currentIndex - 1;
        break;
      case 'ArrowRight':
        nextIndex = currentIndex === -1 || currentIndex === buttons.length - 1 ? 0 : currentIndex + 1;
        break;
      case 'Home':
        nextIndex = 0;
        break;
      default: // 'End'
        nextIndex = buttons.length - 1;
        break;
    }

    event.preventDefault();
    buttons[nextIndex].focus();
  }

  ensureActiveVisible(): void {
    const bar = this.barRef?.nativeElement;
    const activeTab = bar?.querySelector<HTMLElement>('.tab-item.active');
    const active = activeTab?.closest<HTMLElement>('.tab-item-wrap') ?? activeTab;
    if (!bar || !active) return;

    scrollActiveIntoView(bar, active);
  }
}
