import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ApiService, VariableService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { TEMPLATE_PREVIEW_SERVICE, TemplatePreviewError, TemplatePreviewService } from '../../domain/template-preview.interface';
import { TemplateEditorComponent } from './template-editor.component';
import { TemplateBrowserComponent } from './template-browser.component';
import { LiquidEditorComponent } from './liquid-editor.component';

function variable(overrides: Partial<Variable>): Variable {
  return {
    id: overrides.id ?? 'id',
    name: 'name',
    scope: 'global',
    type: 'text',
    classification: 'user',
    value: '',
    ...overrides,
  };
}

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

function editorComponentOf(fixture: ComponentFixture<TemplateEditorComponent>): LiquidEditorComponent {
  return fixture.debugElement.query(d => d.componentInstance instanceof LiquidEditorComponent)
    .componentInstance as LiquidEditorComponent;
}

function browserComponentOf(fixture: ComponentFixture<TemplateEditorComponent>): TemplateBrowserComponent {
  return fixture.debugElement.query(d => d.componentInstance instanceof TemplateBrowserComponent)
    .componentInstance as TemplateBrowserComponent;
}

function previewNode(fixture: ComponentFixture<TemplateEditorComponent>): HTMLElement {
  return fixture.nativeElement.querySelector('.tb-preview');
}

function errorNode(fixture: ComponentFixture<TemplateEditorComponent>): HTMLElement | null {
  return fixture.nativeElement.querySelector('.tb-preview-error');
}

describe('TemplateEditorComponent', () => {
  let fixture: ComponentFixture<TemplateEditorComponent>;
  let component: TemplateEditorComponent;
  let preview: jasmine.SpyObj<TemplatePreviewService>;

  beforeEach(async () => {
    preview = jasmine.createSpyObj<TemplatePreviewService>('TemplatePreviewService', [
      'renderTemplate', 'evaluateCondition', 'evaluateExpression',
    ]);
    preview.renderTemplate.and.resolveTo('rendered');

    TestBed.configureTestingModule({
      imports: [TemplateEditorComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpyObj() },
        { provide: TEMPLATE_PREVIEW_SERVICE, useValue: preview },
      ],
    });

    fixture = TestBed.createComponent(TemplateEditorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('renders through the injected preview service with the component\'s scope and scopeRefId', async () => {
    fixture.componentRef.setInput('scope', 'widget');
    fixture.componentRef.setInput('scopeRefId', 'w1');
    preview.renderTemplate.and.resolveTo('Hello Bob');
    fixture.detectChanges();
    await fixture.whenStable();

    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('Hello {{ vars.greeting }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(preview.renderTemplate).toHaveBeenCalledWith('Hello {{ vars.greeting }}', 'widget', 'w1');
    expect(previewNode(fixture).textContent).toBe('Hello Bob');
  });

  it('clears the preview for an empty template without asking the host', async () => {
    preview.renderTemplate.and.resolveTo('Hello Bob');
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('{{ vars.greeting }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    preview.renderTemplate.calls.reset();

    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(preview.renderTemplate).not.toHaveBeenCalled();
    expect(previewNode(fixture).textContent?.trim()).toBe('');
  });

  it('coalesces rapid edits into a single render', async () => {
    jasmine.clock().install();
    try {
      const editor = editorComponentOf(fixture);
      editor.valueChange.emit('{{');
      editor.valueChange.emit('{{ vars');
      editor.valueChange.emit('{{ vars.greeting }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(preview.renderTemplate).toHaveBeenCalledTimes(1);
    expect(preview.renderTemplate.calls.mostRecent().args[0]).toBe('{{ vars.greeting }}');
  });

  it('a slow earlier render does not overwrite a newer one', async () => {
    let resolveSlow!: (value: string) => void;
    const slow = new Promise<string>(resolve => { resolveSlow = resolve; });
    preview.renderTemplate.and.returnValue(slow);

    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('slow');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    preview.renderTemplate.and.resolveTo('NEWEST');
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('fast');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    resolveSlow('STALE');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(previewNode(fixture).textContent).toBe('NEWEST');
  });

  it('re-renders the preview when the available variables change', async () => {
    const realVariableService = TestBed.inject(VariableService);
    fixture.componentRef.setInput('variables', [variable({ id: 'v1', name: 'system_time', value: '12:00' })]);
    fixture.detectChanges();

    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('{{ vars.system_time }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(preview.renderTemplate).toHaveBeenCalledTimes(1);

    realVariableService.variables.set([variable({ id: 'v1', name: 'system_time', value: '12:01' })]);
    fixture.detectChanges();
    jasmine.clock().install();
    try {
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(preview.renderTemplate).toHaveBeenCalledTimes(2);
    expect(preview.renderTemplate.calls.mostRecent().args[0]).toBe('{{ vars.system_time }}');
  });

  it('keeps the last good preview visible with the error code and message when a render fails', async () => {
    preview.renderTemplate.and.resolveTo('Hello Bob');
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('{{ vars.greeting }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    preview.renderTemplate.and.rejectWith(new TemplatePreviewError('unexpected token', 'TEMPLATE_ERROR'));
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('{{ vars.greeting |');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    const preview_ = previewNode(fixture);
    const error = errorNode(fixture);
    expect(preview_.textContent).toBe('Hello Bob');
    expect(error?.textContent).toContain('TEMPLATE_ERROR');
    expect(error?.textContent).toContain('unexpected token');
    expect(preview_.classList.contains('tb-preview-stale')).toBeTrue();
  });

  it('clears the error and replaces the preview on the next successful render', async () => {
    preview.renderTemplate.and.resolveTo('Hello Bob');
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('{{ vars.greeting }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    preview.renderTemplate.and.rejectWith(new TemplatePreviewError('unexpected token', 'TEMPLATE_ERROR'));
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('{{ vars.greeting |');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    preview.renderTemplate.and.resolveTo('Hello Alice');
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('{{ vars.greeting }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(previewNode(fixture).textContent).toBe('Hello Alice');
    expect(errorNode(fixture)).toBeNull();
    expect(previewNode(fixture).classList.contains('tb-preview-stale')).toBeFalse();
  });

  it('routes a browser insertion to the editor\'s cursor with every insertion form', async () => {
    const editor = editorComponentOf(fixture);
    spyOn(editor, 'insertAtCursor');

    browserComponentOf(fixture).insert.emit({
      text: '{{ vars.greeting }}',
      caret: 5,
      bare: 'vars.greeting',
      wrapped: '{{ vars.greeting }}',
      wrappedCaret: 3,
    });

    expect(editor.insertAtCursor).toHaveBeenCalledWith(
      '{{ vars.greeting }}',
      5,
      jasmine.objectContaining({
        bare: 'vars.greeting',
        wrapped: '{{ vars.greeting }}',
        wrappedCaret: 3,
      }),
    );
  });

  it('emits the edited template on valueChange and previews the same text', async () => {
    const emissions: string[] = [];
    component.valueChange.subscribe(v => emissions.push(v));

    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('edited by user');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(emissions).toEqual(['edited by user']);
    expect(preview.renderTemplate.calls.mostRecent().args[0]).toBe('edited by user');
  });

  it('leaves the template and preview untouched when the browser tab changes', async () => {
    preview.renderTemplate.and.resolveTo('hello Bob');
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('hello {{ vars.greeting }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    const previewBefore = previewNode(fixture).textContent;
    const callsBefore = preview.renderTemplate.calls.count();

    const browser = browserComponentOf(fixture);
    browser.setActiveTab('filters');
    fixture.detectChanges();
    await fixture.whenStable();
    browser.setActiveTab('control-flow');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(editorComponentOf(fixture).value).toBe('hello {{ vars.greeting }}');
    expect(previewNode(fixture).textContent).toBe(previewBefore);
    expect(preview.renderTemplate.calls.count()).toBe(callsBefore);
  });

  it('offers the variables, filters and control-flow browser scoped by the given label', () => {
    const items = [variable({ id: 'v1', name: 'greeting' })];
    fixture.componentRef.setInput('variables', items);
    fixture.componentRef.setInput('scopeLabel', 'Action Button');
    fixture.detectChanges();

    const browserDebugs = fixture.debugElement.queryAll(By.directive(TemplateBrowserComponent));
    expect(browserDebugs.length).toBe(1);
    const browser = browserDebugs[0].componentInstance as TemplateBrowserComponent;
    expect(browser.variables).toBe(items);
    expect(browser.scopeLabel).toBe('Action Button');
  });

  it('replaces the editor contents and re-previews when a new value is pushed in', async () => {
    fixture.componentRef.setInput('value', 'first');
    fixture.detectChanges();
    await fixture.whenStable();
    preview.renderTemplate.calls.reset();

    preview.renderTemplate.and.resolveTo('second-rendered');
    fixture.componentRef.setInput('value', 'second');
    fixture.detectChanges();
    jasmine.clock().install();
    try {
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(editorComponentOf(fixture).value).toBe('second');
    expect(preview.renderTemplate.calls.mostRecent().args[0]).toBe('second');
  });
});

describe('TemplateEditorComponent without a preview service', () => {
  let fixture: ComponentFixture<TemplateEditorComponent>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [TemplateEditorComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpyObj() },
      ],
    });

    fixture = TestBed.createComponent(TemplateEditorComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('echoes the template unchanged and reports no error when no preview service is provided', async () => {
    jasmine.clock().install();
    try {
      editorComponentOf(fixture).valueChange.emit('plain {{ text }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(previewNode(fixture).textContent).toBe('plain {{ text }}');
    expect(errorNode(fixture)).toBeNull();
  });
});
