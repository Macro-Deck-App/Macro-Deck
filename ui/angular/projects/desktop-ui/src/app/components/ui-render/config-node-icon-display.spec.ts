import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { UiNode } from '@macro-deck/runtime';
import { WidgetIconDisplayControlComponent } from '../widget-appearance/widget-icon-display-control.component';
import { el, renderTree, tick } from './ui-render-test-support';

describe('shared-ui-input icon-display', () => {
  afterEach(() => TestBed.resetTestingModule());

  function root(): UiNode {
    return {
      id: 'n',
      type: 'icon-display',
      properties: {
        value: { fit: 'contain', zoom: 100, offsetX: 0, offsetY: 0, opacity: 100 },
        icon: { type: 'icon-pack', reference: 'logo' },
        aspectRatio: 1.5,
        background: '#101010',
        events: ['change'],
      },
    };
  }

  it('maps the framing value, icon, aspect ratio and background onto the control', async () => {
    const rendered = await renderTree(root());
    const host = el(rendered);

    expect(host.querySelector('.config-node-unsupported')).toBeNull();

    const control = rendered.fixture.debugElement.query(By.directive(WidgetIconDisplayControlComponent))
      .componentInstance as WidgetIconDisplayControlComponent;

    expect(control.display).toEqual({ fit: 'contain', zoom: 100, offsetX: 0, offsetY: 0, opacity: 100 });
    expect(control.aspectRatio).toBe(1.5);
    expect(control.previewBackground).toBe('#101010');
  });

  it('emits the complete framing object on a change, never a single field', async () => {
    const rendered = await renderTree(root());
    const control = rendered.fixture.debugElement.query(By.directive(WidgetIconDisplayControlComponent))
      .componentInstance as WidgetIconDisplayControlComponent;

    control.displayChange.emit({ fit: 'cover', zoom: 150, offsetX: 5, offsetY: -5, opacity: 80 });
    await tick(rendered);

    expect(rendered.events).toEqual([
      { nodeId: 'n', name: 'change', data: { fit: 'cover', zoom: 150, offsetX: 5, offsetY: -5, opacity: 80 } },
    ]);
  });

  it('supports a reset, distinct from a change: it clears the stored framing rather than setting a field', async () => {
    const rendered = await renderTree(root());
    const control = rendered.fixture.debugElement.query(By.directive(WidgetIconDisplayControlComponent))
      .componentInstance as WidgetIconDisplayControlComponent;

    control.resetRequested.emit();
    await tick(rendered);

    expect(rendered.events).toEqual([{ nodeId: 'n', name: 'change', data: null }]);
  });
});
