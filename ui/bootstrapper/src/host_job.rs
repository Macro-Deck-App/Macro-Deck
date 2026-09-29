// Kill-on-close ends the host on a forced stop or a crashed bootstrapper. What the host starts breaks away,
// so launched apps outlive Macro Deck; plugins end through the host's own per-plugin jobs instead.

use std::os::windows::io::AsRawHandle;
use std::process::Child;

use windows::core::PCWSTR;
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::System::JobObjects::{
    AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
    SetInformationJobObject, TerminateJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK,
};
use windows::Win32::System::Threading::SetProcessShutdownParameters;

use crate::logging;

// Above the host's default level 0x280: Windows ends higher levels first, so the host outlives
// the bootstrapper's session-end stop instead of being ended alongside it.
const SHUTDOWN_LEVEL_BEFORE_HOST: u32 = 0x2FF;

pub fn shut_down_before_host() {
    if let Err(error) = unsafe { SetProcessShutdownParameters(SHUTDOWN_LEVEL_BEFORE_HOST, 0) } {
        logging::warn(&format!(
            "[host] could not raise the shutdown level above the host: {error}"
        ));
    }
}

pub struct HostJob(HANDLE);

unsafe impl Send for HostJob {}
unsafe impl Sync for HostJob {}

impl HostJob {
    pub fn create_and_assign(child: &Child) -> Option<Self> {
        let job = match unsafe { CreateJobObjectW(None, PCWSTR::null()) } {
            Ok(job) => job,
            Err(error) => {
                logging::warn(&format!("[host] could not create the job object: {error}"));
                return None;
            }
        };

        let mut info = JOBOBJECT_EXTENDED_LIMIT_INFORMATION::default();
        info.BasicLimitInformation.LimitFlags =
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK;

        let configured = unsafe {
            SetInformationJobObject(
                job,
                JobObjectExtendedLimitInformation,
                &info as *const _ as *const std::ffi::c_void,
                std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            )
        };
        if let Err(error) = configured {
            logging::warn(&format!(
                "[host] could not configure the job object: {error}"
            ));
            unsafe {
                let _ = CloseHandle(job);
            }
            return None;
        }

        let process = HANDLE(child.as_raw_handle());
        if let Err(error) = unsafe { AssignProcessToJobObject(job, process) } {
            logging::warn(&format!(
                "[host] could not assign the host process to the job object: {error}"
            ));
            unsafe {
                let _ = CloseHandle(job);
            }
            return None;
        }

        Some(HostJob(job))
    }

    pub fn terminate(&self) {
        if let Err(error) = unsafe { TerminateJobObject(self.0, 1) } {
            logging::warn(&format!("[host] TerminateJobObject failed: {error}"));
        }
    }
}

impl Drop for HostJob {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::HostJob;
    use std::io::{BufRead, BufReader, Write};
    use std::process::{Command, Stdio};
    use std::sync::mpsc;
    use std::time::{Duration, Instant};
    use windows::core::BOOL;
    use windows::Win32::Foundation::{CloseHandle, HANDLE, WAIT_TIMEOUT};
    use windows::Win32::System::JobObjects::IsProcessInJob;
    use windows::Win32::System::Threading::{
        OpenProcess, TerminateProcess, WaitForSingleObject, PROCESS_QUERY_LIMITED_INFORMATION,
        PROCESS_SYNCHRONIZE, PROCESS_TERMINATE,
    };

    const LAUNCH_APP: &str = "[Console]::In.ReadLine() | Out-Null; \
        (Start-Process ping.exe -ArgumentList '-n','120','127.0.0.1' -WindowStyle Hidden -PassThru).Id; \
        Start-Sleep -Seconds 120";

    struct LaunchedApp(HANDLE);

    impl Drop for LaunchedApp {
        fn drop(&mut self) {
            unsafe {
                let _ = TerminateProcess(self.0, 1);
                let _ = CloseHandle(self.0);
            }
        }
    }

    #[test]
    fn an_app_the_host_launched_outlives_the_host_job() {
        let mut host = Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-Command", LAUNCH_APP])
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .expect("start the stand-in host");
        let job = HostJob::create_and_assign(&host).expect("assign the stand-in host to a job");

        let stdout = host.stdout.take().expect("stand-in host stdout");
        let (sender, receiver) = mpsc::channel();
        std::thread::spawn(move || {
            let mut line = String::new();
            let _ = BufReader::new(stdout).read_line(&mut line);
            let _ = sender.send(line);
        });
        host.stdin
            .take()
            .expect("stand-in host stdin")
            .write_all(b"\n")
            .expect("tell the stand-in host to launch the app");
        let app_pid: u32 = receiver
            .recv_timeout(Duration::from_secs(60))
            .expect("the stand-in host reports the launched app")
            .trim()
            .parse()
            .expect("a process id");

        let app = LaunchedApp(
            unsafe {
                OpenProcess(
                    PROCESS_SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE,
                    false,
                    app_pid,
                )
            }
            .expect("open the launched app"),
        );
        let mut in_job = BOOL(1);
        unsafe { IsProcessInJob(app.0, Some(job.0), &mut in_job) }.expect("query job membership");
        assert!(!in_job.as_bool(), "the launched app joined the host job");

        drop(job);

        let deadline = Instant::now() + Duration::from_secs(30);
        while host.try_wait().expect("poll the stand-in host").is_none() {
            assert!(
                Instant::now() < deadline,
                "closing the job did not end the host"
            );
            std::thread::sleep(Duration::from_millis(50));
        }
        assert_eq!(
            unsafe { WaitForSingleObject(app.0, 5_000) },
            WAIT_TIMEOUT,
            "closing the host job ended the launched app"
        );
    }
}
