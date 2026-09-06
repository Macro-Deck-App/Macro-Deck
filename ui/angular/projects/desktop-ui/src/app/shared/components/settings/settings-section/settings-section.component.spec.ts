import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SettingsSectionComponent } from './settings-section.component';

@Component({
  standalone: true,
  imports: [SettingsSectionComponent],
  template: `
    <shared-settings-section heading="Theme" [description]="description()">
      <span class="projected">body</span>
    </shared-settings-section>
  `,
})
class HostComponent {
  readonly description = signal<string | undefined>(undefined);
}

describe('SettingsSectionComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  function section(): HTMLElement {
    return fixture.nativeElement.querySelector('shared-settings-section');
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
    const title = section().querySelector('.settings-section__title') as HTMLElement;
    expect(title.textContent?.trim()).toBe('Theme');
  });

  it('does not leak the heading onto the host as a native title tooltip', () => {
    expect(section().hasAttribute('title')).toBe(false);
  });

  it('projects body content', () => {
    expect(section().querySelector('.projected')?.textContent).toBe('body');
  });

  it('renders the description only when provided', () => {
    expect(section().querySelector('.settings-section__desc')).toBeNull();

    fixture.componentInstance.description.set('How Macro Deck looks.');
    fixture.detectChanges();

    const desc = section().querySelector('.settings-section__desc') as HTMLElement;
    expect(desc.textContent?.trim()).toBe('How Macro Deck looks.');
  });
});
