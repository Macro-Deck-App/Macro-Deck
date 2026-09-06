import { Injectable, signal } from '@angular/core';

import { ShellDroppedPath, ShellFileDropEvent, shellBridge } from '../util/shell-bridge';

export interface ShellDropPosition {
  x: number;
  y: number;
}

export type ShellFileDropFilter = (paths: ShellDroppedPath[], at: ShellDropPosition) => ShellDroppedPath[];

interface DropTarget {
  element: HTMLElement;
  accept: ShellFileDropFilter;
  drop: (paths: ShellDroppedPath[], at: ShellDropPosition) => void;
}

@Injectable({ providedIn: 'root' })
export class ShellFileDropService {
  readonly supported = typeof shellBridge()?.onFileDrop === 'function';

  readonly activeTarget = signal<HTMLElement | null>(null);

  private readonly targets: DropTarget[] = [];
  private unlisten: Promise<() => void> | null = null;
  private dragged: ShellDroppedPath[] = [];

  register(
    element: HTMLElement,
    accept: ShellFileDropFilter,
    drop: (paths: ShellDroppedPath[], at: ShellDropPosition) => void,
  ): () => void {
    const target: DropTarget = { element, accept, drop };
    this.targets.push(target);
    this.subscribe();

    return () => {
      const index = this.targets.indexOf(target);
      if (index >= 0) {
        this.targets.splice(index, 1);
      }
      if (this.activeTarget() === element) {
        this.activeTarget.set(null);
      }
      if (this.targets.length === 0) {
        this.unsubscribe();
      }
    };
  }

  handleEvent(event: ShellFileDropEvent): void {
    if (event.kind === 'leave') {
      this.dragged = [];
      this.activeTarget.set(null);
      return;
    }

    if (event.paths.length > 0) {
      this.dragged = event.paths;
    }

    const at: ShellDropPosition = { x: event.x, y: event.y };
    const target = this.targetAt(event.x, event.y);
    const accepted = target ? target.accept(this.dragged, at) : [];

    if (event.kind !== 'drop') {
      this.activeTarget.set(accepted.length > 0 ? target?.element ?? null : null);
      return;
    }

    this.activeTarget.set(null);
    this.dragged = [];
    if (target && accepted.length > 0) {
      target.drop(accepted, at);
    }
  }

  private targetAt(x: number, y: number): DropTarget | null {
    let node = document.elementFromPoint(x, y);
    while (node) {
      const target = this.targets.find(candidate => candidate.element === node);
      if (target) {
        return target;
      }
      node = node.parentElement;
    }
    return null;
  }

  private subscribe(): void {
    const bridge = shellBridge();
    if (this.unlisten || !bridge?.onFileDrop) {
      return;
    }
    this.unlisten = bridge.onFileDrop(event => this.handleEvent(event));
  }

  private unsubscribe(): void {
    const pending = this.unlisten;
    this.unlisten = null;
    this.dragged = [];
    void pending?.then(stop => stop()).catch(() => undefined);
  }
}
