use std::fs::OpenOptions;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;

pub const BACKUP_EXTENSION: &str = "macroDeckBackup";

const CONNECT_TIMEOUT: Duration = Duration::from_secs(5);
const READ_TIMEOUT: Duration = Duration::from_secs(30);

pub fn select_download_port(
    packaged: bool,
    ui_port: Option<u16>,
    current_port: Option<u16>,
) -> Option<u16> {
    if packaged {
        ui_port
    } else {
        current_port
    }
}

pub fn is_backup_id(value: &str) -> bool {
    let groups: Vec<&str> = value.split('-').collect();
    groups.len() == 5
        && groups.iter().zip([8, 4, 4, 4, 12]).all(|(group, length)| {
            group.len() == length && group.chars().all(|c| c.is_ascii_hexdigit())
        })
}

pub fn with_extension(path: PathBuf, extension: &str) -> (PathBuf, bool) {
    let has_extension = path
        .extension()
        .and_then(|value| value.to_str())
        .is_some_and(|value| value.eq_ignore_ascii_case(extension));
    if has_extension {
        return (path, false);
    }

    let mut name = path.file_name().unwrap_or_default().to_os_string();
    name.push(".");
    name.push(extension);
    (path.with_file_name(name), true)
}

pub fn default_file_name(name: &str) -> String {
    let (path, _) = with_extension(PathBuf::from(name), BACKUP_EXTENSION);
    path.to_string_lossy().into_owned()
}

struct TempFile {
    path: PathBuf,
}

impl Drop for TempFile {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

static NEXT_TEMP_FILE: AtomicU64 = AtomicU64::new(0);

fn temp_path_beside(target: &Path) -> PathBuf {
    let name = target.file_name().unwrap_or_default().to_string_lossy();
    let nanos = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|elapsed| elapsed.subsec_nanos())
        .unwrap_or_default();
    target.with_file_name(format!(
        ".{name}.{}-{nanos}-{}.part",
        std::process::id(),
        NEXT_TEMP_FILE.fetch_add(1, Ordering::Relaxed)
    ))
}

pub async fn download_to(port: u16, backup_id: &str, target: &Path) -> Result<(), String> {
    if !is_backup_id(backup_id) {
        return Err(format!("not a backup id: {backup_id}"));
    }

    // The archive carries key material, so a proxy from the environment must never see it.
    let client = reqwest::Client::builder()
        .no_proxy()
        .connect_timeout(CONNECT_TIMEOUT)
        .read_timeout(READ_TIMEOUT)
        .build()
        .map_err(|error| error.to_string())?;

    let mut response = client
        .get(format!(
            "http://127.0.0.1:{port}/api/backups/{backup_id}/download"
        ))
        .send()
        .await
        .map_err(|error| error.to_string())?;
    if !response.status().is_success() {
        return Err(format!("the host answered {}", response.status()));
    }

    // Written beside the chosen file and renamed over it only when complete, so a broken transfer
    // never costs the copy it would have replaced; the same folder keeps the rename atomic.
    let temp = TempFile {
        path: temp_path_beside(target),
    };
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temp.path)
        .map_err(|error| error.to_string())?;
    while let Some(chunk) = response.chunk().await.map_err(|error| error.to_string())? {
        file.write_all(&chunk).map_err(|error| error.to_string())?;
    }
    file.sync_all().map_err(|error| error.to_string())?;
    drop(file);

    std::fs::rename(&temp.path, target).map_err(|error| error.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Read;
    use std::net::TcpListener;

    const BACKUP_ID: &str = "15a6311f-3a9f-44cb-b815-e0650e97dd21";

    fn serve_once(response: Vec<u8>) -> u16 {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        std::thread::spawn(move || {
            let (mut stream, _) = listener.accept().unwrap();
            let mut request = [0u8; 4096];
            let _ = stream.read(&mut request);
            let _ = stream.write_all(&response);
        });
        port
    }

    struct ScratchDir(PathBuf);

    impl ScratchDir {
        fn new() -> Self {
            let dir = std::env::temp_dir().join(format!(
                "macro-deck-backup-download-{}-{}",
                std::process::id(),
                NEXT_TEMP_FILE.fetch_add(1, Ordering::Relaxed)
            ));
            std::fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }

        fn join(&self, name: &str) -> PathBuf {
            self.0.join(name)
        }

        fn entries(&self) -> usize {
            std::fs::read_dir(&self.0).unwrap().count()
        }
    }

    impl Drop for ScratchDir {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn a_successful_download_is_written_byte_for_byte() {
        let body = b"archive-bytes-\x00\x01\x02".to_vec();
        let mut response = format!(
            "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
            body.len()
        )
        .into_bytes();
        response.extend_from_slice(&body);
        let port = serve_once(response);
        let scratch = ScratchDir::new();
        let target = scratch.join("copy.macroDeckBackup");

        let result = tauri::async_runtime::block_on(download_to(port, BACKUP_ID, &target));

        assert_eq!(result, Ok(()));
        assert_eq!(std::fs::read(&target).unwrap(), body);
    }

    #[test]
    fn a_refused_download_leaves_an_existing_file_untouched() {
        let port = serve_once(
            b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".to_vec(),
        );
        let scratch = ScratchDir::new();
        let target = scratch.join("existing.macroDeckBackup");
        std::fs::write(&target, b"previous copy").unwrap();

        let result = tauri::async_runtime::block_on(download_to(port, BACKUP_ID, &target));

        assert!(result.is_err());
        assert_eq!(std::fs::read(&target).unwrap(), b"previous copy");
    }

    #[test]
    fn a_download_cut_short_leaves_no_file_behind() {
        let port = serve_once(
            b"HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\nonly-part"
                .to_vec(),
        );
        let scratch = ScratchDir::new();
        let target = scratch.join("truncated.macroDeckBackup");

        let result = tauri::async_runtime::block_on(download_to(port, BACKUP_ID, &target));

        assert!(result.is_err());
        assert_eq!(
            scratch.entries(),
            0,
            "neither the target nor a partial file remains"
        );
    }

    #[test]
    fn a_download_cut_short_keeps_the_copy_it_would_have_replaced() {
        let port = serve_once(
            b"HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\nonly-part"
                .to_vec(),
        );
        let scratch = ScratchDir::new();
        let target = scratch.join("earlier.macroDeckBackup");
        std::fs::write(&target, b"earlier copy").unwrap();

        let result = tauri::async_runtime::block_on(download_to(port, BACKUP_ID, &target));

        assert!(result.is_err());
        assert_eq!(std::fs::read(&target).unwrap(), b"earlier copy");
        assert_eq!(scratch.entries(), 1, "no partial file is left beside it");
    }

    #[test]
    fn a_completed_download_replaces_an_earlier_copy() {
        let body = b"new archive".to_vec();
        let mut response = format!(
            "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
            body.len()
        )
        .into_bytes();
        response.extend_from_slice(&body);
        let port = serve_once(response);
        let scratch = ScratchDir::new();
        let target = scratch.join("replaced.macroDeckBackup");
        std::fs::write(&target, b"earlier copy").unwrap();

        let result = tauri::async_runtime::block_on(download_to(port, BACKUP_ID, &target));

        assert_eq!(result, Ok(()));
        assert_eq!(std::fs::read(&target).unwrap(), body);
    }

    #[test]
    fn only_a_backup_id_reaches_the_url() {
        assert!(is_backup_id(BACKUP_ID));
        assert!(!is_backup_id("../../api/secrets"));
        assert!(!is_backup_id("15a6311f3a9f44cbb815e0650e97dd21"));
        assert!(!is_backup_id(""));
    }

    #[test]
    fn the_backup_extension_is_appended_only_when_missing() {
        assert_eq!(
            with_extension(PathBuf::from("/tmp/mine"), BACKUP_EXTENSION),
            (PathBuf::from("/tmp/mine.macroDeckBackup"), true)
        );
        assert_eq!(
            with_extension(PathBuf::from("/tmp/mine.MACRODECKBACKUP"), BACKUP_EXTENSION),
            (PathBuf::from("/tmp/mine.MACRODECKBACKUP"), false)
        );
        assert_eq!(
            with_extension(PathBuf::from("/tmp/mine.zip"), BACKUP_EXTENSION),
            (PathBuf::from("/tmp/mine.zip.macroDeckBackup"), true)
        );
        assert_eq!(
            default_file_name("20260923T190435Z-15a6311f"),
            "20260923T190435Z-15a6311f.macroDeckBackup"
        );
    }

    #[test]
    fn a_packaged_build_downloads_only_from_the_host_it_launched() {
        assert_eq!(
            select_download_port(true, Some(62309), Some(5191)),
            Some(62309)
        );
        assert_eq!(select_download_port(true, None, Some(5191)), None);
        assert_eq!(select_download_port(false, None, Some(5191)), Some(5191));
    }
}
