use std::sync::atomic::{AtomicBool, Ordering};

use tauri::menu::{MenuBuilder, MenuItemBuilder};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::utils::platform::Target;
use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder};
use tauri_plugin_dialog::{DialogExt, MessageDialogKind};

use crate::bridge;
use crate::host;
use crate::localization::{self, keys};
use crate::logging;
use crate::updater;

pub const MAIN_WINDOW: &str = "main";

static MAIN_WINDOW_LOADED: AtomicBool = AtomicBool::new(false);

static CAPABILITIES_REGISTERED: AtomicBool = AtomicBool::new(false);

const SHELL_BRIDGE_SCRIPT: &str = include_str!("shell-bridge.js");
const EXTERNAL_LINKS_SCRIPT: &str = include_str!("external-links.js");
const DISABLE_CONTEXT_MENU_SCRIPT: &str = include_str!("disable-context-menu.js");

pub(crate) const MIN_WINDOW_WIDTH: f64 = 900.0;
pub(crate) const MIN_WINDOW_HEIGHT: f64 = 640.0;

// Checked at compile time rather than in a test: these are constant relationships, so a build on
// any platform fails outright if one is broken. A minimum above the default would open the window
// larger than `inner_size` asks for.
const _: () = assert!(MIN_WINDOW_WIDTH <= 1280.0);
const _: () = assert!(MIN_WINDOW_HEIGHT <= 800.0);
const _: () = assert!(MIN_WINDOW_WIDTH >= 220.0 + 400.0);

#[cfg(target_os = "macos")]
const UI_ROOT_FONT_SIZE: f64 = 16.0;

#[cfg(target_os = "macos")]
const STATUS_BAR_HEIGHT_REM: f64 = 3.25;

#[cfg(target_os = "macos")]
const STATUS_BAR_HEIGHT: f64 = STATUS_BAR_HEIGHT_REM * UI_ROOT_FONT_SIZE;

#[cfg(target_os = "macos")]
const WINDOW_BUTTON_SIZE: f64 = 14.0;

#[cfg(target_os = "macos")]
const WINDOW_BUTTON_CONTAINER_OFFSET: f64 = 9.0;

fn should_disable_context_menu(packaged: bool, devtools_requested: bool) -> bool {
    packaged && !devtools_requested
}

#[cfg(target_os = "macos")]
fn traffic_light_position(title_bar_height: f64) -> tauri::LogicalPosition<f64> {
    let top = (title_bar_height - WINDOW_BUTTON_SIZE) / 2.0;
    tauri::LogicalPosition::new(top, top + WINDOW_BUTTON_CONTAINER_OFFSET)
}

fn packaged_window_url(port: u16, version: &str) -> String {
    format!("http://127.0.0.1:{port}/admin?v={version}")
}

pub fn main_window_url(app: &AppHandle) -> Option<String> {
    if host::is_packaged() {
        // The bootstrapper assigned this loopback port to the host, so the UI
        // is same-origin with the API and the origin survives restarts (the
        // port is persisted). There is deliberately no fallback to the public
        // listener: it never trusts the desktop UI, so loading from it shows
        // an unauthorized window instead of failing loudly.
        let port = app.state::<std::sync::Arc<host::HostState>>().ui_port()?;
        // Must be the baked release version, not package_info().version: on a
        // beta RPM the latter is the mapped version, so two different beta
        // builds would cache-bust to the same URL and WebKitGTK could serve the
        // pre-upgrade /admin document after the upgrade (issue #271).
        Some(packaged_window_url(port, &updater::current_version(app)))
    } else {
        Some(
            app.config()
                .build
                .dev_url
                .as_ref()
                .map(|url| url.to_string())
                .unwrap_or_else(|| "http://localhost:4200".to_string()),
        )
    }
}

pub fn is_main_window_loaded() -> bool {
    MAIN_WINDOW_LOADED.load(Ordering::SeqCst)
}

pub fn mark_main_window_loaded() {
    MAIN_WINDOW_LOADED.store(true, Ordering::SeqCst);
}

pub fn start_in_background(app: &AppHandle) {
    #[cfg(target_os = "macos")]
    let _ = app.set_activation_policy(tauri::ActivationPolicy::Accessory);
    #[cfg(not(target_os = "macos"))]
    let _ = app;
}

pub fn create_main_window(app: &AppHandle) {
    let Some(raw_url) = main_window_url(app) else {
        show_error_dialog(
            app,
            &localization::t(keys::ERRORS_COULD_NOT_START_TITLE),
            &localization::t(keys::ERRORS_HOST_PORT_NOT_RESOLVED),
        );
        return;
    };
    let url: tauri::Url = match raw_url.parse() {
        Ok(url) => url,
        Err(error) => {
            logging::error(&format!("[window] invalid UI url: {error}"));
            return;
        }
    };

    if host::is_packaged() && !CAPABILITIES_REGISTERED.load(Ordering::SeqCst) {
        let origin = format!("http://{}", url.authority());
        let capability = tauri::ipc::CapabilityBuilder::new("main-window-runtime")
            .local(false)
            .remote(origin.clone())
            .window(MAIN_WINDOW)
            .permission("core:default")
            .permission("core:window:allow-start-dragging")
            .permission("allow-get-host-port")
            .permission("allow-get-shell-info")
            .permission("allow-get-cursor-position")
            .permission("allow-open-external")
            .permission("allow-show-open-dialog")
            .permission("allow-save-file")
            .permission("allow-take-opened-files")
            .permission("allow-take-menu-action")
            .permission("allow-set-hotkey-capture")
            .permission("allow-check-for-update")
            .permission("allow-get-update-state")
            .permission("allow-request-update-check")
            .permission("allow-cancel-update-download")
            .permission("allow-get-update-channel")
            .permission("allow-set-update-channel")
            .permission("allow-get-update-mode")
            .permission("allow-set-update-mode")
            .permission("allow-get-hide-dock-icon")
            .permission("allow-set-hide-dock-icon")
            .permission("core:event:allow-listen")
            .permission("core:event:allow-unlisten");
        if let Err(error) = app.add_capability(capability) {
            logging::error(&format!(
                "[window] could not register IPC capability for {origin}: {error}"
            ));
        }

        // install_update is Windows/macOS only: Linux is notification-only and
        // never installs an update itself (issue #271), so the grant is scoped
        // to a second capability instead of the shared one above.
        let in_app_update = tauri::ipc::CapabilityBuilder::new("main-window-runtime-in-app-update")
            .local(false)
            .remote(origin.clone())
            .window(MAIN_WINDOW)
            .platforms([Target::Windows, Target::MacOS])
            .permission("allow-install-update");
        if let Err(error) = app.add_capability(in_app_update) {
            logging::error(&format!(
                "[window] could not register in-app-update IPC capability for {origin}: {error}"
            ));
        }

        CAPABILITIES_REGISTERED.store(true, Ordering::SeqCst);
    }

    let devtools_requested = std::env::var("MACRODECK_DEVTOOLS")
        .map(|v| v == "1")
        .unwrap_or(false);

    let mut builder = WebviewWindowBuilder::new(app, MAIN_WINDOW, WebviewUrl::External(url))
        .title("Macro Deck")
        .inner_size(1280.0, 800.0)
        .min_inner_size(MIN_WINDOW_WIDTH, MIN_WINDOW_HEIGHT)
        .center()
        .visible(false)
        .zoom_hotkeys_enabled(false)
        .initialization_script(SHELL_BRIDGE_SCRIPT)
        .initialization_script(EXTERNAL_LINKS_SCRIPT);

    if should_disable_context_menu(host::is_packaged(), devtools_requested) {
        builder = builder.initialization_script(DISABLE_CONTEXT_MENU_SCRIPT);
    }

    #[cfg(target_os = "macos")]
    let builder = builder
        .title_bar_style(tauri::TitleBarStyle::Overlay)
        .hidden_title(true)
        .traffic_light_position(traffic_light_position(STATUS_BAR_HEIGHT));

    let window = match builder.build() {
        Ok(window) => window,
        Err(error) => {
            logging::error(&format!("[window] could not create main window: {error}"));
            return;
        }
    };

    // While the window is still hidden: restores the saved geometry corrected
    // for the current display configuration (not maximized - see `reveal`).
    //
    // Handed to the main thread rather than run here, because this function is
    // driven from the async startup task and every window getter the restore
    // needs marshals to the main thread. See window_state.rs for the startup
    // deadlock this ordering exists to avoid.
    dispatch_to_main_thread(app, "restore the saved window geometry", {
        let window = window.clone();
        move || crate::window_state::restore_geometry(&window)
    });

    if devtools_requested {
        window.open_devtools();
    }

    {
        let app = app.clone();
        let window = window.clone();
        tauri::async_runtime::spawn(async move {
            tokio::time::sleep(std::time::Duration::from_secs(4)).await;
            dispatch_to_main_thread(&app, "reveal the main window", move || {
                if !window.is_visible().unwrap_or(true) {
                    reveal(&window);
                }
            });
        });
    }

    let app = app.clone();
    let label = window.label().to_string();
    window.clone().on_window_event(move |event| match event {
        tauri::WindowEvent::CloseRequested { .. } => crate::window_state::flush(&app),
        tauri::WindowEvent::Destroyed => {
            MAIN_WINDOW_LOADED.store(false, Ordering::SeqCst);
            // Only when the user asked for it (issue #378). Minimizing never
            // reaches this arm, so it keeps the dock icon. show_main_window puts
            // the policy back before it shows the next window.
            #[cfg(target_os = "macos")]
            if crate::dock_icon::is_enabled(&app) {
                let _ = app.set_activation_policy(tauri::ActivationPolicy::Accessory);
            }
        }
        tauri::WindowEvent::DragDrop(drag) => {
            if let Some(window) = app.get_webview_window(&label) {
                bridge::forward_drag_drop(&window, drag);
            }
        }
        tauri::WindowEvent::Moved(_) | tauri::WindowEvent::Resized(_) => {
            if let Some(window) = app.get_webview_window(&label) {
                crate::window_state::record_normal_bounds(&window);
            }
        }
        _ => {}
    });
}

fn dispatch_to_main_thread(app: &AppHandle, what: &str, work: impl FnOnce() + Send + 'static) {
    if let Err(error) = app.run_on_main_thread(work) {
        logging::error(&format!("[window] could not {what}: {error}"));
    }
}

pub fn reveal(window: &tauri::WebviewWindow) {
    crate::window_state::restore_maximized(window);
    let _ = window.show();
    let _ = window.set_focus();
}

pub fn show_main_window(app: &AppHandle) {
    #[cfg(target_os = "macos")]
    let _ = app.set_activation_policy(tauri::ActivationPolicy::Regular);

    if let Some(window) = app.get_webview_window(MAIN_WINDOW) {
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
    } else if app
        .state::<std::sync::Arc<crate::host::HostState>>()
        .ready
        .load(Ordering::SeqCst)
    {
        create_main_window(app);
    }
}

pub fn setup_tray(app: &AppHandle) -> tauri::Result<()> {
    let show = MenuItemBuilder::with_id("show", localization::t(keys::TRAY_SHOW)).build(app)?;
    let quit = MenuItemBuilder::with_id("quit", localization::t(keys::TRAY_QUIT)).build(app)?;
    let menu = MenuBuilder::new(app).item(&show).item(&quit).build()?;

    // macOS gets a flat black silhouette because it is used as a template image:
    // the system recolours it for the menu bar's appearance and for the selected
    // state, which a coloured icon would not follow. Windows takes the icon's
    // foreground artwork instead of the simplified glyph the other two use: its
    // glow and gradients are what keep the keys solid in the notification area.
    let icon_bytes: &[u8] = if cfg!(target_os = "macos") {
        include_bytes!("../icons/tray-icon-macos.png")
    } else if cfg!(windows) {
        include_bytes!("../icons/tray-icon-win.png")
    } else {
        include_bytes!("../icons/tray-icon.png")
    };
    let icon = tauri::image::Image::from_bytes(icon_bytes)?;

    let mut builder = TrayIconBuilder::with_id("main")
        .icon(icon)
        .icon_as_template(cfg!(target_os = "macos"))
        .tooltip("Macro Deck")
        .menu(&menu)
        .on_menu_event(|app, event| match event.id().as_ref() {
            "show" => show_main_window(app),
            "quit" => crate::request_quit(app),
            _ => {}
        });

    if !cfg!(target_os = "macos") {
        builder = builder
            .show_menu_on_left_click(false)
            .on_tray_icon_event(|tray, event| {
                if let TrayIconEvent::Click {
                    button: MouseButton::Left,
                    button_state: MouseButtonState::Up,
                    ..
                } = event
                {
                    show_main_window(tray.app_handle());
                }
            });
    }

    builder.build(app)?;
    Ok(())
}

pub fn show_error_dialog(app: &AppHandle, title: &str, message: &str) {
    logging::error(&format!("[dialog] {title}: {message}"));
    app.dialog()
        .message(message)
        .title(title)
        .kind(MessageDialogKind::Error)
        .blocking_show();
}

#[cfg(test)]
mod tests {
    use super::*;

    fn production_source() -> &'static str {
        include_str!("window.rs")
            .split("#[cfg(test)]")
            .next()
            .expect("splitting on the test module always yields a first part")
    }

    #[test]
    fn packaged_window_url_is_cache_busted_by_version() {
        assert_eq!(
            packaged_window_url(51000, "3.0.0-beta.7"),
            "http://127.0.0.1:51000/admin?v=3.0.0-beta.7"
        );
    }

    #[test]
    fn packaged_window_url_differs_between_versions() {
        assert_ne!(
            packaged_window_url(51000, "3.0.0-beta.6"),
            packaged_window_url(51000, "3.0.0-beta.7")
        );
    }

    #[test]
    fn packaged_window_url_differs_for_the_same_package_version_across_release_versions() {
        let url_42 = packaged_window_url(
            51000,
            &updater::current_version_for("3.0.0-beta.42", "3.0.0"),
        );
        let url_43 = packaged_window_url(
            51000,
            &updater::current_version_for("3.0.0-beta.43", "3.0.0"),
        );
        assert_ne!(url_42, url_43);
    }

    #[test]
    fn packaged_window_url_origin_is_unchanged_by_the_cache_bust() {
        // The query string must not change the origin (scheme://host:port), or
        // localStorage/cookies and the runtime IPC capability would not survive.
        let url: tauri::Url = packaged_window_url(51000, "3.0.0-beta.7").parse().unwrap();
        assert_eq!(url.authority(), "127.0.0.1:51000");
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn traffic_lights_are_centered_in_the_status_bar() {
        let position = traffic_light_position(STATUS_BAR_HEIGHT);

        let top = position.y - WINDOW_BUTTON_CONTAINER_OFFSET;
        let bottom = STATUS_BAR_HEIGHT - (top + WINDOW_BUTTON_SIZE);
        assert_eq!(top, bottom);
        assert_eq!(top, 19.0);
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn status_bar_height_matches_the_rendered_bar() {
        assert_eq!(STATUS_BAR_HEIGHT, 52.0);
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn traffic_lights_are_inset_from_the_left_as_far_as_from_the_top() {
        for height in [28.0, 48.0, STATUS_BAR_HEIGHT, 96.0] {
            let position = traffic_light_position(height);

            let top = position.y - WINDOW_BUTTON_CONTAINER_OFFSET;
            assert_eq!(position.x, top, "height {height}");
        }
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn traffic_lights_sit_19pt_into_the_status_bar_corner() {
        let position = traffic_light_position(STATUS_BAR_HEIGHT);

        assert_eq!(position.x, 19.0);
        assert_eq!(position.y, 28.0);
    }

    #[test]
    fn the_window_is_centered_before_it_is_built() {
        let source = production_source();

        let center = source
            .find(".center()")
            .expect("create_main_window must center the window on first launch");
        let build = source
            .find("builder.build()")
            .expect("create_main_window must build the window");
        assert!(
            center < build,
            "centering must happen on the builder, before build() is called"
        );
    }

    #[test]
    fn saved_geometry_is_restored_after_the_window_is_built() {
        let source = production_source();

        let build = source
            .find("builder.build()")
            .expect("create_main_window must build the window");
        let restore = source
            .find("window_state::restore_geometry(&window)")
            .expect("create_main_window must restore the saved geometry");
        assert!(
            build < restore,
            "geometry can only be restored once the window exists"
        );
    }

    #[test]
    fn closing_flushes_the_window_state_before_the_window_is_destroyed() {
        let source = production_source();

        let close_requested = source
            .find("tauri::WindowEvent::CloseRequested")
            .expect("window.rs must handle CloseRequested");
        let flush = source
            .find("window_state::flush(&app)")
            .expect("CloseRequested must flush the window state");
        let destroyed = source
            .find("tauri::WindowEvent::Destroyed")
            .expect("window.rs must handle Destroyed");
        assert!(
            close_requested < destroyed,
            "CloseRequested must be handled before Destroyed, while geometry is still readable"
        );
        assert!(
            close_requested < flush && flush < destroyed,
            "the flush call must live inside the CloseRequested arm"
        );
    }

    #[test]
    fn reveal_applies_the_maximized_state_before_showing_the_window() {
        let source = production_source();

        let reveal = source
            .split("pub fn reveal")
            .nth(1)
            .and_then(|rest| rest.split("pub fn show_main_window").next())
            .expect("window.rs must define reveal before show_main_window");
        let maximize = reveal
            .find("restore_maximized")
            .expect("reveal must restore the maximized state");
        let show = reveal
            .find("window.show()")
            .expect("reveal must show the window");
        assert!(
            maximize < show,
            "the maximized state must be applied before the window is shown"
        );
    }

    #[test]
    fn context_menu_disabled_only_in_packaged_builds() {
        assert!(should_disable_context_menu(true, false));
        assert!(!should_disable_context_menu(false, false));
        assert!(!should_disable_context_menu(true, true));
        assert!(!should_disable_context_menu(false, true));
    }

    #[test]
    fn closing_tears_the_webview_down_instead_of_hiding_it() {
        let source = production_source();

        assert!(
            !source.contains("api.prevent_close()"),
            "closing must not be intercepted, or the WebView stays resident"
        );
        assert!(
            !source.contains("window.hide()"),
            "nothing may hide the main window in place of destroying it"
        );
        assert!(source.contains("tauri::WindowEvent::Destroyed"));
    }

    #[test]
    fn closing_drops_the_dock_icon_only_when_the_preference_is_on() {
        let source = production_source();

        let destroy_handler = source
            .split("tauri::WindowEvent::Destroyed")
            .nth(1)
            .and_then(|rest| rest.split("tauri::WindowEvent::DragDrop").next())
            .expect("window.rs must handle Destroyed before DragDrop");
        let guard = destroy_handler
            .find("crate::dock_icon::is_enabled(&app)")
            .expect("closing must consult the stored preference");
        let accessory = destroy_handler
            .find("tauri::ActivationPolicy::Accessory")
            .expect("closing must be able to drop the dock icon");
        assert!(
            guard < accessory,
            "the dock icon may only be dropped when the preference says so"
        );

        assert_eq!(
            source
                .matches("set_activation_policy(tauri::ActivationPolicy::Accessory)")
                .count(),
            2
        );
    }

    #[test]
    fn destroying_the_window_clears_the_loaded_flag() {
        let source = production_source();

        let destroy_handler = source
            .split("tauri::WindowEvent::Destroyed")
            .nth(1)
            .and_then(|rest| rest.split("tauri::WindowEvent::DragDrop").next())
            .expect("window.rs must handle Destroyed before DragDrop");
        assert!(destroy_handler.contains("MAIN_WINDOW_LOADED.store(false"));
    }

    #[test]
    fn the_loaded_flag_follows_the_page_rather_than_the_window() {
        assert!(!is_main_window_loaded());

        mark_main_window_loaded();
        assert!(is_main_window_loaded());

        MAIN_WINDOW_LOADED.store(false, Ordering::SeqCst);
        assert!(!is_main_window_loaded());
    }

    #[test]
    fn showing_the_window_always_restores_the_dock_icon() {
        let source = production_source();

        let show = source
            .split("pub fn show_main_window")
            .nth(1)
            .expect("window.rs must define show_main_window");
        let restore = show
            .find("set_activation_policy(tauri::ActivationPolicy::Regular)")
            .expect("show_main_window must restore the Regular activation policy");
        let show_call = show
            .find("window.show()")
            .expect("show_main_window must show the window");
        assert!(
            restore < show_call,
            "the policy must be restored before the window is shown"
        );
        assert!(!show[..restore].contains("dock_icon"));
    }

    #[test]
    fn external_links_script_routes_links_to_the_os_browser() {
        // Regression guard for issue #133: the WebView drops every new-window
        // request, so both entry points (window.open and a plain link click) have
        // to end up at the open_external command instead.
        assert!(EXTERNAL_LINKS_SCRIPT.contains("open_external"));
        assert!(EXTERNAL_LINKS_SCRIPT.contains("window.open = function"));
        assert!(EXTERNAL_LINKS_SCRIPT.contains("addEventListener('click'"));
        assert!(EXTERNAL_LINKS_SCRIPT.contains("addEventListener('auxclick'"));
    }

    #[test]
    fn external_links_script_leaves_handled_events_alone() {
        assert!(EXTERNAL_LINKS_SCRIPT.contains("event.defaultPrevented"));
        // Same-origin URLs belong to the app and must not leave the window.
        assert!(EXTERNAL_LINKS_SCRIPT.contains("window.location.origin"));
    }

    #[test]
    fn disable_context_menu_script_cancels_the_native_default() {
        assert!(DISABLE_CONTEXT_MENU_SCRIPT.contains("contextmenu"));
        assert!(DISABLE_CONTEXT_MENU_SCRIPT.contains("preventDefault"));
    }
}
