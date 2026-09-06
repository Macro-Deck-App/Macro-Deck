import {
  Directive, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, computed, effect, inject
} from '@angular/core';

import { ShellDropKind, acceptsShellDropPath, shellDropKind } from '../../domain/shell-drop.util';
import { ShellDropPosition, ShellFileDropService } from '../../services/shell-file-drop.service';
import { ShellDroppedPath } from '../../util/shell-bridge';

export interface ShellDrop {
  kind: ShellDropKind;
  path: string;
  x: number;
  y: number;
}

// Paths come from the shell bridge, the only drop channel that fires inside the desktop shell at all -
// Tauri owns drag-and-drop for the WebView and swallows the HTML5 drop event. Outside the shell
// this stays inert, so gate any drop hint on supported. Registering a target *rejects* a payload
// rather than passing it on: the service stops at the first registered ancestor whatever its filter
// says, so a narrow target nested in a wider one shadows it for every kind, not only its own.
@Directive({
  selector: '[sharedShellDropTarget]',
  standalone: true,
  host: {
    '[class.shell-drop-active]': 'dropActive() && highlightHost',
  },
})
export class ShellDropTargetDirective implements OnInit, OnDestroy {
  @Input({ required: true }) dropKinds: readonly ShellDropKind[] = [];

  @Input() highlightHost = true;

  @Input() dropAcceptAt?: (kind: ShellDropKind, at: ShellDropPosition) => boolean;

  @Output() shellDropped = new EventEmitter<ShellDrop>();

  @Output() shellDropHover = new EventEmitter<ShellDrop | null>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly fileDrop = inject(ShellFileDropService);

  readonly dropActive = computed(() => this.fileDrop.activeTarget() === this.host.nativeElement);

  private unregisterDrop: (() => void) | null = null;

  private hovering: ShellDrop | null = null;

  get supported(): boolean {
    return this.fileDrop.supported;
  }

  constructor() {
    effect(() => {
      if (this.fileDrop.activeTarget() !== this.host.nativeElement) {
        this.setHover(null);
      }
    });
  }

  ngOnInit(): void {
    if (!this.fileDrop.supported) {
      return;
    }

    this.unregisterDrop = this.fileDrop.register(
      this.host.nativeElement,
      (paths, at) => {
        const accepted = paths.filter(path => this.accepts(path, at)).slice(0, 1);
        const kind = accepted.length > 0 ? shellDropKind(accepted[0]) : null;
        this.setHover(kind ? { kind, path: accepted[0].path, x: at.x, y: at.y } : null);
        return accepted;
      },
      (paths, at) => {
        const kind = shellDropKind(paths[0]);
        if (kind) {
          this.shellDropped.emit({ kind, path: paths[0].path, x: at.x, y: at.y });
        }
      },
    );
  }

  ngOnDestroy(): void {
    this.unregisterDrop?.();
  }

  private setHover(next: ShellDrop | null): void {
    const unchanged = next === null
      ? this.hovering === null
      : this.hovering?.kind === next.kind
        && this.hovering.path === next.path
        && this.hovering.x === next.x
        && this.hovering.y === next.y;

    if (unchanged) {
      return;
    }

    this.hovering = next;
    this.shellDropHover.emit(next);
  }

  private accepts(path: ShellDroppedPath, at: ShellDropPosition): boolean {
    if (!acceptsShellDropPath(path, this.dropKinds)) {
      return false;
    }

    const kind = shellDropKind(path);
    return kind !== null && (this.dropAcceptAt?.(kind, at) ?? true);
  }
}
