import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DetailPageComponent } from './detail-page.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

describe('DetailPageComponent', () => {
  let fixture: ComponentFixture<DetailPageComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DetailPageComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(DetailPageComponent);
    fixture.componentInstance.backLabel = 'Back to the store';
    fixture.detectChanges();
  });

  function surface(): HTMLElement {
    return fixture.nativeElement.querySelector('.dp-surface') as HTMLElement;
  }

  function backButton(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.dp-back') as HTMLButtonElement;
  }

  it('does not emit close as soon as the back control is activated, only once the exit animation ends', () => {
    const closed = jasmine.createSpy('closed');
    fixture.componentInstance.close.subscribe(closed);

    backButton().click();
    fixture.detectChanges();
    expect(closed).not.toHaveBeenCalled();

    surface().dispatchEvent(new Event('animationend'));
    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('leaves the page visible and does not emit close when a requested close is cancelled', () => {
    const closed = jasmine.createSpy('closed');
    fixture.componentInstance.close.subscribe(closed);

    fixture.componentInstance.requestClose();
    fixture.componentInstance.cancelClose();
    fixture.detectChanges();

    surface().dispatchEvent(new Event('animationend'));

    expect(closed).not.toHaveBeenCalled();
    expect(surface().classList.contains('dp-leaving')).toBeFalse();
  });
});
