import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  Input,
  ViewChild,
  afterNextRender,
  inject,
  signal,
} from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, ModalComponent, OverlayPanelComponent, TranslatePipe } from '@shared';
import { isEditorCompact } from './widget-editor-shell.metrics';

@Component({
  selector: 'app-widget-editor-shell',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-editor-shell.component.html',
  styleUrls: ['./widget-editor-shell.component.scss'],
})
export class WidgetEditorShellComponent implements AfterViewInit {
  @Input() sidebarLabel = inject(LocalizationService).translateKey(AppStrings.Widgets.Editor.Appearance);

  private static idCounter = 0;
  protected readonly sidebarId = `widget-editor-shell-sidebar-${WidgetEditorShellComponent.idCounter++}`;

  @ViewChild('toggleButton') private toggleButtonRef?: ElementRef<HTMLButtonElement>;
  @ViewChild('closeButton') private closeButtonRef?: ElementRef<HTMLButtonElement>;
  @ViewChild('sidebarScroll') private sidebarScrollRef?: ElementRef<HTMLElement>;

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  private resizeObserver: ResizeObserver | null = null;

  private readonly compactState = signal(false);
  private readonly drawerOpenState = signal(false);

  readonly isCompact = this.compactState.asReadonly();
  readonly drawerOpen = this.drawerOpenState.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resizeObserver?.disconnect());
  }

  ngAfterViewInit(): void {
    const element = this.host.nativeElement;
    // Ahead of the observer's first delivery, so the panel never paints a frame at the bare padding.
    this.applyScrollbarGutter();
    this.resizeObserver = new ResizeObserver(entries => {
      const width = entries[0]?.contentRect.width ?? element.getBoundingClientRect().width;
      this.applyAvailableWidth(width);
    });
    this.resizeObserver.observe(element);
  }

  applyAvailableWidth(width: number): void {
    this.applyScrollbarGutter();

    const rootFontSizePx = parseFloat(getComputedStyle(document.documentElement).fontSize);
    const compact = isEditorCompact(width, rootFontSizePx);
    if (compact === this.compactState()) return;

    this.compactState.set(compact);
    if (compact) {
      if (ModalComponent.isAnyOpen()) {
        this.openDrawer(false);
      }
      return;
    }

    const active = document.activeElement;
    const compactControlHadFocus = active === this.closeButtonRef?.nativeElement
      || active === this.toggleButtonRef?.nativeElement;
    this.drawerOpenState.set(false);
    if (compactControlHadFocus) {
      this.sidebarScrollRef?.nativeElement.focus();
    }
  }

  // Mirrors the scrollbar gutter reserved at the end edge onto the start one: WebKit stacks both
  // gutters of stable both-edges at the end, so the panel content is otherwise off-centre there.
  private applyScrollbarGutter(): void {
    const scroll = this.sidebarScrollRef?.nativeElement;
    if (!scroll) return;

    scroll.style.setProperty('--editor-sidebar-gutter', `${scroll.offsetWidth - scroll.clientWidth}px`);
  }

  protected toggleDrawer(): void {
    if (this.drawerOpenState()) {
      this.closeDrawer();
    } else {
      this.openDrawer(true);
    }
  }

  protected closeDrawer(): void {
    if (!this.drawerOpenState()) return;
    this.drawerOpenState.set(false);
    this.toggleButtonRef?.nativeElement.focus();
  }

  protected onSidebarEscape(): void {
    if (!this.drawerOpenState()) return;
    if (ModalComponent.isAnyOpen() || OverlayPanelComponent.isAnyOpen()) return;
    this.closeDrawer();
  }

  private openDrawer(moveFocusToClose: boolean): void {
    if (this.drawerOpenState()) return;
    this.drawerOpenState.set(true);
    if (moveFocusToClose) {
      afterNextRender(() => this.closeButtonRef?.nativeElement.focus(), { injector: this.injector });
    }
  }
}
