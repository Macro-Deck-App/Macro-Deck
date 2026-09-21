import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { UiNode, UiNodeEvent, UiPreviewEntry, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { ApiService, ConnectionState, UiSessionHandle, UiSessionOpenRequest, UiSessionRejection, UiSessionService, UiWidgetTreeComponent } from '@shared';
import { UiTreeComponent } from '../../../ui-render/ui-tree.component';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';
import { PreviewCanvasComponent } from './preview-canvas.component';

class FakeUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  readonly fault = signal<UiSessionRejection | null>(null);
  readonly generation = signal(0);
  closed = false;
  readonly sent: UiNodeEvent[] = [];

  send(event: UiNodeEvent): void {
    this.sent.push(event);
  }

  close(): void {
    this.closed = true;
  }
}

function previewEntry(id: string, overrides: Partial<UiPreviewEntry> = {}): UiPreviewEntry {
  return { id, view: 'MainView', scenario: 'Default', profile: 'widget', ownerId: '', ...overrides };
}

function buttonTree(label: string): UiNode {
  return {
    id: 'root',
    type: UiComponents.Button,
    properties: {},
    children: [
      { id: 'label', type: UiComponents.Text, properties: { [UiComponentProperties.Text]: label } },
    ],
  };
}

describe('PreviewCanvasComponent', () => {
  let opens: UiSessionOpenRequest[];
  let closesOf: Map<FakeUiSessionHandle, number>;
  let handles: FakeUiSessionHandle[];
  let connectionState: ReturnType<typeof signal<ConnectionState>>;

  function createFixture(): ComponentFixture<PreviewCanvasComponent> {
    const fixture = TestBed.createComponent(PreviewCanvasComponent);
    fixture.detectChanges();
    return fixture;
  }

  async function settle(fixture: ComponentFixture<PreviewCanvasComponent>): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    opens = [];
    closesOf = new Map();
    handles = [];
    connectionState = signal<ConnectionState>('connected');

    const fakeUiSessions = {
      open: (request: UiSessionOpenRequest): UiSessionHandle => {
        opens.push(request);
        const handle = new FakeUiSessionHandle();
        const originalClose = handle.close.bind(handle);
        handle.close = () => {
          closesOf.set(handle, (closesOf.get(handle) ?? 0) + 1);
          originalClose();
        };
        handles.push(handle);
        return handle;
      },
    };

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      imports: [PreviewCanvasComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: UiSessionService, useValue: fakeUiSessions },
        { provide: ApiService, useValue: apiSpy },
      ],
    });
  });

  it('renders through the real renderer, driven by the live session handle', async () => {
    const fixture = createFixture();
    const preview = previewEntry('p1');
    fixture.componentRef.setInput('preview', preview);
    await settle(fixture);

    expect(opens).toEqual([{ kind: 'preview', previewId: 'p1' }]);

    const tree = buttonTree('Connect');
    handles[0].root.set(tree);
    await settle(fixture);

    const renderers = fixture.debugElement.queryAll(By.directive(UiWidgetTreeComponent));
    expect(renderers.length).toBe(1);

    const rendererInstance = renderers[0].componentInstance as UiWidgetTreeComponent;
    expect(rendererInstance.root()).toBe(handles[0].root());

    expect((renderers[0].nativeElement as HTMLElement).textContent).toContain('Connect');
  });

  it('re-layouts synchronously on resize, without opening a new session', async () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('preview', previewEntry('p1'));
    await settle(fixture);
    fixture.componentInstance['applySize'](400, 300);
    await settle(fixture);
    expect(opens.length).toBe(1);

    // The resize path itself, then a single detectChanges() - no tick() of any debounce.
    fixture.componentInstance['applySize'](612, 384);
    fixture.detectChanges();

    expect(fixture.componentInstance['box']()).toEqual({ width: 612, height: 384 });
    expect(opens.length).toBe(1);
    expect(handles[0].closed).toBeFalse();
  });

  it('keeps the size it was given instead of shrinking itself once laid out', async () => {
    const fixture = createFixture();
    // Attached to the document on purpose: the canvas only really lays out - and only an implementation
    // that measured itself back could drift - when it is in the page.
    document.body.appendChild(fixture.nativeElement as HTMLElement);
    try {
      fixture.componentRef.setInput('preview', previewEntry('p1'));
      await settle(fixture);

      fixture.componentInstance['applySize'](612, 384);
      fixture.detectChanges();

      for (let frame = 0; frame < 5; frame++) {
        await new Promise(resolve => requestAnimationFrame(() => resolve(null)));
        fixture.detectChanges();
      }

      expect(fixture.componentInstance['box']()).toEqual({ width: 612, height: 384 });
    } finally {
      (fixture.nativeElement as HTMLElement).remove();
    }
  });

  it('tracks the live size in the readout while dragging, not the outer panel including chrome', async () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('preview', previewEntry('p1'));
    await settle(fixture);

    // The host carries non-zero chrome (toolbar etc.) around the canvas surface - an implementation
    // that measured the outer element instead of the canvas itself would report a size including it.
    const hostElement = fixture.nativeElement as HTMLElement;
    hostElement.style.padding = '40px';
    hostElement.style.border = '10px solid black';

    fixture.componentInstance['applySize'](612, 384);
    fixture.detectChanges();
    expect(hostElement.textContent).toContain('612 × 384');

    fixture.componentInstance['applySize'](900, 500);
    fixture.detectChanges();
    expect(hostElement.textContent).toContain('900 × 500');
    expect(hostElement.textContent).not.toContain('612 × 384');
  });

  it('closes the previous handle exactly once when switching previews, and rewires events', async () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('preview', previewEntry('a'));
    await settle(fixture);
    const handleA = handles[0];

    fixture.componentRef.setInput('preview', previewEntry('b'));
    await settle(fixture);
    const handleB = handles[1];

    expect(closesOf.get(handleA)).toBe(1);
    expect(handleB.closed).toBeFalse();

    handleB.root.set(buttonTree('B'));
    await settle(fixture);

    const tree = fixture.debugElement.query(By.directive(UiWidgetTreeComponent))
      .componentInstance as UiWidgetTreeComponent;
    tree.nodeEvent.emit({ nodeId: 'root', name: 'press' });

    expect(handleB.sent.length).toBe(1);
    expect(handleA.sent.length).toBe(0);

    fixture.destroy();
    expect(closesOf.get(handleB)).toBe(1);
    expect(closesOf.get(handleA)).toBe(1);
  });

  it('refresh closes the current handle and reopens the same preview', async () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('preview', previewEntry('p1'));
    await settle(fixture);
    const handleBefore = handles[0];

    fixture.componentInstance['refresh']();
    fixture.detectChanges();

    expect(handleBefore.closed).toBeTrue();
    expect(opens).toEqual([{ kind: 'preview', previewId: 'p1' }, { kind: 'preview', previewId: 'p1' }]);
  });

  it('chooses the widget renderer for a widget.* root and the config renderer otherwise', async () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('preview', previewEntry('p1', { profile: 'config' }));
    await settle(fixture);

    handles[0].root.set({ id: 'root', type: 'config.field', properties: {} } as unknown as UiNode);
    await settle(fixture);

    expect(fixture.debugElement.queryAll(By.directive(UiTreeComponent)).length).toBe(1);
    expect(fixture.debugElement.queryAll(By.directive(UiWidgetTreeComponent)).length).toBe(0);
  });
  describe('while its plugin restarts', () => {
    function text(fixture: ComponentFixture<PreviewCanvasComponent>): string {
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    function content(fixture: ComponentFixture<PreviewCanvasComponent>): HTMLElement {
      return (fixture.nativeElement as HTMLElement).querySelector('.canvas-content') as HTMLElement;
    }

    async function showing(label: string): Promise<ComponentFixture<PreviewCanvasComponent>> {
      const fixture = createFixture();
      fixture.componentRef.setInput('preview', previewEntry('p1', { ownerId: 'plugin.example' }));
      fixture.componentRef.setInput('catalogRevision', 1);
      await settle(fixture);
      handles[0].root.set(buttonTree(label));
      await settle(fixture);
      return fixture;
    }

    it('keeps the last tree, inert, and says it is waiting once the plugin is gone', async () => {
      const fixture = await showing('Connect');

      handles[0].root.set(null);
      handles[0].fault.set({ code: 'PROVIDER_DISCONNECTED', message: 'gone' });
      await settle(fixture);

      expect(text(fixture)).toContain('Connect');
      expect(content(fixture).classList).toContain('stale');
      expect(content(fixture).inert).toBeTrue();
      expect(text(fixture)).toContain('The plugin is not connected');
    });

    it('reopens exactly once when a reload finds the plugin back, and leaves a live preview alone', async () => {
      const fixture = await showing('Connect');
      handles[0].fault.set({ code: 'PROVIDER_DISCONNECTED' });
      await settle(fixture);

      fixture.componentRef.setInput('catalogRevision', 2);
      await settle(fixture);
      fixture.componentRef.setInput('catalogRevision', 3);
      await settle(fixture);

      expect(opens.length).toBe(2);
      handles[1].root.set(buttonTree('Rebuilt'));
      await settle(fixture);
      expect(text(fixture)).toContain('Rebuilt');
      expect(content(fixture).classList).not.toContain('stale');
    });

    it('waits without opening while its scenario is unlisted, then opens once it is listed again', async () => {
      const fixture = await showing('Connect');

      fixture.componentRef.setInput('available', false);
      fixture.componentRef.setInput('ownerConnected', false);
      fixture.componentRef.setInput('catalogRevision', 2);
      await settle(fixture);

      expect(opens.length).toBe(1);
      expect(text(fixture)).toContain('The plugin is not connected');
      expect(text(fixture)).toContain('Connect');

      fixture.componentRef.setInput('available', true);
      fixture.componentRef.setInput('ownerConnected', true);
      fixture.componentRef.setInput('catalogRevision', 3);
      await settle(fixture);

      expect(opens.length).toBe(2);
    });

    it('keeps trying, less and less often, while its scenario is listed but the plugin does not answer', async () => {
      const fixture = await showing('Connect');
      jasmine.clock().install();
      try {
        handles[0].fault.set({ code: 'PROVIDER_DISCONNECTED' });
        await settle(fixture);

        jasmine.clock().tick(1999);
        expect(opens.length).toBe(1);
        jasmine.clock().tick(1);
        await settle(fixture);
        expect(opens.length).toBe(2);

        handles[1].rejection.set({ code: 'PROVIDER_DISCONNECTED' });
        await settle(fixture);
        jasmine.clock().tick(2000);
        expect(opens.length).toBe(2);
        jasmine.clock().tick(2000);
        await settle(fixture);
        expect(opens.length).toBe(3);

        handles[2].root.set(buttonTree('Back'));
        await settle(fixture);
        jasmine.clock().tick(60000);
        expect(opens.length).toBe(3);
      } finally {
        jasmine.clock().uninstall();
      }
    });

    it('says the scenario no longer exists when its plugin is connected but no longer lists it', async () => {
      const fixture = await showing('Connect');

      fixture.componentRef.setInput('available', false);
      fixture.componentRef.setInput('ownerConnected', true);
      fixture.componentRef.setInput('catalogRevision', 2);
      await settle(fixture);

      expect(text(fixture)).toContain('This scenario no longer exists');
      expect(text(fixture)).not.toContain('The plugin is not connected');
      expect(opens.length).toBe(1);
    });

    it('shows a failure the plugin reported, and retries it on the next reload', async () => {
      const fixture = await showing('Connect');
      handles[0].rejection.set({ code: 'PROVIDER_REJECTED', message: 'That preview could not be built: boom' });
      await settle(fixture);

      expect(text(fixture)).toContain('This preview could not be rendered');
      expect(text(fixture)).toContain('boom');

      fixture.componentRef.setInput('catalogRevision', 2);
      await settle(fixture);
      expect(opens.length).toBe(2);
    });

    it('says a session another window took over was closed, without the host\'s own text, and leaves it closed', async () => {
      const fixture = await showing('Connect');
      handles[0].fault.set({ message: 'The preview was refreshed.' });
      await settle(fixture);

      expect(text(fixture)).toContain('This preview was closed');
      expect(text(fixture)).not.toContain('The preview was refreshed.');

      fixture.componentRef.setInput('catalogRevision', 2);
      await settle(fixture);

      expect(opens.length).toBe(1);
    });

    it('shows no host text for a fault the plugin caused, only for a refused open', async () => {
      const fixture = await showing('Connect');
      handles[0].fault.set({ code: 'PROVIDER_FAULTED', message: 'The integration serving this view failed.' });
      await settle(fixture);

      expect(text(fixture)).toContain('This preview could not be rendered');
      expect(text(fixture)).not.toContain('The integration serving this view failed.');
    });
  });

  describe('restored from the URL', () => {
    it('starts at the restored size, once, and only opens when the scenario is listed', async () => {
      const fixture = createFixture();
      fixture.componentRef.setInput('initialSize', { width: 321, height: 123 });
      fixture.componentRef.setInput('available', false);
      fixture.componentRef.setInput('ownerConnected', false);
      fixture.componentRef.setInput('preview', previewEntry('p1', { profile: '' }));
      await settle(fixture);

      expect(opens.length).toBe(0);
      expect(fixture.componentInstance['box']()).toEqual({ width: 321, height: 123 });

      fixture.componentRef.setInput('preview', previewEntry('p1', { profile: 'widget' }));
      fixture.componentRef.setInput('available', true);
      fixture.componentRef.setInput('catalogRevision', 1);
      await settle(fixture);

      expect(opens.length).toBe(1);
      expect(fixture.componentInstance['box']()).toEqual({ width: 321, height: 123 });

      fixture.componentRef.setInput('preview', previewEntry('p2'));
      await settle(fixture);
      expect(fixture.componentInstance['box']()).toEqual({ width: 150, height: 150 });
    });

    it('takes the profile default once the real entry replaces a stub restored without a size', async () => {
      const fixture = createFixture();
      fixture.componentRef.setInput('available', false);
      fixture.componentRef.setInput('preview', previewEntry('p1', { profile: '' }));
      await settle(fixture);

      fixture.componentRef.setInput('preview', previewEntry('p1', { profile: 'widget' }));
      await settle(fixture);

      expect(fixture.componentInstance['box']()).toEqual({ width: 150, height: 150 });
    });
  });
});
