import { Provider, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UiNode, UiNodeEvent } from '@macro-deck/runtime';
import { HOST_URL_RESOLVER } from '@shared';
import { UiTreeComponent } from './ui-tree.component';

export interface RenderedTree {
  fixture: ComponentFixture<UiTreeComponent>;
  events: UiNodeEvent[];
}

export async function renderTree(
  root: UiNode | null,
  values: Record<string, unknown> | null = null,
  providers: Provider[] = [],
): Promise<RenderedTree> {
  // A spec may render more than one independent tree per `it` (e.g. to compare a node with and
  // without a declared event), so each call gets its own fresh testing module.
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    imports: [UiTreeComponent],
    providers: [
      provideZonelessChangeDetection(),
      { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ...providers,
    ],
  });

  const fixture = TestBed.createComponent(UiTreeComponent);
  const events: UiNodeEvent[] = [];
  fixture.componentInstance.nodeEvent.subscribe((event: UiNodeEvent) => events.push(event));

  fixture.componentRef.setInput('root', root);
  fixture.componentRef.setInput('values', values);
  await settle(fixture);

  return { fixture, events };
}

export async function updateTree(
  rendered: RenderedTree,
  patch: { root?: UiNode | null; values?: Record<string, unknown> | null },
): Promise<void> {
  if ('root' in patch) rendered.fixture.componentRef.setInput('root', patch.root ?? null);
  if ('values' in patch) rendered.fixture.componentRef.setInput('values', patch.values ?? null);
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
async function settle(fixture: ComponentFixture<UiTreeComponent>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  await Promise.resolve();
  await Promise.resolve();
  fixture.detectChanges();
  await fixture.whenStable();
}
