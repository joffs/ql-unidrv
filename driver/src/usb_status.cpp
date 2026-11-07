#include "usb_status.h"

#include <windows.h>

namespace ql {
namespace {

const wchar_t kUsbPrintClass[] =
    L"SYSTEM\\CurrentControlSet\\Control\\DeviceClasses\\{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";

// One overlapped read or write with a timeout: the byte count, or -1 (error / timeout).
int Transfer(HANDLE h, void* data, DWORD size, bool write, DWORD timeoutMs) {
    OVERLAPPED ov = {};
    ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!ov.hEvent) return -1;
    DWORD n = 0;
    BOOL ok = write ? WriteFile(h, data, size, &n, &ov) : ReadFile(h, data, size, &n, &ov);
    int result = static_cast<int>(n);
    if (!ok) {
        result = -1;
        if (GetLastError() == ERROR_IO_PENDING) {
            if (WaitForSingleObject(ov.hEvent, timeoutMs) == WAIT_OBJECT_0) {
                if (GetOverlappedResult(h, &ov, &n, FALSE)) result = static_cast<int>(n);
            } else {
                CancelIoEx(h, &ov);
                GetOverlappedResult(h, &ov, &n, TRUE);
            }
        }
    }
    CloseHandle(ov.hEvent);
    return result;
}

bool ReadMessage(HANDLE h, StatusBytes& s, DWORD timeoutMs) {
    return Transfer(h, s.data(), static_cast<DWORD>(s.size()), false, timeoutMs) == 32 && s[0] == 0x80 && s[1] == 0x20;
}

}  // namespace

std::wstring UsbInterfaceOfPort(const wchar_t* port) {
    if (!port || _wcsnicmp(port, L"USB", 3) != 0) return L"";
    std::wstring want(port);
    while (!want.empty() && want.back() == L':') want.pop_back();
    HKEY cls;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, kUsbPrintClass, 0, KEY_READ, &cls) != ERROR_SUCCESS) return L"";
    std::wstring result;
    wchar_t name[512];
    for (DWORD i = 0;; ++i) {
        DWORD len = ARRAYSIZE(name);
        if (RegEnumKeyExW(cls, i, name, &len, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
        const std::wstring key(name);
        DWORD number = 0, size = sizeof(number);
        wchar_t base[64] = {};
        DWORD baseSize = sizeof(base) - sizeof(wchar_t);
        if (RegGetValueW(cls, (key + L"\\#\\Device Parameters").c_str(), L"Port Number", RRF_RT_REG_DWORD, nullptr,
                         &number, &size) != ERROR_SUCCESS ||
            RegGetValueW(cls, (key + L"\\#\\Device Parameters").c_str(), L"Base Name", RRF_RT_REG_SZ, nullptr, base,
                         &baseSize) != ERROR_SUCCESS)
            continue;
        wchar_t portName[80];
        wsprintfW(portName, L"%s%03lu", base, number);
        if (lstrcmpiW(portName, want.c_str()) != 0) continue;
        wchar_t link[512] = {};
        DWORD linkSize = sizeof(link) - sizeof(wchar_t);
        if (RegGetValueW(cls, (key + L"\\#").c_str(), L"SymbolicLink", RRF_RT_REG_SZ, nullptr, link, &linkSize) ==
            ERROR_SUCCESS)
            result = link;
        else if (key.size() > 4)
            result = L"\\\\?\\" + key.substr(4);
        break;
    }
    RegCloseKey(cls);
    return result;
}

bool QueryUsbStatus(const std::wstring& path, std::vector<StatusBytes>& messages, StatusBytes& reply, int timeoutMs) {
    if (path.empty()) return false;
    HANDLE h = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                           OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    bool got = false;
    StatusBytes s;
    for (int i = 0; i < 16 && ReadMessage(h, s, 300); ++i) messages.push_back(s);
    uint8_t request[] = {0x1B, 0x69, 0x53};
    if (Transfer(h, request, sizeof(request), true, 1000) == static_cast<int>(sizeof(request))) {
        const ULONGLONG start = GetTickCount64();
        while (!got && GetTickCount64() - start < static_cast<ULONGLONG>(timeoutMs)) {
            if (!ReadMessage(h, s, 300)) {
                Sleep(50);
                continue;
            }
            messages.push_back(s);
            if (s[18] == 0x00) {
                reply = s;
                got = true;
            }
        }
    }
    CloseHandle(h);
    return got;
}

}  // namespace ql
