import { Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SettingsRowComponent } from './settings-row.component';

@Component({
  standalone: true,
  imports: [SettingsRowComponent],
  template: `
    <div class="dialog">
      <shared-settings-row label="Design" description="Wähle, wie Macro Deck aussehen soll.">
        <div class="wide-control">Hell Dunkel Systemeinstellung</div>
      </shared-settings-row>
    </div>
  `,
  styles: '.dialog { width: 240px; overflow: hidden; } .wide-control { width: 200px; }',
})
class HostComponent {}

describe('SettingsRowComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('gives a control too wide to sit beside its label a line of its own', () => {
    const dialog: HTMLElement = fixture.nativeElement.querySelector('.dialog');
    const info: HTMLElement = fixture.nativeElement.querySelector('.settings-row__info');
    const control: HTMLElement = fixture.nativeElement.querySelector('.settings-row__control');

    expect(dialog.scrollWidth).toBeLessThanOrEqual(dialog.clientWidth);
    expect(control.getBoundingClientRect().top).toBeGreaterThanOrEqual(info.getBoundingClientRect().bottom);
  });
});
