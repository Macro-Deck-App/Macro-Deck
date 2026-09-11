use serde::{Deserialize, Serialize};
use tauri::ipc::{InvokeBody, Request};
use tauri::{AppHandle, Emitter, Manager};
use tauri_plugin_dialog::{DialogExt, FilePath};
use tauri_plugin_opener::OpenerExt;

use crate::host;
use crate::localization::{self, keys};
use crate::logging;
use crate::updater;
use crate::window;

pub const AUTOSTART_ARG: &str = "--autostart";

pub const MINIMIZED_ARG: &str = "--minimized";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ShellInfo {
    pub shell_version: String,
    pub tauri_version: String,
    pub webview_version: Option<String>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct OpenDialogOptions {
    pub directory: Option<bool>,
    pub extensions: Option<Vec<String>>,
}

const SAVE_FILE_NAME_HEADER: &str = "x-macrodeck-file-name";
const SAVE_FILE_EXTENSIONS_HEADER: &str = "x-macrodeck-extensions";

const SAVE_FILE_FALLBACK_NAME: &str = "export";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SaveFileResult {
    pub saved: bool,
    pub canceled: bool,
    pub path: Option<String>,
    pub error: Option<String>,
}

impl SaveFileResult {
    fn canceled() -> Self {
        Self {
            saved: false,
            canceled: true,
            path: None,
            error: None,
        }
    }

    fn saved(path: String) -> Self {
        Self {
            saved: true,
            canceled: false,
            path: Some(path),
            error: None,
        }
    }

    fn failed(error: String) -> Self {
        Self {
            saved: false,
            canceled: false,
            path: None,
            error: Some(error),
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CursorPosition {
    pub x: i32,
    pub y: i32,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct DroppedPath {
    pub path: String,
    pub directory: bool,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct FileDropPayload {
    pub kind: &'static str,
    pub paths: Vec<DroppedPath>,
    pub x: f64,
    pub y: f64,
}

pub const FILE_DROP_EVENT: &str = "file-drop";

static CURSOR_ERROR_LOGGED: std::sync::atomic::AtomicBool =
    std::sync::atomic::AtomicBool::new(false);

pub fn should_start_hidden(_app: &AppHandle) -> bool {
    host::is_packaged() && has_launch_flags(std::env::args())
}

pub fn has_launch_flags(args: impl Iterator<Item = String>) -> bool {
    let mut autostart = false;
    let mut minimized = false;
    for arg in args {
        autostart |= arg == AUTOSTART_ARG;
        minimized |= arg == MINIMIZED_ARG;
    }
    autostart && minimized
}

#[tauri::command]
pub fn get_host_port() -> Option<u16> {
    host::current_port()
}

#[tauri::command]
pub fn get_shell_info(app: AppHandle) -> ShellInfo {
    ShellInfo {
        // Not package_info().version: on a beta RPM that is the mapped version,
        // which would make Settings -> About show a different shell version
        // than the host, and poison it in bug reports (issue #271).
        shell_version: updater::current_version(&app),
        tauri_version: tauri::VERSION.to_string(),
        webview_version: tauri::webview_version().ok(),
    }
}

#[tauri::command]
pub fn get_cursor_position(app: AppHandle) -> Option<CursorPosition> {
    match app.cursor_position() {
        Ok(position) => Some(CursorPosition {
            x: position.x.round() as i32,
            y: position.y.round() as i32,
        }),
        Err(error) => {
            // Logged once: the UI samples this on a timer, and a platform that
            // cannot report the cursor fails every single tick.
            if !CURSOR_ERROR_LOGGED.swap(true, std::sync::atomic::Ordering::Relaxed) {
                logging::error(&format!("[bridge] could not read cursor position: {error}"));
            }
            None
        }
    }
}

#[tauri::command]
pub fn open_external(app: AppHandle, url: String) -> bool {
    let Ok(parsed) = url.parse::<tauri::Url>() else {
        return false;
    };
    if parsed.scheme() != "http" && parsed.scheme() != "https" {
        return false;
    }
    app.opener().open_url(url, None::<&str>).is_ok()
}

#[tauri::command]
pub async fn show_open_dialog(app: AppHandle, options: OpenDialogOptions) -> Option<String> {
    let directory = options.directory.unwrap_or(false);
    let extensions = options.extensions.unwrap_or_default();

    // Picking a file on Windows goes through our own dialog: the shell rejects app execution
    // aliases unless FOS_NOVALIDATE is set, and rfd exposes no way to set it (issue #864).
    #[cfg(windows)]
    if !directory {
        let parent = app
            .get_webview_window(window::MAIN_WINDOW)
            .and_then(|window| window.hwnd().ok())
            .map(|hwnd| hwnd.0 as isize);
        let label = localization::t(keys::FILES_OPEN_DIALOG_FILTER);
        // The dialog is modal and blocks its thread for as long as it is open, which must not be
        // a runtime worker other commands are waiting on.
        return tauri::async_runtime::spawn_blocking(move || {
            crate::windows_file_dialog::pick_file(parent, label, extensions)
        })
        .await
        .ok()
        .flatten()
        .map(|path| plain_path(&path.display().to_string()));
    }

    let mut dialog = app.dialog().file();
    if let Some(window) = app.get_webview_window(window::MAIN_WINDOW) {
        dialog = dialog.set_parent(&window);
    }
    if !directory && !extensions.is_empty() {
        let filters: Vec<&str> = extensions.iter().map(String::as_str).collect();
        dialog = dialog.add_filter(localization::t(keys::FILES_OPEN_DIALOG_FILTER), &filters);
    }

    let (tx, mut rx) = tauri::async_runtime::channel(1);
    let respond = move |path: Option<FilePath>| {
        let _ = tx.try_send(path);
    };
    if directory {
        dialog.pick_folder(respond);
    } else {
        dialog.pick_file(respond);
    }

    rx.recv()
        .await
        .flatten()
        .and_then(|path| path.into_path().ok())
        .map(|path| path.display().to_string())
}

#[tauri::command]
pub async fn save_file(app: AppHandle, request: Request<'_>) -> Result<SaveFileResult, String> {
    let InvokeBody::Raw(data) = request.body() else {
        return Err("save_file expects a raw request body".to_string());
    };

    let headers = request.headers();
    let file_name = headers
        .get(SAVE_FILE_NAME_HEADER)
        .and_then(|value| value.to_str().ok())
        .map(sanitize_save_file_name)
        .unwrap_or_else(|| SAVE_FILE_FALLBACK_NAME.to_string());
    let extensions = headers
        .get(SAVE_FILE_EXTENSIONS_HEADER)
        .and_then(|value| value.to_str().ok())
        .map(parse_save_file_extensions)
        .unwrap_or_default();

    let mut dialog = app.dialog().file().set_file_name(&file_name);
    if let Some(window) = app.get_webview_window(window::MAIN_WINDOW) {
        dialog = dialog.set_parent(&window);
    }
    if !extensions.is_empty() {
        let filters: Vec<&str> = extensions.iter().map(String::as_str).collect();
        dialog = dialog.add_filter("Macro Deck", &filters);
    }

    let (tx, mut rx) = tauri::async_runtime::channel(1);
    dialog.save_file(move |path: Option<FilePath>| {
        let _ = tx.try_send(path);
    });

    let Some(path) = rx
        .recv()
        .await
        .flatten()
        .and_then(|path| path.into_path().ok())
    else {
        return Ok(SaveFileResult::canceled());
    };

    match std::fs::write(&path, data) {
        Ok(()) => Ok(SaveFileResult::saved(path.display().to_string())),
        Err(error) => {
            logging::error(&format!(
                "[bridge] could not write {}: {error}",
                path.display()
            ));
            Ok(SaveFileResult::failed(error.to_string()))
        }
    }
}

pub fn sanitize_save_file_name(raw: &str) -> String {
    let decoded = percent_encoding::percent_decode_str(raw).decode_utf8_lossy();
    let name = decoded
        .rsplit(['/', '\\'])
        .next()
        .unwrap_or_default()
        .trim();
    if name.is_empty() || name == "." || name == ".." {
        return SAVE_FILE_FALLBACK_NAME.to_string();
    }
    name.to_string()
}

pub fn parse_save_file_extensions(raw: &str) -> Vec<String> {
    raw.split(',')
        .map(|extension| extension.trim().trim_start_matches('.'))
        .filter(|extension| {
            !extension.is_empty() && extension.chars().all(|c| c.is_ascii_alphanumeric())
        })
        .map(str::to_string)
        .collect()
}

pub fn webview_position(x: f64, y: f64, scale_factor: f64) -> (f64, f64) {
    let divisor = if cfg!(windows) && scale_factor > 0.0 {
        scale_factor
    } else {
        1.0
    };
    (x / divisor, y / divisor)
}

pub fn forward_drag_drop(window: &tauri::WebviewWindow, event: &tauri::DragDropEvent) {
    let scale_factor = window.scale_factor().unwrap_or(1.0);
    let (kind, paths, position) = match event {
        tauri::DragDropEvent::Enter { paths, position } => {
            ("enter", dropped_paths(paths), Some(position))
        }
        tauri::DragDropEvent::Over { position } => ("over", Vec::new(), Some(position)),
        tauri::DragDropEvent::Drop { paths, position } => {
            ("drop", dropped_paths(paths), Some(position))
        }
        _ => ("leave", Vec::new(), None),
    };
    let (x, y) = position
        .map(|position| webview_position(position.x, position.y, scale_factor))
        .unwrap_or((0.0, 0.0));
    let payload = FileDropPayload { kind, paths, x, y };
    if let Err(error) = window.emit(FILE_DROP_EVENT, payload) {
        logging::error(&format!("[bridge] could not emit drag-drop event: {error}"));
    }
}

fn dropped_paths(paths: &[std::path::PathBuf]) -> Vec<DroppedPath> {
    paths
        .iter()
        .map(|path| {
            let resolved = std::fs::canonicalize(path).unwrap_or_else(|_| path.clone());
            DroppedPath {
                path: plain_path(&resolved.display().to_string()),
                directory: resolved.is_dir(),
            }
        })
        .collect()
}

fn plain_path(path: &str) -> String {
    match path.strip_prefix(r"\\?\") {
        Some(rest) => match rest.strip_prefix(r"UNC\") {
            Some(unc) => format!(r"\\{unc}"),
            None => rest.to_string(),
        },
        None => path.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn args(list: &[&str]) -> impl Iterator<Item = String> {
        list.iter()
            .map(|s| s.to_string())
            .collect::<Vec<_>>()
            .into_iter()
    }

    // Canonicalizing a dropped path resolves symlinks (issue #395), but on Windows it also returns a
    // verbatim path - and that string ends up in a Launch Application parameter, which ShellExecuteEx
    // will not take.
    #[test]
    fn a_canonicalized_path_loses_its_verbatim_prefix() {
        assert_eq!(
            plain_path(r"\\?\C:\Program Files\App\app.exe"),
            r"C:\Program Files\App\app.exe"
        );
        assert_eq!(
            plain_path(r"\\?\UNC\server\share\app.exe"),
            r"\\server\share\app.exe"
        );
        assert_eq!(
            plain_path("/Applications/Calculator.app"),
            "/Applications/Calculator.app"
        );
    }

    // WebView2 sends absent JS properties as JSON null; a strict Vec/bool field
    // makes the whole invoke fail before the picker ever opens (issue #122).
    #[test]
    fn open_dialog_options_accept_null_fields() {
        let options: OpenDialogOptions =
            serde_json::from_str(r#"{"directory":null,"extensions":null}"#).unwrap();

        assert_eq!(options.directory, None);
        assert_eq!(options.extensions, None);
    }

    #[test]
    fn open_dialog_options_accept_missing_fields() {
        let options: OpenDialogOptions = serde_json::from_str("{}").unwrap();

        assert_eq!(options.directory, None);
        assert_eq!(options.extensions, None);
    }

    #[test]
    fn open_dialog_options_read_provided_values() {
        let options: OpenDialogOptions =
            serde_json::from_str(r#"{"directory":true,"extensions":["png"]}"#).unwrap();

        assert_eq!(options.directory, Some(true));
        assert_eq!(options.extensions, Some(vec!["png".to_string()]));
    }

    #[test]
    fn drag_position_scales_only_where_the_runtime_reports_device_pixels() {
        let (x, y) = webview_position(200.0, 100.0, 2.0);

        if cfg!(windows) {
            assert_eq!((x, y), (100.0, 50.0));
        } else {
            assert_eq!((x, y), (200.0, 100.0));
        }
    }

    #[test]
    fn drag_position_survives_a_zero_scale_factor() {
        assert_eq!(webview_position(10.0, 20.0, 0.0), (10.0, 20.0));
    }

    #[test]
    fn save_file_name_is_decoded_and_reduced_to_a_bare_name() {
        assert_eq!(
            sanitize_save_file_name("M%C3%BCller.macroDeckProfile"),
            "Müller.macroDeckProfile"
        );
        assert_eq!(sanitize_save_file_name("..%2F..%2Fetc%2Fpasswd"), "passwd");
        assert_eq!(
            sanitize_save_file_name("C%3A%5Ctemp%5Cpack.macroDeckIconPack"),
            "pack.macroDeckIconPack"
        );
    }

    #[test]
    fn save_file_name_falls_back_when_unusable() {
        assert_eq!(sanitize_save_file_name(""), "export");
        assert_eq!(sanitize_save_file_name("   "), "export");
        assert_eq!(sanitize_save_file_name(".."), "export");
        assert_eq!(sanitize_save_file_name("some%2Ffolder%2F"), "export");
    }

    #[test]
    fn shell_bridge_sends_the_save_file_metadata_headers() {
        let script = include_str!("shell-bridge.js");

        assert!(script.contains("'save_file'"));
        assert!(script.contains(SAVE_FILE_NAME_HEADER));
        assert!(script.contains(SAVE_FILE_EXTENSIONS_HEADER));
        assert!(script.contains("encodeURIComponent"));
    }

    const SHARED_COMMANDS: [&str; 19] = [
        "get_host_port",
        "get_shell_info",
        "get_cursor_position",
        "open_external",
        "show_open_dialog",
        "save_file",
        "take_opened_files",
        // Only the macOS menu ever parks an action, but the grant is shared:
        // the UI drains the slot on every platform rather than branching on one.
        "take_menu_action",
        "set_hotkey_capture",
        "check_for_update",
        "get_update_state",
        "request_update_check",
        "cancel_update_download",
        "get_update_channel",
        "set_update_channel",
        // Granted everywhere although Automatic is meaningless on Linux
        // (issue #715): the preference is still safe to read and set there,
        // and get_update_mode reports automaticSupported so the UI can hide
        // the option without knowing the platform itself.
        "get_update_mode",
        "set_update_mode",
        // Granted everywhere although the setting is macOS-only: the get is
        // what the UI asks to find out whether to render it at all (issue #378).
        "get_hide_dock_icon",
        "set_hide_dock_icon",
    ];

    const IN_APP_UPDATE_COMMANDS: [&str; 1] = ["install_update"];

    #[test]
    fn every_app_command_is_wired_up_end_to_end() {
        let handler = include_str!("main.rs");
        let manifest = include_str!("../build.rs");
        let dev_capability = include_str!("../capabilities/main.json");
        let runtime_capability = include_str!("window.rs");
        let script = include_str!("shell-bridge.js");

        for command in SHARED_COMMANDS {
            let permission = format!("allow-{}", command.replace('_', "-"));
            assert!(
                handler.contains(command),
                "{command} is missing from the invoke handler in main.rs"
            );
            assert!(
                manifest.contains(command),
                "{command} is missing from the app manifest in build.rs"
            );
            assert!(
                dev_capability.contains(&permission),
                "{permission} is missing from capabilities/main.json"
            );
            assert!(
                runtime_capability.contains(&permission),
                "{permission} is missing from the runtime capability in window.rs"
            );
            assert!(
                script.contains(&format!("'{command}'")),
                "{command} is never invoked from shell-bridge.js, so the UI cannot reach it"
            );
        }

        let in_app_update_capability = include_str!("../capabilities/in-app-update.json");
        for command in IN_APP_UPDATE_COMMANDS {
            let permission = format!("allow-{}", command.replace('_', "-"));
            assert!(
                handler.contains(command),
                "{command} is missing from the invoke handler in main.rs"
            );
            assert!(
                manifest.contains(command),
                "{command} is missing from the app manifest in build.rs"
            );
            assert!(
                in_app_update_capability.contains(&permission),
                "{permission} is missing from capabilities/in-app-update.json"
            );
            assert!(
                runtime_capability.contains(&permission),
                "{permission} is missing from the runtime capability in window.rs"
            );
        }
    }

    #[test]
    fn shell_version_resolves_through_the_baked_release_version() {
        let source = include_str!("bridge.rs");
        assert!(source.contains("shell_version: updater::current_version(&app)"));
    }

    #[test]
    fn in_app_install_is_not_granted_on_linux() {
        let dev_capability = include_str!("../capabilities/main.json");
        assert!(
            !dev_capability.contains("allow-install-update"),
            "capabilities/main.json must not grant allow-install-update"
        );

        let in_app_update_capability = include_str!("../capabilities/in-app-update.json");
        let parsed: serde_json::Value = serde_json::from_str(in_app_update_capability)
            .expect("capabilities/in-app-update.json must be valid JSON");
        let platforms: Vec<&str> = parsed["platforms"]
            .as_array()
            .expect("in-app-update.json must declare a platforms array")
            .iter()
            .map(|value| value.as_str().expect("platforms entries must be strings"))
            .collect();
        assert!(platforms.contains(&"windows"));
        assert!(platforms.contains(&"macOS"));
        assert!(!platforms.contains(&"linux"));

        let runtime_capability = include_str!("window.rs");
        let in_app_update_block = runtime_capability
            .split("main-window-runtime-in-app-update")
            .nth(1)
            .and_then(|rest| rest.split("app.add_capability(in_app_update)").next())
            .expect("window.rs must define the main-window-runtime-in-app-update capability");
        assert!(in_app_update_block.contains("Target::Windows"));
        assert!(in_app_update_block.contains("Target::MacOS"));
        assert!(!in_app_update_block.contains("Target::Linux"));
    }

    #[test]
    fn shell_bridge_exposes_the_cursor_position_command() {
        let script = include_str!("shell-bridge.js");

        assert!(script.contains("getCursorPosition"));
        assert!(script.contains("'get_cursor_position'"));
    }

    #[test]
    fn shell_bridge_exposes_the_update_channel_commands() {
        let script = include_str!("shell-bridge.js");

        assert!(script.contains("getUpdateChannel"));
        assert!(script.contains("'get_update_channel'"));
        assert!(script.contains("setUpdateChannel"));
        assert!(script.contains("'set_update_channel'"));
    }

    #[test]
    fn shell_bridge_exposes_the_update_mode_commands() {
        let script = include_str!("shell-bridge.js");

        assert!(script.contains("getUpdateMode"));
        assert!(script.contains("'get_update_mode'"));
        assert!(script.contains("setUpdateMode"));
        assert!(script.contains("'set_update_mode'"));
    }

    #[test]
    fn shell_bridge_exposes_the_dock_icon_commands() {
        let script = include_str!("shell-bridge.js");

        assert!(script.contains("getHideDockIcon"));
        assert!(script.contains("'get_hide_dock_icon'"));
        assert!(script.contains("setHideDockIcon"));
        assert!(script.contains("'set_hide_dock_icon'"));
    }

    #[test]
    fn shell_bridge_exposes_the_host_stopping_event() {
        let script = include_str!("shell-bridge.js");

        assert!(script.contains("onHostStopping"));
        assert!(script.contains(&format!("'{}'", crate::HOST_STOPPING_EVENT)));
    }

    #[test]
    fn save_file_extensions_keep_only_plain_extensions() {
        assert_eq!(
            parse_save_file_extensions(".macroDeckProfile, zip"),
            vec!["macroDeckProfile".to_string(), "zip".to_string()]
        );
        assert!(parse_save_file_extensions("").is_empty());
        assert!(parse_save_file_extensions("../*, a b").is_empty());
    }

    #[test]
    fn hidden_start_requires_both_launch_flags() {
        assert!(has_launch_flags(args(&[
            "app",
            "--autostart",
            "--minimized"
        ])));
        assert!(!has_launch_flags(args(&["app", "--autostart"])));
        assert!(!has_launch_flags(args(&["app", "--minimized"])));
        assert!(!has_launch_flags(args(&["app"])));
    }
}
