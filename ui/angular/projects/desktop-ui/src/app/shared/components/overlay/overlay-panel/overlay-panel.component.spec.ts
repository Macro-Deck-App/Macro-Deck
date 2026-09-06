import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OverlayPanelComponent } from './overlay-panel.component';

describe('OverlayPanelComponent', () => {
  let fixture: ComponentFixture<OverlayPanelComponent>;
  let component: OverlayPanelComponent;
  const appendedAnchors: HTMLElement[] = [];

  function createAnchor(style: string): HTMLElement {
    const el = document.createElement('div');
    el.setAttribute('style', `position: fixed; ${style}`);
    document.body.appendChild(el);
    appendedAnchors.push(el);
    return el;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OverlayPanelComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(OverlayPanelComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    for (const el of appendedAnchors.splice(0)) {
      el.remove();
    }
  });

  it('mounts on open and unmounts immediately on close without animateExit', () => {
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    expect(component.rendered()).toBeTrue();

    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();
    expect(component.rendered()).toBeFalse();
    expect(component.closing()).toBeFalse();
  });

  it('keeps the panel mounted through the exit animation when animateExit is set', () => {
    jasmine.clock().install();
    try {
      fixture.componentRef.setInput('animateExit', true);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();
      expect(component.rendered()).toBeTrue();

      fixture.componentRef.setInput('isOpen', false);
      fixture.detectChanges();
      expect(component.rendered()).toBeTrue();
      expect(component.closing()).toBeTrue();

      jasmine.clock().tick(150);
      expect(component.rendered()).toBeFalse();
      expect(component.closing()).toBeFalse();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('cancels a pending exit when reopened mid-animation', () => {
    jasmine.clock().install();
    try {
      fixture.componentRef.setInput('animateExit', true);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      fixture.componentRef.setInput('isOpen', false);
      fixture.detectChanges();
      expect(component.closing()).toBeTrue();

      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();
      expect(component.closing()).toBeFalse();
      expect(component.rendered()).toBeTrue();

      jasmine.clock().tick(150);
      expect(component.rendered()).toBeTrue();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('dismisses on an outside click even when another document listener stops propagation', () => {
    // Stands in for Tauri's drag-region script, which swallows mousedown on the titlebar.
    const swallow = (event: MouseEvent): void => event.stopImmediatePropagation();
    document.addEventListener('mousedown', swallow);
    const outside = createAnchor('top: 0; left: 0; width: 40px; height: 20px;');
    let dismissed = 0;
    component.dismissed.subscribe(() => dismissed++);

    try {
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      outside.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));

      expect(dismissed).toBe(1);
    } finally {
      document.removeEventListener('mousedown', swallow);
    }
  });

  describe('an ancestor that establishes the fixed containing block', () => {
    function hostInside(css: string): HTMLElement {
      const wrapper = document.createElement('div');
      wrapper.setAttribute('style', `position: absolute; top: 140px; left: 90px; ${css}`);
      document.body.appendChild(wrapper);
      wrapper.appendChild(fixture.nativeElement);
      appendedAnchors.push(wrapper);
      return wrapper;
    }

    for (const [label, css] of [['a transform', 'transform: translateZ(0);'], ['containment', 'contain: content;']]) {
      it(`still lands on the anchor under ${label}`, () => {
        hostInside(css);
        const anchor = createAnchor('top: 200px; left: 50px; width: 80px; height: 30px;');
        const rect = anchor.getBoundingClientRect();

        fixture.componentRef.setInput('anchor', anchor);
        fixture.componentRef.setInput('anchorOffset', 0);
        fixture.componentRef.setInput('isOpen', true);
        fixture.detectChanges();

        // What matters is where the panel ends up on screen, whatever the component did to get it
        // there - so this asserts the rendered rect against the anchor, not the internal coordinates.
        const panel = document.querySelector('.op-panel') as HTMLElement;
        // Measured where the pop-in animation leaves it: while it plays it scales the box, and a
        // rect read mid-animation describes the animation rather than the placement.
        panel.getAnimations().forEach(animation => animation.finish());
        const panelRect = panel.getBoundingClientRect();

        expect(Math.round(panelRect.top)).toBe(Math.round(rect.bottom));
        expect(Math.round(panelRect.left)).toBe(Math.round(rect.left));
      });

      it(`is not left inside the ancestor that would clip it under ${label}`, () => {
        // Position alone is not enough: paint containment cuts off whatever crosses the ancestor's
        // edge, which sliced a row menu in half even once it pointed at the right place.
        const wrapper = hostInside(css);
        const anchor = createAnchor('top: 200px; left: 50px; width: 80px; height: 30px;');

        fixture.componentRef.setInput('anchor', anchor);
        fixture.componentRef.setInput('isOpen', true);
        fixture.detectChanges();

        const panel = document.querySelector('.op-panel') as HTMLElement;
        expect(wrapper.contains(panel)).toBeFalse();
      });
    }

    it('does not dismiss on a pointerdown inside the panel it moved out', () => {
      // Moving the panel out of the clipping ancestor takes it out of the host as well, so an
      // outside-click check that only knows the host reads a click on the panel's own items as a
      // click outside. The panel is then dismissed on mousedown and the click that would have
      // activated the item never lands - the menu opens and nothing in it works.
      hostInside('contain: content;');
      const anchor = createAnchor('top: 200px; left: 50px; width: 80px; height: 30px;');

      fixture.componentRef.setInput('anchor', anchor);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      let dismissed = 0;
      component.dismissed.subscribe(() => dismissed++);

      const panel = document.querySelector('.op-panel') as HTMLElement;
      panel.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));

      expect(dismissed).toBe(0);
    });

    it('is unaffected when no ancestor establishes one', () => {
      const anchor = createAnchor('top: 200px; left: 50px; width: 80px; height: 30px;');
      const rect = anchor.getBoundingClientRect();

      fixture.componentRef.setInput('anchor', anchor);
      fixture.componentRef.setInput('anchorOffset', 0);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      expect(component.pos().top).toBe(rect.bottom);
      expect(component.pos().left).toBe(rect.left);
    });
  });

  describe('anchorOffset', () => {
    it('sits flush against the anchor when anchorOffset is 0', () => {
      const anchor = createAnchor('top: 200px; left: 50px; width: 80px; height: 30px;');
      const rect = anchor.getBoundingClientRect();

      fixture.componentRef.setInput('anchor', anchor);
      fixture.componentRef.setInput('anchorOffset', 0);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      expect(component.pos().top).toBe(rect.bottom);
    });

    it('keeps a gap by default, protecting menus from being forced flush', () => {
      const anchor = createAnchor('top: 200px; left: 50px; width: 80px; height: 30px;');
      const rect = anchor.getBoundingClientRect();

      fixture.componentRef.setInput('anchor', anchor);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      expect(component.pos().top).toBeGreaterThan(rect.bottom);
    });

    it('honours a supplied offset without affecting the horizontal position', () => {
      const anchor = createAnchor('top: 200px; left: 50px; width: 80px; height: 30px;');
      const rect = anchor.getBoundingClientRect();

      fixture.componentRef.setInput('anchor', anchor);
      fixture.componentRef.setInput('anchorOffset', 8);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      expect(component.pos().top).toBe(rect.bottom + 8);
      expect(component.pos().left).toBe(rect.left);
    });

    it('keeps the gap on the correct side when it flips above the anchor', () => {
      const panelHeight = 50;
      const anchorTop = window.innerHeight - 60;
      const anchor = createAnchor(`top: ${anchorTop}px; left: 40px; width: 80px; height: 20px;`);
      const rect = anchor.getBoundingClientRect();

      fixture.componentRef.setInput('anchor', anchor);
      fixture.componentRef.setInput('maxHeight', panelHeight);
      fixture.componentRef.setInput('anchorOffset', 0);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();
      expect(component.pos().top + panelHeight).toBe(rect.top);

      fixture.componentRef.setInput('isOpen', false);
      fixture.detectChanges();

      const offsetFixture = TestBed.createComponent(OverlayPanelComponent);
      const offsetComponent = offsetFixture.componentInstance;
      offsetFixture.componentRef.setInput('anchor', anchor);
      offsetFixture.componentRef.setInput('maxHeight', panelHeight);
      offsetFixture.componentRef.setInput('anchorOffset', 8);
      offsetFixture.componentRef.setInput('isOpen', true);
      offsetFixture.detectChanges();
      expect(offsetComponent.pos().top + panelHeight).toBe(rect.top - 8);
    });

    it('ignores the offset in x/y coordinate mode', () => {
      const x = 100;
      const y = 100;

      fixture.componentRef.setInput('x', x);
      fixture.componentRef.setInput('y', y);
      fixture.componentRef.setInput('anchorOffset', 8);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();

      expect(component.pos().top).toBe(y);
      expect(component.pos().left).toBe(x);
    });

    it('does not let the offset leak into horizontal collision handling', () => {
      const minWidth = 100;
      const anchor = createAnchor(
        `top: 200px; left: ${window.innerWidth - 30}px; width: 80px; height: 30px;`,
      );

      fixture.componentRef.setInput('anchor', anchor);
      fixture.componentRef.setInput('minWidth', minWidth);
      fixture.componentRef.setInput('isOpen', true);
      fixture.detectChanges();
      const defaultLeft = component.pos().left;

      fixture.componentRef.setInput('isOpen', false);
      fixture.detectChanges();

      const offsetFixture = TestBed.createComponent(OverlayPanelComponent);
      const offsetComponent = offsetFixture.componentInstance;
      offsetFixture.componentRef.setInput('anchor', anchor);
      offsetFixture.componentRef.setInput('minWidth', minWidth);
      offsetFixture.componentRef.setInput('anchorOffset', 8);
      offsetFixture.componentRef.setInput('isOpen', true);
      offsetFixture.detectChanges();
      const offsetLeft = offsetComponent.pos().left;

      expect(offsetLeft).toBe(defaultLeft);
      expect(defaultLeft + minWidth).toBeLessThanOrEqual(window.innerWidth);
    });
  });
});
