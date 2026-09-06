// Bootstrapper-owned preference for whether and how the periodic check acts on
// a new release (issue #715): off skips the feed entirely, notify-only asks
// before downloading, and automatic keeps the previous behaviour of
// downloading first and asking only before installing. The preference has to
// live here rather than on the host: it must be readable before the host is
// up, and it decides whether the feed is contacted at all.
//
// Kept in its own module rather than growing updater.rs (already ~1300
// lines): this file owns the on-disk preference and its Tauri commands;
// updater.rs owns turning the resolved mode into a periodic-check action.

use std::fs;
use std::path::{Path, PathBuf};

use serde::Serialize;
use tauri::{AppHandle, Manager};

use crate::logging;
use crate::updater::{self, UpdateInstallStrategy};

const STORE_FILE_NAME: &str = "update-mode";

#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum UpdateMode {
    Off,
    NotifyOnly,
    Automatic,
}

impl UpdateMode {
    pub fn as_str(self) -> &'static str {
        match self {
            UpdateMode::Off => "off",
            UpdateMode::NotifyOnly => "notifyOnly",
            UpdateMode::Automatic => "automatic",
        }
    }
}

const DEFAULT_MODE: UpdateMode = UpdateMode::NotifyOnly;

pub(crate) fn resolve_mode(stored: Option<UpdateMode>) -> UpdateMode {
    stored.unwrap_or(DEFAULT_MODE)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateModeStatus {
    pub mode: UpdateMode,
    pub automatic_supported: bool,
}

impl UpdateModeStatus {
    fn for_mode(mode: UpdateMode) -> Self {
        Self {
            mode,
            automatic_supported: updater::install_strategy() == UpdateInstallStrategy::InApp,
        }
    }
}

pub(crate) fn parse_stored(raw: &str) -> Option<UpdateMode> {
    match raw.trim().to_ascii_lowercase().as_str() {
        "off" => Some(UpdateMode::Off),
        "notifyonly" | "notify-only" | "notify_only" => Some(UpdateMode::NotifyOnly),
        "automatic" => Some(UpdateMode::Automatic),
        _ => None,
    }
}

fn store_path(dir: &Path) -> PathBuf {
    dir.join(STORE_FILE_NAME)
}

pub(crate) fn read_stored(dir: &Path) -> Option<UpdateMode> {
    fs::read_to_string(store_path(dir))
        .ok()
        .and_then(|raw| parse_stored(&raw))
}

pub(crate) fn write_stored(dir: &Path, mode: UpdateMode) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    fs::write(store_path(dir), mode.as_str())
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

pub fn current(app: &AppHandle) -> UpdateMode {
    let dir = config_dir(app);
    let stored = read_stored(&dir);
    let mode = resolve_mode(stored);
    if stored.is_none() {
        if let Err(error) = write_stored(&dir, mode) {
            logging::warn(&format!(
                "[update-mode] could not persist the default update mode: {error}"
            ));
        }
    }
    mode
}

pub fn set(app: &AppHandle, mode: UpdateMode) -> Result<(), String> {
    write_stored(&config_dir(app), mode).map_err(|error| error.to_string())
}

#[tauri::command]
pub fn get_update_mode(app: AppHandle) -> UpdateModeStatus {
    UpdateModeStatus::for_mode(current(&app))
}

#[tauri::command]
pub fn set_update_mode(app: AppHandle, mode: String) -> Result<UpdateModeStatus, String> {
    let parsed = parse_stored(&mode).ok_or_else(|| format!("unknown update mode: {mode}"))?;
    set(&app, parsed)?;
    Ok(UpdateModeStatus::for_mode(parsed))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::update_channel;

    #[test]
    fn nothing_stored_resolves_to_notify_only_even_with_a_channel_file_present() {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-update-mode-{}-{}",
            std::process::id(),
            line!()
        ));
        assert!(read_stored(&dir).is_none());
        assert_eq!(resolve_mode(read_stored(&dir)), UpdateMode::NotifyOnly);

        update_channel::write_stored(&dir, update_channel::UpdateChannel::Stable)
            .expect("channel write must succeed");
        assert!(
            read_stored(&dir).is_none(),
            "an existing channel file must not be mistaken for a stored mode"
        );
        assert_eq!(
            resolve_mode(read_stored(&dir)),
            UpdateMode::NotifyOnly,
            "an install that predates the update mode must not be migrated to the old background-download behaviour"
        );

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn parse_stored_accepts_the_canonical_tokens() {
        assert_eq!(parse_stored("off"), Some(UpdateMode::Off));
        assert_eq!(parse_stored("notifyOnly"), Some(UpdateMode::NotifyOnly));
        assert_eq!(parse_stored("automatic"), Some(UpdateMode::Automatic));
    }

    #[test]
    fn parse_stored_is_case_and_whitespace_insensitive_and_accepts_notify_only_variants() {
        assert_eq!(parse_stored("NotifyOnly"), Some(UpdateMode::NotifyOnly));
        assert_eq!(parse_stored(" notifyonly\n"), Some(UpdateMode::NotifyOnly));
        assert_eq!(parse_stored("notify-only"), Some(UpdateMode::NotifyOnly));
        assert_eq!(parse_stored("notify_only"), Some(UpdateMode::NotifyOnly));
    }

    #[test]
    fn parse_stored_rejects_unknown_tokens() {
        assert_eq!(parse_stored(""), None);
        assert_eq!(parse_stored("   "), None);
        assert_eq!(parse_stored("notify"), None);
        assert_eq!(parse_stored("true"), None);
        assert_eq!(parse_stored("stable"), None);
    }

    #[test]
    fn each_variant_serializes_to_the_canonical_token_and_round_trips() {
        for mode in [
            UpdateMode::Off,
            UpdateMode::NotifyOnly,
            UpdateMode::Automatic,
        ] {
            let serialized = serde_json::to_value(mode).unwrap();
            let token = serialized.as_str().unwrap();
            assert_eq!(parse_stored(token), Some(mode));
        }
        assert_eq!(serde_json::to_value(UpdateMode::Off).unwrap(), "off");
        assert_eq!(
            serde_json::to_value(UpdateMode::NotifyOnly).unwrap(),
            "notifyOnly"
        );
        assert_eq!(
            serde_json::to_value(UpdateMode::Automatic).unwrap(),
            "automatic"
        );
    }

    #[test]
    fn a_rejected_token_leaves_the_previously_stored_value_untouched() {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-update-mode-{}-{}",
            std::process::id(),
            line!()
        ));
        write_stored(&dir, UpdateMode::Off).expect("write must succeed");

        assert!(parse_stored("nonsense").is_none());
        assert_eq!(read_stored(&dir), Some(UpdateMode::Off));

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_written_mode_round_trips_through_disk() {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-update-mode-{}-{}",
            std::process::id(),
            line!()
        ));
        assert!(read_stored(&dir).is_none());

        write_stored(&dir, UpdateMode::Off).expect("write must succeed");
        assert_eq!(read_stored(&dir), Some(UpdateMode::Off));
        assert_eq!(resolve_mode(read_stored(&dir)), UpdateMode::Off);

        write_stored(&dir, UpdateMode::Automatic).expect("overwrite must succeed");
        assert_eq!(read_stored(&dir), Some(UpdateMode::Automatic));

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_status_serializes_automatic_supported_as_camel_case_and_keeps_the_stored_mode() {
        let value = serde_json::to_value(UpdateModeStatus {
            mode: UpdateMode::Automatic,
            automatic_supported: true,
        })
        .unwrap();
        assert_eq!(value["mode"], "automatic");
        assert_eq!(value["automaticSupported"], true);

        let value = serde_json::to_value(UpdateModeStatus {
            mode: UpdateMode::Automatic,
            automatic_supported: false,
        })
        .unwrap();
        assert_eq!(
            value["mode"], "automatic",
            "the stored mode must be reported even when unsupported"
        );
        assert_eq!(value["automaticSupported"], false);
    }

    #[test]
    fn setting_a_mode_does_not_disturb_the_stored_update_channel() {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-update-mode-{}-{}",
            std::process::id(),
            line!()
        ));
        update_channel::write_stored(&dir, update_channel::UpdateChannel::Beta)
            .expect("channel write must succeed");

        write_stored(&dir, UpdateMode::Off).expect("mode write must succeed");

        assert_eq!(
            update_channel::read_stored(&dir),
            Some(update_channel::UpdateChannel::Beta)
        );
        assert_eq!(read_stored(&dir), Some(UpdateMode::Off));

        let _ = fs::remove_dir_all(&dir);
    }
}
