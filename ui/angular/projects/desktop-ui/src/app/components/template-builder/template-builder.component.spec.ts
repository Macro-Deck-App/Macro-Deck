import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import { TEMPLATE_PREVIEW_SERVICE, TemplatePreviewService } from '../../domain/template-preview.interface';
import { TemplateBuilderComponent } from './template-builder.component';
import { TemplateEditorComponent } from './template-editor.component';
import { LiquidEditorComponent } from './liquid-editor.component';

function apiSpyObj(): jasmine.SpyObj<ApiService> {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
    'getVariables', 'getIntegrations', 'onNotification',
  ]);
  apiSpy.getVariables.and.resolveTo({ variables: [] });
  apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

function editorDebugElement(fixture: ComponentFixture<TemplateBuilderComponent>) {
  return fixture.debugElement.query(d => d.componentInstance instanceof TemplateEditorComponent);
}

function embeddedEditorLiquid(fixture: ComponentFixture<TemplateBuilderComponent>): LiquidEditorComponent {
  return fixture.debugElement.query(d => d.componentInstance instanceof LiquidEditorComponent)
    .componentInstance as LiquidEditorComponent;
}

describe('TemplateBuilderComponent', () => {
  let fixture: ComponentFixture<TemplateBuilderComponent>;
  let component: TemplateBuilderComponent;
  let preview: jasmine.SpyObj<TemplatePreviewService>;

  beforeEach(async () => {
    preview = jasmine.createSpyObj<TemplatePreviewService>('TemplatePreviewService', [
      'renderTemplate', 'evaluateCondition', 'evaluateExpression',
    ]);
    preview.renderTemplate.and.resolveTo('rendered');

    TestBed.configureTestingModule({
      imports: [TemplateBuilderComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpyObj() },
        { provide: TEMPLATE_PREVIEW_SERVICE, useValue: preview },
      ],
    });

    fixture = TestBed.createComponent(TemplateBuilderComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  async function openModal(): Promise<void> {
    component.open.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function closeViaDismiss(action: () => void): Promise<void> {
    jasmine.clock().install();
    try {
      action();
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders only the trigger until it is opened', () => {
    expect(fixture.nativeElement.querySelector('.tb-trigger')).toBeTruthy();
    expect(editorDebugElement(fixture)).toBeNull();
    expect(preview.renderTemplate).not.toHaveBeenCalled();
  });

  it('opens the editor seeded with the current value and scope', async () => {
    fixture.componentRef.setInput('value', 'original {{ vars.x }}');
    fixture.componentRef.setInput('scope', 'widget');
    fixture.componentRef.setInput('scopeRefId', 'w1');
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.tb-trigger').click();
    fixture.detectChanges();
    await fixture.whenStable();

    const editor = editorDebugElement(fixture).componentInstance as TemplateEditorComponent;
    expect(editor.value).toBe('original {{ vars.x }}');
    expect(editor.scope).toBe('widget');
    expect(editor.scopeRefId).toBe('w1');
  });

  it('emits the edited template on Insert and closes', async () => {
    await openModal();
    embeddedEditorLiquid(fixture).valueChange.emit('edited template');
    fixture.detectChanges();
    await fixture.whenStable();

    const applied: string[] = [];
    component.apply$.subscribe(v => applied.push(v));

    const insertButton = fixture.nativeElement.querySelectorAll('[modal-footer] button')[1] as HTMLButtonElement;
    await closeViaDismiss(() => insertButton.click());

    expect(applied).toEqual(['edited template']);
    expect(editorDebugElement(fixture)).toBeNull();
  });

  it('leaves the original value untouched and emits nothing on Cancel', async () => {
    fixture.componentRef.setInput('value', 'original');
    fixture.detectChanges();
    await openModal();

    embeddedEditorLiquid(fixture).valueChange.emit('abandoned draft');
    fixture.detectChanges();
    await fixture.whenStable();

    const applied: string[] = [];
    component.apply$.subscribe(v => applied.push(v));

    const cancelButton = fixture.nativeElement.querySelectorAll('[modal-footer] button')[0] as HTMLButtonElement;
    await closeViaDismiss(() => cancelButton.click());

    expect(applied).toEqual([]);
    expect(component.value).toBe('original');
  });

  it('shows the original value again when reopened after Cancel', async () => {
    fixture.componentRef.setInput('value', 'original');
    fixture.detectChanges();
    await openModal();

    embeddedEditorLiquid(fixture).valueChange.emit('abandoned draft');
    fixture.detectChanges();
    await fixture.whenStable();

    const cancelButton = fixture.nativeElement.querySelectorAll('[modal-footer] button')[0] as HTMLButtonElement;
    await closeViaDismiss(() => cancelButton.click());

    fixture.nativeElement.querySelector('.tb-trigger').click();
    fixture.detectChanges();
    await fixture.whenStable();

    const editor = editorDebugElement(fixture).componentInstance as TemplateEditorComponent;
    expect(editor.value).toBe('original');
  });

  it('shows the applied value when reopened after Insert', async () => {
    await openModal();
    embeddedEditorLiquid(fixture).valueChange.emit('applied');
    fixture.detectChanges();
    await fixture.whenStable();

    const insertButton = fixture.nativeElement.querySelectorAll('[modal-footer] button')[1] as HTMLButtonElement;
    await closeViaDismiss(() => insertButton.click());

    fixture.componentRef.setInput('value', 'applied');
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.nativeElement.querySelector('.tb-trigger').click();
    fixture.detectChanges();
    await fixture.whenStable();

    const editor = editorDebugElement(fixture).componentInstance as TemplateEditorComponent;
    expect(editor.value).toBe('applied');
  });

  it('cannot be opened while disabled', () => {
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();

    const applied: string[] = [];
    component.apply$.subscribe(v => applied.push(v));

    fixture.nativeElement.querySelector('.tb-trigger').click();
    fixture.detectChanges();

    expect(editorDebugElement(fixture)).toBeNull();
    expect(applied).toEqual([]);
  });

  it('hosts exactly one editor and no nested builder', async () => {
    await openModal();

    const editors = fixture.debugElement.queryAll(By.directive(TemplateEditorComponent));
    const nestedBuilders = fixture.debugElement.queryAll(By.directive(TemplateBuilderComponent));

    expect(editors.length).toBe(1);
    expect(nestedBuilders.length).toBe(0);
  });
});
