use std::path::{Path, PathBuf};
use std::sync::OnceLock;

use tauri::{AppHandle, Manager, Runtime, WebviewWindowBuilder};

pub const MARKER_FILE_NAME: &str = "portable";
const DATA_DIRECTORY_NAME: &str = "Data";
const BOOTSTRAPPER_DIRECTORY_NAME: &str = "bootstrapper";

// Keep the marker lookup in sync with MacroDeckDataRootResolver: both anchor on the shell
// executable, so the host and the bootstrapper always pick the same Data folder.
pub fn data_dir() -> Option<PathBuf> {
    static DATA_DIR: OnceLock<Option<PathBuf>> = OnceLock::new();
    DATA_DIR
        .get_or_init(|| {
            if crate::host::is_development_build() {
                return None;
            }
            let executable = crate::host::shell_executable()?;
            data_dir_for(executable.parent()?, |path| path.is_file())
        })
        .clone()
}

fn data_dir_for(app_dir: &Path, is_file: impl Fn(&Path) -> bool) -> Option<PathBuf> {
    is_file(&app_dir.join(MARKER_FILE_NAME)).then(|| app_dir.join(DATA_DIRECTORY_NAME))
}

pub fn config_dir_in(data_dir: &Path) -> PathBuf {
    data_dir.join(BOOTSTRAPPER_DIRECTORY_NAME)
}

pub fn app_config_dir<R: Runtime>(app: &AppHandle<R>) -> tauri::Result<PathBuf> {
    match data_dir() {
        Some(dir) => Ok(config_dir_in(&dir)),
        None => app.path().app_config_dir(),
    }
}

pub fn app_cache_dir<R: Runtime>(app: &AppHandle<R>) -> tauri::Result<PathBuf> {
    match data_dir() {
        Some(dir) => Ok(config_dir_in(&dir).join("cache")),
        None => app.path().app_cache_dir(),
    }
}

// WebView2 shares one environment per process, so every window has to name the same folder.
pub fn with_webview_data<'a, R: Runtime, M: Manager<R>>(
    builder: WebviewWindowBuilder<'a, R, M>,
) -> WebviewWindowBuilder<'a, R, M> {
    match data_dir() {
        Some(dir) => builder.data_directory(dir.join("webview")),
        None => builder,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn marker_beside_the_app_selects_its_data_folder() {
        let app_dir = Path::new("/portable/Macro Deck");
        let marker = app_dir.join(MARKER_FILE_NAME);
        let data = data_dir_for(app_dir, |path| path == marker);
        assert_eq!(data, Some(app_dir.join("Data")));
    }

    #[test]
    fn no_marker_means_no_portable_data_folder() {
        assert_eq!(data_dir_for(Path::new("/installed"), |_| false), None);
    }

    #[test]
    fn bootstrapper_state_stays_inside_the_data_folder() {
        let data = Path::new("/portable/Data");
        assert!(config_dir_in(data).starts_with(data));
    }
}
