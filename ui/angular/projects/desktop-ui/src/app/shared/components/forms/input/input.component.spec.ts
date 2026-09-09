import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { InputComponent } from './input.component';

describe('InputComponent', () => {
  let fixture: ComponentFixture<InputComponent>;
  let component: InputComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [InputComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(InputComponent);
    component = fixture.componentInstance;
  });

  function control(): HTMLInputElement | HTMLTextAreaElement {
    return fixture.nativeElement.querySelector('.control');
  }

  it('renders a single input by default and forwards the type', () => {
    component.type = 'number';
    fixture.detectChanges();

    const el = control();
    expect(el.tagName).toBe('INPUT');
    expect((el as HTMLInputElement).type).toBe('number');
  });

  it('keeps what is being typed into a number box when the model only renormalises it', () => {
    component.type = 'number';
    component.onInput('1.10');

    component.writeValue(1.1);

    expect(component.value).toBe('1.10');
  });

  it('takes a number box to the value the model changed to', () => {
    component.type = 'number';
    component.onInput('14');

    component.writeValue(20);

    expect(component.value).toBe(20);
  });

  it('brings a number box back to the model value once typing is over', () => {
    component.type = 'number';
    component.onInput('1e3');
    component.writeValue(1000);

    expect(component.value).toBe('1e3');

    component.onBlur();

    expect(component.value).toBe(1000);
  });

  it('brings an emptied number box back to a model value the caller kept', () => {
    component.type = 'number';
    component.writeValue(14);
    component.onInput('');

    component.onBlur();

    expect(component.value).toBe(14);
  });

  it('leaves an emptied number box empty for a model that accepted the empty value', () => {
    component.type = 'number';
    component.writeValue(14);
    component.onInput('');
    component.writeValue('');

    component.onBlur();

    expect(component.value).toBe('');
  });

  it('renders a textarea when multiline is set', () => {
    component.multiline = true;
    fixture.detectChanges();

    expect(control().tagName).toBe('TEXTAREA');
  });

  it('writes the model value into the control', async () => {
    component.writeValue('hello');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.value).toBe('hello');
    expect((control() as HTMLInputElement).value).toBe('hello');
  });

  it('emits through the registered onChange when the value changes', () => {
    const changes: Array<string | number> = [];
    component.registerOnChange(v => changes.push(v));

    component.onInput('typed');

    expect(changes).toEqual(['typed']);
    expect(component.value).toBe('typed');
  });

  it('reflects the invalid state as a host class', () => {
    component.invalid = true;
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).classList.contains('is-invalid')).toBeTrue();
  });

  it('focuses the control after view init when autofocus is set', () => {
    component.autofocus = true;
    fixture.detectChanges();

    expect(document.activeElement).toBe(control());
  });

  it('forwards ariaLabel to the control as aria-label', () => {
    component.ariaLabel = 'Search actions';
    fixture.detectChanges();

    expect(control().getAttribute('aria-label')).toBe('Search actions');
  });

  it('omits aria-label when ariaLabel is not set', () => {
    fixture.detectChanges();

    expect(control().hasAttribute('aria-label')).toBeFalse();
  });
});
