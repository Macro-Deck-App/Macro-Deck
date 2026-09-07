use std::path::PathBuf;

use serde_json::Value;

fn manifest_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
}

fn load(rel: &str) -> Value {
    let path = manifest_dir().join(rel);
    let raw = std::fs::read_to_string(&path)
        .unwrap_or_else(|e| panic!("failed to read {}: {e}", path.display()));
    serde_json::from_str(&raw).unwrap_or_else(|e| panic!("invalid JSON in {}: {e}", path.display()))
}

fn existing_file(rel: &str) -> bool {
    manifest_dir().join(rel).is_file()
}

fn repository_file(rel: &str) -> String {
    let path = manifest_dir().join("../..").join(rel);
    std::fs::read_to_string(&path)
        .unwrap_or_else(|e| panic!("failed to read {}: {e}", path.display()))
}

fn repository_script_code(rel: &str) -> String {
    repository_file(rel)
        .lines()
        .filter(|line| !line.trim_start().starts_with(';'))
        .collect::<Vec<_>>()
        .join("\n")
}

fn png_dimensions_and_dpi(rel: &str) -> (u32, u32, u32, u32) {
    let path = manifest_dir().join(rel);
    let data =
        std::fs::read(&path).unwrap_or_else(|e| panic!("failed to read {}: {e}", path.display()));
    assert!(data.len() > 24, "{rel} is too small to be a PNG");
    let width = u32::from_be_bytes(data[16..20].try_into().unwrap());
    let height = u32::from_be_bytes(data[20..24].try_into().unwrap());

    let mut offset = 8; // past the 8-byte PNG signature
    let mut density = None;
    while offset + 8 <= data.len() {
        let length = u32::from_be_bytes(data[offset..offset + 4].try_into().unwrap()) as usize;
        let chunk_type = &data[offset + 4..offset + 8];
        let chunk_data = offset + 8;
        if chunk_type == b"pHYs" {
            assert_eq!(length, 9, "{rel}'s pHYs chunk has an unexpected length");
            assert!(
                data.len() >= chunk_data + 9,
                "{rel}'s pHYs chunk is truncated"
            );
            let x_ppm = u32::from_be_bytes(data[chunk_data..chunk_data + 4].try_into().unwrap());
            let y_ppm =
                u32::from_be_bytes(data[chunk_data + 4..chunk_data + 8].try_into().unwrap());
            assert_eq!(data[chunk_data + 8], 1, "{rel}'s pHYs unit must be metres");
            density = Some((x_ppm, y_ppm));
            break;
        }
        if chunk_type == b"IEND" {
            break;
        }
        offset = chunk_data + length + 4; // skip the chunk's own data and CRC
    }

    let (x_ppm, y_ppm) = density.unwrap_or_else(|| panic!("{rel} has no pHYs chunk"));
    (width, height, x_ppm, y_ppm)
}

#[test]
fn nsis_defines_a_start_menu_folder() {
    let cfg = load("tauri.windows.conf.json");
    let folder = cfg["bundle"]["windows"]["nsis"]["startMenuFolder"].as_str();
    assert!(
        matches!(folder, Some(name) if !name.trim().is_empty()),
        "nsis.startMenuFolder must be a non-empty string"
    );
}

#[test]
fn nsis_installer_images_exist() {
    let cfg = load("tauri.windows.conf.json");
    let nsis = &cfg["bundle"]["windows"]["nsis"];
    let keys = [
        "installerIcon",
        "uninstallerIcon",
        "headerImage",
        "uninstallerHeaderImage",
        "sidebarImage",
    ];
    for key in keys {
        let rel = nsis[key]
            .as_str()
            .unwrap_or_else(|| panic!("nsis.{key} must be set"));
        assert!(
            existing_file(rel),
            "nsis.{key} points to a missing file: {rel}"
        );
    }
}

#[test]
fn nsis_hooks_stop_the_host_process() {
    // Regression guard for issue #131: the NSIS template only checks the main binary
    // for a running process, so a live host keeps its own image file locked and the
    // update fails. The hook file has to be wired up and name distinct Development and
    // release host binaries (kept in sync with host_binary_name_for() in src/host.rs).
    let cfg = load("tauri.windows.conf.json");
    let hooks = cfg["bundle"]["windows"]["nsis"]["installerHooks"]
        .as_str()
        .expect("nsis.installerHooks must be set");
    assert!(
        existing_file(hooks),
        "nsis.installerHooks points to a missing file: {hooks}"
    );
    let script = repository_script_code(&format!("ui/bootstrapper/{hooks}"));
    for binary in ["MacroDeckHost.exe", "MacroDeckHostDevelopment.exe"] {
        assert!(
            script.contains(binary),
            "the installer hooks must select the {binary} host binary"
        );
    }
    assert!(
        script.contains("!if \"${PRODUCTNAME}\" == \"Macro Deck\""),
        "the installer hooks must select the host name from the product identity"
    );
    // The root-cause guard for issue #131: the template !includes this file BEFORE it defines
    // PRODUCTNAME, so a selector at file top level silently compares against an undefined value
    // and always resolves the same way. The first "!macro " must precede the first "${PRODUCTNAME}"
    // so the selector only runs once the include site has expanded the macro body, by which point
    // the template's own !define is in scope.
    let first_macro = script
        .find("!macro ")
        .expect("hooks.nsh must define at least one macro");
    let first_productname = script
        .find("${PRODUCTNAME}")
        .expect("hooks.nsh must reference ${PRODUCTNAME}");
    assert!(
        first_macro < first_productname,
        "the channel selector must live inside a macro body: found ${{PRODUCTNAME}} at byte {first_productname} \
         before the first !macro at byte {first_macro}, which means it could be evaluated at file top \
         level - the exact issue #131 regression, where PRODUCTNAME was still undefined at that point"
    );
    for hook in [
        "NSIS_HOOK_PREINSTALL",
        "NSIS_HOOK_POSTINSTALL",
        "NSIS_HOOK_PREUNINSTALL",
    ] {
        assert!(
            script.contains(&format!("!macro {hook}")),
            "the installer hooks must define {hook}: the uninstall pass of an update runs \
             before the install pass, so both need to stop the host, and the firewall rule \
             (issue #345) needs a post-install hook to target the copied host binary"
        );
    }
}

#[test]
fn bundle_publisher_and_license_are_set() {
    let cfg = load("tauri.conf.json");
    assert_eq!(
        cfg["bundle"]["publisher"].as_str(),
        Some("Manuel Mayer"),
        "bundle.publisher must be \"Manuel Mayer\""
    );
    let license = cfg["bundle"]["licenseFile"]
        .as_str()
        .expect("bundle.licenseFile must be set");
    assert!(
        existing_file(license),
        "bundle.licenseFile points to a missing file: {license}"
    );
}

#[test]
fn development_tauri_config_has_an_isolated_identity() {
    let cfg = load("tauri.conf.json");
    assert_eq!(cfg["productName"].as_str(), Some("Macro Deck Development"));
    assert_eq!(cfg["mainBinaryName"].as_str(), Some("MacroDeckDevelopment"));
    assert_eq!(
        cfg["identifier"].as_str(),
        Some("app.macro-deck.macrodeck.development")
    );
    assert_eq!(
        cfg["plugins"]["updater"]["endpoints"][0].as_str(),
        Some("https://updater.macro-deck.app/releases/development-{{target}}.json")
    );
}

#[test]
fn development_windows_installer_has_a_separate_start_menu_folder() {
    let cfg = load("tauri.windows.conf.json");
    assert_eq!(
        cfg["bundle"]["windows"]["nsis"]["startMenuFolder"].as_str(),
        Some("Macro Deck Development")
    );
}

#[test]
fn bundle_metadata_for_native_packages_is_set() {
    let cfg = load("tauri.conf.json");
    for key in [
        "homepage",
        "license",
        "copyright",
        "shortDescription",
        "longDescription",
    ] {
        let value = cfg["bundle"][key]
            .as_str()
            .unwrap_or_else(|| panic!("bundle.{key} must be set"));
        assert!(!value.trim().is_empty(), "bundle.{key} must not be empty");
    }
}

#[test]
fn linux_bundle_declares_runtime_dependencies() {
    let cfg = load("tauri.linux.conf.json");
    for family in ["deb", "rpm"] {
        let depends = cfg["bundle"]["linux"][family]["depends"]
            .as_array()
            .unwrap_or_else(|| panic!("bundle.linux.{family}.depends must be an array"));
        assert!(
            !depends.is_empty(),
            "bundle.linux.{family}.depends must not be empty"
        );
        let joined = depends
            .iter()
            .map(|v| v.as_str().unwrap_or_default().to_lowercase())
            .collect::<Vec<_>>()
            .join(" ");
        for needle in ["webkit", "gtk"] {
            assert!(
                joined.contains(needle),
                "bundle.linux.{family}.depends must mention {needle}"
            );
        }
        assert!(
            joined.contains("appindicator"),
            "bundle.linux.{family}.depends must mention an app-indicator package"
        );
    }
}

#[test]
fn linux_rpm_declares_a_release() {
    let cfg = load("tauri.linux.conf.json");
    let release = cfg["bundle"]["linux"]["rpm"]["release"].as_str();
    assert!(
        matches!(release, Some(value) if !value.trim().is_empty()),
        "bundle.linux.rpm.release must be a non-empty string"
    );
}

#[test]
fn linux_deb_declares_a_section_and_priority() {
    let cfg = load("tauri.linux.conf.json");
    assert_eq!(
        cfg["bundle"]["linux"]["deb"]["section"].as_str(),
        Some("utils")
    );
    assert_eq!(
        cfg["bundle"]["linux"]["deb"]["priority"].as_str(),
        Some("optional")
    );
}

#[test]
fn linux_desktop_template_and_metainfo_exist() {
    let cfg = load("tauri.linux.conf.json");
    for family in ["deb", "rpm"] {
        let template = cfg["bundle"]["linux"][family]["desktopTemplate"]
            .as_str()
            .unwrap_or_else(|| panic!("bundle.linux.{family}.desktopTemplate must be set"));
        assert!(
            existing_file(template),
            "bundle.linux.{family}.desktopTemplate points to a missing file: {template}"
        );

        let files = cfg["bundle"]["linux"][family]["files"]
            .as_object()
            .unwrap_or_else(|| panic!("bundle.linux.{family}.files must be an object"));
        assert!(
            !files.is_empty(),
            "bundle.linux.{family}.files must not be empty"
        );
        for source in files.values() {
            let source = source
                .as_str()
                .unwrap_or_else(|| panic!("bundle.linux.{family}.files entries must be strings"));
            assert!(
                existing_file(source),
                "bundle.linux.{family}.files points to a missing file: {source}"
            );
        }
    }
}

#[test]
fn linux_metainfo_id_matches_the_release_identifier() {
    let release = load("release.conf.json");
    let metainfo =
        repository_file("ui/bootstrapper/packaging/linux/app.macro-deck.macrodeck.metainfo.xml");
    assert_eq!(
        release["identifier"].as_str(),
        Some("app.macro-deck.macrodeck"),
        "the release identifier must stay app.macro-deck.macrodeck"
    );
    assert!(
        metainfo.contains("<id>app.macro-deck.macrodeck</id>"),
        "metainfo <id> must be app.macro-deck.macrodeck"
    );
    assert!(
        metainfo.contains("<launchable type=\"desktop-id\">Macro Deck.desktop</launchable>"),
        "metainfo <launchable> must name the bundler's actual desktop file: \"Macro Deck.desktop\""
    );
}

#[test]
fn development_windows_host_has_a_separate_apphost_name() {
    let project = repository_file("host/src/MacroDeckHost/MacroDeckHost.csproj");
    assert!(project.contains("RenameDevelopmentWindowsHostApphost"));
    assert!(project.contains("MacroDeckHostDevelopment.exe"));
}

#[test]
fn macos_stages_the_host_in_resources() {
    let cfg = load("tauri.macos.conf.json");
    assert_eq!(
        cfg["bundle"]["resources"]["host-publish/"].as_str(),
        Some("host/"),
        "the macOS bundle must stage host-publish as the host/ resource"
    );
    assert!(
        cfg["bundle"]["macOS"]["files"].is_null(),
        "bundle.macOS.files copies into Contents/MacOS, where codesign rejects the host's wwwroot data files"
    );
}

/// The TCC prompt is the one piece of Macro Deck text macOS renders on its own, from the bundle rather
/// than from the app - so it is localized by shipping an `.lproj`, and it silently stays English if the
/// resource mapping or the declared localizations go missing.
#[test]
fn macos_ships_the_apple_events_prompt_in_every_declared_language() {
    let cfg = load("tauri.macos.conf.json");
    let plist = std::fs::read_to_string(
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("packaging/macos/Info.plist"),
    )
    .expect("Info.plist");
    assert!(
        plist.contains("CFBundleLocalizations"),
        "macOS only looks in an .lproj for a bundle that declares its localizations"
    );

    for culture in ["en", "de", "it", "cs", "pl", "es", "fr"] {
        let source = format!("packaging/macos/{culture}.lproj/InfoPlist.strings");
        let target = format!("{culture}.lproj/InfoPlist.strings");
        assert_eq!(
            cfg["bundle"]["resources"][&source].as_str(),
            Some(target.as_str()),
            "the {culture} usage description must land in Contents/Resources/{culture}.lproj"
        );
        assert!(
            plist.contains(&format!("<string>{culture}</string>")),
            "Info.plist must declare the {culture} localization"
        );

        let strings = std::fs::read_to_string(manifest_dir().join(&source))
            .unwrap_or_else(|e| panic!("failed to read {source}: {e}"));
        assert!(
            strings.contains("NSAppleEventsUsageDescription"),
            "the {culture} strings file must override the usage description"
        );
        assert!(
            !strings.to_ascii_lowercase().contains("spotify"),
            "the Apple Events prompt must describe general macOS system actions"
        );
    }
}

#[test]
fn macos_signs_the_app_and_host_for_apple_events_automation() {
    let cfg = load("tauri.macos.conf.json");
    assert_eq!(
        cfg["bundle"]["macOS"]["entitlements"].as_str(),
        Some("entitlements.plist"),
        "the macOS app must use the shared signing entitlements"
    );

    let entitlements = std::fs::read_to_string(manifest_dir().join("entitlements.plist"))
        .expect("entitlements.plist");
    assert!(
        entitlements.contains("<key>com.apple.security.automation.apple-events</key>\n\t\t<true/>"),
        "the hardened app must be allowed to request Apple Events Automation permission"
    );

    let workflow = repository_file(".github/workflows/build.yml");
    assert!(
        workflow.contains(
            "sign-macos-host.sh ui/bootstrapper/host-publish ui/bootstrapper/entitlements.plist"
        ),
        "the embedded host must be signed with the same Apple Events entitlement"
    );
}

#[test]
fn macos_ships_the_adaptive_app_icon_and_an_icns_fallback() {
    let cfg = load("tauri.macos.conf.json");
    assert_eq!(
        cfg["bundle"]["resources"]["packaging/macos/Assets.car"].as_str(),
        Some("Assets.car"),
        "the compiled icon catalog must be staged as Contents/Resources/Assets.car"
    );
    assert!(
        existing_file("packaging/macos/Assets.car"),
        "packaging/macos/Assets.car is missing - regenerate it with ci/scripts/generate-brand-assets.sh"
    );
    assert!(
        existing_file("packaging/macos/Macro Deck.icon/icon.json"),
        "the Icon Composer source the catalog is compiled from must stay checked in"
    );

    let info_plist = cfg["bundle"]["macOS"]["infoPlist"]
        .as_str()
        .expect("bundle.macOS.infoPlist must point at the Info.plist carrying CFBundleIconName");
    let plist = std::fs::read_to_string(manifest_dir().join(info_plist))
        .unwrap_or_else(|e| panic!("failed to read {info_plist}: {e}"));
    assert!(
        plist.contains("<key>CFBundleIconName</key>")
            && plist.contains("<string>Macro Deck</string>"),
        "{info_plist} must set CFBundleIconName to the icon's name inside Assets.car"
    );

    let base = load("tauri.conf.json");
    let icons = base["bundle"]["icon"]
        .as_array()
        .expect("bundle.icon must be an array")
        .iter()
        .filter_map(Value::as_str)
        .collect::<Vec<_>>();
    assert!(
        icons.contains(&"icons/icon.icns") && existing_file("icons/icon.icns"),
        "bundle.icon must keep icons/icon.icns, which is what CFBundleIconFile ends up pointing at"
    );
}

#[test]
fn host_publish_is_single_file() {
    // Regression guard for issue #316: notarization requires a hardened-runtime signature
    // on every Mach-O in the bundle, so the publish output must be a single launcher that
    // the CI pre-sign step covers in one pass instead of hundreds of loose runtime dylibs.
    // Trimming stays off because integration discovery reflects over the host assemblies.
    let project = repository_file("host/src/MacroDeckHost/MacroDeckHost.csproj");
    assert!(
        project.contains("Condition=\"'$(RuntimeIdentifier)' != ''\""),
        "the single-file properties must be scoped to a RuntimeIdentifier condition"
    );
    for property in [
        "<PublishSingleFile>true</PublishSingleFile>",
        "<SelfContained>true</SelfContained>",
        "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>",
        "<PublishTrimmed>false</PublishTrimmed>",
    ] {
        assert!(
            project.contains(property),
            "MacroDeckHost.csproj must set {property}"
        );
    }
}

#[test]
fn hooks_include_host_lock_with_filedir() {
    let script = repository_file("ui/bootstrapper/installer/hooks.nsh");
    assert!(
        script.contains("!include \"${__FILEDIR__}\\host-lock.nsh\""),
        "hooks.nsh must !include host-lock.nsh via ${{__FILEDIR__}}, not a bare relative path"
    );
    assert!(
        existing_file("installer/host-lock.nsh"),
        "ui/bootstrapper/installer/host-lock.nsh must exist"
    );
}

#[test]
fn hooks_include_firewall_with_filedir() {
    // Same failure mode as hooks_include_host_lock_with_filedir, for the firewall-rule macros
    // added for issue #345.
    let script = repository_file("ui/bootstrapper/installer/hooks.nsh");
    assert!(
        script.contains("!include \"${__FILEDIR__}\\firewall.nsh\""),
        "hooks.nsh must !include firewall.nsh via ${{__FILEDIR__}}, not a bare relative path"
    );
    assert!(
        existing_file("installer/firewall.nsh"),
        "ui/bootstrapper/installer/firewall.nsh must exist"
    );
}

#[test]
fn host_lock_is_independent_of_the_template() {
    let code = repository_script_code("ui/bootstrapper/installer/host-lock.nsh");
    for symbol in [
        "PRODUCTNAME",
        "MAINBINARYNAME",
        "INSTALLMODE",
        "PassiveMode",
        "CheckIfAppIsRunning",
        "MessageBox",
        "Abort",
    ] {
        assert!(
            !code.contains(symbol),
            "host-lock.nsh must not reference {symbol} in actual script code: that would make it \
             depend on the Tauri template and break installer/tests/host-lock-harness.nsi, which \
             compiles it alone"
        );
    }
}

#[test]
fn host_lock_verifies_the_probe_instead_of_sleeping() {
    // A regression back to a fixed sleep (the original issue #131 bug) would still compile and
    // still pass a casual read, so pin the actual verification calls instead: a real directory
    // walk plus a non-destructive write probe, never one that would truncate the very files
    // being checked for a lock.
    let script = repository_file("ui/bootstrapper/installer/host-lock.nsh");
    for needle in ["FindFirst", "FindClose", "FindProcess"] {
        assert!(
            script.contains(needle),
            "host-lock.nsh must call {needle} to verify the process/lock state itself"
        );
    }
    assert!(
        script.contains("FileOpen $2 \"${Directory}\\$1\" a"),
        "host-lock.nsh must probe each file by opening it for append (read/write, no truncation)"
    );
    assert!(
        !script.contains("FileOpen $2 \"${Directory}\\$1\" w"),
        "host-lock.nsh must never open a probed file for write: \"w\" truncates it, destroying \
         the very file the installer is about to replace"
    );
}

#[test]
fn firewall_is_independent_of_the_template() {
    let code = repository_script_code("ui/bootstrapper/installer/firewall.nsh");
    for symbol in [
        "PRODUCTNAME",
        "MAINBINARYNAME",
        "INSTALLMODE",
        "PassiveMode",
        "CheckIfAppIsRunning",
        "MessageBox",
        "Abort",
    ] {
        assert!(
            !code.contains(symbol),
            "firewall.nsh must not reference {symbol} in actual script code: that would make it \
             depend on the Tauri template and break installer/tests/firewall-harness.nsi, which \
             compiles it alone"
        );
    }
}

#[test]
fn firewall_rule_is_program_scoped_all_profile_tcp() {
    // Pins the shape of the rule itself (issue #345's acceptance criteria), independent of the
    // exact command-building code in firewall.nsh.
    let script = repository_script_code("ui/bootstrapper/installer/firewall.nsh");
    for needle in [
        "advfirewall firewall add rule",
        "dir=in",
        "action=allow",
        "enable=yes",
        "profile=any",
        "protocol=TCP",
    ] {
        assert!(
            script.contains(needle),
            "firewall.nsh must build a rule containing {needle}"
        );
    }
    assert!(
        !script.contains("localport"),
        "firewall.nsh must never pin localport: the public port is overridable through \
         MACRO_DECK_PORT, so a port-pinned rule would silently stop matching after an override"
    );
}

#[test]
fn firewall_deletes_before_it_adds() {
    let script = repository_script_code("ui/bootstrapper/installer/firewall.nsh");
    let delete_at = script
        .find("firewall delete rule")
        .expect("firewall.nsh must build a delete-rule clause");
    let add_at = script
        .find("firewall add rule")
        .expect("firewall.nsh must build an add-rule clause");
    assert!(
        delete_at < add_at,
        "the delete clause must be built before the add clause, or an upgrade stacks duplicate \
         firewall rules instead of replacing the existing one"
    );
}

#[test]
fn firewall_ships_with_the_runas_verb() {
    let script = repository_file("ui/bootstrapper/installer/firewall.nsh");
    assert!(
        script.contains("!define MACRODECK_FIREWALL_VERB \"runas\""),
        "firewall.nsh must default MACRODECK_FIREWALL_VERB to \"runas\" - the harness-only \
         \"open\" override must never ship"
    );
}

#[test]
fn hooks_host_timeout_budget_is_generous_and_polls_often_enough() {
    let script = repository_file("ui/bootstrapper/installer/hooks.nsh");
    let timeout = parse_define_u64(&script, "MACRODECK_HOST_TIMEOUT_MS");
    let poll = parse_define_u64(&script, "MACRODECK_HOST_POLL_MS");
    assert!(
        timeout >= 10_000,
        "MACRODECK_HOST_TIMEOUT_MS must be at least 10s, got {timeout}ms"
    );
    assert!(
        poll > 0 && poll <= timeout / 10,
        "MACRODECK_HOST_POLL_MS ({poll}ms) must be > 0 and no more than a tenth of the timeout \
         ({timeout}ms), or the host gets barely a handful of chances to be noticed as stopped"
    );
}

fn parse_define_u64(script: &str, name: &str) -> u64 {
    let needle = format!("!define {name} ");
    let start = script
        .find(&needle)
        .unwrap_or_else(|| panic!("hooks.nsh must define {name}"))
        + needle.len();
    let rest = &script[start..];
    let end = rest
        .find(|c: char| !c.is_ascii_digit())
        .unwrap_or(rest.len());
    rest[..end]
        .parse()
        .unwrap_or_else(|e| panic!("{name} is not a plain integer: {e}"))
}

#[test]
fn hooks_never_offer_ignore_for_a_locked_host_file() {
    // Ignore is exactly how issue #131 produced a half-updated installation: NSIS copies
    // whatever it can and silently skips the rest. Users must only ever see Retry/Cancel.
    let script = repository_file("ui/bootstrapper/installer/hooks.nsh");
    assert!(
        script.contains("AllowSkipFiles off"),
        "hooks.nsh must set AllowSkipFiles off"
    );
    assert!(
        script.contains("MB_RETRYCANCEL"),
        "hooks.nsh must offer Retry/Cancel, not Abort/Retry/Ignore"
    );
    for forbidden in ["MB_ABORTRETRYIGNORE", "IDIGNORE"] {
        assert!(
            !script.contains(forbidden),
            "hooks.nsh must not contain {forbidden}: Ignore must never be offered for a locked host file"
        );
    }
}

#[test]
fn hooks_handle_unattended_runs() {
    let script = repository_file("ui/bootstrapper/installer/hooks.nsh");
    for needle in ["${Silent}", "$PassiveMode", "$UpdateMode", "AttachConsole"] {
        assert!(
            script.contains(needle),
            "hooks.nsh must handle unattended installs: missing {needle}"
        );
    }
}

#[test]
fn installer_test_harnesses_and_ci_job_exist() {
    for rel in [
        "installer/tests/hooks-harness.nsi",
        "installer/tests/host-lock-harness.nsi",
        "installer/tests/mini-upgrade.nsi",
        "installer/tests/firewall-harness.nsi",
        "installer/tests/Invoke-InstallerTests.ps1",
        "installer/tests/Get-NsisToolset.ps1",
    ] {
        assert!(existing_file(rel), "missing installer test file: {rel}");
    }
    let workflow = repository_file(".github/workflows/ci.yml");
    assert!(
        workflow.contains("Invoke-InstallerTests.ps1"),
        "ci.yml must run Invoke-InstallerTests.ps1, or the installer-hooks job can be dropped silently"
    );
}

const FILE_ASSOCIATIONS: [(&str, &str); 5] = [
    ("macroDeckProfile", "application/vnd.macro-deck.profile"),
    ("macroDeckFolder", "application/vnd.macro-deck.folder"),
    ("macroDeckWidget", "application/vnd.macro-deck.widget"),
    ("macroDeckIconPack", "application/vnd.macro-deck.icon-pack"),
    ("macroDeckPlugin", "application/vnd.macro-deck.plugin"),
];

#[test]
fn release_config_declares_every_file_association() {
    let associations = load("release.conf.json")["bundle"]["fileAssociations"]
        .as_array()
        .expect("bundle.fileAssociations must be an array")
        .clone();

    for (extension, mime_type) in FILE_ASSOCIATIONS {
        let association = associations
            .iter()
            .find(|entry| entry["ext"][0].as_str() == Some(extension))
            .unwrap_or_else(|| panic!("no file association declares .{extension}"));

        assert_eq!(
            association["mimeType"].as_str(),
            Some(mime_type),
            ".{extension} must map to {mime_type} - the Linux .desktop MimeType= comes from here"
        );
        assert!(
            association["exportedType"]["identifier"].is_string(),
            ".{extension} needs an exportedType.identifier or macOS ignores it"
        );
        let name = association["name"]
            .as_str()
            .unwrap_or_else(|| panic!(".{extension} must declare a name"));
        assert!(
            !name.contains(' '),
            "association name {name:?} becomes a Windows ProgId and must not contain spaces"
        );
    }
}

#[test]
fn development_configs_declare_no_file_associations() {
    for config in [
        "tauri.conf.json",
        "tauri.linux.conf.json",
        "tauri.macos.conf.json",
        "tauri.windows.conf.json",
    ] {
        assert!(
            load(config)["bundle"]["fileAssociations"].is_null(),
            "{config} must not declare fileAssociations - they belong to release.conf.json only"
        );
    }
}

#[test]
fn linux_packages_install_the_mime_package() {
    let cfg = load("tauri.linux.conf.json");
    for packaging in ["deb", "rpm"] {
        let target = cfg["bundle"]["linux"][packaging]["files"]
            ["/usr/share/mime/packages/app.macro-deck.macrodeck.mime.xml"]
            .as_str()
            .unwrap_or_else(|| panic!("{packaging} must install the MIME package"));
        assert!(
            existing_file(target),
            "{packaging} installs {target}, which does not exist"
        );
    }
}

#[test]
fn the_mime_package_covers_every_associated_extension() {
    let mime = repository_file("ui/bootstrapper/packaging/linux/app.macro-deck.macrodeck.mime.xml");

    for (extension, mime_type) in FILE_ASSOCIATIONS {
        assert!(
            mime.contains(&format!("<mime-type type=\"{mime_type}\">")),
            "the MIME package is missing {mime_type}"
        );
        assert!(
            mime.contains(&format!("<glob pattern=\"*.{extension}\"/>")),
            "the MIME package is missing a glob for .{extension}"
        );
        assert!(
            mime.contains(&format!(
                "<glob pattern=\"*.{}\"/>",
                extension.to_ascii_lowercase()
            )),
            "the MIME package is missing the lowercase glob for .{extension}"
        );
    }
}

#[test]
fn linux_metainfo_provides_every_associated_media_type() {
    let metainfo =
        repository_file("ui/bootstrapper/packaging/linux/app.macro-deck.macrodeck.metainfo.xml");

    for (_, mime_type) in FILE_ASSOCIATIONS {
        assert!(
            metainfo.contains(&format!("<mediatype>{mime_type}</mediatype>")),
            "metainfo must declare <mediatype>{mime_type}</mediatype>"
        );
    }
}

#[test]
fn linux_packages_depend_on_the_mime_database_tools() {
    let cfg = load("tauri.linux.conf.json");
    for packaging in ["deb", "rpm"] {
        let depends = cfg["bundle"]["linux"][packaging]["depends"]
            .as_array()
            .unwrap_or_else(|| panic!("{packaging}.depends must be an array"))
            .iter()
            .filter_map(|value| value.as_str())
            .collect::<Vec<_>>()
            .join(" ");
        for tool in ["shared-mime-info", "desktop-file-utils"] {
            assert!(
                depends.contains(tool),
                "{packaging} must depend on {tool} so the databases are rebuilt on install"
            );
        }
    }
}

#[test]
fn every_release_build_applies_the_release_config() {
    let workflow = repository_file(".github/workflows/build.yml");
    let bundle_steps = workflow
        .lines()
        .filter(|line| {
            line.contains("npm run tauri build") || line.contains("npm run tauri bundle")
        })
        .collect::<Vec<_>>();

    assert_eq!(
        bundle_steps.len(),
        4,
        "expected one Tauri build or bundle step per bundle pass"
    );
    for step in bundle_steps {
        assert!(
            step.contains("--config release.conf.json"),
            "this bundle step does not apply release.conf.json: {step}"
        );
    }
}

#[test]
fn the_desktop_entry_passes_the_opened_files_to_the_app() {
    let template = repository_file("ui/bootstrapper/packaging/linux/main.desktop");
    let exec = template
        .lines()
        .find(|line| line.starts_with("Exec="))
        .expect("the desktop template must define Exec");

    assert!(
        exec.contains("%F") || exec.contains("%U"),
        "Exec needs a field code or the opened file never reaches the app: {exec}"
    );
    let wm_class = template
        .lines()
        .find(|line| line.starts_with("StartupWMClass="))
        .expect("the desktop template must define StartupWMClass");
    assert!(
        !wm_class.contains('%'),
        "StartupWMClass must stay the plain binary name: {wm_class}"
    );
}

// --- macOS DMG styling and install flow (issue #355) ---

#[test]
fn macos_dmg_layout_is_configured() {
    let cfg = load("tauri.macos.conf.json");
    let dmg = &cfg["bundle"]["macOS"]["dmg"];
    assert!(
        dmg["background"]
            .as_str()
            .is_some_and(|value| !value.trim().is_empty()),
        "bundle.macOS.dmg.background must be a non-empty string"
    );
    for (section, keys) in [
        ("windowSize", ["width", "height"]),
        ("appPosition", ["x", "y"]),
        ("applicationFolderPosition", ["x", "y"]),
        ("windowPosition", ["x", "y"]),
    ] {
        for key in keys {
            assert!(
                dmg[section][key].is_number(),
                "bundle.macOS.dmg.{section}.{key} must be numeric"
            );
        }
    }

    for config in ["release.conf.json", "tauri.conf.json"] {
        assert!(
            load(config)["bundle"]["macOS"]["dmg"].is_null(),
            "{config} must not declare bundle.macOS.dmg"
        );
    }
}

#[test]
fn macos_dmg_background_exists() {
    let cfg = load("tauri.macos.conf.json");
    let background = cfg["bundle"]["macOS"]["dmg"]["background"]
        .as_str()
        .expect("bundle.macOS.dmg.background must be set");
    assert!(
        existing_file(background),
        "bundle.macOS.dmg.background points to a missing file: {background}"
    );
}

#[test]
fn macos_dmg_icons_are_laid_out_for_a_left_to_right_drag() {
    let cfg = load("tauri.macos.conf.json");
    let dmg = &cfg["bundle"]["macOS"]["dmg"];
    let window_width = dmg["windowSize"]["width"].as_f64().unwrap();
    let window_height = dmg["windowSize"]["height"].as_f64().unwrap();
    let app_x = dmg["appPosition"]["x"].as_f64().unwrap();
    let app_y = dmg["appPosition"]["y"].as_f64().unwrap();
    let applications_x = dmg["applicationFolderPosition"]["x"].as_f64().unwrap();
    let applications_y = dmg["applicationFolderPosition"]["y"].as_f64().unwrap();

    assert_eq!(app_y, applications_y, "both icons must sit on the same row");
    assert!(
        app_x < applications_x,
        "the app icon must sit left of the Applications icon for a left-to-right drag"
    );

    const MIN_CENTRE_DISTANCE_FROM_EDGE: f64 = 128.0;
    for (label, x, y) in [
        ("app", app_x, app_y),
        ("Applications", applications_x, applications_y),
    ] {
        assert!(
            x >= MIN_CENTRE_DISTANCE_FROM_EDGE && x <= window_width - MIN_CENTRE_DISTANCE_FROM_EDGE,
            "{label} icon x={x} is not >= 64pt clear of both left/right edges of a {window_width}pt window"
        );
        assert!(
            y >= MIN_CENTRE_DISTANCE_FROM_EDGE && y <= window_height - MIN_CENTRE_DISTANCE_FROM_EDGE,
            "{label} icon y={y} is not >= 64pt clear of both top/bottom edges of a {window_height}pt window"
        );
    }
}

#[test]
fn macos_dmg_window_leaves_room_below_the_icons_for_finder_chrome() {
    let cfg = load("tauri.macos.conf.json");
    let dmg = &cfg["bundle"]["macOS"]["dmg"];
    let window_height = dmg["windowSize"]["height"].as_f64().unwrap();
    let icon_y = dmg["appPosition"]["y"].as_f64().unwrap();

    // Finder draws its title bar and status bar over the top and bottom of the
    // background instead of beside it, so the window has to be taller than the
    // artwork or it clips the icons and their labels (issue #594).
    const ICON_HALF_HEIGHT: f64 = 64.0;
    const LABEL_HEIGHT: f64 = 24.0;
    const FINDER_CHROME_HEIGHT: f64 = 100.0;

    let below_labels = window_height - (icon_y + ICON_HALF_HEIGHT + LABEL_HEIGHT);
    assert!(
        below_labels >= FINDER_CHROME_HEIGHT,
        "only {below_labels}pt is left below the icon labels in a {window_height}pt window; \
         Finder's chrome needs at least {FINDER_CHROME_HEIGHT}pt or it cuts the layout off"
    );
}

#[test]
fn macos_dmg_background_is_a_retina_png_matching_the_window() {
    let cfg = load("tauri.macos.conf.json");
    let dmg = &cfg["bundle"]["macOS"]["dmg"];
    let background = dmg["background"]
        .as_str()
        .expect("dmg.background must be set");
    let window_width = dmg["windowSize"]["width"]
        .as_u64()
        .expect("dmg.windowSize.width must be set");
    let window_height = dmg["windowSize"]["height"]
        .as_u64()
        .expect("dmg.windowSize.height must be set");

    let (width, height, x_ppm, y_ppm) = png_dimensions_and_dpi(background);
    assert_eq!(
        u64::from(width),
        window_width * 2,
        "the background must be rendered at 2x the window width for a crisp Retina DMG"
    );
    assert_eq!(
        u64::from(height),
        window_height * 2,
        "the background must be rendered at 2x the window height for a crisp Retina DMG"
    );
    for ppm in [x_ppm, y_ppm] {
        assert!(
            (5668..=5670).contains(&ppm),
            "pHYs density {ppm} ppm is not ~144 DPI"
        );
    }
}

#[test]
fn macos_dmg_has_no_license_prompt() {
    let cfg = load("tauri.macos.conf.json");
    let bundle = cfg["bundle"].as_object().expect("bundle must be an object");
    assert!(
        bundle.contains_key("licenseFile"),
        "bundle.licenseFile must be present (set to null) to remove the DMG EULA sheet"
    );
    assert_eq!(
        bundle["licenseFile"],
        Value::Null,
        "bundle.licenseFile must be null on macOS - Apache-2.0 needs no acceptance"
    );

    let base = load("tauri.conf.json");
    let windows_license = base["bundle"]["licenseFile"]
        .as_str()
        .expect("the base bundle.licenseFile must stay a non-empty string for the Windows NSIS license page");
    assert!(!windows_license.trim().is_empty());
}

#[test]
fn release_dmg_styling_survives_ci() {
    let workflow = repository_file(".github/workflows/build.yml");
    assert!(
        workflow.contains("TAURI_BUNDLER_DMG_IGNORE_CI: \"true\""),
        "build.yml's DMG build step must set TAURI_BUNDLER_DMG_IGNORE_CI to the literal \"true\" - \
         the bundler compares against exactly that string, so any other value silently skips the styling"
    );
}
