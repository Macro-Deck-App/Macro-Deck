use tauri::AppHandle;

pub fn open(app: &AppHandle, url: &str) -> Result<(), String> {
    // Inside an AppImage the inherited PATH finds the bundled xdg-open, which does nothing on
    // Plasma 6, and the bundle's library and GTK paths would leak into the browser.
    #[cfg(target_os = "linux")]
    if let Some(appdir) = crate::appimage::dir() {
        return crate::appimage::open_url(url, &appdir).map_err(|error| error.to_string());
    }

    use tauri_plugin_opener::OpenerExt;
    app.opener()
        .open_url(url, None::<&str>)
        .map_err(|error| error.to_string())
}
