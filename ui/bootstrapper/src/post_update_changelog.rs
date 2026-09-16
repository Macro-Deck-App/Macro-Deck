use std::fs;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

use crate::logging;
use crate::updater;

const STORE_FILE_NAME: &str = "post-update-changelog.json";

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PostUpdateChangelog {
    pub version: String,
    pub notes: String,
    pub published_at: Option<String>,
}

fn store_path(dir: &Path) -> PathBuf {
    dir.join(STORE_FILE_NAME)
}

pub(crate) fn write(dir: &Path, entry: &PostUpdateChangelog) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    let json = serde_json::to_string(entry).map_err(std::io::Error::other)?;
    fs::write(store_path(dir), json)
}

pub(crate) fn read(dir: &Path) -> Option<PostUpdateChangelog> {
    let raw = fs::read_to_string(store_path(dir)).ok()?;
    serde_json::from_str(&raw).ok()
}

pub(crate) fn clear(dir: &Path) {
    let path = store_path(dir);
    if path.exists() {
        if let Err(error) = fs::remove_file(&path) {
            logging::warn(&format!(
                "[post-update-changelog] could not remove {}: {error}",
                path.display()
            ));
        }
    }
}

pub(crate) fn entry_for_install(
    version: &str,
    notes: Option<&str>,
    published_at: Option<&str>,
) -> Option<PostUpdateChangelog> {
    let notes = notes.map(str::trim).filter(|notes| !notes.is_empty())?;
    Some(PostUpdateChangelog {
        version: version.to_string(),
        notes: notes.to_string(),
        published_at: published_at.map(str::to_string),
    })
}

// A stored entry whose version is not the running one belongs to an install
// that never completed (a declined UAC prompt, a failed installer) and is discarded.
pub(crate) fn pending_for(dir: &Path, current_version: &str) -> Option<PostUpdateChangelog> {
    match read(dir) {
        Some(entry) if entry.version == current_version => Some(entry),
        _ => {
            clear(dir);
            None
        }
    }
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

pub fn remember(app: &AppHandle, version: &str, notes: Option<&str>, published_at: Option<&str>) {
    let dir = config_dir(app);
    let Some(entry) = entry_for_install(version, notes, published_at) else {
        clear(&dir);
        return;
    };
    if let Err(error) = write(&dir, &entry) {
        logging::warn(&format!(
            "[post-update-changelog] could not remember the changelog of {version}: {error}"
        ));
    }
}

pub fn forget(app: &AppHandle) {
    clear(&config_dir(app));
}

#[tauri::command]
pub fn get_post_update_changelog(app: AppHandle) -> Option<PostUpdateChangelog> {
    pending_for(&config_dir(&app), &updater::current_version(&app))
}

#[tauri::command]
pub fn dismiss_post_update_changelog(app: AppHandle) {
    forget(&app);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(line: u32) -> PathBuf {
        std::env::temp_dir().join(format!(
            "macro-deck-post-update-changelog-{}-{line}",
            std::process::id()
        ))
    }

    #[test]
    fn the_changelog_of_the_installed_version_is_offered_after_the_restart() {
        let dir = temp_dir(line!());
        let entry =
            entry_for_install("3.2.0", Some("## Fixes\n- a fix"), Some("2026-09-16")).unwrap();
        write(&dir, &entry).unwrap();

        assert_eq!(pending_for(&dir, "3.2.0"), Some(entry));
        assert!(
            read(&dir).is_some(),
            "the entry stays until the user dismissed it"
        );

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_install_that_never_completed_offers_nothing_and_forgets_the_entry() {
        let dir = temp_dir(line!());
        let entry = entry_for_install("3.2.0", Some("notes"), None).unwrap();
        write(&dir, &entry).unwrap();

        assert_eq!(pending_for(&dir, "3.1.0"), None);
        assert_eq!(read(&dir), None);

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_fresh_install_has_no_changelog_to_show() {
        let dir = temp_dir(line!());

        assert_eq!(pending_for(&dir, "3.2.0"), None);
    }

    #[test]
    fn dismissing_removes_the_entry() {
        let dir = temp_dir(line!());
        write(
            &dir,
            &entry_for_install("3.2.0", Some("notes"), None).unwrap(),
        )
        .unwrap();

        clear(&dir);

        assert_eq!(pending_for(&dir, "3.2.0"), None);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_release_without_notes_remembers_nothing() {
        assert_eq!(entry_for_install("3.2.0", None, None), None);
        assert_eq!(entry_for_install("3.2.0", Some("  \n"), None), None);
    }

    #[test]
    fn an_unreadable_file_is_treated_as_no_changelog() {
        let dir = temp_dir(line!());
        fs::create_dir_all(&dir).unwrap();
        fs::write(store_path(&dir), "not json").unwrap();

        assert_eq!(pending_for(&dir, "3.2.0"), None);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_entry_serializes_as_camel_case() {
        let value = serde_json::to_value(
            entry_for_install("3.2.0", Some("notes"), Some("2026-09-16")).unwrap(),
        )
        .unwrap();
        assert_eq!(value["version"], "3.2.0");
        assert_eq!(value["notes"], "notes");
        assert_eq!(value["publishedAt"], "2026-09-16");
    }
}
