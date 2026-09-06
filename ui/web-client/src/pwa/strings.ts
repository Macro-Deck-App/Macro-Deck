import { ClientAppStrings, Strings } from '@macro-deck/runtime';
import { type AppUpdatePhase } from './app-update';
import { type PwaAvailability } from './pwa-install';

export function updateStatusKey(phase: AppUpdatePhase): string {
  switch (phase) {
    case 'checking': return ClientAppStrings.WebClient.Update.Checking;
    case 'available': return ClientAppStrings.WebClient.Install.UpdateDescription;
    case 'applying': return ClientAppStrings.WebClient.Update.Applying;
    case 'reloading': return ClientAppStrings.WebClient.Update.Reloading;
    case 'checkFailed': return ClientAppStrings.WebClient.Update.CheckFailed;
    case 'applyFailed': return ClientAppStrings.WebClient.Update.ApplyFailed;
    default: return ClientAppStrings.WebClient.Update.UpToDate;
  }
}

export function updateActionKey(phase: AppUpdatePhase): string | null {
  switch (phase) {
    case 'checking':
    case 'applying':
    case 'reloading':
    case 'unsupported':
      return null;
    case 'available': return ClientAppStrings.WebClient.Install.UpdateNow;
    case 'checkFailed':
    case 'applyFailed':
      return Strings.Common.Retry;
    default: return ClientAppStrings.WebClient.Update.CheckAction;
  }
}

export function installStatusKey(availability: PwaAvailability): string {
  return availability === 'runningAsApp'
    ? ClientAppStrings.WebClient.Install.Installed
    : ClientAppStrings.WebClient.Install.StatusBrowserTab;
}

export function installHintKey(availability: PwaAvailability): string | null {
  switch (availability) {
    case 'manualOnly': return ClientAppStrings.WebClient.Install.InstallManualIos;
    case 'requiresHttps': return ClientAppStrings.WebClient.Install.RequiresHttps;
    case 'unsupported': return ClientAppStrings.WebClient.Install.Unsupported;
    default: return null;
  }
}
