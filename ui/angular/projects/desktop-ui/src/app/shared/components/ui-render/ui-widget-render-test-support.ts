import { ApplicationRef, Provider, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { HOST_URL_RESOLVER } from '../../transport/host-url';
import { UiNode, UiNodeEvent } from '@macro-deck/runtime';
import { UiWidgetTreeComponent } from './ui-widget-tree.component';

export interface RenderedTree {
  fixture: ComponentFixture<UiWidgetTreeComponent>;
  events: UiNodeEvent[];
}

export async function renderTree(
  root: UiNode | null,
  providers: Provider[] = [],
): Promise<RenderedTree> {
  // A spec may render more than one independent tree per `it` (e.g. to compare a node with and
  // without a declared event), so each call gets its own fresh testing module.
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [UiWidgetTreeComponent],
    providers: [
      provideZonelessChangeDetection(),
      { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ...providers,
    ],
  });

  const fixture = TestBed.createComponent(UiWidgetTreeComponent);
  const events: UiNodeEvent[] = [];
  fixture.componentInstance.nodeEvent.subscribe((event: UiNodeEvent) => events.push(event));

  fixture.componentRef.setInput('root', root);
  await settle(fixture);

  return { fixture, events };
}

export async function updateTree(
  rendered: RenderedTree,
  patch: { root?: UiNode | null },
): Promise<void> {
  if ('root' in patch) rendered.fixture.componentRef.setInput('root', patch.root ?? null);
  await settle(rendered.fixture);
}

export function el(rendered: RenderedTree): HTMLElement {
  return rendered.fixture.nativeElement as HTMLElement;
}

export async function tick(rendered: RenderedTree): Promise<void> {
  await settle(rendered.fixture);
}

// NgModel defers writeValue through a bare Promise.resolve().then() (Angular's own workaround for
// ExpressionChangedAfterItHasBeenChecked). NgZone normally hides that, but this app runs zoneless,
// so whenStable() can resolve before that microtask lands - hence draining it once by hand.
// The widget tree itself is drawn by an afterRenderEffect, an application-level hook that
// fixture.detectChanges() does not run - ApplicationRef.tick() does, and the frame after it is
// where the renderer's resize observer answers.
async function settle(fixture: ComponentFixture<UiWidgetTreeComponent>): Promise<void> {
  const appRef = TestBed.inject(ApplicationRef);

  fixture.detectChanges();
  appRef.tick();
  await fixture.whenStable();
  await Promise.resolve();
  await Promise.resolve();
  fixture.detectChanges();
  appRef.tick();
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
  appRef.tick();
  await fixture.whenStable();
}
