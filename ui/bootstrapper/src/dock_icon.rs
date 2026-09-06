// Bootstrapper-owned preference for whether closing the window to the menu bar
// also drops the macOS Dock icon (issue #378). It lives here rather than on the
// host because the close handler has to answer it synchronously on the UI
// thread, with no round trip and no dependency on the host being up.
//
// Both commands are granted on every platform, unlike the platform-scoped
// install_update grant (capabilities/in-app-update.json): get_hide_dock_icon is
// itself what the UI asks to decide whether to render the toggle at all, so it
// has to be reachable everywhere to answer `supported: false`. install_update
// can be ACL-scoped because a different, always-granted command carries its
// platform signal (checkForUpdate().installStrategy); get_shell_info exposes no
// platform field.

use std::fs;
use std::path::{Path, PathBuf};

use serde::Serialize;
use tauri::{AppHandle, Manager};

const STORE_FILE_NAME: &str = "hide-dock-icon";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DockIconStatus {
    pub supported: bool,
    pub enabled: bool,
}

impl DockIconStatus {
    fn for_enabled(enabled: bool) -> Self {
        let supported = cfg!(target_os = "macos");
        Self {
            supported,
            enabled: supported && enabled,
        }
    }
}

pub(crate) fn parse_stored(raw: &str) -> Option<bool> {
    match raw.trim().to_ascii_lowercase().as_str() {
        "true" => Some(true),
        "false" => Some(false),
        _ => None,
    }
}

fn store_path(dir: &Path) -> PathBuf {
    dir.join(STORE_FILE_NAME)
}

pub(crate) fn read_stored(dir: &Path) -> Option<bool> {
    fs::read_to_string(store_path(dir))
        .ok()
        .and_then(|raw| parse_stored(&raw))
}

pub(crate) fn write_stored(dir: &Path, enabled: bool) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    fs::write(store_path(dir), if enabled { "true" } else { "false" })
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

pub fn is_enabled(app: &AppHandle) -> bool {
    read_stored(&config_dir(app)).unwrap_or(false)
}

#[tauri::command]
pub fn get_hide_dock_icon(app: AppHandle) -> DockIconStatus {
    DockIconStatus::for_enabled(is_enabled(&app))
}

#[tauri::command]
pub fn set_hide_dock_icon(app: AppHandle, enabled: bool) -> Result<DockIconStatus, String> {
    // Never write on a platform that has no Dock: the preference would be dead
    // state that a later macOS install of the same profile could not have set.
    if !cfg!(target_os = "macos") {
        return Ok(DockIconStatus::for_enabled(false));
    }
    write_stored(&config_dir(&app), enabled).map_err(|error| error.to_string())?;
    Ok(DockIconStatus::for_enabled(enabled))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_stored_distinguishes_unset_from_a_stored_false() {
        assert_eq!(parse_stored("true"), Some(true));
        assert_eq!(parse_stored("false"), Some(false));
        assert_eq!(parse_stored(""), None);
        assert_eq!(parse_stored("   "), None);
        assert_eq!(parse_stored("nonsense"), None);
    }

    #[test]
    fn parse_stored_reads_a_written_token_case_and_whitespace_insensitively() {
        assert_eq!(parse_stored("true\n"), Some(true));
        assert_eq!(parse_stored("True"), Some(true));
        assert_eq!(parse_stored(" FALSE "), Some(false));
    }

    #[test]
    fn the_status_reports_support_only_on_macos() {
        let status = DockIconStatus::for_enabled(true);
        assert_eq!(status.supported, cfg!(target_os = "macos"));
        assert_eq!(status.enabled, cfg!(target_os = "macos"));
        assert!(!DockIconStatus::for_enabled(false).enabled);
    }

    #[test]
    fn the_status_serializes_as_camel_case() {
        let value = serde_json::to_value(DockIconStatus::for_enabled(true)).unwrap();
        assert_eq!(value["supported"], cfg!(target_os = "macos"));
        assert_eq!(value["enabled"], cfg!(target_os = "macos"));
    }

    #[test]
    fn a_written_preference_round_trips_through_disk() {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-hide-dock-icon-{}-{}",
            std::process::id(),
            line!()
        ));
        assert_eq!(read_stored(&dir), None);

        write_stored(&dir, true).expect("write must succeed");
        assert_eq!(read_stored(&dir), Some(true));

        write_stored(&dir, false).expect("overwrite must succeed");
        assert_eq!(read_stored(&dir), Some(false));

        let _ = fs::remove_dir_all(&dir);
    }
}
