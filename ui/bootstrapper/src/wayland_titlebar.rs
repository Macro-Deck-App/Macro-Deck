// tao 0.35 covers its Wayland header bar with an event box that takes every click (tauri issue 13440).
// Remove once tauri ships tao 0.36 or later, which drops that event box.
#[cfg(target_os = "linux")]
pub fn release_titlebar_buttons(window: &tauri::WebviewWindow) {
    use tauri::Manager;

    let target = window.clone();
    crate::window::dispatch_to_main_thread(
        window.app_handle(),
        "release the title bar buttons",
        move || release(&target),
    );
}

#[cfg(not(target_os = "linux"))]
pub fn release_titlebar_buttons(_window: &tauri::WebviewWindow) {}

#[cfg(target_os = "linux")]
fn release(window: &tauri::WebviewWindow) {
    use gtk::prelude::*;

    let Ok(gtk_window) = window.gtk_window() else {
        return;
    };
    if !gtk_window.display().backend().is_wayland() {
        return;
    }
    match gtk_window
        .titlebar()
        .and_then(|titlebar| titlebar.downcast::<gtk::EventBox>().ok())
    {
        Some(event_box) => event_box.set_above_child(false),
        None => crate::logging::warn(
            "[window] the Wayland title bar is no longer tao's event box; revisit wayland_titlebar.rs",
        ),
    }
}
