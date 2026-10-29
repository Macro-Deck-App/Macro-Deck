// Macro Deck Bootstrapper: the installed entry point on every platform. It
// starts the .NET host, provides the tray icon, opens the WebView window that
// loads the Angular configuration UI from the host, and owns update handling.
// In dev (cargo/tauri dev) the host is started manually and the window loads
// the Angular dev server; nothing is spawned.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod appearance;
#[cfg(any(target_os = "linux", all(test, unix)))]
mod appimage;
mod backup_download;
mod bridge;
mod deep_links;
mod dock_icon;
mod external_url;
mod host;
mod host_error_window;
#[cfg(windows)]
mod host_job;
mod host_supervisor;
mod install_integrity;
mod install_state;
mod launch_handoff;
mod localization;
mod logging;
mod loopback_secret;
mod mcp_bridge;
mod menu;
mod notifications;
mod opened_files;
mod post_update_changelog;
mod quit_after_update;
mod redact;
mod release_notes;
mod tray_badge;
mod update_channel;
mod update_mode;
mod update_state;
mod update_window;
mod updater;
mod wayland_titlebar;
mod window;
mod window_geometry;
mod window_state;
#[cfg(windows)]
mod windows_file_dialog;
#[cfg(windows)]
mod windows_screen_color;

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

use tauri::{AppHandle, Emitter, Manager, RunEvent};
use updater::QuitInstall;

pub const HOST_STOPPING_EVENT: &str = "host-stopping";

static QUITTING: AtomicBool = AtomicBool::new(false);

const QUIT_INSTALL_FAILURE_GRACE: Duration = Duration::from_secs(30);

pub fn mark_quitting(_app: &AppHandle) {
    QUITTING.store(true, Ordering::SeqCst);
}

pub fn clear_quitting() {
    QUITTING.store(false, Ordering::SeqCst);
}

pub fn is_quitting() -> bool {
    QUITTING.load(Ordering::SeqCst)
}

pub fn claim_quit() -> bool {
    !QUITTING.swap(true, Ordering::SeqCst)
}

pub fn quitting_while<T, E>(step: impl FnOnce() -> Result<T, E>) -> Result<T, E> {
    hold_quitting(&QUITTING, step)
}

fn hold_quitting<T, E>(flag: &AtomicBool, step: impl FnOnce() -> Result<T, E>) -> Result<T, E> {
    let was_quitting = flag.swap(true, Ordering::SeqCst);
    let result = step();
    if result.is_err() && !was_quitting {
        flag.store(false, Ordering::SeqCst);
    }
    result
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
        let steps = quit_steps(updater::install_on_quit(&app).await);
        if steps.stop_host {
            host::stop(&app).await;
        }
        if steps.show_failure {
            update_window::wait_until_closed(&app, QUIT_INSTALL_FAILURE_GRACE).await;
        }
        app.exit(0);
    });
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
struct QuitSteps {
    stop_host: bool,
    show_failure: bool,
}

fn quit_steps(outcome: QuitInstall) -> QuitSteps {
    QuitSteps {
        stop_host: outcome != QuitInstall::Installed,
        show_failure: outcome == QuitInstall::Failed,
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum ExitRequest {
    Allow,
    Prevent,
    PreventAndQuit,
}

// While a quit-time install runs, closing its window must not end the app: the exit handler would
// stop the host in the middle of the pre-update backup.
fn decide_exit(
    quitting: bool,
    requested_with_code: bool,
    install_on_quit_running: bool,
) -> ExitRequest {
    if quitting {
        return if !requested_with_code && install_on_quit_running {
            ExitRequest::Prevent
        } else {
            ExitRequest::Allow
        };
    }
    if requested_with_code {
        ExitRequest::PreventAndQuit
    } else {
        ExitRequest::Prevent
    }
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

// The opener's own link script cancels target=_blank clicks and invokes a command no capability
// grants, so links went nowhere; external-links.js routes them to open_external instead.
fn opener_plugin<R: tauri::Runtime>() -> impl tauri::plugin::Plugin<R> {
    tauri_plugin_opener::Builder::new()
        .open_js_links_on_click(false)
        .build()
}

fn main() {
    // Before anything Tauri: an AI client's bridge process must never claim or forward to the
    // single-instance lock, open a window or touch the running app.
    if mcp_bridge::is_invocation(std::env::args_os()) {
        std::process::exit(mcp_bridge::run());
    }

    // A relaunch races the instance that started it: the single-instance lock is only released once
    // that process is gone, and claiming it too early makes this instance forward its argv to a
    // dying process and exit - the app would simply disappear.
    if std::env::var_os(host::RELAUNCH_MARKER_ENVIRONMENT_VARIABLE).is_some() {
        std::thread::sleep(host::RELAUNCH_SETTLE_DELAY);
    }

    tauri::Builder::default()
        // Must stay registered before single_instance: a second instance exits inside its setup.
        .plugin(launch_handoff::plugin())
        .plugin(tauri_plugin_single_instance::init(|app, argv, cwd| {
            if let Some(executable) = launch_handoff::take_target(app) {
                launch_handoff::hand_over(app, executable, argv, cwd);
                return;
            }
            let launch = deep_links::classify(argv);
            if launch.wants_window {
                window::show_main_window(app);
            }
            opened_files::queue(app, launch.files);
            deep_links::queue(app, launch.links);
        }))
        .plugin(tauri_plugin_dialog::init())
        .plugin(opener_plugin())
        .register_uri_scheme_protocol(host_error_window::SCHEME, |context, request| {
            host_error_window::respond(context.webview_label(), &request)
        })
        .plugin(
            tauri_plugin_updater::Builder::new()
                // Compares against the baked release version rather than
                // package_info().version, so the RPM's mapped build version
                // never masks a real update on the beta feed (issue #271).
                .default_version_comparator(updater::version_comparator)
                .build(),
        )
        .register_uri_scheme_protocol(update_window::SCHEME, update_window::handle_request)
        .manage(Arc::new(host::HostState::new()))
        .manage(opened_files::PendingOpenFiles::default())
        .manage(deep_links::PendingDeepLinks::default())
        .invoke_handler(tauri::generate_handler![
            bridge::get_host_port,
            bridge::reauthenticate,
            bridge::get_shell_info,
            bridge::get_cursor_position,
            bridge::open_external,
            bridge::show_open_dialog,
            bridge::pick_screen_color,
            bridge::save_file,
            bridge::save_backup,
            dock_icon::get_hide_dock_icon,
            dock_icon::set_hide_dock_icon,
            appearance::set_appearance,
            menu::set_hotkey_capture,
            menu::take_menu_action,
            opened_files::take_opened_files,
            deep_links::take_deep_links,
            updater::check_for_update,
            updater::install_update,
            updater::get_update_state,
            updater::request_update_check,
            updater::cancel_update_download,
            update_channel::get_update_channel,
            update_channel::set_update_channel,
            update_mode::get_update_mode,
            update_mode::set_update_mode,
            post_update_changelog::get_post_update_changelog,
            post_update_changelog::dismiss_post_update_changelog
        ])
        .on_menu_event(menu::handle_event)
        .on_page_load(|webview, payload| {
            if webview.label() == window::MAIN_WINDOW
                && matches!(payload.event(), tauri::webview::PageLoadEvent::Finished)
            {
                logging::info(&format!("[window] page loaded: {}", payload.url()));
                let app = webview.app_handle();
                window::mark_main_window_loaded();
                let suppress_reveal = window::take_suppress_reveal();
                if let Some(window) = app.get_webview_window(window::MAIN_WINDOW) {
                    if !suppress_reveal && !window.is_visible().unwrap_or(false) {
                        window::reveal(&window);
                    }
                }
                opened_files::notify(app);
                deep_links::notify(app);
            }
        })
        .setup(|app| {
            let handle = app.handle().clone();
            init_logging(&handle);
            #[cfg(windows)]
            host_job::shut_down_before_host();
            logging::info(&format!(
                "[app] Macro Deck Bootstrapper {} starting (packaged: {})",
                updater::current_version(&handle),
                host::is_packaged()
            ));
            if let Some(dir) = logging::logs_directory() {
                logging::info(&format!("[app] logs directory: {}", dir.display()));
            }
            if quit_after_update::take_for_launch(&handle) {
                logging::info("[app] relaunched by the installer after an install on quit; exiting");
                mark_quitting(&handle);
                handle.exit(0);
                return Ok(());
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
            launch_handoff::clear(&handle);
            window::setup_tray(&handle)?;
            menu::setup(&handle)?;
            let launch = deep_links::classify(deep_links::launch_arguments());
            opened_files::queue(&handle, launch.files);
            deep_links::queue(&handle, launch.links);
            install_integrity::spawn(&handle);
            tauri::async_runtime::spawn(async move { startup(handle).await });
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building Macro Deck Bootstrapper")
        // Must stay the last thing main does: the runtime installs this
        // callback before it starts pumping events, and an Opened that arrives
        // with no callback in place is dropped without a trace.
        .run(|app, event| match event {
            RunEvent::ExitRequested { api, code, .. } => match decide_exit(
                QUITTING.load(Ordering::SeqCst),
                code.is_some(),
                updater::install_on_quit_running(),
            ) {
                ExitRequest::Allow => {}
                ExitRequest::Prevent => api.prevent_exit(),
                ExitRequest::PreventAndQuit => {
                    api.prevent_exit();
                    request_quit(app);
                }
            },
            // A tray quit goes request_quit -> app.exit(0) -> Exit with no
            // CloseRequested, so the window-state flush there is the only
            // chance to persist that session's geometry.
            RunEvent::Exit => {
                window_state::flush(app);
                host::stop_before_exit(app);
            }
            #[cfg(target_os = "macos")]
            RunEvent::Opened { urls } => {
                let files = opened_files::paths_from_urls(&urls);
                let links = deep_links::from_urls(&urls);
                let only_rejected_links = files.is_empty()
                    && links.is_empty()
                    && urls.iter().any(deep_links::is_link_url);
                if !only_rejected_links {
                    window::show_main_window(app);
                }
                opened_files::queue(app, files);
                deep_links::queue(app, links);
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

#[cfg(test)]
mod tests {
    use tauri::plugin::Plugin;

    use std::sync::atomic::{AtomicBool, Ordering};

    use super::{decide_exit, hold_quitting, quit_steps, ExitRequest, QuitInstall, QuitSteps};

    fn quit_can_be_claimed(flag: &AtomicBool) -> bool {
        !flag.swap(true, Ordering::SeqCst)
    }

    #[test]
    fn quit_works_again_after_an_install_that_failed_without_quitting() {
        let flag = AtomicBool::new(false);

        let result = hold_quitting(&flag, || Err::<(), _>("bundle not writable"));

        assert!(result.is_err());
        assert!(quit_can_be_claimed(&flag));
    }

    #[test]
    fn a_failed_install_on_quit_keeps_the_app_quitting() {
        let flag = AtomicBool::new(true);

        let _ = hold_quitting(&flag, || Err::<(), _>("bundle not writable"));

        assert!(!quit_can_be_claimed(&flag));
    }

    #[test]
    fn the_app_is_quitting_while_the_install_runs_and_after_it_succeeds() {
        let flag = AtomicBool::new(false);

        let seen = hold_quitting(&flag, || Ok::<_, ()>(flag.load(Ordering::SeqCst)));

        assert_eq!(seen, Ok(true));
        assert!(!quit_can_be_claimed(&flag));
    }

    #[test]
    fn a_failed_install_on_quit_stops_the_host_and_shows_the_failure_before_exiting() {
        assert_eq!(
            quit_steps(QuitInstall::Failed),
            QuitSteps {
                stop_host: true,
                show_failure: true
            }
        );
    }

    #[test]
    fn a_quit_without_a_pending_install_only_stops_the_host() {
        assert_eq!(
            quit_steps(QuitInstall::NotPending),
            QuitSteps {
                stop_host: true,
                show_failure: false
            }
        );
    }

    #[test]
    fn an_installed_update_already_stopped_the_host() {
        assert_eq!(
            quit_steps(QuitInstall::Installed),
            QuitSteps {
                stop_host: false,
                show_failure: false
            }
        );
    }

    #[test]
    fn closing_the_last_window_during_a_quit_time_install_does_not_exit() {
        assert_eq!(decide_exit(true, false, true), ExitRequest::Prevent);
    }

    #[test]
    fn the_quit_itself_still_exits_while_an_install_runs() {
        assert_eq!(decide_exit(true, true, true), ExitRequest::Allow);
    }

    #[test]
    fn closing_the_last_window_after_a_failed_install_on_quit_exits() {
        assert_eq!(decide_exit(true, false, false), ExitRequest::Allow);
    }

    #[test]
    fn closing_the_last_window_without_a_quit_keeps_running() {
        assert_eq!(decide_exit(false, false, false), ExitRequest::Prevent);
    }

    #[test]
    fn an_exit_request_with_a_code_starts_the_quit() {
        assert_eq!(decide_exit(false, true, false), ExitRequest::PreventAndQuit);
    }

    #[test]
    fn opener_plugin_injects_no_link_script_that_would_swallow_external_links() {
        let plugin = super::opener_plugin::<tauri::Wry>();

        assert!(plugin.initialization_script().is_none());
    }
}
