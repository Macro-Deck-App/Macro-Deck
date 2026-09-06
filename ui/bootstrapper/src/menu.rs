use serde::Serialize;
use std::sync::Mutex;
use std::time::Duration;
use tauri::{AppHandle, Emitter};
use tauri_plugin_dialog::DialogExt;

use crate::localization::{self, keys};
use crate::logging;
use crate::opened_files;
use crate::window;

pub const MENU_ACTION_EVENT: &str = "menu-action";

const SETTINGS_ITEM_ID: &str = "menu-settings";
const IMPORT_ITEM_ID: &str = "menu-import";
const DISCORD_ITEM_ID: &str = "menu-discord";

const SETTINGS_ACTION: &str = "settings";

const DISCORD_URL: &str = "https://discord.macro-deck.app";

#[cfg(target_os = "macos")]
const WEBSITE_URL: &str = "https://macro-deck.app";

static PENDING_ACTION: Mutex<Option<String>> = Mutex::new(None);

const CAPTURE_TIMEOUT: Duration = Duration::from_secs(60);

struct CaptureState {
    active: bool,
    generation: u64,
}

impl CaptureState {
    const fn new() -> Self {
        Self {
            active: false,
            generation: 0,
        }
    }

    fn request(&mut self, active: bool) -> Option<u64> {
        if self.active == active {
            return None;
        }
        self.active = active;
        self.generation += 1;
        Some(self.generation)
    }

    fn is_current(&self, generation: u64) -> bool {
        self.active && self.generation == generation
    }
}

static CAPTURE: Mutex<CaptureState> = Mutex::new(CaptureState::new());

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct MenuAction {
    action: String,
}

pub fn setup(app: &AppHandle) -> tauri::Result<()> {
    #[cfg(target_os = "macos")]
    {
        use tauri::menu::{AboutMetadataBuilder, MenuBuilder, MenuItemBuilder, SubmenuBuilder};

        use crate::updater;

        let about = AboutMetadataBuilder::new()
            .version(Some(updater::current_version(app)))
            .website(Some(WEBSITE_URL))
            .website_label(Some("macro-deck.app"))
            .build();

        let settings =
            MenuItemBuilder::with_id(SETTINGS_ITEM_ID, localization::t(keys::MENU_SETTINGS))
                .accelerator("Cmd+,")
                .build(app)?;
        let application = SubmenuBuilder::new(app, "Macro Deck")
            .about_with_text(localization::t(keys::MENU_ABOUT), Some(about))
            .separator()
            .item(&settings)
            .separator()
            .services()
            .separator()
            .hide()
            .hide_others()
            .show_all()
            .separator()
            .quit()
            .build()?;

        let import = MenuItemBuilder::with_id(IMPORT_ITEM_ID, localization::t(keys::MENU_IMPORT))
            .accelerator("Cmd+O")
            .build(app)?;
        let file = SubmenuBuilder::new(app, localization::t(keys::MENU_FILE))
            .item(&import)
            .separator()
            .close_window()
            .build()?;

        // Every item here is a WebView editing command, and on macOS the key
        // equivalents only work because the menu carries them: without these
        // items ⌘C/⌘V/⌘Z are dead inside the window, so the menu stays even
        // though nothing in it is Macro Deck's own.
        let edit = SubmenuBuilder::new(app, localization::t(keys::MENU_EDIT))
            .undo()
            .redo()
            .separator()
            .cut()
            .copy()
            .paste()
            .select_all()
            .build()?;

        let view = SubmenuBuilder::new(app, localization::t(keys::MENU_VIEW))
            .fullscreen()
            .build()?;

        let window_menu = SubmenuBuilder::new(app, localization::t(keys::MENU_WINDOW))
            .minimize()
            .maximize()
            .separator()
            .bring_all_to_front()
            .build()?;

        let discord =
            MenuItemBuilder::with_id(DISCORD_ITEM_ID, localization::t(keys::MENU_DISCORD))
                .build(app)?;
        let help = SubmenuBuilder::new(app, localization::t(keys::MENU_HELP))
            .item(&discord)
            .build()?;

        let menu = MenuBuilder::new(app)
            .items(&[&application, &file, &edit, &view, &window_menu, &help])
            .build()?;
        app.set_menu(menu)?;
    }

    #[cfg(not(target_os = "macos"))]
    let _ = app;

    Ok(())
}

pub fn handle_event(app: &AppHandle, event: tauri::menu::MenuEvent) {
    match event.id().as_ref() {
        SETTINGS_ITEM_ID => {
            window::show_main_window(app);
            dispatch(app, SETTINGS_ACTION);
        }
        IMPORT_ITEM_ID => import_archive(app),
        DISCORD_ITEM_ID => open_url(app, DISCORD_URL),
        _ => {}
    }
}

#[tauri::command]
pub fn take_menu_action() -> Option<String> {
    PENDING_ACTION.lock().ok().and_then(|mut slot| slot.take())
}

#[tauri::command]
pub fn set_hotkey_capture(app: AppHandle, active: bool) {
    let Ok(mut state) = CAPTURE.lock() else {
        return;
    };
    let Some(generation) = state.request(active) else {
        return;
    };
    set_menu_enabled(&app, !active);
    drop(state);

    if !active {
        return;
    }

    // The UI cannot be trusted to always send the release: a crash or a reload while recording
    // would otherwise leave the menu disabled for the rest of the session.
    let handle = app.clone();
    tauri::async_runtime::spawn(async move {
        tokio::time::sleep(CAPTURE_TIMEOUT).await;
        let Ok(mut state) = CAPTURE.lock() else {
            return;
        };
        if state.is_current(generation) && state.request(false).is_some() {
            logging::warn("[menu] hotkey capture was never released; restoring the menu");
            set_menu_enabled(&handle, true);
        }
    });
}

#[cfg(target_os = "macos")]
fn set_menu_enabled(app: &AppHandle, enabled: bool) {
    use tauri::menu::MenuItemKind;

    let Some(menu) = app.menu() else {
        return;
    };
    let items = match menu.items() {
        Ok(items) => items,
        Err(error) => {
            logging::error(&format!("[menu] could not read the menu items: {error}"));
            return;
        }
    };

    for item in items {
        if let MenuItemKind::Submenu(submenu) = item {
            if let Err(error) = submenu.set_enabled(enabled) {
                logging::error(&format!("[menu] could not toggle a submenu: {error}"));
            }
        }
    }
}

#[cfg(not(target_os = "macos"))]
fn set_menu_enabled(_app: &AppHandle, _enabled: bool) {}

fn dispatch(app: &AppHandle, action: &str) {
    if window::is_main_window_loaded() {
        emit(app, action.to_string());
        return;
    }

    if let Ok(mut slot) = PENDING_ACTION.lock() {
        *slot = Some(action.to_string());
    }
}

fn emit(app: &AppHandle, action: String) {
    if let Err(error) = app.emit(MENU_ACTION_EVENT, MenuAction { action }) {
        logging::error(&format!("[menu] could not emit menu action: {error}"));
    }
}

fn import_archive(app: &AppHandle) {
    window::show_main_window(app);

    let handle = app.clone();
    app.dialog()
        .file()
        .add_filter(
            localization::t(keys::MENU_ARCHIVE_FILTER),
            &opened_files::ARCHIVE_EXTENSIONS,
        )
        .pick_file(move |path| {
            let Some(path) = path.and_then(|picked| picked.into_path().ok()) else {
                return;
            };
            opened_files::queue(&handle, vec![path.display().to_string()]);
        });
}

fn open_url(app: &AppHandle, url: &str) {
    use tauri_plugin_opener::OpenerExt;

    if let Err(error) = app.opener().open_url(url, None::<&str>) {
        logging::error(&format!("[menu] could not open {url}: {error}"));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_settings_action_is_the_name_the_ui_listens_for() {
        assert_eq!(SETTINGS_ACTION, "settings");
        assert_eq!(MENU_ACTION_EVENT, "menu-action");
    }

    #[test]
    fn the_menu_action_payload_is_camel_case_json() {
        let value = serde_json::to_value(MenuAction {
            action: SETTINGS_ACTION.to_string(),
        })
        .unwrap();

        assert_eq!(value["action"], "settings");
    }

    #[test]
    fn a_parked_action_is_taken_only_once() {
        *PENDING_ACTION.lock().unwrap() = Some(SETTINGS_ACTION.to_string());

        let first = PENDING_ACTION.lock().unwrap().take();
        let second = PENDING_ACTION.lock().unwrap().take();

        assert_eq!(first.as_deref(), Some(SETTINGS_ACTION));
        assert_eq!(second, None);
    }

    #[test]
    fn a_repeated_capture_request_is_not_a_change() {
        let mut state = CaptureState::new();

        assert_eq!(state.request(true), Some(1));
        assert_eq!(state.request(true), None);
        assert_eq!(state.request(false), Some(2));
        assert_eq!(state.request(false), None);
    }

    #[test]
    fn a_watchdog_only_matches_its_own_capture() {
        let mut state = CaptureState::new();
        let first = state.request(true).unwrap();

        assert!(state.is_current(first));

        state.request(false);
        assert!(!state.is_current(first));

        let second = state.request(true).unwrap();
        assert!(!state.is_current(first));
        assert!(state.is_current(second));
    }

    #[test]
    fn the_help_menu_points_at_the_discord_invite() {
        assert_eq!(DISCORD_URL, "https://discord.macro-deck.app");
    }
}
