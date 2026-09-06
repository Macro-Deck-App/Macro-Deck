import { Component, ViewChild, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DropdownMenuComponent } from './dropdown-menu.component';

@Component({
  standalone: true,
  imports: [DropdownMenuComponent],
  template: `
    <shared-dropdown-menu [isOpen]="true" [closeOnItemClick]="closeOnItemClick">
      <ng-template #menuContent>
        <div class="probe-panel">
          <span class="probe-heading">Heading</span>
          <button type="button" class="probe-item">Item</button>
          <button type="button" class="probe-disabled" disabled>Disabled</button>
          <input class="probe-input" />
        </div>
      </ng-template>
    </shared-dropdown-menu>
  `,
})
class HostComponent {
  closeOnItemClick = true;
  @ViewChild(DropdownMenuComponent) menu!: DropdownMenuComponent;
}

describe('DropdownMenuComponent closing on an item', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  const click = (selector: string): void => {
    fixture.nativeElement.querySelector(selector).click();
    fixture.detectChanges();
  };

  const render = (closeOnItemClick: boolean): void => {
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    host.closeOnItemClick = closeOnItemClick;
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();
  });

  it('closes once an item is activated', () => {
    render(true);
    click('.probe-item');

    expect(host.menu.isOpen).toBeFalse();
  });

  it('stays open for a click that hit no item', () => {
    // The panel has padding and headings of its own; closing on those would dismiss the menu when
    // the user merely missed.
    render(true);
    click('.probe-heading');

    expect(host.menu.isOpen).toBeTrue();
  });

  it('stays open for a disabled item', () => {
    render(true);
    click('.probe-disabled');

    expect(host.menu.isOpen).toBeTrue();
  });

  it('leaves a panel-style menu alone, which is why this is opt-in', () => {
    // The grid settings menu projects a whole settings panel; closing on the first click on one of
    // its controls would make it unusable.
    render(false);

    click('.probe-item');

    expect(host.menu.isOpen).toBeTrue();
  });
});
