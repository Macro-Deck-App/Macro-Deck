import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouteReuseStrategy, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { RecreateOnParamChangeStrategy, recreateOnParamChange } from './recreate-on-param-change.strategy';

let created = 0;

@Component({ standalone: true, template: '' })
class PageComponent {
  constructor() {
    created++;
  }
}

describe('RecreateOnParamChangeStrategy', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    created = 0;
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([
          { path: 'detail/:kind/:id', data: recreateOnParamChange(), component: PageComponent },
          { path: 'plain/:id', component: PageComponent },
        ]),
        { provide: RouteReuseStrategy, useClass: RecreateOnParamChangeStrategy },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  it('opens a fresh page when a marked route switches to another item', async () => {
    const first = await harness.navigateByUrl('/detail/IconPack/a', PageComponent);
    const second = await harness.navigateByUrl('/detail/Plugin/b', PageComponent);

    expect(second).not.toBe(first);
    expect(created).toBe(2);
  });

  it('keeps the page when a marked route navigates to the same item again', async () => {
    const first = await harness.navigateByUrl('/detail/Plugin/b', PageComponent);
    const again = await harness.navigateByUrl('/detail/Plugin/b?tab=reviews', PageComponent);

    expect(again).toBe(first);
    expect(created).toBe(1);
  });

  it('leaves unmarked routes reusing their page as before', async () => {
    const first = await harness.navigateByUrl('/plain/a', PageComponent);
    const second = await harness.navigateByUrl('/plain/b', PageComponent);

    expect(second).toBe(first);
    expect(created).toBe(1);
  });
});
