use std::collections::HashMap;
use std::fs::{self, File};
use std::io::{self, Read};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, OnceLock};
use std::time::{Duration, UNIX_EPOCH};

use base64::Engine;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tauri::{AppHandle, Manager};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};
use tokio::sync::watch;

use crate::host::{self, HostState, IntegrityPostResponse};
use crate::localization::{self, keys};
use crate::logging;

pub const MANIFEST_FILE: &str = "install-manifest.json";
pub const SIGNATURE_FILE: &str = "install-manifest.json.sig";
const FORMAT_VERSION: u32 = 1;
const MANIFEST_REQUIREMENT: &str = env!("MACRODECK_INSTALL_MANIFEST");
const LOGGED_PATHS: usize = 20;
const STAT_VERDICT_WAIT: Duration = Duration::from_secs(2);
const HASH_START_FALLBACK: Duration = Duration::from_secs(60);
const RETRY_DELAYS_SECS: [u64; 4] = [2, 5, 15, 30];
const RETRY_INTERVAL_SECS: u64 = 60;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Manifest {
    format_version: u32,
    version: String,
    commit: String,
    files: Vec<ManifestFile>,
}

#[derive(Deserialize)]
struct ManifestFile {
    path: String,
    size: u64,
    sha256: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Damage {
    ManifestMissing,
    ManifestUnreadable,
    SignatureMissing,
    SignatureInvalid,
    VersionMismatch {
        manifest: String,
        expected: String,
    },
    Files {
        missing: Vec<String>,
        modified: Vec<String>,
    },
}

impl Damage {
    pub fn reason(&self) -> &'static str {
        match self {
            Damage::ManifestMissing => "manifestMissing",
            Damage::ManifestUnreadable => "manifestUnreadable",
            Damage::SignatureMissing => "signatureMissing",
            Damage::SignatureInvalid => "signatureInvalid",
            Damage::VersionMismatch { .. } => "versionMismatch",
            Damage::Files { .. } => "files",
        }
    }

    fn counts(&self) -> (usize, usize) {
        match self {
            Damage::Files { missing, modified } => (missing.len(), modified.len()),
            _ => (0, 0),
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Verdict {
    NoManifest,
    Intact,
    Damaged(Damage),
}

pub struct Expectations<'a> {
    pub public_key: &'a str,
    pub version: &'a str,
    pub manifest_required: bool,
}

pub enum StatPass {
    Done(Verdict),
    Hash(Manifest),
}

fn file_path(host_dir: &Path, relative: &str) -> PathBuf {
    relative
        .split('/')
        .fold(host_dir.to_path_buf(), |path, part| path.join(part))
}

fn decode_text(value: &str) -> Option<String> {
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(value.trim())
        .ok()?;
    String::from_utf8(bytes).ok()
}

// Same encoding and checks as tauri-plugin-updater: tauri signer writes base64 of a minisign signature file.
pub fn signature_is_valid(data: &[u8], signature: &str, public_key: &str) -> bool {
    let Some(key) =
        decode_text(public_key).and_then(|key| minisign_verify::PublicKey::decode(&key).ok())
    else {
        return false;
    };
    let Some(signature) =
        decode_text(signature).and_then(|text| minisign_verify::Signature::decode(&text).ok())
    else {
        return false;
    };
    key.verify(data, &signature, true).is_ok()
}

pub fn stat_pass(host_dir: &Path, expected: &Expectations) -> StatPass {
    let damaged = |damage| StatPass::Done(Verdict::Damaged(damage));
    let bytes = match fs::read(host_dir.join(MANIFEST_FILE)) {
        Ok(bytes) => bytes,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return if expected.manifest_required {
                damaged(Damage::ManifestMissing)
            } else {
                StatPass::Done(Verdict::NoManifest)
            };
        }
        Err(_) => return damaged(Damage::ManifestUnreadable),
    };
    let Ok(signature) = fs::read_to_string(host_dir.join(SIGNATURE_FILE)) else {
        return damaged(Damage::SignatureMissing);
    };
    if !signature_is_valid(&bytes, &signature, expected.public_key) {
        return damaged(Damage::SignatureInvalid);
    }
    let manifest = match serde_json::from_slice::<Manifest>(&bytes) {
        Ok(manifest) if manifest.format_version == FORMAT_VERSION => manifest,
        _ => return damaged(Damage::ManifestUnreadable),
    };
    if manifest.version != expected.version {
        return damaged(Damage::VersionMismatch {
            manifest: manifest.version,
            expected: expected.version.to_string(),
        });
    }

    let mut missing = Vec::new();
    let mut modified = Vec::new();
    for file in &manifest.files {
        match fs::metadata(file_path(host_dir, &file.path)) {
            Ok(metadata) if metadata.is_file() => {
                if metadata.len() != file.size {
                    modified.push(file.path.clone());
                }
            }
            _ => missing.push(file.path.clone()),
        }
    }
    if missing.is_empty() && modified.is_empty() {
        StatPass::Hash(manifest)
    } else {
        damaged(Damage::Files { missing, modified })
    }
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq, Eq)]
struct CachedDigest {
    size: u64,
    modified: u64,
    sha256: String,
}

#[derive(Serialize, Deserialize, Default, Debug)]
pub struct HashCache {
    files: HashMap<String, CachedDigest>,
}

impl HashCache {
    pub fn load(path: &Path) -> Self {
        fs::read(path)
            .ok()
            .and_then(|bytes| serde_json::from_slice(&bytes).ok())
            .unwrap_or_default()
    }

    pub fn save(&self, path: &Path) -> io::Result<()> {
        if let Some(dir) = path.parent() {
            fs::create_dir_all(dir)?;
        }
        let staging = path.with_extension("json.tmp");
        fs::write(
            &staging,
            serde_json::to_vec(self).map_err(io::Error::other)?,
        )?;
        fs::rename(staging, path)
    }
}

fn modified_nanos(metadata: &fs::Metadata) -> u64 {
    metadata
        .modified()
        .ok()
        .and_then(|time| time.duration_since(UNIX_EPOCH).ok())
        .map_or(0, |elapsed| elapsed.as_nanos() as u64)
}

fn sha256_file(path: &Path) -> io::Result<String> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; 256 * 1024];
    loop {
        let read = file.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
    }
    Ok(hex::encode(hasher.finalize()))
}

pub struct HashOutcome {
    pub verdict: Verdict,
    pub hashed: usize,
    pub unreadable: Vec<String>,
}

pub fn hash_pass(host_dir: &Path, manifest: &Manifest, cache: &mut HashCache) -> HashOutcome {
    let mut missing = Vec::new();
    let mut modified = Vec::new();
    let mut hashed = 0;
    let mut unreadable = Vec::new();
    for file in &manifest.files {
        let path = file_path(host_dir, &file.path);
        let Ok(metadata) = fs::metadata(&path) else {
            missing.push(file.path.clone());
            continue;
        };
        let stamp = modified_nanos(&metadata);
        let cached = cache
            .files
            .get(&file.path)
            .filter(|cached| cached.size == metadata.len() && cached.modified == stamp)
            .map(|cached| cached.sha256.clone());
        let digest = match cached {
            Some(digest) => digest,
            None => match sha256_file(&path) {
                Ok(digest) => {
                    hashed += 1;
                    cache.files.insert(
                        file.path.clone(),
                        CachedDigest {
                            size: metadata.len(),
                            modified: stamp,
                            sha256: digest.clone(),
                        },
                    );
                    digest
                }
                Err(_) => {
                    unreadable.push(file.path.clone());
                    continue;
                }
            },
        };
        if metadata.len() != file.size || !digest.eq_ignore_ascii_case(&file.sha256) {
            modified.push(file.path.clone());
        }
    }
    let verdict = if missing.is_empty() && modified.is_empty() {
        Verdict::Intact
    } else {
        Verdict::Damaged(Damage::Files { missing, modified })
    };
    HashOutcome {
        verdict,
        hashed,
        unreadable,
    }
}

pub fn prune_other_caches(dir: &Path, keep: &Path) {
    let Ok(entries) = fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let name = entry.file_name().to_string_lossy().into_owned();
        if path != keep && name.starts_with("install-integrity-") && name.ends_with(".json") {
            let _ = fs::remove_file(path);
        }
    }
}

pub fn cache_file_name(install_identity: &str, commit: &str) -> String {
    let digest = Sha256::digest(format!("{install_identity}\n{commit}").as_bytes());
    format!("install-integrity-{}.json", &hex::encode(digest)[..16])
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum InstallKind {
    Windows,
    Macos,
    AppImage,
    LinuxPackage,
}

pub fn install_kind_for(os: &str, appimage: bool) -> InstallKind {
    match os {
        "windows" => InstallKind::Windows,
        "macos" => InstallKind::Macos,
        _ if appimage => InstallKind::AppImage,
        _ => InstallKind::LinuxPackage,
    }
}

fn install_kind() -> InstallKind {
    install_kind_for(std::env::consts::OS, std::env::var_os("APPIMAGE").is_some())
}

pub fn offers_download(kind: InstallKind) -> bool {
    kind != InstallKind::LinuxPackage
}

pub fn damage_text(kind: InstallKind) -> String {
    if offers_download(kind) {
        localization::t(keys::HOST_ERROR_INSTALLATION_DAMAGED)
    } else {
        localization::t(keys::HOST_ERROR_INSTALLATION_DAMAGED_PACKAGE)
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct IntegrityReportBody {
    pub status: &'static str,
    pub reason: &'static str,
    pub missing: usize,
    pub modified: usize,
    pub install_kind: InstallKind,
}

pub fn report_body(damage: &Damage, kind: InstallKind) -> IntegrityReportBody {
    let (missing, modified) = damage.counts();
    IntegrityReportBody {
        status: "damaged",
        reason: damage.reason(),
        missing,
        modified,
        install_kind: kind,
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AttemptOutcome {
    Delivered,
    Retry,
    HostCannotShow,
}

// 404 or a 2xx without the ack comes from a host older than the endpoint (its SPA fallback answers
// 200). It can also follow a secret rotation between proof and post, which costs one extra dialog.
pub fn classify(response: &IntegrityPostResponse) -> AttemptOutcome {
    match *response {
        IntegrityPostResponse::NoSecret | IntegrityPostResponse::TransportError => {
            AttemptOutcome::Retry
        }
        IntegrityPostResponse::Status { code, acknowledged } => match code {
            200..=299 if acknowledged => AttemptOutcome::Delivered,
            408 | 429 | 500..=599 => AttemptOutcome::Retry,
            _ => AttemptOutcome::HostCannotShow,
        },
    }
}

pub fn retry_delay(attempt: usize) -> Duration {
    Duration::from_secs(
        RETRY_DELAYS_SECS
            .get(attempt)
            .copied()
            .unwrap_or(RETRY_INTERVAL_SECS),
    )
}

// The verdict side marks damage then reads ready; the host side sets ready then calls try_start.
// With both SeqCst at least one of them starts delivery, and the running flag keeps it to one loop.
#[derive(Debug)]
pub struct Delivery {
    damaged: AtomicBool,
    running: AtomicBool,
    fallback_shown: AtomicBool,
    generation: AtomicU64,
    delivered_generation: AtomicU64,
}

impl Default for Delivery {
    fn default() -> Self {
        Self {
            damaged: AtomicBool::new(false),
            running: AtomicBool::new(false),
            fallback_shown: AtomicBool::new(false),
            generation: AtomicU64::new(0),
            delivered_generation: AtomicU64::new(u64::MAX),
        }
    }
}

impl Delivery {
    pub fn mark_damaged(&self) {
        self.damaged.store(true, Ordering::SeqCst);
    }

    pub fn host_gone(&self) {
        self.generation.fetch_add(1, Ordering::SeqCst);
    }

    pub fn generation(&self) -> u64 {
        self.generation.load(Ordering::SeqCst)
    }

    pub fn is_delivered(&self) -> bool {
        self.delivered_generation.load(Ordering::SeqCst) == self.generation()
    }

    pub fn mark_delivered(&self, generation: u64) {
        self.delivered_generation
            .store(generation, Ordering::SeqCst);
    }

    pub fn try_start(&self, host_ready: bool) -> bool {
        host_ready
            && self.damaged.load(Ordering::SeqCst)
            && !self.is_delivered()
            && self
                .running
                .compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst)
                .is_ok()
    }

    pub fn finish(&self, host_ready: bool) -> bool {
        self.running.store(false, Ordering::SeqCst);
        self.try_start(host_ready)
    }

    pub fn take_fallback(&self) -> bool {
        !self.fallback_shown.swap(true, Ordering::SeqCst)
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Progress {
    Pending,
    Hashing,
    Final(Verdict),
}

struct Runtime {
    progress: watch::Sender<Progress>,
    hash_allowed: watch::Sender<bool>,
    delivery: Delivery,
    kind: InstallKind,
}

static RUNTIME: OnceLock<Runtime> = OnceLock::new();

fn runtime() -> &'static Runtime {
    RUNTIME.get_or_init(|| Runtime {
        progress: watch::Sender::new(Progress::Pending),
        hash_allowed: watch::Sender::new(false),
        delivery: Delivery::default(),
        kind: install_kind(),
    })
}

pub fn install_damage() -> Option<InstallKind> {
    let runtime = RUNTIME.get()?;
    matches!(
        *runtime.progress.borrow(),
        Progress::Final(Verdict::Damaged(_))
    )
    .then_some(runtime.kind)
}

fn public_key(app: &AppHandle) -> String {
    app.config()
        .plugins
        .0
        .get("updater")
        .and_then(|updater| updater.get("pubkey"))
        .and_then(|key| key.as_str())
        .unwrap_or_default()
        .to_string()
}

fn install_identity(host_dir: &Path) -> String {
    std::env::var("APPIMAGE").unwrap_or_else(|_| {
        host_dir
            .canonicalize()
            .unwrap_or_else(|_| host_dir.to_path_buf())
            .to_string_lossy()
            .into_owned()
    })
}

fn log_damage(damage: &Damage) {
    match damage {
        Damage::Files { missing, modified } => {
            logging::error(&format!(
                "[integrity] the installation is damaged: {} missing and {} modified files",
                missing.len(),
                modified.len()
            ));
            for path in missing.iter().take(LOGGED_PATHS) {
                logging::error(&format!("[integrity] missing: {path}"));
            }
            for path in modified.iter().take(LOGGED_PATHS) {
                logging::error(&format!("[integrity] modified: {path}"));
            }
        }
        Damage::VersionMismatch { manifest, expected } => logging::error(&format!(
            "[integrity] the installation is damaged: the installed files are version {manifest}, Macro Deck is {expected}"
        )),
        other => logging::error(&format!(
            "[integrity] the installation is damaged: {}",
            other.reason()
        )),
    }
}

pub fn spawn(app: &AppHandle) {
    if !host::is_packaged() {
        return;
    }
    let runtime = runtime();
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        let host_dir = host::host_dir(&app);
        let public_key = public_key(&app);
        let version = crate::updater::current_version(&app);
        let cache_dir = app.path().app_cache_dir().ok();
        let stat_dir = host_dir.clone();
        let stat = tauri::async_runtime::spawn_blocking(move || {
            stat_pass(
                &stat_dir,
                &Expectations {
                    public_key: &public_key,
                    version: &version,
                    manifest_required: MANIFEST_REQUIREMENT == "required",
                },
            )
        })
        .await;
        let manifest = match stat {
            Ok(StatPass::Hash(manifest)) => manifest,
            Ok(StatPass::Done(verdict)) => return conclude(&app, verdict, None),
            Err(error) => {
                logging::warn(&format!("[integrity] the check did not run: {error}"));
                return;
            }
        };

        runtime.progress.send_replace(Progress::Hashing);
        let mut allowed = runtime.hash_allowed.subscribe();
        let _ =
            tokio::time::timeout(HASH_START_FALLBACK, allowed.wait_for(|allowed| *allowed)).await;

        let cache_path = cache_dir.map(|dir| {
            dir.join(cache_file_name(
                &install_identity(&host_dir),
                &manifest.commit,
            ))
        });
        let hashed = tauri::async_runtime::spawn_blocking(move || {
            let mut cache = cache_path
                .as_deref()
                .map(HashCache::load)
                .unwrap_or_default();
            let outcome = hash_pass(&host_dir, &manifest, &mut cache);
            if let Some(path) = cache_path.as_deref() {
                if let Err(error) = cache.save(path) {
                    logging::warn(&format!(
                        "[integrity] could not save the hash cache: {error}"
                    ));
                }
                if let Some(dir) = path.parent() {
                    prune_other_caches(dir, path);
                }
            }
            (outcome, manifest.files.len())
        })
        .await;
        match hashed {
            Ok((outcome, files)) => {
                for path in outcome.unreadable.iter().take(LOGGED_PATHS) {
                    logging::warn(&format!(
                        "[integrity] could not read {path}; it may be held by another program"
                    ));
                }
                conclude(&app, outcome.verdict, Some((files, outcome.hashed)))
            }
            Err(error) => logging::warn(&format!("[integrity] the check did not finish: {error}")),
        }
    });
}

fn conclude(app: &AppHandle, verdict: Verdict, hashed: Option<(usize, usize)>) {
    let runtime = runtime();
    match &verdict {
        Verdict::NoManifest => {
            logging::info("[integrity] no install manifest in this build; skipping the check")
        }
        Verdict::Intact => {
            let (files, hashed) = hashed.unwrap_or_default();
            logging::info(&format!(
                "[integrity] the installation is intact ({files} files, {hashed} hashed)"
            ));
        }
        Verdict::Damaged(damage) => log_damage(damage),
    }
    let damaged = matches!(verdict, Verdict::Damaged(_));
    runtime.progress.send_replace(Progress::Final(verdict));
    if !damaged {
        return;
    }
    runtime.delivery.mark_damaged();
    let ready = app.state::<Arc<HostState>>().ready.load(Ordering::SeqCst);
    if runtime.delivery.try_start(ready) {
        spawn_delivery(app);
    }
    crate::host_error_window::refresh_installation_damage(app);
}

pub fn host_ready(app: &AppHandle) {
    let Some(runtime) = RUNTIME.get() else {
        return;
    };
    runtime.hash_allowed.send_replace(true);
    if runtime.delivery.try_start(true) {
        spawn_delivery(app);
    }
}

pub fn host_gone() {
    if let Some(runtime) = RUNTIME.get() {
        runtime.delivery.host_gone();
    }
}

pub async fn settle_before_error_window() {
    let Some(runtime) = RUNTIME.get() else {
        return;
    };
    runtime.hash_allowed.send_replace(true);
    let mut progress = runtime.progress.subscribe();
    let _ = tokio::time::timeout(
        STAT_VERDICT_WAIT,
        progress.wait_for(|progress| *progress != Progress::Pending),
    )
    .await;
}

fn spawn_delivery(app: &AppHandle) {
    let app = app.clone();
    tauri::async_runtime::spawn(async move { deliver(&app).await });
}

async fn deliver(app: &AppHandle) {
    let runtime = runtime();
    let damage = match &*runtime.progress.borrow() {
        Progress::Final(Verdict::Damaged(damage)) => damage.clone(),
        _ => return,
    };
    let body = report_body(&damage, runtime.kind);
    loop {
        let mut attempt = 0;
        loop {
            let ready = app.state::<Arc<HostState>>().ready.load(Ordering::SeqCst);
            if !ready || runtime.delivery.is_delivered() {
                break;
            }
            let generation = runtime.delivery.generation();
            let response = host::post_installation_integrity(app, &body).await;
            match classify(&response) {
                AttemptOutcome::Delivered => {
                    runtime.delivery.mark_delivered(generation);
                    logging::info("[integrity] reported the damaged installation to the host");
                    break;
                }
                AttemptOutcome::HostCannotShow => {
                    runtime.delivery.mark_delivered(generation);
                    logging::warn(&format!(
                        "[integrity] the host cannot show the notice ({response:?}); showing it natively"
                    ));
                    show_fallback(app, runtime);
                    break;
                }
                AttemptOutcome::Retry => {
                    tokio::time::sleep(retry_delay(attempt)).await;
                    attempt += 1;
                }
            }
        }
        let ready = app.state::<Arc<HostState>>().ready.load(Ordering::SeqCst);
        if !runtime.delivery.finish(ready) {
            return;
        }
    }
}

fn show_fallback(app: &AppHandle, runtime: &Runtime) {
    if !runtime.delivery.take_fallback() {
        return;
    }
    let dialog = app
        .dialog()
        .message(damage_text(runtime.kind))
        .title(localization::t(keys::INSTALLATION_DAMAGED_TITLE))
        .kind(MessageDialogKind::Warning);
    if !offers_download(runtime.kind) {
        dialog.show(|_| {});
        return;
    }
    let handle = app.clone();
    dialog
        .buttons(MessageDialogButtons::OkCancelCustom(
            localization::t(keys::UPDATE_OPEN_DOWNLOAD_PAGE),
            localization::t(keys::UPDATE_LATER),
        ))
        .show(move |open| {
            if open {
                if let Err(error) =
                    crate::external_url::open(&handle, crate::updater::DOWNLOAD_PAGE_URL)
                {
                    logging::warn(&format!(
                        "[integrity] could not open the download page: {error}"
                    ));
                }
            }
        });
}

#[cfg(test)]
mod tests {
    use super::*;

    const VERSION: &str = "3.1.0-beta.2";

    fn fixture() -> PathBuf {
        Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/install-integrity")
    }

    fn fixture_key() -> String {
        fs::read_to_string(fixture().join("public-key"))
            .unwrap()
            .trim()
            .to_string()
    }

    fn copy_dir(from: &Path, to: &Path) {
        fs::create_dir_all(to).unwrap();
        for entry in fs::read_dir(from).unwrap() {
            let entry = entry.unwrap();
            let target = to.join(entry.file_name());
            if entry.file_type().unwrap().is_dir() {
                copy_dir(&entry.path(), &target);
            } else {
                fs::copy(entry.path(), target).unwrap();
            }
        }
    }

    struct Installed(PathBuf);

    impl std::ops::Deref for Installed {
        type Target = Path;

        fn deref(&self) -> &Path {
            &self.0
        }
    }

    impl Drop for Installed {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
            let _ = fs::remove_file(self.0.with_extension("cache.json"));
        }
    }

    fn installed_copy(name: &str) -> Installed {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-integrity-{name}-{}",
            std::process::id()
        ));
        let _ = fs::remove_dir_all(&dir);
        copy_dir(&fixture().join("host"), &dir);
        Installed(dir)
    }

    fn check(host_dir: &Path, key: &str, version: &str, required: bool) -> Verdict {
        let expected = Expectations {
            public_key: key,
            version,
            manifest_required: required,
        };
        match stat_pass(host_dir, &expected) {
            StatPass::Done(verdict) => verdict,
            StatPass::Hash(manifest) => {
                hash_pass(host_dir, &manifest, &mut HashCache::default()).verdict
            }
        }
    }

    fn files(missing: &[&str], modified: &[&str]) -> Verdict {
        Verdict::Damaged(Damage::Files {
            missing: missing.iter().map(|path| path.to_string()).collect(),
            modified: modified.iter().map(|path| path.to_string()).collect(),
        })
    }

    #[test]
    fn an_installation_matching_its_signed_manifest_is_intact() {
        let dir = installed_copy("intact");
        assert_eq!(check(&dir, &fixture_key(), VERSION, true), Verdict::Intact);
    }

    #[test]
    fn a_missing_file_is_reported_without_hashing() {
        let dir = installed_copy("missing");
        fs::remove_file(dir.join("wwwroot/admin/main.js")).unwrap();
        let expected = Expectations {
            public_key: &fixture_key(),
            version: VERSION,
            manifest_required: true,
        };
        assert!(matches!(
            stat_pass(&dir, &expected),
            StatPass::Done(verdict) if verdict == files(&["wwwroot/admin/main.js"], &[])
        ));
    }

    #[test]
    fn a_file_with_the_same_size_but_other_content_is_reported_as_modified() {
        let dir = installed_copy("modified");
        fs::write(
            dir.join("runtime/shared/libhost.txt"),
            "runtime library tampered\n",
        )
        .unwrap();
        assert_eq!(
            check(&dir, &fixture_key(), VERSION, true),
            files(&[], &["runtime/shared/libhost.txt"])
        );
    }

    #[test]
    fn files_the_manifest_does_not_list_are_ignored() {
        let dir = installed_copy("extra");
        fs::write(dir.join("leftover-from-an-older-build.js"), "old").unwrap();
        fs::create_dir_all(dir.join("plugins")).unwrap();
        fs::write(dir.join("plugins/state.json"), "{}").unwrap();
        assert_eq!(check(&dir, &fixture_key(), VERSION, true), Verdict::Intact);
    }

    #[test]
    fn an_edited_manifest_fails_its_signature() {
        let dir = installed_copy("edited-manifest");
        let manifest = fs::read_to_string(dir.join(MANIFEST_FILE)).unwrap();
        fs::write(
            dir.join(MANIFEST_FILE),
            manifest.replace("\"size\": 41", "\"size\": 42"),
        )
        .unwrap();
        assert_eq!(
            check(&dir, &fixture_key(), VERSION, true),
            Verdict::Damaged(Damage::SignatureInvalid)
        );
    }

    #[test]
    fn a_missing_signature_is_damage() {
        let dir = installed_copy("no-signature");
        fs::remove_file(dir.join(SIGNATURE_FILE)).unwrap();
        assert_eq!(
            check(&dir, &fixture_key(), VERSION, true),
            Verdict::Damaged(Damage::SignatureMissing)
        );
    }

    #[test]
    fn a_manifest_signed_with_another_key_is_rejected() {
        let release_key =
            serde_json::from_str::<serde_json::Value>(include_str!("../tauri.conf.json")).unwrap()
                ["plugins"]["updater"]["pubkey"]
                .as_str()
                .unwrap()
                .to_string();
        let dir = installed_copy("foreign-key");
        assert_eq!(
            check(&dir, &release_key, VERSION, true),
            Verdict::Damaged(Damage::SignatureInvalid)
        );
    }

    #[test]
    fn host_files_from_another_version_are_damage() {
        let dir = installed_copy("version");
        assert_eq!(
            check(&dir, &fixture_key(), "3.1.0", true),
            Verdict::Damaged(Damage::VersionMismatch {
                manifest: VERSION.to_string(),
                expected: "3.1.0".to_string(),
            })
        );
    }

    #[test]
    fn a_missing_manifest_is_damage_only_when_the_build_requires_one() {
        let dir = installed_copy("no-manifest");
        fs::remove_file(dir.join(MANIFEST_FILE)).unwrap();
        assert_eq!(
            check(&dir, &fixture_key(), VERSION, true),
            Verdict::Damaged(Damage::ManifestMissing)
        );
        assert_eq!(
            check(&dir, &fixture_key(), VERSION, false),
            Verdict::NoManifest
        );
    }

    #[test]
    fn a_missing_host_directory_is_damage_when_the_manifest_is_required() {
        let dir =
            std::env::temp_dir().join(format!("macro-deck-integrity-gone-{}", std::process::id()));
        assert_eq!(
            check(&dir, &fixture_key(), VERSION, true),
            Verdict::Damaged(Damage::ManifestMissing)
        );
    }

    #[test]
    fn unchanged_files_are_not_hashed_again_on_the_next_start() {
        let dir = installed_copy("cache");
        let expected = Expectations {
            public_key: &fixture_key(),
            version: VERSION,
            manifest_required: true,
        };
        let StatPass::Hash(manifest) = stat_pass(&dir, &expected) else {
            panic!("the fixture should pass the stat pass");
        };
        let cache_path = dir.with_extension("cache.json");
        let mut cache = HashCache::default();
        let first = hash_pass(&dir, &manifest, &mut cache);
        assert_eq!((first.verdict, first.hashed), (Verdict::Intact, 5));
        cache.save(&cache_path).unwrap();

        let mut reloaded = HashCache::load(&cache_path);
        let second = hash_pass(&dir, &manifest, &mut reloaded);
        assert_eq!((second.verdict, second.hashed), (Verdict::Intact, 0));

        let changed = dir.join("wwwroot/index.html");
        fs::write(&changed, "<!doctype html><title>Macro Deck</title>?").unwrap();
        let later = std::time::SystemTime::now() + Duration::from_secs(5);
        File::options()
            .write(true)
            .open(&changed)
            .unwrap()
            .set_modified(later)
            .unwrap();
        let third = hash_pass(&dir, &manifest, &mut reloaded);
        assert_eq!(third.hashed, 1);
        assert_eq!(third.verdict, files(&[], &["wwwroot/index.html"]));
    }

    #[cfg(unix)]
    #[test]
    fn a_file_that_cannot_be_read_right_now_is_not_reported_as_damage() {
        use std::os::unix::fs::PermissionsExt;
        let dir = installed_copy("unreadable");
        let locked = dir.join("wwwroot/index.html");
        fs::set_permissions(&locked, fs::Permissions::from_mode(0o000)).unwrap();
        let expected = Expectations {
            public_key: &fixture_key(),
            version: VERSION,
            manifest_required: true,
        };
        let StatPass::Hash(manifest) = stat_pass(&dir, &expected) else {
            panic!("the fixture should pass the stat pass");
        };
        let outcome = hash_pass(&dir, &manifest, &mut HashCache::default());
        fs::set_permissions(&locked, fs::Permissions::from_mode(0o644)).unwrap();
        assert_eq!(outcome.verdict, Verdict::Intact);
        assert_eq!(outcome.unreadable, ["wwwroot/index.html"]);
    }

    #[test]
    fn caches_of_earlier_builds_are_removed() {
        let dir =
            std::env::temp_dir().join(format!("macro-deck-integrity-prune-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let keep = dir.join(cache_file_name("host", "new"));
        let old = dir.join(cache_file_name("host", "old"));
        let unrelated = dir.join("appearance.json");
        for path in [&keep, &old, &unrelated] {
            fs::write(path, "{}").unwrap();
        }
        prune_other_caches(&dir, &keep);
        let remaining = (keep.exists(), old.exists(), unrelated.exists());
        let _ = fs::remove_dir_all(&dir);
        assert_eq!(remaining, (true, false, true));
    }

    #[test]
    fn a_corrupt_cache_is_ignored() {
        let path = std::env::temp_dir().join(format!(
            "macro-deck-integrity-corrupt-{}.json",
            std::process::id()
        ));
        fs::write(&path, "not json").unwrap();
        let cache = HashCache::load(&path);
        let _ = fs::remove_file(&path);
        assert!(cache.files.is_empty());
    }

    #[test]
    fn caches_are_separate_per_installation_and_build() {
        let deb = cache_file_name("/usr/lib/Macro Deck/host", "abc1234");
        assert_ne!(
            deb,
            cache_file_name("/home/me/Macro_Deck.AppImage", "abc1234")
        );
        assert_ne!(deb, cache_file_name("/usr/lib/Macro Deck/host", "def5678"));
        assert_eq!(deb, cache_file_name("/usr/lib/Macro Deck/host", "abc1234"));
    }

    #[test]
    fn linux_packages_are_told_to_use_their_package_manager() {
        assert_eq!(install_kind_for("windows", false), InstallKind::Windows);
        assert_eq!(install_kind_for("macos", false), InstallKind::Macos);
        assert_eq!(install_kind_for("linux", true), InstallKind::AppImage);
        assert_eq!(install_kind_for("linux", false), InstallKind::LinuxPackage);
        assert!(offers_download(InstallKind::AppImage));
        assert!(!offers_download(InstallKind::LinuxPackage));
    }

    #[test]
    fn the_report_names_the_reason_and_counts() {
        let body = serde_json::to_value(report_body(
            &Damage::Files {
                missing: vec!["a".into()],
                modified: vec!["b".into(), "c".into()],
            },
            InstallKind::LinuxPackage,
        ))
        .unwrap();
        assert_eq!(
            body,
            serde_json::json!({
                "status": "damaged",
                "reason": "files",
                "missing": 1,
                "modified": 2,
                "installKind": "linuxPackage",
            })
        );
    }

    fn status(code: u16, acknowledged: bool) -> IntegrityPostResponse {
        IntegrityPostResponse::Status { code, acknowledged }
    }

    #[test]
    fn only_an_acknowledged_answer_counts_as_delivered() {
        assert_eq!(classify(&status(200, true)), AttemptOutcome::Delivered);
        assert_eq!(
            classify(&status(200, false)),
            AttemptOutcome::HostCannotShow
        );
    }

    #[test]
    fn a_host_without_the_endpoint_gets_the_native_notice() {
        assert_eq!(
            classify(&status(404, false)),
            AttemptOutcome::HostCannotShow
        );
        assert_eq!(
            classify(&status(405, false)),
            AttemptOutcome::HostCannotShow
        );
    }

    #[test]
    fn an_unauthenticated_or_busy_host_is_retried_not_given_up_on() {
        assert_eq!(
            classify(&IntegrityPostResponse::NoSecret),
            AttemptOutcome::Retry
        );
        assert_eq!(
            classify(&IntegrityPostResponse::TransportError),
            AttemptOutcome::Retry
        );
        for code in [408, 429, 500, 503] {
            assert_eq!(
                classify(&status(code, false)),
                AttemptOutcome::Retry,
                "{code}"
            );
        }
    }

    #[test]
    fn retries_back_off_and_then_settle_on_a_minute() {
        let delays: Vec<u64> = (0..6)
            .map(|attempt| retry_delay(attempt).as_secs())
            .collect();
        assert_eq!(delays, [2, 5, 15, 30, 60, 60]);
    }

    #[test]
    fn a_verdict_before_the_host_is_ready_is_delivered_once_it_is() {
        let delivery = Delivery::default();
        delivery.mark_damaged();
        assert!(!delivery.try_start(false));
        assert!(delivery.try_start(true));
        assert!(!delivery.try_start(true));
    }

    #[test]
    fn a_host_ready_before_the_verdict_is_delivered_to_when_it_arrives() {
        let delivery = Delivery::default();
        assert!(!delivery.try_start(true));
        delivery.mark_damaged();
        assert!(delivery.try_start(true));
    }

    #[test]
    fn an_intact_installation_never_reports() {
        let delivery = Delivery::default();
        assert!(!delivery.try_start(true));
    }

    #[test]
    fn a_restarted_host_is_told_again() {
        let delivery = Delivery::default();
        delivery.mark_damaged();
        assert!(delivery.try_start(true));
        delivery.mark_delivered(delivery.generation());
        assert!(!delivery.finish(true));

        delivery.host_gone();
        assert!(delivery.try_start(true));
    }

    #[test]
    fn a_delivery_to_the_previous_host_does_not_count_for_the_restarted_one() {
        let delivery = Delivery::default();
        delivery.mark_damaged();
        assert!(delivery.try_start(true));
        let before_restart = delivery.generation();
        delivery.host_gone();
        delivery.mark_delivered(before_restart);
        assert!(!delivery.is_delivered());
    }

    #[test]
    fn a_host_ready_again_while_the_loop_winds_down_is_still_told() {
        let delivery = Delivery::default();
        delivery.mark_damaged();
        assert!(delivery.try_start(true));
        delivery.host_gone();
        assert!(!delivery.try_start(true));
        assert!(delivery.finish(true));
    }

    #[test]
    fn the_native_notice_is_shown_at_most_once() {
        let delivery = Delivery::default();
        assert!(delivery.take_fallback());
        assert!(!delivery.take_fallback());
    }
}
