// Macro Deck Bootstrapper: the installed entry point on every platform. It
// starts the .NET host, provides the tray icon, opens the WebView window that
// loads the Angular configuration UI from the host, and owns update handling.
// In dev (cargo/tauri dev) the host is started manually and the window loads
// the Angular dev server; nothing is spawned.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod bridge;
mod dock_icon;
mod host;
#[cfg(windows)]
mod host_job;
mod install_state;
mod localization;
mod logging;
mod menu;
mod notifications;
mod opened_files;
mod redact;
mod update_channel;
mod update_mode;
mod update_state;
mod updater;
mod window;
mod window_geometry;
mod window_state;
#[cfg(windows)]
mod windows_file_dialog;

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use tauri::{AppHandle, Emitter, Manager, RunEvent};

pub const HOST_STOPPING_EVENT: &str = "host-stopping";

static QUITTING: AtomicBool = AtomicBool::new(false);

pub fn mark_quitting(_app: &AppHandle) {
    QUITTING.store(true, Ordering::SeqCst);
}

pub fn clear_quitting() {
    QUITTING.store(false, Ordering::SeqCst);
}

pub fn is_quitting() -> bool {
    QUITTING.load(Ordering::SeqCst)
}

pub fn request_quit(app: &AppHandle) {
    if QUITTING.swap(true, Ordering::SeqCst) {
        return;
    }
    if let Err(error) = app.emit(HOST_STOPPING_EVENT, ()) {
        logging::error(&format!(
            "[app] could not emit host-stopping event: {error}"
        ));
    }
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        host::stop(&app).await;
        app.exit(0);
    });
}

fn init_logging(app: &AppHandle) {
    let exec_dir = std::env::current_exe()
        .ok()
        .and_then(|path| path.parent().map(|dir| dir.to_path_buf()))
        .unwrap_or_else(|| std::path::PathBuf::from("."));
    let packaged = host::is_packaged() || exec_dir.join(".macro-deck-packaged").exists();
    let home = app
        .path()
        .home_dir()
        .unwrap_or_else(|_| std::path::PathBuf::from("."));
    let os = if cfg!(windows) {
        "windows"
    } else if cfg!(target_os = "macos") {
        "macos"
    } else {
        "linux"
    };
    let override_dir = std::env::var("MACRO_DECK_DATA_DIRECTORY")
        .ok()
        .filter(|value| !value.trim().is_empty())
        .or_else(|| std::env::var("MACRODECK_DATA_DIR").ok());
    let appdata = std::env::var("APPDATA").ok();
    let xdg = std::env::var("XDG_DATA_HOME").ok();
    let context = logging::LoggerContext {
        override_dir,
        portable: std::env::var("MACRODECK_PORTABLE")
            .map(|value| value == "1")
            .unwrap_or(false),
        packaged,
        development: host::is_development_build(),
        data_root_directory_name: host::host_data_root_directory_name(),
        exec_dir,
        working_directory: std::env::current_dir()
            .unwrap_or_else(|_| std::path::PathBuf::from(".")),
        platform_data_root: logging::platform_data_root(
            os,
            appdata.as_deref(),
            xdg.as_deref(),
            &home,
        ),
    };
    logging::init(logging::resolve_logs_dir(&context));
}

async fn startup(app: AppHandle) {
    if !host::ensure_running(&app).await {
        app.exit(1);
        return;
    }

    if bridge::should_start_hidden(&app) {
        window::start_in_background(&app);
    } else {
        window::create_main_window(&app);
    }
    notifications::spawn(&app);
    updater::spawn_periodic_check(app);
}

fn main() {
    // A relaunch races the instance that started it: the single-instance lock is only released once
    // that process is gone, and claiming it too early makes this instance forward its argv to a
    // dying process and exit - the app would simply disappear.
    if std::env::var_os(host::RELAUNCH_MARKER_ENVIRONMENT_VARIABLE).is_some() {
        std::thread::sleep(host::RELAUNCH_SETTLE_DELAY);
    }

    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, argv, _cwd| {
            window::show_main_window(app);
            opened_files::queue(app, opened_files::paths_from_args(argv.into_iter()));
        }))
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(
            tauri_plugin_updater::Builder::new()
                // Compares against the baked release version rather than
                // package_info().version, so the RPM's mapped build version
                // never masks a real update on the beta feed (issue #271).
                .default_version_comparator(updater::version_comparator)
                .build(),
        )
        .manage(Arc::new(host::HostState::new()))
        .manage(opened_files::PendingOpenFiles::default())
        .invoke_handler(tauri::generate_handler![
            bridge::get_host_port,
            bridge::get_shell_info,
            bridge::get_cursor_position,
            bridge::open_external,
            bridge::show_open_dialog,
            bridge::save_file,
            dock_icon::get_hide_dock_icon,
            dock_icon::set_hide_dock_icon,
            menu::set_hotkey_capture,
            menu::take_menu_action,
            opened_files::take_opened_files,
            updater::check_for_update,
            updater::install_update,
            updater::get_update_state,
            updater::request_update_check,
            updater::cancel_update_download,
            update_channel::get_update_channel,
            update_channel::set_update_channel,
            update_mode::get_update_mode,
            update_mode::set_update_mode
        ])
        .on_menu_event(menu::handle_event)
        .on_page_load(|webview, payload| {
            if webview.label() == window::MAIN_WINDOW
                && matches!(payload.event(), tauri::webview::PageLoadEvent::Finished)
            {
                logging::info(&format!("[window] page loaded: {}", payload.url()));
                let app = webview.app_handle();
                window::mark_main_window_loaded();
                if let Some(window) = app.get_webview_window(window::MAIN_WINDOW) {
                    if !window.is_visible().unwrap_or(false) {
                        window::reveal(&window);
                    }
                }
                opened_files::notify(app);
            }
        })
        .setup(|app| {
            let handle = app.handle().clone();
            init_logging(&handle);
            logging::info(&format!(
                "[app] Macro Deck Bootstrapper {} starting (packaged: {})",
                updater::current_version(&handle),
                host::is_packaged()
            ));
            if let Some(dir) = logging::logs_directory() {
                logging::info(&format!("[app] logs directory: {}", dir.display()));
            }
            // A launch from the mounted DMG is not an install - no tray, no
            // host, no window; just the move prompt (issue #355). The dialog
            // has to run off the main thread, hence the spawned task.
            if host::is_packaged() && !install_state::current().is_installed() {
                logging::warn(&format!(
                    "[install] not launched from an installed bundle ({:?}); offering to move to Applications",
                    install_state::current()
                ));
                #[cfg(target_os = "macos")]
                tauri::async_runtime::spawn(async move { install_state::prompt_and_install(handle).await });
                return Ok(());
            }
            window::setup_tray(&handle)?;
            menu::setup(&handle)?;
            opened_files::queue(&handle, opened_files::paths_from_args(std::env::args()));
            tauri::async_runtime::spawn(async move { startup(handle).await });
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building Macro Deck Bootstrapper")
        // Must stay the last thing main does: the runtime installs this
        // callback before it starts pumping events, and an Opened that arrives
        // with no callback in place is dropped without a trace.
        .run(|app, event| match event {
            RunEvent::ExitRequested { api, code, .. } => {
                if QUITTING.load(Ordering::SeqCst) {
                    return;
                }
                match code {
                    None => api.prevent_exit(),
                    Some(_) => {
                        api.prevent_exit();
                        request_quit(app);
                    }
                }
            }
            // A tray quit goes request_quit -> app.exit(0) -> Exit with no
            // CloseRequested, so the window-state flush there is the only
            // chance to persist that session's geometry.
            RunEvent::Exit => window_state::flush(app),
            #[cfg(target_os = "macos")]
            RunEvent::Opened { urls } => {
                window::show_main_window(app);
                opened_files::queue(app, opened_files::paths_from_urls(&urls));
            }
            // Launching an already-running app from Finder, Spotlight or the
            // dock icon: macOS reopens this instance instead of starting a
            // second one, so neither single-instance nor Opened fires. Without
            // this the app is unreachable once it has no dock icon and its
            // window is hidden, leaving only the menu bar icon (issue #378).
            #[cfg(target_os = "macos")]
            RunEvent::Reopen { .. } => window::show_main_window(app),
            _ => {}
        });
}
