#define CAPTURE_RESTORE_BUILD
#include "CaptureRestore.h"

static DWORD fail(CaptureRestoreResult *result, DWORD status, DWORD error) {
    result->status = status;
    result->win32_error = error;
    return status;
}

DWORD WINAPI RestoreCaptureForOwnedWindow(HWND window,
                                          CaptureRestoreResult *result) {
    DWORD thread_id;
    DWORD last_error;

    if (result == NULL || result->size != sizeof(*result) || result->version != 1) {
        return CAPTURE_RESTORE_INVALID_ARGUMENT;
    }

    result->status = CAPTURE_RESTORE_INVALID_ARGUMENT;
    result->caller_pid = GetCurrentProcessId();
    result->owner_pid = 0;
    result->before_affinity = 0xffffffffUL;
    result->after_affinity = 0xffffffffUL;
    result->before_read_ok = 0;
    result->set_ok = 0;
    result->after_read_ok = 0;
    result->win32_error = 0;
    result->reserved = 0;

    if (window == NULL || !IsWindow(window)) {
        return fail(result, CAPTURE_RESTORE_INVALID_WINDOW, ERROR_INVALID_WINDOW_HANDLE);
    }

    SetLastError(ERROR_SUCCESS);
    thread_id = GetWindowThreadProcessId(window, &result->owner_pid);
    last_error = GetLastError();
    if (thread_id == 0) {
        return fail(result, CAPTURE_RESTORE_INVALID_WINDOW,
                    last_error != 0 ? last_error : ERROR_INVALID_WINDOW_HANDLE);
    }
    if (result->owner_pid != result->caller_pid) {
        return fail(result, CAPTURE_RESTORE_FOREIGN_PROCESS, ERROR_ACCESS_DENIED);
    }
    if (GetAncestor(window, GA_ROOT) != window ||
        (GetWindowLongPtrW(window, GWL_STYLE) & WS_CHILD) != 0) {
        return fail(result, CAPTURE_RESTORE_NOT_TOP_LEVEL, ERROR_INVALID_PARAMETER);
    }

    SetLastError(ERROR_SUCCESS);
    result->before_read_ok = GetWindowDisplayAffinity(window, &result->before_affinity) ? 1 : 0;
    /* The pre-query is diagnostic. The setter and verified post-query decide success. */
    SetLastError(ERROR_SUCCESS);
    result->set_ok = SetWindowDisplayAffinity(window, WDA_NONE) ? 1 : 0;
    last_error = GetLastError();
    if (!result->set_ok) {
        return fail(result, CAPTURE_RESTORE_SET_FAILED, last_error);
    }

    SetLastError(ERROR_SUCCESS);
    result->after_read_ok = GetWindowDisplayAffinity(window, &result->after_affinity) ? 1 : 0;
    last_error = GetLastError();
    if (!result->after_read_ok) {
        return fail(result, CAPTURE_RESTORE_QUERY_FAILED, last_error);
    }
    if (result->after_affinity != WDA_NONE) {
        return fail(result, CAPTURE_RESTORE_STILL_PROTECTED, ERROR_INVALID_DATA);
    }

    result->status = CAPTURE_RESTORE_OK;
    return CAPTURE_RESTORE_OK;
}

/* Loading/unloading alone has no effect. User32 calls occur only in the export. */
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID reserved) {
    (void)module;
    (void)reason;
    (void)reserved;
    return TRUE;
}
