import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { ApiService } from '@shared';
import { SecretInputComponent } from './secret-input.component';

describe('SecretInputComponent', () => {
  function render(): HTMLButtonElement[] {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [SecretInputComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: { onNotification: () => EMPTY } },
      ],
    });
    const fixture = TestBed.createComponent(SecretInputComponent);
    fixture.componentRef.setInput('kind', 'Password');
    fixture.componentRef.setInput('value', { $secret: 'abc' });
    fixture.detectChanges();
    return [...fixture.nativeElement.querySelectorAll('shared-button button')] as HTMLButtonElement[];
  }

  it('keeps the stored row on one height', () => {
    const md = render().map(b => [...b.classList].find(c => c.startsWith('sb-icon') || c === 'sb-md'));
    expect(md).toEqual(['sb-icon', 'sb-md', 'sb-icon']);
  });
});
