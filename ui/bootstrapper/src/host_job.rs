// Wraps the host child in a Windows Job Object so a forced stop reaches the plugins it spawned,
// not just the host; kill-on-close also means a crashed bootstrapper takes the whole tree with it.

use std::os::windows::io::AsRawHandle;
use std::process::Child;

use windows::core::PCWSTR;
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::System::JobObjects::{
    AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
    SetInformationJobObject, TerminateJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
};

use crate::logging;

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
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

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
