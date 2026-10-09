import { ClientAppStrings } from '@macro-deck/runtime';
import { Client, SignInResult } from './client';
import { createButton, createErrorBanner, createInput, createOtpInput, createSegmentedControl } from './ui';

type LoginMode = 'code' | 'password';

const PAIRING_CODE_HINT_ID = 'wc-login-code-hint';
const PAIRING_CODE_LENGTH = 6;

// Full-width, Arabic-Indic, Extended Arabic-Indic and Devanagari digits, mapped by hand because
// String#normalize is missing on the legacy targets and would not cover the last three anyway.
const DIGIT_ZEROS = [0xff10, 0x0660, 0x06f0, 0x0966];

function normalizePairingCode(raw: string): string {
  let result = '';
  for (let i = 0; i < raw.length; i++) {
    const ch = raw.charAt(i);
    if (/\s/.test(ch)) continue;
    const point = raw.charCodeAt(i);
    let mapped = ch;
    for (let z = 0; z < DIGIT_ZEROS.length; z++) {
      if (point >= DIGIT_ZEROS[z] && point <= DIGIT_ZEROS[z] + 9) mapped = String(point - DIGIT_ZEROS[z]);
    }
    result += mapped;
  }
  return result;
}

export function createLoginForm(client: Client): { element: HTMLElement } {
  const t = (key: string) => client.translate(key);

  const form = document.createElement('form');
  form.className = 'wc-login';

  const header = document.createElement('div');
  header.className = 'wc-login-header';

  const logo = document.createElement('img');
  logo.className = 'wc-login-logo';
  logo.src = './logo.png';
  logo.alt = '';
  header.appendChild(logo);

  const title = document.createElement('h1');
  title.className = 'wc-login-title';
  // The product's name, which is not translated.
  title.textContent = 'Macro Deck';
  header.appendChild(title);

  const subtitle = document.createElement('p');
  subtitle.className = 'wc-login-subtitle';
  subtitle.textContent = t(ClientAppStrings.Auth.SignInSubtitle);
  header.appendChild(subtitle);
  form.appendChild(header);

  const errorSlot = document.createElement('div');
  form.appendChild(errorSlot);

  const modeSwitch = createSegmentedControl({
    ariaLabel: t(ClientAppStrings.Auth.SignIn),
    stretch: true,
    value: 'code',
    options: [
      { value: 'code', label: t(ClientAppStrings.Auth.PairingCode) },
      { value: 'password', label: t(ClientAppStrings.Auth.Password) },
    ],
    onChange: value => {
      if (submitting) modeSwitch.setValue(mode);
      else setMode(value as LoginMode, true);
    },
  });
  form.appendChild(modeSwitch.element);

  const fields = document.createElement('div');
  fields.className = 'wc-login-fields';
  form.appendChild(fields);

  const code = createOtpInput({
    length: PAIRING_CODE_LENGTH,
    ariaLabel: t(ClientAppStrings.Auth.PairingCode),
    name: 'pairing-code',
    describedBy: PAIRING_CODE_HINT_ID,
    autofocus: true,
    normalize: normalizePairingCode,
    onComplete: () => submitCode(),
  });
  const codeHint = document.createElement('p');
  codeHint.className = 'wc-login-hint';
  codeHint.id = PAIRING_CODE_HINT_ID;
  codeHint.textContent = t(ClientAppStrings.Auth.PairingCodeHint);
  const codeFields = [code.element, codeHint];

  const username = createInput({
    ariaLabel: t(ClientAppStrings.Auth.Username),
    placeholder: t(ClientAppStrings.Auth.Username),
    name: 'username',
  });
  const password = createInput({
    type: 'password',
    ariaLabel: t(ClientAppStrings.Auth.Password),
    placeholder: t(ClientAppStrings.Auth.Password),
    name: 'password',
  });
  const passwordFields = [
    labelled(t(ClientAppStrings.Auth.Username), username.element),
    labelled(t(ClientAppStrings.Auth.Password), password.element),
  ];

  const submit = createButton({
    label: t(ClientAppStrings.Auth.SignIn),
    variant: 'primary',
    type: 'submit',
  });
  form.appendChild(submit.element);

  const forgotHint = document.createElement('p');
  forgotHint.className = 'wc-login-hint';
  forgotHint.textContent = t(ClientAppStrings.Auth.ForgotPasswordHint);

  let mode: LoginMode = 'code';
  let submitting = false;

  function setMode(next: LoginMode, focus: boolean): void {
    mode = next;
    fields.textContent = '';
    const shown = next === 'code' ? codeFields : passwordFields;
    for (let index = 0; index < shown.length; index++) fields.appendChild(shown[index]);
    if (next === 'password') form.appendChild(forgotHint);
    else if (forgotHint.parentNode === form) form.removeChild(forgotHint);
    errorSlot.textContent = '';
    if (focus) (next === 'code' ? code : username).focus();
  }

  setMode('code', false);

  const setSubmitting = (value: boolean): void => {
    submitting = value;
    submit.setLoading(value);
    code.setDisabled(value);
    username.setDisabled(value);
    password.setDisabled(value);
  };

  const showError = (message: string): void => {
    errorSlot.textContent = '';
    errorSlot.appendChild(createErrorBanner({
      message,
      dismissLabel: t(ClientAppStrings.Feedback.Dismiss),
    }).element);
  };

  const run = (attempt: Promise<SignInResult>, onRefused: () => void): void => {
    errorSlot.textContent = '';
    setSubmitting(true);
    void attempt.then(
      result => {
        setSubmitting(false);
        if (result.ok) return;
        onRefused();
        showError(result.message || t(ClientAppStrings.Auth.SignInFailed));
      },
      () => {
        setSubmitting(false);
        showError(t(ClientAppStrings.Auth.SignInFailed));
      });
  };

  function submitCode(): void {
    if (submitting) return;
    const value = code.value();
    // Never sent: the host would still count it against this device's sign-in attempts.
    if (value.length !== PAIRING_CODE_LENGTH) {
      code.setInvalid(true);
      code.focus();
      return;
    }
    run(client.signInWithPairingCode(value), () => {
      code.setValue('');
      code.focus();
    });
  }

  form.onsubmit = event => {
    event.preventDefault();
    if (submitting) return;

    if (mode === 'code') {
      submitCode();
      return;
    }

    const name = username.value().replace(/^\s+|\s+$/g, '');
    if (name.length === 0 || password.value().length === 0) return;

    // The password field is cleared and the name kept: a typo is nearly always in the password,
    // and retyping the name for it is the kind of thing that makes a wall-mounted tablet hated.
    run(client.signIn(name, password.value()), () => password.setValue(''));
  };

  return { element: form };
}

function labelled(text: string, control: HTMLElement): HTMLElement {
  const label = document.createElement('label');
  label.className = 'wc-login-field';

  const caption = document.createElement('span');
  caption.className = 'wc-login-label';
  caption.textContent = text;
  label.appendChild(caption);
  label.appendChild(control);

  return label;
}
