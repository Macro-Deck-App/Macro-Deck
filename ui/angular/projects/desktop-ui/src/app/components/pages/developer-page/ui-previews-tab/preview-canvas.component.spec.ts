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
});
