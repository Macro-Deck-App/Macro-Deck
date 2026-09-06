// The Windows Open dialog, driven directly instead of through rfd (issue #864).
//
// Entries in %LOCALAPPDATA%\Microsoft\WindowsApps are zero-byte appexeclink reparse points, and
// opening one fails with ERROR_CANT_ACCESS_FILE. The common Open dialog opens whatever was picked
// to validate it, so selecting an app execution alias dies inside the dialog with "The file cannot
// be accessed by the system" - the app never sees a result and cannot even report the failure.
// FOS_NOVALIDATE skips that check, and rfd neither sets FILEOPENDIALOGOPTIONS nor lets a caller
// pass any, which is the only reason this module exists.

use std::path::PathBuf;

use windows::core::{HRESULT, HSTRING, PCWSTR};
use windows::Win32::Foundation::{ERROR_CANCELLED, HWND};
use windows::Win32::System::Com::{
    CoCreateInstance, CoInitializeEx, CoTaskMemFree, CoUninitialize, CLSCTX_INPROC_SERVER,
    COINIT_APARTMENTTHREADED,
};
use windows::Win32::UI::Shell::Common::COMDLG_FILTERSPEC;
use windows::Win32::UI::Shell::{
    FileOpenDialog, IFileOpenDialog, FILEOPENDIALOGOPTIONS, FOS_FILEMUSTEXIST, FOS_NOVALIDATE,
    SIGDN_FILESYSPATH,
};

use crate::logging;

const CANCELLED: HRESULT = HRESULT::from_win32(ERROR_CANCELLED.0);

/// Shows the Open dialog and blocks until a file is picked or the dialog is dismissed. `parent` is
/// the raw main-window handle, so the dialog is owned by the app window rather than floating free.
pub fn pick_file(
    parent: Option<isize>,
    filter_label: String,
    extensions: Vec<String>,
) -> Option<PathBuf> {
    // The dialog needs an apartment-threaded COM context, which the async command's pool thread is
    // not ours to change - rfd opens its dialogs on a thread of their own for the same reason.
    std::thread::spawn(move || {
        let initialized = unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED) }.is_ok();
        let picked = show(parent, &filter_label, &extensions);
        if initialized {
            unsafe { CoUninitialize() };
        }
        picked
    })
    .join()
    .unwrap_or_default()
}

fn show(parent: Option<isize>, filter_label: &str, extensions: &[String]) -> Option<PathBuf> {
    unsafe {
        let dialog: IFileOpenDialog =
            CoCreateInstance(&FileOpenDialog, None, CLSCTX_INPROC_SERVER).ok()?;
        dialog.SetOptions(dialog_options()).ok()?;

        let label = HSTRING::from(filter_label);
        let spec = filter_spec(extensions).map(|spec| HSTRING::from(spec.as_str()));
        if let Some(spec) = &spec {
            let filters = [COMDLG_FILTERSPEC {
                pszName: PCWSTR(label.as_ptr()),
                pszSpec: PCWSTR(spec.as_ptr()),
            }];
            dialog.SetFileTypes(&filters).ok()?;
        }

        if let Err(error) = dialog.Show(parent.map(|handle| HWND(handle as *mut _))) {
            if error.code() != CANCELLED {
                logging::error(&format!("[bridge] the open dialog failed: {error}"));
            }
            return None;
        }

        let picked = dialog.GetResult().ok()?;
        let name = picked.GetDisplayName(SIGDN_FILESYSPATH).ok()?;
        let path = name.to_string().ok().map(PathBuf::from);
        CoTaskMemFree(Some(name.0 as *const _));
        path
    }
}

fn dialog_options() -> FILEOPENDIALOGOPTIONS {
    FOS_FILEMUSTEXIST | FOS_NOVALIDATE
}

/// `["exe", "lnk"]` becomes `*.exe;*.lnk`. Without extensions there is no filter and the dialog
/// offers every file, matching what the caller asked rfd for.
fn filter_spec(extensions: &[String]) -> Option<String> {
    if extensions.is_empty() {
        return None;
    }

    Some(
        extensions
            .iter()
            .map(|extension| format!("*.{extension}"))
            .collect::<Vec<_>>()
            .join(";"),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    // Without FOS_NOVALIDATE the dialog opens the picked file to validate it, which is exactly what
    // an app execution alias cannot survive - that is issue #864.
    #[test]
    fn the_dialog_does_not_validate_the_picked_file() {
        assert!(dialog_options().contains(FOS_NOVALIDATE));
    }

    // Skipping validation must not also stop the dialog from insisting the file is really there.
    #[test]
    fn the_dialog_still_requires_the_file_to_exist() {
        assert!(dialog_options().contains(FOS_FILEMUSTEXIST));
    }

    #[test]
    fn extensions_become_one_semicolon_separated_spec() {
        let spec = filter_spec(&["exe".to_string(), "lnk".to_string()]);

        assert_eq!(spec.as_deref(), Some("*.exe;*.lnk"));
    }

    #[test]
    fn no_extensions_means_no_filter() {
        assert_eq!(filter_spec(&[]), None);
    }
}
