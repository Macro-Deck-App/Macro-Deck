import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfigFlowInstructionDto } from '@macro-deck/runtime';
import { ConfigFlowInstructionsComponent } from './config-flow-instructions.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('ConfigFlowInstructionsComponent', () => {
  let fixture: ComponentFixture<ConfigFlowInstructionsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ConfigFlowInstructionsComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(ConfigFlowInstructionsComponent);
  });

  it('renders nothing for an empty list', async () => {
    fixture.componentRef.setInput('instructions', []);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('ol')).toBeNull();
  });

  it('renders one list item per instruction, in document order', async () => {
    const instructions: ConfigFlowInstructionDto[] = [
      { text: 'Open the developer dashboard.' },
      { text: 'Create a new app.' },
      { text: 'Copy the client secret.' },
    ];
    fixture.componentRef.setInput('instructions', instructions);
    fixture.detectChanges();
    await fixture.whenStable();

    const items = fixture.nativeElement.querySelectorAll('li.cfi-item');
    expect(items.length).toBe(3);
    expect(items[0].querySelector('.cfi-text').textContent).toBe('Open the developer dashboard.');
    expect(items[1].querySelector('.cfi-text').textContent).toBe('Create a new app.');
    expect(items[2].querySelector('.cfi-text').textContent).toBe('Copy the client secret.');
  });

  it('renders one shared-copy-value per instruction value', async () => {
    const instructions: ConfigFlowInstructionDto[] = [
      {
        text: 'Paste this redirect URI into the app settings.',
        values: [{ label: 'Redirect URI', value: 'http://192.168.1.5:8080/callback' }],
      },
    ];
    fixture.componentRef.setInput('instructions', instructions);
    fixture.detectChanges();
    await fixture.whenStable();

    const values = fixture.nativeElement.querySelectorAll('shared-copy-value');
    expect(values.length).toBe(1);
  });

  it('renders no shared-copy-value when values is absent or empty', async () => {
    const instructions: ConfigFlowInstructionDto[] = [
      { text: 'No values here.' },
      { text: 'Empty values array.', values: [] },
    ];
    fixture.componentRef.setInput('instructions', instructions);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('shared-copy-value').length).toBe(0);
  });
});
