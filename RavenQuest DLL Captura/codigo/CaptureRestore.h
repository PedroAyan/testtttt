#ifndef CAPTURE_RESTORE_H
#define CAPTURE_RESTORE_H

#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#include <windows.h>

#ifdef CAPTURE_RESTORE_BUILD
#define CAPTURE_RESTORE_API __declspec(dllexport)
#else
#define CAPTURE_RESTORE_API __declspec(dllimport)
#endif

enum CaptureRestoreStatus {
    CAPTURE_RESTORE_OK = 0,
    CAPTURE_RESTORE_INVALID_ARGUMENT = 1,
    CAPTURE_RESTORE_INVALID_WINDOW = 2,
    CAPTURE_RESTORE_FOREIGN_PROCESS = 3,
    CAPTURE_RESTORE_NOT_TOP_LEVEL = 4,
    CAPTURE_RESTORE_SET_FAILED = 5,
    CAPTURE_RESTORE_QUERY_FAILED = 6,
    CAPTURE_RESTORE_STILL_PROTECTED = 7
};

/* Caller initializes size = sizeof(CaptureRestoreResult), version = 1.
   All fields are fixed-width DWORDs; ABI size is 48 bytes on x64. */
typedef struct CaptureRestoreResult {
    DWORD size;
    DWORD version;
    DWORD status;
    DWORD caller_pid;
    DWORD owner_pid;
    DWORD before_affinity;
    DWORD after_affinity;
    DWORD before_read_ok;
    DWORD set_ok;
    DWORD after_read_ok;
    DWORD win32_error;
    DWORD reserved;
} CaptureRestoreResult;

#ifdef __cplusplus
extern "C" {
#endif
CAPTURE_RESTORE_API DWORD WINAPI RestoreCaptureForOwnedWindow(
    HWND window, CaptureRestoreResult *result);
#ifdef __cplusplus
}
#endif

#endif
