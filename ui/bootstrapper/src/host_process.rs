use std::ffi::OsStr;
use std::path::{Path, PathBuf};

const PROCESS_FILE: &str = "host-process";

fn process_file(config_dir: &Path) -> PathBuf {
    config_dir.join(PROCESS_FILE)
}

pub fn persist(config_dir: &Path, pid: u32) -> std::io::Result<()> {
    std::fs::create_dir_all(config_dir)?;
    std::fs::write(process_file(config_dir), pid.to_string())
}

pub fn load(config_dir: &Path) -> Option<u32> {
    std::fs::read_to_string(process_file(config_dir))
        .ok()?
        .trim()
        .parse()
        .ok()
        .filter(|pid| *pid > 0)
}

pub fn clear(config_dir: &Path, pid: u32) {
    if load(config_dir) == Some(pid) {
        let _ = std::fs::remove_file(process_file(config_dir));
    }
}

// A pid outlives a reboot in the file and may belong to anything by then, so the name decides.
pub fn is_host_process(pid: u32, binary_name: &str) -> bool {
    executable_of(pid).is_some_and(|path| matches_binary(&path, binary_name))
}

pub fn matches_binary(path: &Path, binary_name: &str) -> bool {
    path.file_name()
        .and_then(OsStr::to_str)
        .is_some_and(|name| {
            name == binary_name || name.strip_suffix(" (deleted)") == Some(binary_name)
        })
}

#[cfg(target_os = "linux")]
fn executable_of(pid: u32) -> Option<PathBuf> {
    std::fs::read_link(format!("/proc/{pid}/exe")).ok()
}

#[cfg(target_os = "macos")]
fn executable_of(pid: u32) -> Option<PathBuf> {
    use std::os::unix::ffi::OsStrExt;

    let pid = i32::try_from(pid).ok()?;
    let mut buffer = [0u8; libc::PROC_PIDPATHINFO_MAXSIZE as usize];
    let length =
        unsafe { libc::proc_pidpath(pid, buffer.as_mut_ptr().cast(), buffer.len() as u32) };
    if length > 0 {
        return Some(PathBuf::from(OsStr::from_bytes(&buffer[..length as usize])));
    }
    // The path lookup fails once the binary is gone, as after an old bundle was removed.
    let length = unsafe { libc::proc_name(pid, buffer.as_mut_ptr().cast(), buffer.len() as u32) };
    (length > 0).then(|| PathBuf::from(OsStr::from_bytes(&buffer[..length as usize])))
}

#[cfg(windows)]
fn executable_of(pid: u32) -> Option<PathBuf> {
    use std::os::windows::ffi::OsStringExt;
    use windows::core::PWSTR;
    use windows::Win32::Foundation::{CloseHandle, STILL_ACTIVE};
    use windows::Win32::System::Threading::{
        GetExitCodeProcess, OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32,
        PROCESS_QUERY_LIMITED_INFORMATION,
    };

    let process = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) }.ok()?;
    let mut exit_code = 0u32;
    let running = unsafe { GetExitCodeProcess(process, &mut exit_code) }.is_ok()
        && exit_code == STILL_ACTIVE.0 as u32;
    let mut buffer = [0u16; 1024];
    let mut length = buffer.len() as u32;
    let named = unsafe {
        QueryFullProcessImageNameW(
            process,
            PROCESS_NAME_WIN32,
            PWSTR(buffer.as_mut_ptr()),
            &mut length,
        )
    }
    .is_ok();
    unsafe {
        let _ = CloseHandle(process);
    }
    (running && named)
        .then(|| PathBuf::from(std::ffi::OsString::from_wide(&buffer[..length as usize])))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(name: &str) -> PathBuf {
        let dir =
            std::env::temp_dir().join(format!("md-host-process-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        dir
    }

    #[test]
    fn the_pid_survives_a_round_trip_and_only_its_owner_clears_it() {
        let dir = scratch("round-trip");
        assert_eq!(load(&dir), None);
        persist(&dir, 4321).unwrap();
        assert_eq!(load(&dir), Some(4321));
        clear(&dir, 1234);
        assert_eq!(load(&dir), Some(4321));
        clear(&dir, 4321);
        assert_eq!(load(&dir), None);
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn a_replaced_binary_still_counts_on_linux() {
        assert!(matches_binary(
            Path::new("/opt/host/Macro Deck Host (deleted)"),
            "Macro Deck Host"
        ));
        assert!(!matches_binary(
            Path::new("/usr/bin/sleep"),
            "Macro Deck Host"
        ));
    }

    #[cfg(unix)]
    #[test]
    fn a_running_process_is_found_by_name_and_a_finished_one_is_gone() {
        use std::process::{Command, Stdio};
        use std::time::{Duration, Instant};

        let mut child = Command::new("sleep")
            .arg("30")
            .stdout(Stdio::null())
            .spawn()
            .unwrap();
        let pid = child.id();
        let name = executable_of(pid).unwrap();
        let name = name.file_name().unwrap().to_str().unwrap().to_string();

        assert!(is_host_process(pid, &name));
        assert!(!is_host_process(pid, "Macro Deck Host"));

        child.kill().unwrap();
        let deadline = Instant::now() + Duration::from_secs(5);
        while is_host_process(pid, &name) {
            assert!(
                Instant::now() < deadline,
                "the zombie still counts as running"
            );
            std::thread::sleep(Duration::from_millis(20));
        }
        child.wait().unwrap();
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn a_host_whose_binary_was_removed_is_still_found() {
        use std::process::{Command, Stdio};

        let dir = scratch("removed");
        std::fs::create_dir_all(&dir).unwrap();
        let binary = dir.join("stand-in");
        std::fs::copy("/bin/sleep", &binary).unwrap();
        let mut child = Command::new(&binary)
            .arg("30")
            .stdout(Stdio::null())
            .spawn()
            .unwrap();
        std::thread::sleep(std::time::Duration::from_millis(200));
        std::fs::remove_dir_all(&dir).unwrap();

        let found = is_host_process(child.id(), "stand-in");

        child.kill().unwrap();
        child.wait().unwrap();
        assert!(found);
    }

    #[test]
    fn a_recycled_pid_is_no_host() {
        assert!(!is_host_process(std::process::id(), "Macro Deck Host"));
    }
}
