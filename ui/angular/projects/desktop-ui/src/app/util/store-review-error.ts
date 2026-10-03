import { AppStrings, StoreReviewWriteError } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';

export function storeReviewErrorMessage(localization: LocalizationService, error: StoreReviewWriteError | null): string {
  const strings = AppStrings.Store.Reviews;
  const seconds = error?.retryAfterSeconds;
  const hasSeconds = typeof seconds === 'number' && seconds > 0;
  switch (error?.code) {
    case 'sign_in_required':
      return localization.translateKey(strings.Error.SignInRequired);
    case 'download_required':
      return localization.translateKey(strings.Error.DownloadRequired);
    case 'account_suspended':
      return localization.translateKey(strings.Error.AccountSuspended);
    case 'forbidden':
      return localization.translateKey(strings.Error.Forbidden);
    case 'moderated':
      return localization.translateKey(strings.Error.Moderated);
    case 'gone':
      return localization.translateKey(strings.Error.Gone);
    case 'cooldown':
      return hasSeconds
        ? localization.translateKey(strings.Error.CooldownSeconds, { count: Math.ceil(seconds) })
        : localization.translateKey(strings.Error.Cooldown);
    case 'retry_later':
      return hasSeconds
        ? localization.translateKey(strings.Error.RetryLaterSeconds, { count: Math.ceil(seconds) })
        : localization.translateKey(strings.Error.RetryLater);
    case 'validation':
      switch (error.field) {
        case 'Rating':
          return localization.translateKey(strings.Validation.RatingRequired);
        case 'Title':
          return localization.translateKey(strings.Error.InvalidTitle);
        case 'Body':
          return localization.translateKey(strings.Validation.BodyLength);
        default:
          return localization.translateKey(strings.Error.Validation);
      }
    case 'not_found':
      return localization.translateKey(strings.Error.NotFound);
    default:
      return localization.translateKey(strings.Error.PlatformUnavailable);
  }
}
