import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EmptyStateComponent } from './empty-state.component';

@Component({
  standalone: true,
  imports: [EmptyStateComponent],
  template: `<shared-empty-state
    icon="code"
    heading="No variables"
    message="Create one to get started."
    [compact]="compact()" />`,
})
class HostComponent {
  readonly compact = signal(false);
}

describe('EmptyStateComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  function emptyState(): HTMLElement {
    return fixture.nativeElement.querySelector('shared-empty-state') as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('renders the heading text', () => {
    const title = emptyState().querySelector('.es-title') as HTMLElement;
    expect(title.textContent?.trim()).toBe('No variables');
  });

  it('does not leak the heading onto the host as a native title tooltip', () => {
    // A static `title` attribute on <shared-empty-state> would both bind the input and stay on
    // the host element, showing a browser tooltip over the whole placeholder; the input is named
    // `heading` precisely to avoid that (issue #233).
    expect(emptyState().hasAttribute('title')).toBe(false);
  });

  it('renders the message', () => {
    const message = emptyState().querySelector('.es-message') as HTMLElement;
    expect(message.textContent?.trim()).toBe('Create one to get started.');
  });

  it('is full-size by default', () => {
    expect(emptyState().querySelector('.es')?.classList.contains('es-compact')).toBe(false);
    expect(emptyState().querySelector('.es-icon')?.classList.contains('icon-2xl')).toBe(true);
  });

  // The point of the variant: a dropdown must not grow taller when it has nothing to show. The
  // spacing itself lives in CSS (the global tokens are not loaded here), so this pins the wiring.
  it('switches to the compact variant and a smaller icon', () => {
    fixture.componentInstance.compact.set(true);
    fixture.detectChanges();

    expect(emptyState().querySelector('.es')?.classList.contains('es-compact')).toBe(true);
    expect(emptyState().querySelector('.es-icon')?.classList.contains('icon-lg')).toBe(true);
  });
});
