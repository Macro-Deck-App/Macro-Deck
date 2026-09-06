import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { AppStrings, Variable } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ModalComponent, VariableService } from '@shared';
import { TemplateBuilderComponent } from '../../../template-builder/template-builder.component';
import { TemplateEditorComponent } from '../../../template-builder/template-editor.component';
import { TEMPLATE_PREVIEW_SERVICE, TemplatePreviewService } from '../../../../domain/template-preview.interface';
import { TemplateTabComponent } from './template-tab.component';

const DRAFT_KEY = 'md.developerTools.templateDraft';

function variable(name: string, value: string): Variable {
  return {
    id: name,
    name,
    scope: 'global',
    type: 'text',
    classification: 'user',
    value,
  };
}

describe('TemplateTabComponent', () => {
  let fixture: ComponentFixture<TemplateTabComponent>;
  let apiSpy: jasmine.SpyObj<ApiService>;
  let previewSpy: jasmine.SpyObj<TemplatePreviewService>;

  async function setUp(): Promise<void> {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getEventDefinitions',
      'getVariables',
      'onNotification',
    ]);
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('connected') });
    apiSpy.getEventDefinitions.and.resolveTo({
      events: [{
        id: 'obs::scene-changed',
        providerId: 'obs',
        providerName: 'OBS Studio',
        isIntegration: true,
        name: 'Scene Changed',
        deliveryKind: 'push',
        configurationParameters: [],
        payloadParameters: [],
      }],
    });
    apiSpy.getVariables.and.resolveTo({ variables: [] });

    previewSpy = jasmine.createSpyObj<TemplatePreviewService>('TemplatePreviewService', [
      'renderTemplate', 'evaluateCondition', 'evaluateExpression',
    ]);
    previewSpy.renderTemplate.and.resolveTo('rendered');

    TestBed.configureTestingModule({
      imports: [TemplateTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: TEMPLATE_PREVIEW_SERVICE, useValue: previewSpy },
      ],
    });

    fixture = TestBed.createComponent(TemplateTabComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function editorDebugElement() {
    return fixture.debugElement.query(d => d.componentInstance instanceof TemplateEditorComponent);
  }

  function editorInstance(): TemplateEditorComponent {
    return editorDebugElement().componentInstance as TemplateEditorComponent;
  }

  beforeEach(() => {
    localStorage.removeItem(DRAFT_KEY);
  });

  afterEach(() => {
    localStorage.removeItem(DRAFT_KEY);
  });

  it('embeds the shared template editor at global scope', async () => {
    await setUp();

    const editors = fixture.debugElement.queryAll(By.directive(TemplateEditorComponent));
    expect(editors.length).toBe(1);
    const editor = editors[0].componentInstance as TemplateEditorComponent;
    expect(editor.scope).toBe('global');
    expect(editor.scopeRefId).toBeUndefined();
  });

  it('opens no modal and offers no insert or apply action', async () => {
    await setUp();

    const modal = fixture.debugElement.query(d => d.componentInstance instanceof ModalComponent);
    const builder = fixture.debugElement.query(d => d.componentInstance instanceof TemplateBuilderComponent);
    expect(modal).toBeNull();
    expect(builder).toBeNull();

    const localization = TestBed.inject(LocalizationService);
    const insertLabel = localization.translateKey(AppStrings.TemplateBuilder.Insert);
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    expect(buttons.some(button => button.textContent?.trim() === insertLabel)).toBeFalse();
  });

  it('persists the draft as it is typed', async () => {
    await setUp();

    editorInstance().valueChange.emit('{{ vars.kept }}');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(localStorage.getItem(DRAFT_KEY)).toBe('{{ vars.kept }}');
  });

  it('restores a draft written before the tab was created', async () => {
    localStorage.setItem(DRAFT_KEY, '{{ vars.kept }}');

    await setUp();

    expect(editorInstance().value).toBe('{{ vars.kept }}');
  });

  it('keeps the draft when the tab is left and reopened', async () => {
    await setUp();

    editorInstance().valueChange.emit('work in progress');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.destroy();

    const second = TestBed.createComponent(TemplateTabComponent);
    second.detectChanges();
    await second.whenStable();
    second.detectChanges();

    const editorDebug = second.debugElement.query(d => d.componentInstance instanceof TemplateEditorComponent);
    expect((editorDebug.componentInstance as TemplateEditorComponent).value).toBe('work in progress');
  });

  it('persists a cleared scratchpad instead of resurrecting the old draft', async () => {
    await setUp();

    editorInstance().valueChange.emit('something');
    fixture.detectChanges();
    await fixture.whenStable();

    editorInstance().valueChange.emit('');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.destroy();

    const second = TestBed.createComponent(TemplateTabComponent);
    second.detectChanges();
    await second.whenStable();
    second.detectChanges();

    const editorDebug = second.debugElement.query(d => d.componentInstance instanceof TemplateEditorComponent);
    expect((editorDebug.componentInstance as TemplateEditorComponent).value).toBe('');
    expect(localStorage.getItem(DRAFT_KEY)).toBe('');
  });

  it('carries no event selection, payload editing or event tokens', async () => {
    await setUp();

    expect(apiSpy.getEventDefinitions).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('select')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('{{ event.');
  });

  it('previews through the shared host-backed renderer with no event arguments', async () => {
    await setUp();

    jasmine.clock().install();
    try {
      editorInstance().valueChange.emit('{{ vars.x }}');
      jasmine.clock().tick(1000);
    } finally {
      jasmine.clock().uninstall();
    }
    await fixture.whenStable();
    fixture.detectChanges();

    expect(previewSpy.renderTemplate).toHaveBeenCalledTimes(1);
    expect(previewSpy.renderTemplate).toHaveBeenCalledWith('{{ vars.x }}', 'global', undefined);
    expect(fixture.nativeElement.querySelector('.tb-preview')?.textContent).toContain('rendered');
  });

  it('offers the same variables browser as the modal', async () => {
    await setUp();

    TestBed.inject(VariableService).variables.set([variable('system_time', '12:00')]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const browserDebug = fixture.debugElement.query(By.css('shared-template-browser'));
    expect(browserDebug).not.toBeNull();
    const variables = (browserDebug.componentInstance as { variables: Variable[] }).variables;
    expect(variables.some(v => v.name === 'system_time')).toBeTrue();
  });
});
