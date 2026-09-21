// A copy of the desktop UI's theme for windows the bootstrapper paints itself: the
// preference lives on the host, which is not reachable while it is down.

use std::fs;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

use crate::logging;
use crate::update_window;

const STORE_FILE_NAME: &str = "appearance.json";

#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum ThemeMode {
    Light,
    Dark,
    System,
}

#[derive(Clone, PartialEq, Eq, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Appearance {
    pub theme_mode: ThemeMode,
    pub accent_color: String,
}

impl Default for Appearance {
    fn default() -> Self {
        Self {
            theme_mode: ThemeMode::System,
            accent_color: "#2196f3".to_string(),
        }
    }
}

pub(crate) fn parse(theme_mode: &str, accent_color: &str) -> Option<Appearance> {
    let theme_mode = match theme_mode {
        "light" => ThemeMode::Light,
        "dark" => ThemeMode::Dark,
        "system" => ThemeMode::System,
        _ => return None,
    };
    let accent = accent_color.trim();
    let hex = accent.strip_prefix('#')?;
    if hex.len() != 6 || !hex.chars().all(|c| c.is_ascii_hexdigit()) {
        return None;
    }
    Some(Appearance {
        theme_mode,
        accent_color: accent.to_ascii_lowercase(),
    })
}

fn store_path(dir: &Path) -> PathBuf {
    dir.join(STORE_FILE_NAME)
}

pub(crate) fn read_stored(dir: &Path) -> Option<Appearance> {
    let raw = fs::read_to_string(store_path(dir)).ok()?;
    let stored: Appearance = serde_json::from_str(&raw).ok()?;
    let mode = match stored.theme_mode {
        ThemeMode::Light => "light",
        ThemeMode::Dark => "dark",
        ThemeMode::System => "system",
    };
    parse(mode, &stored.accent_color)
}

pub(crate) fn write_stored(dir: &Path, appearance: &Appearance) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    let json = serde_json::to_string(appearance).map_err(std::io::Error::other)?;
    fs::write(store_path(dir), json)
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

pub fn current(app: &AppHandle) -> Appearance {
    read_stored(&config_dir(app)).unwrap_or_default()
}

#[tauri::command]
pub fn set_appearance(app: AppHandle, theme_mode: String, accent_color: String) {
    let Some(appearance) = parse(&theme_mode, &accent_color) else {
        return;
    };
    let dir = config_dir(&app);
    if read_stored(&dir).as_ref() == Some(&appearance) {
        return;
    }
    if let Err(error) = write_stored(&dir, &appearance) {
        logging::warn(&format!(
            "[appearance] could not persist the appearance: {error}"
        ));
        return;
    }
    update_window::apply_appearance(&app);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-appearance-{name}-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&dir);
        dir
    }

    #[test]
    fn a_valid_theme_and_accent_round_trip_through_disk() {
        let dir = temp_dir("round-trip");
        let appearance = parse("dark", "#FF5722").unwrap();

        write_stored(&dir, &appearance).unwrap();

        assert_eq!(read_stored(&dir), Some(appearance));
        assert_eq!(read_stored(&dir).unwrap().accent_color, "#ff5722");
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn anything_but_a_known_mode_and_a_hex_colour_is_rejected() {
        assert_eq!(parse("sepia", "#ff5722"), None);
        assert_eq!(parse("dark", "red"), None);
        assert_eq!(parse("dark", "#ff572"), None);
        assert_eq!(parse("dark", "#ff5722;background:url(x)"), None);
    }

    #[test]
    fn a_hand_edited_file_with_an_unsafe_colour_is_ignored() {
        let dir = temp_dir("unsafe");
        fs::create_dir_all(&dir).unwrap();
        fs::write(
            store_path(&dir),
            r#"{"themeMode":"light","accentColor":"red;x"}"#,
        )
        .unwrap();

        assert_eq!(read_stored(&dir), None);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn without_a_stored_appearance_the_window_follows_the_system_like_the_app_default() {
        assert_eq!(Appearance::default().theme_mode, ThemeMode::System);
    }
}
