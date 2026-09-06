import { ClientAppStrings } from '@macro-deck/runtime';
import { Client } from './client';
import { createButton, createErrorBanner, createInput } from './ui';

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

  const username = createInput({
    ariaLabel: t(ClientAppStrings.Auth.Username),
    placeholder: t(ClientAppStrings.Auth.Username),
    autofocus: true,
    name: 'username',
  });
  form.appendChild(labelled(t(ClientAppStrings.Auth.Username), username.element));

  const password = createInput({
    type: 'password',
    ariaLabel: t(ClientAppStrings.Auth.Password),
    placeholder: t(ClientAppStrings.Auth.Password),
    name: 'password',
  });
  form.appendChild(labelled(t(ClientAppStrings.Auth.Password), password.element));

  const submit = createButton({
    label: t(ClientAppStrings.Auth.SignIn),
    variant: 'primary',
    type: 'submit',
  });
  form.appendChild(submit.element);

  let submitting = false;

  const setSubmitting = (value: boolean): void => {
    submitting = value;
    submit.setLoading(value);
    username.setDisabled(value);
    password.setDisabled(value);
  };

  form.onsubmit = event => {
    event.preventDefault();
    if (submitting) return;

    const name = username.value().replace(/^\s+|\s+$/g, '');
    if (name.length === 0 || password.value().length === 0) return;

    errorSlot.textContent = '';
    setSubmitting(true);

    void client.signIn(name, password.value()).then(
      result => {
        setSubmitting(false);
        if (result.ok) return;
        // The password field is cleared and the name kept: a typo is nearly always in the password,
        // and retyping the name for it is the kind of thing that makes a wall-mounted tablet hated.
        password.setValue('');
        errorSlot.textContent = '';
        errorSlot.appendChild(createErrorBanner({
          message: result.message || t(ClientAppStrings.Auth.SignInFailed),
          dismissLabel: t(ClientAppStrings.Feedback.Dismiss),
        }).element);
      },
      () => {
        setSubmitting(false);
        errorSlot.textContent = '';
        errorSlot.appendChild(createErrorBanner({
          message: t(ClientAppStrings.Auth.SignInFailed),
          dismissLabel: t(ClientAppStrings.Feedback.Dismiss),
        }).element);
      });
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
