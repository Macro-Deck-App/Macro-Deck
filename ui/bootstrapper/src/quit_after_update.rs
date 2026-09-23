use std::fs;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

use crate::logging;
use crate::updater;

// The NSIS installer relaunches the app after every updater-driven install, so an
// install made on quit leaves this marker for the relaunched process to exit on.
const MARKER_FILE_NAME: &str = "quit-after-update.json";

const MARKER_MAX_AGE_SECS: u64 = 15 * 60;

// Passed to every Windows installer run; NSIS forwards it to the relaunch through /ARGS.
pub const RELAUNCH_ARG: &str = "--relaunched-by-updater";

#[derive(Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct QuitMarker {
    version: String,
    written_at: u64,
}

fn marker_path(dir: &Path) -> PathBuf {
    dir.join(MARKER_FILE_NAME)
}

pub(crate) fn write(dir: &Path, version: &str, now: u64) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    let marker = QuitMarker {
        version: version.to_string(),
        written_at: now,
    };
    let json = serde_json::to_string(&marker).map_err(std::io::Error::other)?;
    fs::write(marker_path(dir), json)
}

pub(crate) fn clear(dir: &Path) {
    let path = marker_path(dir);
    if path.exists() {
        if let Err(error) = fs::remove_file(&path) {
            logging::warn(&format!(
                "[quit-after-update] could not remove {}: {error}",
                path.display()
            ));
        }
    }
}

// A marker for another version belongs to an install that never completed, and a
// stale one to a relaunch that never came; neither may swallow a launch by the user.
pub(crate) fn take(dir: &Path, current_version: &str, now: u64, relaunched: bool) -> bool {
    let marker = fs::read_to_string(marker_path(dir))
        .ok()
        .and_then(|raw| serde_json::from_str::<QuitMarker>(&raw).ok());
    clear(dir);
    relaunched
        && marker.is_some_and(|marker| {
            marker.version == current_version
                && now >= marker.written_at
                && now - marker.written_at <= MARKER_MAX_AGE_SECS
        })
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

pub fn remember(app: &AppHandle, version: &str, now: u64) {
    if let Err(error) = write(&config_dir(app), version, now) {
        logging::warn(&format!(
            "[quit-after-update] could not remember the quit for {version}: {error}"
        ));
    }
}

pub fn forget(app: &AppHandle) {
    clear(&config_dir(app));
}

pub fn take_for_launch(app: &AppHandle) -> bool {
    take(
        &config_dir(app),
        &updater::current_version(app),
        updater::now_secs(),
        std::env::args().any(|argument| argument == RELAUNCH_ARG),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(line: u32) -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-quit-after-update-{}-{line}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&dir);
        dir
    }

    #[test]
    fn the_relaunch_right_after_an_install_on_quit_exits_once() {
        let dir = temp_dir(line!());
        write(&dir, "3.1.0", 1_000).unwrap();

        assert!(take(&dir, "3.1.0", 1_030, true));
        assert!(
            !take(&dir, "3.1.0", 1_031, true),
            "a later relaunch by the updater must start normally"
        );
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_launch_by_the_user_never_exits_even_with_a_fresh_marker() {
        let dir = temp_dir(line!());
        write(&dir, "3.1.0", 1_000).unwrap();

        assert!(!take(&dir, "3.1.0", 1_030, false));
        assert!(
            !marker_path(&dir).exists(),
            "the leftover marker is cleared on any launch"
        );
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_relaunch_after_a_manual_install_starts_normally() {
        let dir = temp_dir(line!());

        assert!(!take(&dir, "3.1.0", 1_030, true));
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_launch_after_a_failed_install_starts_normally() {
        let dir = temp_dir(line!());
        write(&dir, "3.1.0", 1_000).unwrap();

        assert!(!take(&dir, "3.0.0", 1_030, true));
        assert!(!marker_path(&dir).exists());
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_marker_left_behind_long_ago_never_swallows_a_launch() {
        let dir = temp_dir(line!());
        write(&dir, "3.1.0", 1_000).unwrap();

        assert!(!take(&dir, "3.1.0", 1_000 + MARKER_MAX_AGE_SECS + 1, true));
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_marker_from_the_future_never_swallows_a_launch() {
        let dir = temp_dir(line!());
        write(&dir, "3.1.0", 5_000).unwrap();

        assert!(!take(&dir, "3.1.0", 1_000, true));
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_unreadable_marker_starts_normally() {
        let dir = temp_dir(line!());
        fs::create_dir_all(&dir).unwrap();
        fs::write(marker_path(&dir), "not json").unwrap();

        assert!(!take(&dir, "3.1.0", 1_000, true));
        assert!(!marker_path(&dir).exists());
        let _ = fs::remove_dir_all(&dir);
    }
}
