import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ActionBlockParameter, WIDGET_TARGET_SELF } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { KeyboardComboEditorComponent } from '../../../forms/keyboard-combo-editor/keyboard-combo-editor.component';
import { ParamRowComponent } from './param-row.component';
import { provideColorPaletteTesting } from '../../../../../testing/color-palette-test-support';

function accessibleName(element: Element): string {
  const labelledBy = element.getAttribute('aria-labelledby');
  if (labelledBy) {
    return labelledBy.split(' ')
      .map(id => document.getElementById(id)?.textContent?.trim() ?? '')
      .join(' ');
  }
  return element.getAttribute('aria-label') ?? '';
}

describe('ParamRowComponent accessible names', () => {
  const label = 'Visible label';
  let fixture: ComponentFixture<ParamRowComponent>;

  const parameter = (overrides: Partial<ActionBlockParameter>): ActionBlockParameter =>
    ({ name: 'probe', type: 'string', label, value: '', ...overrides }) as ActionBlockParameter;

  const render = async (param: ActionBlockParameter, allowReferences = true): Promise<HTMLElement> => {
    fixture.componentRef.setInput('blockId', 'block-1');
    fixture.componentRef.setInput('param', param);
    fixture.componentRef.setInput('allowReferences', allowReferences);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(async () => {
    const api = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    api.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });
    const store = {
      updateParam: () => undefined,
      errorsFor: () => [],
      pickerVariables: () => [],
      previewScope: () => 'global',
      previewScopeRefId: () => undefined,
      hasOwnerWidget: () => true,
      cacheParamLabel: () => undefined,
    };

    await TestBed.configureTestingModule({
      imports: [ParamRowComponent],
      providers: [
        provideZonelessChangeDetection(), ...provideColorPaletteTesting(),
        { provide: ApiService, useValue: api },
        { provide: ActionFlowStore, useValue: store },
        { provide: ActionOptionsService, useValue: { loadLabeledOptions: async () => ({ options: [] }) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ParamRowComponent);
  });

  const options = [{ value: 'a', label: 'Option A' }];
  const namedControls: Array<[string, Partial<ActionBlockParameter>, string, boolean?]> = [
    ['string with references', { type: 'string' }, '[role=textbox]'],
    ['string without references', { type: 'string' }, 'input', false],
    ['url', { type: 'url' }, '[role=textbox]'],
    ['ip address', { type: 'ipaddress' }, '[role=textbox]'],
    ['number', { type: 'number', value: 1 }, 'input[type=number]'],
    ['slider', { type: 'number', value: 1, min: 0, max: 10, showSlider: true }, 'input[type=range]'],
    ['slider number', { type: 'number', value: 1, min: 0, max: 10, showSlider: true }, 'input[type=number]'],
    ['checkbox', { type: 'boolean', value: false }, 'input[type=checkbox]'],
    ['switch', { type: 'boolean', value: false, literalOnly: true }, 'input[type=checkbox]'],
    ['choice', { type: 'choice', options }, 'button[role=combobox]'],
    ['searchable choice', { type: 'choice', options, searchable: true }, 'input'],
    ['dynamic choice', { type: 'dynamic-choice' }, 'button[role=combobox]'],
    ['searchable dynamic choice', { type: 'dynamic-choice', searchable: true }, 'input'],
    ['autocomplete', { type: 'autocomplete' }, 'input'],
    ['widget target', { type: 'widget-target', value: 'other' }, 'input'],
    ['multi-select', { type: 'multiselect', options, value: [] }, 'button.ms-trigger'],
    ['password', { type: 'password' }, 'input.si-input'],
    ['password without references', { type: 'password' }, 'input[type=password]', false],
    ['secret', { type: 'secret' }, 'input.si-input'],
    ['secret without references', { type: 'secret' }, 'input[type=password]', false],
    ['color', { type: 'color' }, 'button.cp-chip'],
    ['file', { type: 'file' }, 'input'],
    ['folder', { type: 'folder' }, 'input'],
    ['image', { type: 'image' }, 'input'],
    ['hotkey', { type: 'hotkey' }, 'button.hk-field'],
    ['duration', { type: 'duration', value: 0 }, 'input'],
    ['date and time', { type: 'datetime' }, 'input'],
  ];

  for (const [title, overrides, selector, allowReferences] of namedControls) {
    it(`names the ${title} control after the visible label`, async () => {
      const host = await render(parameter(overrides), allowReferences ?? true);

      const control = host.querySelector(selector);
      expect(control).withContext(selector).not.toBeNull();
      expect(accessibleName(control!)).toContain(label);
    });
  }

  it('names the code editor once CodeMirror replaced the text area', async () => {
    const host = await render(parameter({ type: 'code' }));

    expect(accessibleName(host.querySelector('textarea')!)).toBe(label);
    for (let attempt = 0; attempt < 200 && !host.querySelector('.cm-content'); attempt++) {
      await new Promise(resolve => setTimeout(resolve, 25));
    }
    expect(accessibleName(host.querySelector('.cm-content')!)).toBe(label);
  });

  it('names the select inside a key combination', async () => {
    const host = await render(parameter({ type: 'keyboard-combo' }));
    fixture.debugElement.query(By.directive(KeyboardComboEditorComponent)).componentInstance.setMode('select');
    await fixture.whenStable();

    const name = accessibleName(host.querySelector('.kce-select button[role=combobox]')!);
    expect(name.startsWith('Key ')).withContext(name).toBeTrue();
  });

  it('names the text step of a key sequence', async () => {
    const host = await render(parameter({
      type: 'keyboard-sequence',
      value: { steps: [{ type: 'text', text: '' }] } as never,
    }));

    expect(accessibleName(host.querySelector('.kse-text [role=textbox]')!)).not.toBe('');
  });

  it('names the step type select of a key sequence', async () => {
    const host = await render(parameter({ type: 'keyboard-sequence' }));

    expect(accessibleName(host.querySelector('.kse-select button[role=combobox]')!)).not.toBe('');
  });

  const groups: Array<[string, Partial<ActionBlockParameter>]> = [
    ['key and value pairs', { type: 'keyvalue', value: {} }],
    ['a key combination', { type: 'keyboard-combo' }],
    ['a key sequence', { type: 'keyboard-sequence' }],
    ['an icon', { type: 'icon' }],
    ['a stored secret', { type: 'secret', value: { $secret: 'secret-1' } as never }],
    ['the owning widget', { type: 'widget-target', value: WIDGET_TARGET_SELF }],
  ];

  for (const [title, overrides] of groups) {
    it(`labels the group around ${title}`, async () => {
      const host = await render(parameter(overrides));

      const group = host.querySelector('.param-value[role=group]');
      expect(group).not.toBeNull();
      expect(accessibleName(group!)).toContain(label);
    });
  }

  it('adds no group around a single named control', async () => {
    const host = await render(parameter({ type: 'number', value: 1 }));

    expect(host.querySelector('.param-value')!.hasAttribute('role')).toBeFalse();
  });

  it('shows the label of a color row once', async () => {
    const host = await render(parameter({ type: 'color' }));

    expect(host.querySelector('.cp-label')).toBeNull();
  });
});
