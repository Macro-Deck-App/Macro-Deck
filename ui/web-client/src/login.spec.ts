import { ClientAppStrings } from '@macro-deck/runtime';
import { Client, type SignInResult } from './client';
import { createLoginForm } from './login';

describe('login form', () => {
  let client: Client;
  let form: HTMLFormElement;
  const refused: SignInResult = { ok: false, message: 'refused' };

  const segment = (key: string) => Array.prototype.slice.call(form.querySelectorAll('.wc-seg-option'))
    .filter((candidate: HTMLButtonElement) => candidate.textContent === client.translate(key))[0] as HTMLButtonElement;
  const input = (name: string) => form.querySelector(`input[name="${name}"]`) as HTMLInputElement | null;
  const type = (value: string) => {
    const field = input('pairing-code') as HTMLInputElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
  };
  const submit = () => form.dispatchEvent(new Event('submit', { cancelable: true }));
  const settle = () => new Promise(resolve => setTimeout(resolve, 0));

  beforeEach(() => {
    client = new Client(() => 'http://host', 'client-1');
    spyOn(client, 'signInWithPairingCode').and.returnValue(Promise.resolve(refused));
    spyOn(client, 'signIn').and.returnValue(Promise.resolve(refused));
    form = createLoginForm(client).element as HTMLFormElement;
    document.body.appendChild(form);
  });

  afterEach(() => form.remove());

  it('offers the pairing code as a six-digit field that brings up the numeric keypad', () => {
    const field = input('pairing-code') as HTMLInputElement;

    expect(form.querySelectorAll('.wc-otp-cell').length).toBe(6);
    expect(field.getAttribute('inputmode')).toBe('numeric');
    expect(field.getAttribute('autocomplete')).toBe('one-time-code');
  });

  it('switches to username and password and back to the pairing code', () => {
    segment(ClientAppStrings.Auth.Password).click();
    expect(input('pairing-code')).toBeNull();
    expect(input('password')).not.toBeNull();

    segment(ClientAppStrings.Auth.PairingCode).click();
    expect(input('pairing-code')).not.toBeNull();
    expect(input('password')).toBeNull();
  });

  it('signs in as soon as the sixth digit is typed', () => {
    type('12345');
    expect(client.signInWithPairingCode).not.toHaveBeenCalled();

    type('123456');
    expect(client.signInWithPairingCode).toHaveBeenCalledWith('123456');
  });

  it('marks an incomplete code as invalid instead of sending it', () => {
    type('12345');
    submit();

    expect(client.signInWithPairingCode).not.toHaveBeenCalled();
    expect((input('pairing-code') as HTMLInputElement).getAttribute('aria-invalid')).toBe('true');
  });

  it('accepts a pasted code with a space and keeps only six digits in the boxes', () => {
    type('123 456');

    expect(client.signInWithPairingCode).toHaveBeenCalledWith('123456');
    const shown = Array.prototype.slice.call(form.querySelectorAll('.wc-otp-cell'))
      .map((cell: HTMLElement) => cell.textContent).join('');
    expect(shown).toBe('123456');
  });

  it('accepts digits from full-width, Arabic and Devanagari keyboards', async () => {
    for (const typed of ['１２３４５６', '١٢٣٤٥٦', '۱۲۳۴۵۶', '१२३४५६']) {
      type(typed);
      await settle();
    }

    expect((client.signInWithPairingCode as jasmine.Spy).calls.allArgs())
      .toEqual([['123456'], ['123456'], ['123456'], ['123456']]);
  });

  it('empties the boxes after a refused code', async () => {
    type('123456');
    await settle();

    expect((input('pairing-code') as HTMLInputElement).value).toBe('');
  });

  it('signs in with username and password after switching', () => {
    segment(ClientAppStrings.Auth.Password).click();
    (input('username') as HTMLInputElement).value = ' owner ';
    (input('password') as HTMLInputElement).value = 'secret';
    submit();

    expect(client.signIn).toHaveBeenCalledWith('owner', 'secret');
  });
});
