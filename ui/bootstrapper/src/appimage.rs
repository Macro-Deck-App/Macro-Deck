use std::ffi::OsString;
use std::os::unix::ffi::{OsStrExt, OsStringExt};
use std::path::{Path, PathBuf};

// The AppImage's gtk hook forces these for the bundled GTK; a launched browser must not inherit them.
const FORCED_BY_BUNDLE: [&str; 3] = ["APPDIR", "GTK_THEME", "GDK_BACKEND"];

#[cfg(target_os = "linux")]
pub fn dir() -> Option<PathBuf> {
    let dir = PathBuf::from(std::env::var_os("APPDIR")?);
    (dir.is_absolute() && dir.parent().is_some()).then_some(dir)
}

pub fn bundle_roots(appdir: &Path) -> Vec<PathBuf> {
    let mut roots = vec![appdir.to_path_buf()];
    if let Ok(canonical) = appdir.canonicalize() {
        if trimmed(canonical.as_os_str().as_bytes()) != trimmed(appdir.as_os_str().as_bytes()) {
            roots.push(canonical);
        }
    }
    roots
}

pub fn environment_outside(
    variables: impl IntoIterator<Item = (OsString, OsString)>,
    roots: &[PathBuf],
) -> Vec<(OsString, OsString)> {
    variables
        .into_iter()
        .filter(|(name, _)| !FORCED_BY_BUNDLE.iter().any(|forced| name == forced))
        .filter_map(|(name, value)| {
            without_bundled_entries(value, roots).map(|value| (name, value))
        })
        .collect()
}

fn without_bundled_entries(value: OsString, roots: &[PathBuf]) -> Option<OsString> {
    let bytes = value.as_bytes();
    if !bytes
        .split(|byte| *byte == b':')
        .any(|entry| is_bundled(entry, roots))
    {
        return Some(value);
    }
    let kept: Vec<&[u8]> = bytes
        .split(|byte| *byte == b':')
        .filter(|entry| !entry.is_empty() && !is_bundled(entry, roots))
        .collect();
    (!kept.is_empty()).then(|| OsString::from_vec(kept.join(&b':')))
}

fn is_bundled(entry: &[u8], roots: &[PathBuf]) -> bool {
    roots.iter().any(|root| {
        let root = trimmed(root.as_os_str().as_bytes());
        !root.is_empty()
            && entry.starts_with(root)
            && (entry.len() == root.len() || entry[root.len()] == b'/')
    })
}

fn trimmed(path: &[u8]) -> &[u8] {
    let end = path
        .iter()
        .rposition(|byte| *byte != b'/')
        .map_or(0, |last| last + 1);
    &path[..end]
}

#[cfg(target_os = "linux")]
pub fn open_url(url: &str, appdir: &Path) -> std::io::Result<()> {
    let environment = environment_outside(std::env::vars_os(), &bundle_roots(appdir));
    let working_directory = working_directory();

    let mut launchers = open::commands(url);
    let mut bundled = std::process::Command::new(appdir.join("usr/bin/xdg-open"));
    bundled.arg(url);
    launchers.push(bundled);

    let mut last_error = None;
    for mut launcher in launchers {
        launcher
            .env_clear()
            .envs(environment.iter().map(|(name, value)| (name, value)))
            .current_dir(&working_directory);
        match spawn_detached(&mut launcher) {
            Ok(()) => return Ok(()),
            Err(error) => last_error = Some(error),
        }
    }
    Err(last_error.unwrap_or_else(|| std::io::ErrorKind::NotFound.into()))
}

#[cfg(target_os = "linux")]
fn working_directory() -> PathBuf {
    ["OWD", "HOME"]
        .into_iter()
        .filter_map(std::env::var_os)
        .map(PathBuf::from)
        .find(|path| path.is_dir())
        .unwrap_or_else(|| PathBuf::from("/"))
}

#[cfg(target_os = "linux")]
fn spawn_detached(command: &mut std::process::Command) -> std::io::Result<()> {
    use std::os::unix::process::CommandExt;
    use std::process::Stdio;

    command
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    // Same detach as the open crate: the launcher leaves our session, so the browser it starts
    // outlives Macro Deck and never receives signals meant for it.
    unsafe {
        command.pre_exec(|| {
            match libc::fork() {
                -1 => return Err(std::io::Error::last_os_error()),
                0 => {}
                _ => libc::_exit(0),
            }
            if libc::setsid() == -1 {
                return Err(std::io::Error::last_os_error());
            }
            Ok(())
        });
    }
    command.spawn()?.wait().map(|_| ())
}

#[cfg(test)]
mod tests {
    use super::*;

    const APPDIR: &str = "/tmp/.mount_MacroDabc123";

    fn outside(variables: &[(&str, &str)]) -> Vec<(String, String)> {
        environment_outside(
            variables
                .iter()
                .map(|(name, value)| (OsString::from(name), OsString::from(value))),
            &[PathBuf::from(APPDIR)],
        )
        .into_iter()
        .map(|(name, value)| (name.into_string().unwrap(), value.into_string().unwrap()))
        .collect()
    }

    fn value_of(variables: &[(String, String)], name: &str) -> Option<String> {
        variables
            .iter()
            .find(|(candidate, _)| candidate == name)
            .map(|(_, value)| value.clone())
    }

    #[test]
    fn the_system_path_is_what_remains_of_the_launcher_path() {
        let path = format!(
            "{APPDIR}/usr/bin/:{APPDIR}/usr/sbin/:{APPDIR}/usr/games/:{APPDIR}/bin/:{APPDIR}/sbin/:/usr/local/bin:/usr/bin"
        );
        let result = outside(&[("PATH", &path)]);
        assert_eq!(
            value_of(&result, "PATH").as_deref(),
            Some("/usr/local/bin:/usr/bin")
        );
    }

    #[test]
    fn variables_that_only_pointed_into_the_bundle_are_removed() {
        let result = outside(&[
            ("GTK_PATH", &format!("{APPDIR}//usr/lib/gtk-3.0")),
            (
                "GDK_PIXBUF_MODULE_FILE",
                &format!("{APPDIR}//usr/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"),
            ),
            ("PYTHONHOME", &format!("{APPDIR}/usr/")),
            (
                "LD_LIBRARY_PATH",
                &format!("{APPDIR}/usr/lib/:{APPDIR}/usr/lib/x86_64-linux-gnu/:"),
            ),
            ("PYTHONPATH", &format!("{APPDIR}/usr/share/pyshared/:")),
        ]);
        assert!(result.is_empty(), "left over: {result:?}");
    }

    #[test]
    fn the_users_own_entries_survive_in_order() {
        let result = outside(&[
            ("XDG_DATA_DIRS", &format!("{APPDIR}/usr/share/:{APPDIR}/usr/share:/usr/share:/home/u/.local/share/flatpak/exports/share:/usr/local/share")),
            ("QT_PLUGIN_PATH", &format!("{APPDIR}/usr/lib/qt5/plugins/:/usr/lib/qt6/plugins")),
        ]);
        assert_eq!(
            value_of(&result, "XDG_DATA_DIRS").as_deref(),
            Some("/usr/share:/home/u/.local/share/flatpak/exports/share:/usr/local/share")
        );
        assert_eq!(
            value_of(&result, "QT_PLUGIN_PATH").as_deref(),
            Some("/usr/lib/qt6/plugins")
        );
    }

    #[test]
    fn values_the_bundle_forced_are_dropped() {
        let result = outside(&[
            ("APPDIR", APPDIR),
            ("GTK_THEME", "Adwaita:light"),
            ("GDK_BACKEND", "x11"),
        ]);
        assert!(result.is_empty(), "left over: {result:?}");
    }

    #[test]
    fn unrelated_variables_are_passed_through_unchanged() {
        let variables = [
            ("HOME", "/home/u"),
            ("DISPLAY", ":0"),
            ("KDE_SESSION_VERSION", "6"),
            ("XDG_CURRENT_DESKTOP", "KDE"),
            ("http_proxy", "http://proxy.local:3128"),
            ("APPIMAGE", "/home/u/Applications/Macro.Deck.AppImage"),
            ("EMPTY", ""),
            ("PATH", "/usr/bin::/bin"),
        ];
        let result = outside(&variables);
        let expected: Vec<(String, String)> = variables
            .iter()
            .map(|(name, value)| (name.to_string(), value.to_string()))
            .collect();
        assert_eq!(result, expected);
    }

    #[test]
    fn a_sibling_directory_sharing_the_prefix_is_not_the_bundle() {
        let result = outside(&[(
            "PATH",
            &format!("{APPDIR}X/usr/bin:{APPDIR}/usr/bin:/usr/bin"),
        )]);
        assert_eq!(
            value_of(&result, "PATH"),
            Some(format!("{APPDIR}X/usr/bin:/usr/bin"))
        );
    }

    #[test]
    fn entries_under_the_canonical_bundle_path_are_removed() {
        let real = std::env::temp_dir()
            .canonicalize()
            .unwrap()
            .join(format!("md-appimage-real-{}", std::process::id()));
        let link = std::env::temp_dir().join(format!("md-appimage-link-{}", std::process::id()));
        std::fs::create_dir_all(&real).unwrap();
        let _ = std::fs::remove_file(&link);
        std::os::unix::fs::symlink(&real, &link).unwrap();

        let roots = bundle_roots(&link);
        let path = format!("{}/usr/bin:/usr/bin", real.display());
        let result = environment_outside([(OsString::from("PATH"), OsString::from(path))], &roots);

        std::fs::remove_file(&link).unwrap();
        std::fs::remove_dir_all(&real).unwrap();
        assert_eq!(
            result,
            vec![(OsString::from("PATH"), OsString::from("/usr/bin"))]
        );
    }
}
