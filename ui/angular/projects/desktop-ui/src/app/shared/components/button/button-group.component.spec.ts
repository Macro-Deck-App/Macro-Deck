import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ButtonGroupComponent } from './button-group.component';

describe('ButtonGroupComponent', () => {
  let fixture: ComponentFixture<ButtonGroupComponent>;

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function render(inputs: Record<string, unknown> = {}): HTMLElement {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    return host();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ButtonGroupComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(ButtonGroupComponent);
  });

  it('defaults to a start-aligned row with the standard gap', () => {
    expect(render().className).toBe('sbg sbg-align-start sbg-gap-sm');
  });

  it('reflects the alignment on the host', () => {
    expect(render({ align: 'end' }).classList).toContain('sbg-align-end');
    expect(render({ align: 'between' }).classList).toContain('sbg-align-between');
  });

  it('reflects the gap on the host', () => {
    expect(render({ gap: 'xs' }).classList).toContain('sbg-gap-xs');
    expect(render({ gap: 'md' }).classList).toContain('sbg-gap-md');
  });

  it('opts into wrapping', () => {
    expect(render({ wrap: true }).classList).toContain('sbg-wrap');
  });

  it('exposes role=group only when the row is named', () => {
    expect(render().getAttribute('role')).toBeNull();
    expect(render({ ariaLabel: 'Log levels' }).getAttribute('role')).toBe('group');
    expect(host().getAttribute('aria-label')).toBe('Log levels');
  });
});
