import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';

import { emitsEvent, UiNode, UiNodeEvent } from '@macro-deck/runtime';

export interface UiNodePressedEvent {
  nodeId: string;
  pressed: boolean;
}

@Injectable()
export class UiNodeEventBus {
  private readonly eventSubject = new Subject<UiNodeEvent>();
  readonly events$ = this.eventSubject.asObservable();

  emit(node: UiNode, name: string, data?: unknown): void {
    if (!emitsEvent(node, name)) return;
    this.eventSubject.next(data === undefined ? { nodeId: node.id, name } : { nodeId: node.id, name, data });
  }

  private readonly pressedSubject = new Subject<UiNodePressedEvent>();

  readonly pressed$: Observable<UiNodePressedEvent> = this.pressedSubject.asObservable();

  setPressed(node: UiNode, pressed: boolean): void {
    this.pressedSubject.next({ nodeId: node.id, pressed });
  }
}
