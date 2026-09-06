import { AppStrings } from '@macro-deck/runtime';

export const EXPORT_PASSWORD_MIN_LENGTH = 8;

export const GENERATED_EXPORT_PASSWORD_LENGTH = 24;

const ALPHABET = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';

export type ExportPasswordStrength = 'too-short' | 'weak' | 'fair' | 'strong';

export interface ExportPasswordAssessment {
  readonly strength: ExportPasswordStrength;
  readonly label: string;
  readonly acceptable: boolean;
}

export function generateExportPassword(length = GENERATED_EXPORT_PASSWORD_LENGTH): string {
  const limit = 256 - (256 % ALPHABET.length);
  const out: string[] = [];
  const buffer = new Uint8Array(length);

  while (out.length < length) {
    crypto.getRandomValues(buffer);
    for (const byte of buffer) {
      if (byte < limit && out.length < length) {
        out.push(ALPHABET[byte % ALPHABET.length]);
      }
    }
  }

  return out.join('');
}

export type PasswordTranslator = (key: string, args?: Record<string, unknown>) => string;

export function assessExportPassword(
  password: string,
  t: PasswordTranslator,
): ExportPasswordAssessment {
  if (password.length < EXPORT_PASSWORD_MIN_LENGTH) {
    return {
      strength: 'too-short',
      label: t(AppStrings.Portable.Password.TooShort, { minimum: EXPORT_PASSWORD_MIN_LENGTH }),
      acceptable: false
    };
  }

  const classes = [/[a-z]/, /[A-Z]/, /[0-9]/, /[^a-zA-Z0-9]/].filter(pattern => pattern.test(password)).length;
  const score = password.length + 4 * (classes - 1);

  if (score >= 24) {
    return { strength: 'strong', label: t(AppStrings.Portable.Password.Strong), acceptable: true };
  }

  if (score >= 16) {
    return { strength: 'fair', label: t(AppStrings.Portable.Password.Fair), acceptable: true };
  }

  return { strength: 'weak', label: t(AppStrings.Portable.Password.Weak), acceptable: true };
}
