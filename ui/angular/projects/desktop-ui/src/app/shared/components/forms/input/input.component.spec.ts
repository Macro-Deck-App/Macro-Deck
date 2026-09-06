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
