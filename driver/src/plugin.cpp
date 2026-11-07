// Unidrv render plug-in for the Brother QL-800 (ARM64).
//
// Unidrv renders and halftones each page into 1 bpp bands and hands them to ImageProcessing.
// The plug-in collects a whole page, then emits the QL raster protocol itself (see ql_raster.h).
// With the colour mode "Black and red" (DK-22251, not on the QL-570) Unidrv renders 24 bpp bands
// instead; the plug-in separates them into a black and a red plane (ql::SeparateRgbLine; Unidrv's bands are R,G,B, not BGR). Like
// Brother's driver, two-colour is used on 62 mm continuous only; on other papers red prints black.
// On a USB port the plug-in also asks the printer for the loaded roll first (usb_status.h): on a
// black-only roll a two-colour job would stop the printer, so red prints black there too.
// GPD option commands only notify the plug-in through CommandCallback (IDs: template.GPD.in).
// Page and document ends are taken from hooked DDI calls (DrvSendPage, DrvEndDoc), which GDI
// always makes, instead of GPD end commands.
//
// A page is held back until the next page or the end of the document arrives, because the
// terminator differs: FF for "more pages follow", Ctrl-Z for the last page.
//
// Multi-up papers ("62mm x 2/3/4") are pages several tapes wide: each strip is sent as a label of its
// own, left strip first. A paper that the printer's model doesn't have (a label format based on
// e.g. 62 x 60 mm on a QL-810W) prints on continuous tape of the same width and length instead of
// sending the printer a paper it doesn't know.
//
// User label formats are Windows forms: the form name selected in the DEVMODE is looked up in
// C:\ProgramData\QLUnidrv\labels.tsv (labels.h), which supplies the Brother media, length and feed.
// The lookup wins over whatever paper Unidrv mapped the form to (custom size or a GPD paper).
//
// Diagnostics (off by default): while C:\ProgramData\QLUnidrv\log\plugin.log exists, calls are
// appended to it (install.ps1 -Logging on creates it, -Logging off deletes it); at 1 MB it is moved
// to plugin.old.log. Only administrators can create it (the folder is not writable for users).

#include <windows.h>
#include <objbase.h>
#pragma warning(push)
#pragma warning(disable : 4005)  // ddraw.h (via winddi.h) redefines CO_E_NOTINITIALIZED
#include <winddi.h>
#pragma warning(pop)
#include <winspool.h>
#include <compstui.h>
#include <winddiui.h>
#include <printoem.h>
#include <initguid.h>
#include <prcomoem.h>
#define STRSAFE_NO_DEPRECATE
#include <strsafe.h>
#include <aclapi.h>

#include <algorithm>
#include <new>
#include <vector>

#include "labels.h"
#include "ql_raster.h"
#include "usb_status.h"

namespace {

constexpr DWORD kSignature = 'QL8A';

enum CallbackId : DWORD {
    kStartDoc = 1, kStartPage = 2, kEndPage = 3, kEndDoc = 4, kCopies = 5, kNoop = 9,
    kAutoCutOn = 10, kAutoCutOff = 11, kCutAtEndOn = 12, kCutAtEndOff = 13,
    kResNormal = 14, kResHigh = 15, kSpeed = 16, kQuality = 17, kMirrorOff = 18, kMirrorOn = 19,
    kTrimOff = 30, kTrimOn = 31, kRotateOff = 32, kRotateOn = 33,
    kColorMono = 20, kColorRedBlack = 21,
    // StartDoc of the other models' GPDs: 40 + ql::Model (41 QL-810W, 42 QL-820NWB, 43 QL-570,
    // 44 QL-560, 45 QL-550, 46 QL-500, 47 QL-650TD, 48 QL-700, 49 QL-710W, 50 QL-720NW, 51 QL-1050,
    // 52 QL-1060N, 53 QL-580N, 54 QL-1100, 55 QL-1110NWB, 56 QL-1115NWB, 57 QL-600; fixed IDs, see
    // ql::Model and MODELS in gen/media.py)
    kStartDocModelBase = 40, kStartDocLast = 57,
    kCustomSize = 199,      // params: PhysPaperWidth, PhysPaperLength (master units)
    kCutEveryBase = 200,    // 200 + n, n = 1..99
    kPaperBase = 10000,     // 10000 + feedDots * 100 + media index
};
constexpr double kMasterUnits = 600.0;
constexpr uint16_t kDefaultFeedDots = 35;  // 3 mm
constexpr uint16_t kTwoColorPaper = 259;    // 62 mm continuous (DK-22251)

LONG g_objects = 0;
LONG g_locks = 0;

struct QlPdev {
    const ql::Media* media = nullptr;
    uint16_t feedDots = kDefaultFeedDots;  // continuous: feed margin; die-cut: the paper's top offset
    int customLength01 = 0;  // custom size: requested length in 0.1 mm, 0 for GPD paper sizes
    int surfaceTopDots = kDefaultFeedDots;  // top margin of Unidrv's page (GPD paper or custom size)
    bool haveLabel = false;  // the selected form is a user label format from labels.tsv
    ql::LabelFormat label;
    int lineOffset = 0;      // label line = surface line + lineOffset
    int copies = 1;          // device copies: each page is repeated (as Brother's driver does)
    ql::JobSettings settings;
    int surfaceWidth = 0;    // printable area in device pixels (from GDIINFO)
    int surfaceHeight = 0;
    bool usbPort = true;     // the queue's port is USB (no compression on the QL-810W/820NWB)
    bool inkIsOne = true;    // 1 bpp without colour table: Unidrv printer convention, 1 = black
    bool redBlack = false;   // colour mode "Black and red" selected (24 bpp bands)
    bool rollChecked = false;  // the loaded roll was asked for (once per job, before any output)
    bool blackRoll = false;    // ... and it prints black only: red prints black
    bool landscape = false;  // Unidrv hands landscape pages over unrotated (feed direction across)
    std::vector<uint8_t> landscapeBits;  // landscape page, surfaceWidth x surfaceHeight, 1 bpp
    std::vector<uint8_t> landscapeRed;   // its red plane (two-colour pages only)
    int landscapeStride = 0;
    ql::Page page;
    bool pageActive = false;
    std::vector<uint8_t> pending;  // encoded page waiting for its terminator
    bool havePending = false;
    int pagesEncoded = 0;
    IPrintOemDriverUni* helper = nullptr;  // owned by the QlRender object
    PFN unidrvSendPage = nullptr;           // Unidrv's own DDI functions (called from our hooks)
    PFN unidrvEndDoc = nullptr;
};

// The log folder is writable by SYSTEM and administrators only (install.ps1). The plug-in runs in the
// spooler's PrintIsolationHost (SYSTEM), impersonating the job's user, so it writes its logs with the
// process's own rights (WithProcessRights) and therefore refuses to write anywhere else: the folder must be
// a real folder at exactly kLogDir that only SYSTEM and administrators own and may change (one a user made
// before install.ps1 ran, e.g. after a driver-only install, is not used), it stays open while it is used,
// and the files are opened relative to that handle and must be plain files with one link (the classic
// printer-driver redirection through junctions, symbolic links or hard links can't reach anything).
constexpr wchar_t kLogDir[] = L"C:\\ProgramData\\QLUnidrv\\log";
constexpr wchar_t kLogPath[] = L"C:\\ProgramData\\QLUnidrv\\log\\plugin.log";
constexpr wchar_t kLogName[] = L"plugin.log";
constexpr wchar_t kOldLogName[] = L"plugin.old.log";
constexpr wchar_t kStatusLogName[] = L"printer-status.log";
constexpr LONGLONG kLogMaxBytes = 1024 * 1024;
constexpr LONGLONG kStatusLogMaxBytes = 256 * 1024;

// Runs fn on a new thread and waits for it. A new thread doesn't inherit the job thread's impersonation, so
// fn runs with the process's own rights while the job thread never drops its impersonation (reverting and
// restoring it instead could leave the job running as SYSTEM if the restore failed).
template <typename F>
void WithProcessRights(F&& fn) {
    struct Call {
        F* fn;
        static DWORD WINAPI Run(void* p) {
            (*static_cast<Call*>(p)->fn)();
            return 0;
        }
    } call{&fn};
    HANDLE t = CreateThread(nullptr, 0, &Call::Run, &call, 0, nullptr);
    if (!t) return;
    WaitForSingleObject(t, INFINITE);
    CloseHandle(t);
}

// The handle's final path is exactly dir (name null) or dir\name (no junction or link on the way).
bool IsAtPath(HANDLE h, const wchar_t* dir, const wchar_t* name) {
    wchar_t want[MAX_PATH];
    if (FAILED(name ? StringCchPrintfW(want, MAX_PATH, L"%s\\%s", dir, name) : StringCchCopyW(want, MAX_PATH, dir)))
        return false;
    wchar_t final[MAX_PATH + 8];
    const DWORD n = GetFinalPathNameByHandleW(h, final, MAX_PATH + 8, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
    return n > 4 && n < MAX_PATH + 8 && wcsncmp(final, L"\\\\?\\", 4) == 0 && _wcsicmp(final + 4, want) == 0;
}

// SYSTEM, administrators or TrustedInstaller.
bool IsAdminSid(PSID sid) {
    if (IsWellKnownSid(sid, WinLocalSystemSid) || IsWellKnownSid(sid, WinBuiltinAdministratorsSid)) return true;
    SID_IDENTIFIER_AUTHORITY nt = SECURITY_NT_AUTHORITY;
    PSID trustedInstaller = nullptr;
    if (!AllocateAndInitializeSid(&nt, 6, 80, 956008885, 3418522649, 1831038044, 1853292631, 2271478464, 0,
                                  0, &trustedInstaller))
        return false;
    const bool ti = EqualSid(sid, trustedInstaller) != 0;
    FreeSid(trustedInstaller);
    return ti;
}

// Owned by an administrator SID, and no one else may add to, delete from or change the folder itself
// (inherit-only entries such as CREATOR OWNER don't apply to it).
bool OnlyAdminsMayChange(HANDLE dir) {
    PSID owner = nullptr;
    PACL dacl = nullptr;
    PSECURITY_DESCRIPTOR sd = nullptr;
    if (GetSecurityInfo(dir, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION, &owner, nullptr,
                        &dacl, nullptr, &sd) != ERROR_SUCCESS)
        return false;
    constexpr ACCESS_MASK kChange = FILE_ADD_FILE | FILE_ADD_SUBDIRECTORY | FILE_DELETE_CHILD | FILE_WRITE_ATTRIBUTES |
                                    FILE_WRITE_EA | DELETE | WRITE_DAC | WRITE_OWNER | GENERIC_WRITE | GENERIC_ALL;
    bool ok = owner && IsAdminSid(owner) && dacl;
    for (DWORD i = 0; ok && i < dacl->AceCount; ++i) {
        ACE_HEADER* ace = nullptr;
        if (!GetAce(dacl, i, reinterpret_cast<void**>(&ace))) ok = false;
        else if ((ace->AceFlags & INHERIT_ONLY_ACE) || ace->AceType == ACCESS_DENIED_ACE_TYPE) continue;
        else if (ace->AceType != ACCESS_ALLOWED_ACE_TYPE) ok = false;  // object or callback entries: never ours
        else {
            auto* allowed = reinterpret_cast<ACCESS_ALLOWED_ACE*>(ace);
            ok = !(allowed->Mask & kChange) || IsAdminSid(&allowed->SidStart);
        }
    }
    LocalFree(sd);
    return ok;
}

// The log folder, if it is safe (see above); open without delete sharing, so it can't be renamed, deleted or
// replaced while it is used. INVALID_HANDLE_VALUE otherwise.
HANDLE OpenLogDir() {
    HANDLE d = CreateFileW(kLogDir, FILE_LIST_DIRECTORY | FILE_TRAVERSE | FILE_READ_ATTRIBUTES | READ_CONTROL,
                           FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING,
                           FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (d == INVALID_HANDLE_VALUE) return d;
    BY_HANDLE_FILE_INFORMATION info;
    if (!GetFileInformationByHandle(d, &info) || !(info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) ||
        (info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) || !IsAtPath(d, kLogDir, nullptr) ||
        !OnlyAdminsMayChange(d)) {
        CloseHandle(d);
        return INVALID_HANDLE_VALUE;
    }
    return d;
}

// NtCreateFile (ntdll, always loaded): the way to open a file relative to a folder handle.
struct NtUnicodeString {
    USHORT Length, MaximumLength;
    PWSTR Buffer;
};
struct NtObjectAttributes {
    ULONG Length;
    HANDLE RootDirectory;
    NtUnicodeString* ObjectName;
    ULONG Attributes;
    void* SecurityDescriptor;
    void* SecurityQualityOfService;
};
struct NtIoStatusBlock {
    void* Pointer;
    ULONG_PTR Information;
};
using NtCreateFileFn = LONG(NTAPI*)(HANDLE*, ACCESS_MASK, NtObjectAttributes*, NtIoStatusBlock*, LARGE_INTEGER*,
                                    ULONG, ULONG, ULONG, ULONG, void*, ULONG);

// Opens name in the log folder dir (open existing only, or open or create). INVALID_HANDLE_VALUE if it is
// missing (openExisting) or not a plain file with one link at exactly that path.
HANDLE OpenLogFile(HANDLE dir, const wchar_t* name, DWORD access, bool openExisting) {
    static const auto ntCreateFile =
        reinterpret_cast<NtCreateFileFn>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtCreateFile"));
    if (!ntCreateFile) return INVALID_HANDLE_VALUE;
    const auto bytes = static_cast<USHORT>(wcslen(name) * sizeof(wchar_t));
    NtUnicodeString uname = {bytes, static_cast<USHORT>(bytes + sizeof(wchar_t)), const_cast<PWSTR>(name)};
    NtObjectAttributes oa = {sizeof(oa), dir, &uname, 0x40 /* OBJ_CASE_INSENSITIVE */, nullptr, nullptr};
    NtIoStatusBlock io = {};
    HANDLE h = nullptr;
    constexpr ULONG kFileOpen = 1, kFileOpenIf = 3;
    constexpr ULONG kOptions = 0x40 /* FILE_NON_DIRECTORY_FILE */ | 0x20 /* FILE_SYNCHRONOUS_IO_NONALERT */ |
                               0x00200000 /* FILE_OPEN_REPARSE_POINT */;
    if (ntCreateFile(&h, access | FILE_READ_ATTRIBUTES | SYNCHRONIZE, &oa, &io, nullptr, FILE_ATTRIBUTE_NORMAL,
                     FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, openExisting ? kFileOpen : kFileOpenIf,
                     kOptions, nullptr, 0) < 0)
        return INVALID_HANDLE_VALUE;
    BY_HANDLE_FILE_INFORMATION info;
    if (!GetFileInformationByHandle(h, &info) ||
        (info.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) ||
        info.nNumberOfLinks != 1 || !IsAtPath(h, kLogDir, name)) {
        CloseHandle(h);
        return INVALID_HANDLE_VALUE;
    }
    return h;
}

// plugin.log -> plugin.old.log, renamed through the checked handle, within the open folder.
void RotateLog(HANDLE dir, HANDLE h) {
    constexpr size_t kNameBytes = sizeof(kOldLogName) - sizeof(wchar_t);
    alignas(FILE_RENAME_INFO) BYTE buf[sizeof(FILE_RENAME_INFO) + sizeof(kOldLogName)] = {};
    auto* r = reinterpret_cast<FILE_RENAME_INFO*>(buf);
    r->ReplaceIfExists = TRUE;
    r->RootDirectory = dir;
    r->FileNameLength = static_cast<DWORD>(kNameBytes);
    memcpy(r->FileName, kOldLogName, kNameBytes + sizeof(wchar_t));
    // NtSetInformationFile(FileRenameInformation): kernel32's SetFileInformationByHandle takes the name as a DOS
    // path and refuses a relative name with RootDirectory (ERROR_INVALID_NAME, found on x64 2026-10-25).
    using NtSetInformationFileFn = LONG(NTAPI*)(HANDLE, NtIoStatusBlock*, void*, ULONG, ULONG);
    static const auto ntSetInformationFile = reinterpret_cast<NtSetInformationFileFn>(
        GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtSetInformationFile"));
    if (!ntSetInformationFile) return;
    NtIoStatusBlock io = {};
    constexpr ULONG kFileRenameInformation = 10;
    ntSetInformationFile(h, &io, r, static_cast<ULONG>(sizeof(buf)), kFileRenameInformation);  // may race with another process: fine
}

void WriteLogLine(const char* line, DWORD n) {
    HANDLE dir = OpenLogDir();
    if (dir == INVALID_HANDLE_VALUE) return;
    HANDLE h = OpenLogFile(dir, kLogName, FILE_APPEND_DATA | DELETE, true);  // no file: logging is off
    LARGE_INTEGER size;
    if (h != INVALID_HANDLE_VALUE && GetFileSizeEx(h, &size) && size.QuadPart > kLogMaxBytes) {
        RotateLog(dir, h);
        CloseHandle(h);
        h = OpenLogFile(dir, kLogName, FILE_APPEND_DATA, false);
    }
    if (h != INVALID_HANDLE_VALUE) {
        DWORD written = 0;
        WriteFile(h, line, n, &written, nullptr);
        CloseHandle(h);
    }
    CloseHandle(dir);
}

void Log(const char* fmt, ...) {
    // Logging is on only while plugin.log exists (users may read the folder, so this needs no thread).
    if (GetFileAttributesW(kLogPath) == INVALID_FILE_ATTRIBUTES) return;
    char buf[1024];
    SYSTEMTIME t;
    GetLocalTime(&t);
    StringCchPrintfA(buf, ARRAYSIZE(buf) - 2, "%02d:%02d:%02d.%03d [%lu] ", t.wHour, t.wMinute, t.wSecond,
                     t.wMilliseconds, GetCurrentProcessId());
    size_t n = strlen(buf);
    va_list args;
    va_start(args, fmt);
    StringCchVPrintfA(buf + n, ARRAYSIZE(buf) - 2 - n, fmt, args);  // too long: cut, still terminated
    va_end(args);
    n = strlen(buf);
    buf[n++] = '\r';
    buf[n++] = '\n';
    WithProcessRights([&] { WriteLogLine(buf, static_cast<DWORD>(n)); });
}

// The most frequent colours of a 24 bpp band (bytes in memory order), to check the separation.
void LogBandColours(const uint8_t* src, int width, int height, int stride, DWORD compression) {
    struct Entry { uint32_t c; unsigned n; };
    Entry e[8] = {};
    int used = 0;
    unsigned other = 0;
    for (int row = 0; row < height; ++row) {
        const uint8_t* px = src + static_cast<size_t>(row) * stride;
        for (int x = 0; x < width; ++x, px += 3) {
            const uint32_t c = (px[0] << 16) | (px[1] << 8) | px[2];
            int i = 0;
            while (i < used && e[i].c != c) ++i;
            if (i < used) ++e[i].n;
            else if (used < 8) e[used++] = { c, 1 };
            else ++other;
        }
    }
    char s[300];
    int n = 0;
    for (int i = 0; i < used; ++i) n += wsprintfA(s + n, " %06lx:%u", static_cast<unsigned long>(e[i].c), e[i].n);
    s[n] = 0;
    Log("band colours (bytes 0,1,2; compression %lu):%s other %u", compression, s, other);
}

void Write(PDEVOBJ pdevobj, QlPdev& p, const std::vector<uint8_t>& data) {
    if (!p.helper || data.empty()) return;
    DWORD written = 0;
    const HRESULT hr = p.helper->DrvWriteSpoolBuf(pdevobj, const_cast<uint8_t*>(data.data()),
                                                  static_cast<DWORD>(data.size()), &written);
    Log("write %lu bytes -> hr 0x%08lx, written %lu", static_cast<DWORD>(data.size()), hr, written);
}

void EnsurePage(QlPdev& p, int bandWidth) {
    if (p.pageActive) return;
    if (!p.media) p.media = ql::MediaByPtdId(259);  // 62 mm continuous
    if (!ql::ModelHasMedia(p.settings.model, *p.media)) {
        const ql::Media* c = ql::ContinuousForWidth(p.media->widthMm, p.settings.model);
        Log("paper %u is not in model %d's table: continuous %u mm instead", p.media->ptdId,
            static_cast<int>(p.settings.model), c->widthMm);
        // The label format's length (0.1 mm) is more exact than the table's whole millimetres.
        if (p.media->kind == ql::MediaKind::DieCut)
            p.customLength01 = p.haveLabel && p.label.length01 > 0 ? p.label.length01 : p.media->lengthMm * 10;
        p.media = c;
        p.feedDots = kDefaultFeedDots;
    }
    const int surfaceWidth = p.surfaceWidth > 0 ? p.surfaceWidth : bandWidth;
    // Portrait: one surface row per raster line. Landscape: one surface column per raster line.
    const int width = p.landscape ? p.surfaceHeight : surfaceWidth;
    int lines = p.landscape ? surfaceWidth : p.surfaceHeight;
    // Two-colour only where Brother offers it: 62 mm continuous on the QL-800/810W/820NWB.
    const bool twoColorPaper = p.redBlack && ql::Traits(p.settings.model).twoColor && p.media->ptdId == kTwoColorPaper;
    const bool twoColor = twoColorPaper && !p.blackRoll;
    p.settings.twoColor = twoColor;
    if (p.landscape) {
        p.landscapeStride = (surfaceWidth + 7) / 8;
        p.landscapeBits.assign(static_cast<size_t>(p.landscapeStride) * p.surfaceHeight, 0);
        if (twoColor) p.landscapeRed.assign(p.landscapeBits.size(), 0);
        else p.landscapeRed.clear();
    }
    // Brother's line count for continuous lengths (Unidrv rounds the custom page slightly
    // differently); the offset keeps the drawing where the application put it when the label's
    // feed margin differs from the page's top margin.
    const ql::PageGeometry g = ql::ComputePageGeometry(*p.media, p.settings.highRes, lines, p.surfaceTopDots,
                                                       p.customLength01, p.feedDots,
                                                       ql::Traits(p.settings.model).minLength01);
    lines = g.lines;
    p.lineOffset = g.lineOffset;
    p.page.Reset(*p.media, width, lines, twoColor);
    p.pageActive = true;
    Log("page start: media %u (%u strips), surface %dx%d, lines %d, offset %d%s%s", p.media->ptdId, p.media->strips,
        p.surfaceWidth, p.surfaceHeight, lines, p.lineOffset, p.landscape ? ", landscape" : "",
        twoColor        ? ", black and red"
        : twoColorPaper ? ", black and red requested: the loaded roll prints black only, red prints black"
        : p.redBlack    ? ", black and red requested: not on this paper/model, red prints black"
                        : "");
}

// The selected form is a user label format: its media, length and feed replace the paper Unidrv
// chose for the form's size.
void ApplyLabel(QlPdev& p) {
    if (!p.haveLabel) return;
    const ql::Media* m = ql::MediaByPtdId(p.label.paperId, p.settings.model);
    if (!m) {
        Log("label format '%ls': unknown paper %u, ignored", p.label.name.c_str(), p.label.paperId);
        return;
    }
    p.media = m;
    p.feedDots = p.label.feedDots;
    p.customLength01 = m->kind == ql::MediaKind::Continuous ? p.label.length01 : 0;
    Log("label format '%ls': media %u, length %d (0.1 mm), feed %u", p.label.name.c_str(), m->ptdId,
        p.customLength01, p.feedDots);
}

// Name of the selected form: dmFormName (only if DM_FORMNAME is set; otherwise it may be stale), or
// for applications that only set dmPaperSize, the forms-database entry it indexes (1-based, between
// the predefined sizes and DMPAPER_USER). Logged jobs: Unidrv sets DM_FORMNAME whenever the name is
// filled (dmFields 0x5816f43); custom sizes come without it and with an empty name.
std::wstring FormName(PDEVOBJ pdevobj, const DEVMODEW* dm) {
    if (!dm) return std::wstring();
    if ((dm->dmFields & DM_FORMNAME) && dm->dmFormName[0]) return std::wstring(dm->dmFormName, wcsnlen(dm->dmFormName, CCHFORMNAME));
    if (!(dm->dmFields & DM_PAPERSIZE) || dm->dmPaperSize <= DMPAPER_LAST || dm->dmPaperSize >= DMPAPER_USER ||
        !pdevobj->hPrinter)
        return std::wstring();
    DWORD needed = 0, count = 0;
    EnumFormsW(pdevobj->hPrinter, 1, nullptr, 0, &needed, &count);
    if (!needed) return std::wstring();
    std::vector<BYTE> buf(needed);
    if (!EnumFormsW(pdevobj->hPrinter, 1, buf.data(), needed, &needed, &count) ||
        static_cast<DWORD>(dm->dmPaperSize) > count)
        return std::wstring();
    const auto* forms = reinterpret_cast<const FORM_INFO_1W*>(buf.data());
    const wchar_t* name = forms[dm->dmPaperSize - 1].pName;
    return name ? std::wstring(name) : std::wstring();
}

// The queue's printer and port name ("USB001"); false if unknown.
bool PrinterAndPort(PDEVOBJ pdevobj, std::wstring& printer, std::wstring& port) {
    if (!pdevobj || !pdevobj->hPrinter) return false;
    DWORD needed = 0;
    GetPrinterW(pdevobj->hPrinter, 5, nullptr, 0, &needed);
    if (!needed) return false;
    std::vector<BYTE> buf(needed);
    if (!GetPrinterW(pdevobj->hPrinter, 5, buf.data(), needed, &needed)) return false;
    const auto* info = reinterpret_cast<const PRINTER_INFO_5W*>(buf.data());
    if (!info->pPortName) return false;
    printer = info->pPrinterName ? info->pPrinterName : L"";
    port = info->pPortName;
    return true;
}

// Brother's 810W/820NWB driver compresses unless the port is USB (port name "USB..." or "PTUSB...").
bool IsUsbPort(PDEVOBJ pdevobj) {
    std::wstring printer, port;
    if (!PrinterAndPort(pdevobj, printer, port)) return true;
    return _wcsnicmp(port.c_str(), L"USB", 3) == 0 || _wcsnicmp(port.c_str(), L"PTUSB", 5) == 0;
}

// Messages read from the printer go to the driver's status log, which the label tool reads next to its
// own (QlDevice.cs, StatusLog), so the status window and the monitor still see what the driver drained:
//   yyyy-MM-dd HH:mm:ss TAB printer TAB 32 bytes hex TAB summary
// Rare (black-and-red jobs only); emptied at 256 KB.
void WriteStatusLog(const std::wstring& printer, const std::vector<ql::StatusBytes>& messages) {
    HANDLE dir = OpenLogDir();
    if (dir == INVALID_HANDLE_VALUE) return;
    HANDLE h = OpenLogFile(dir, kStatusLogName, FILE_APPEND_DATA | FILE_WRITE_DATA, false);
    CloseHandle(dir);
    if (h == INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER size, zero = {};
    if (GetFileSizeEx(h, &size) && size.QuadPart > kStatusLogMaxBytes && SetFilePointerEx(h, zero, nullptr, FILE_BEGIN))
        SetEndOfFile(h);
    SetFilePointerEx(h, zero, nullptr, FILE_END);  // FILE_WRITE_DATA writes at the file pointer
    char name[256] = {};
    WideCharToMultiByte(CP_UTF8, 0, printer.c_str(), -1, name, sizeof(name) - 1, nullptr, nullptr);
    SYSTEMTIME t;
    GetLocalTime(&t);
    for (const ql::StatusBytes& m : messages) {
        char line[512];
        int n = wsprintfA(line, "%04d-%02d-%02d %02d:%02d:%02d\t%s\t", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute,
                          t.wSecond, name);
        for (size_t i = 0; i < m.size(); ++i) n += wsprintfA(line + n, i ? " %02X" : "%02X", m[i]);
        n += wsprintfA(line + n, "\tread by the driver (black-and-red job: roll check)\r\n");
        DWORD written = 0;
        WriteFile(h, line, static_cast<DWORD>(n), &written, nullptr);
    }
    CloseHandle(h);
}

void AppendStatusLog(const std::wstring& printer, const std::vector<ql::StatusBytes>& messages) {
    if (!messages.empty()) WithProcessRights([&] { WriteStatusLog(printer, messages); });
}

// A black-and-red job on a USB printer: ask the printer which roll is loaded, once per job and before
// anything of the job is written. A black-only roll would stop the printer with an error that needs
// the power button, so then the job prints black. Any doubt (no USB, no
// answer, printer busy or in an error state) leaves the job as it is.
void CheckRoll(PDEVOBJ pdevobj, QlPdev& p) {
    if (p.rollChecked) return;
    p.rollChecked = true;
    const DEVMODEW* dm = pdevobj ? pdevobj->pPublicDM : nullptr;
    const bool colour = dm && (dm->dmFields & DM_COLOR) && dm->dmColor == DMCOLOR_COLOR;
    if (!colour || !ql::Traits(p.settings.model).twoColor || !p.usbPort) return;
    std::wstring printer, port;
    if (!PrinterAndPort(pdevobj, printer, port)) return;
    const std::wstring path = ql::UsbInterfaceOfPort(port.c_str());
    if (path.empty()) {
        Log("roll check: no usbprint interface for port %ls", port.c_str());
        return;
    }
    std::vector<ql::StatusBytes> messages;
    ql::StatusBytes reply = {};
    const ULONGLONG start = GetTickCount64();
    const bool got = ql::QueryUsbStatus(path, messages, reply, 1500);
    AppendStatusLog(printer, messages);
    p.blackRoll = got && ql::BlackOnlyRoll(reply);
    Log("roll check on %ls: %s after %lu ms, %u messages%s", port.c_str(), got ? "reply" : "no reply",
        static_cast<unsigned long>(GetTickCount64() - start), static_cast<unsigned>(messages.size()),
        !got ? "" : p.blackRoll ? ", black-only roll: red prints black" : ", red/black roll (or unknown)");
}

// Landscape: Brother prints the landscape page rotated 90 degrees clockwise onto the label
// (reference/captures/ql800/62mm_landscape.prn): label pixel (x, y) = landscape pixel (y, height - 1 - x).
void RotateLandscape(QlPdev& p) {
    const int height = p.surfaceHeight;  // = label width in pixels
    std::vector<uint8_t> line((height + 7) / 8);
    auto column = [&](const std::vector<uint8_t>& bits, int col) {
        std::fill(line.begin(), line.end(), uint8_t(0));
        for (int x = 0; x < height; ++x) {
            const int ly = height - 1 - x;
            if ((bits[static_cast<size_t>(ly) * p.landscapeStride + (col >> 3)] >> (7 - (col & 7))) & 1)
                line[x >> 3] |= static_cast<uint8_t>(0x80 >> (x & 7));
        }
    };
    for (int y = 0; y < p.page.Lines(); ++y) {
        const int col = y - p.lineOffset;  // landscape column feeding this label line
        if (col < 0 || col >= p.surfaceWidth) continue;
        column(p.landscapeBits, col);
        p.page.SetSourceLine(y, line.data(), p.inkIsOne, height);
        if (!p.landscapeRed.empty()) {
            column(p.landscapeRed, col);
            p.page.SetRedLine(y, line.data(), height);
        }
    }
}

void FlushPending(PDEVOBJ pdevobj, QlPdev& p, uint8_t terminator) {
    if (!p.havePending) return;
    p.pending.push_back(terminator);
    if (terminator == ql::kLastPage) ql::AppendJobTrailer(p.pending, p.settings.model);
    Write(pdevobj, p, p.pending);
    p.pending.clear();
    p.havePending = false;
}

void FinishPage(PDEVOBJ pdevobj, QlPdev& p) {
    EnsurePage(p, p.surfaceWidth);  // blank page if no band arrived
    if (p.landscape) RotateLandscape(p);
    for (int c = 0; c < p.copies; ++c) {
        for (int strip = 0; strip < p.page.GetMedia().strips; ++strip) {
            FlushPending(pdevobj, p, ql::kNextPage);
            p.pending.clear();
            ql::AppendPage(p.pending, p.settings, p.page, p.feedDots, p.pagesEncoded == 0, strip);
            p.havePending = true;
            ++p.pagesEncoded;
        }
    }
    p.pageActive = false;
}

// DDI hooks. Unidrv renders the page (ImageProcessing for every band) inside its DrvSendPage.
BOOL APIENTRY HookSendPage(SURFOBJ* pso) {
    auto pdevobj = reinterpret_cast<PDEVOBJ>(pso->dhpdev);
    auto* p = static_cast<QlPdev*>(pdevobj->pdevOEM);
    const BOOL ok = reinterpret_cast<BOOL(APIENTRY*)(SURFOBJ*)>(p->unidrvSendPage)(pso);
    Log("DrvSendPage -> %d", ok);
    try {
        FinishPage(pdevobj, *p);
    } catch (const std::bad_alloc&) {
        return FALSE;
    }
    return ok;
}

BOOL APIENTRY HookEndDoc(SURFOBJ* pso, FLONG fl) {
    auto pdevobj = reinterpret_cast<PDEVOBJ>(pso->dhpdev);
    auto* p = static_cast<QlPdev*>(pdevobj->pdevOEM);
    Log("DrvEndDoc fl=0x%lx pending=%d", fl, p->havePending);
    if (fl & ED_ABORTDOC) {
        p->havePending = false;
    } else {
        try {
            FlushPending(pdevobj, *p, ql::kLastPage);
        } catch (const std::bad_alloc&) {
        }
    }
    return reinterpret_cast<BOOL(APIENTRY*)(SURFOBJ*, FLONG)>(p->unidrvEndDoc)(pso, fl);
}

DRVFN g_hooks[] = {
    {INDEX_DrvSendPage, reinterpret_cast<PFN>(HookSendPage)},
    {INDEX_DrvEndDoc, reinterpret_cast<PFN>(HookEndDoc)},
};

class QlRender final : public IPrintOemUni {
public:
    QlRender() { InterlockedIncrement(&g_objects); }
    ~QlRender() {
        if (helper_) helper_->Release();
        InterlockedDecrement(&g_objects);
    }

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, LPVOID* ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IPrintOemUni) {
            *ppv = static_cast<IPrintOemUni*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        const ULONG r = InterlockedDecrement(&refs_);
        if (r == 0) delete this;
        return r;
    }

    // IPrintOemCommon
    STDMETHODIMP GetInfo(DWORD mode, PVOID buffer, DWORD size, PDWORD needed) override {
        if (!needed) return E_POINTER;
        *needed = sizeof(DWORD);
        if (!buffer || size < sizeof(DWORD)) return E_FAIL;
        switch (mode) {
        case OEMGI_GETSIGNATURE: *static_cast<DWORD*>(buffer) = kSignature; return S_OK;
        case OEMGI_GETVERSION: *static_cast<DWORD*>(buffer) = 0x00000001; return S_OK;
        default: return E_FAIL;
        }
    }
    STDMETHODIMP DevMode(DWORD, POEMDMPARAM) override { return E_NOTIMPL; }

    // IPrintOemEngine
    STDMETHODIMP EnableDriver(DWORD, DWORD, PDRVENABLEDATA pded) override {
        if (pded) {
            pded->iDriverVersion = PRINTER_OEMINTF_VERSION;
            pded->c = sizeof(g_hooks) / sizeof(g_hooks[0]);
            pded->pdrvfn = g_hooks;
        }
        return S_OK;
    }
    STDMETHODIMP DisableDriver() override { return S_OK; }

    STDMETHODIMP EnablePDEV(PDEVOBJ pdevobj, PWSTR, ULONG, HSURF*, ULONG cjGdiInfo, GDIINFO* gdiInfo, ULONG,
                            DEVINFO*, DRVENABLEDATA* unidrv, PDEVOEM* devOem) override {
        auto* pdev = new (std::nothrow) QlPdev();
        if (!pdev) return E_OUTOFMEMORY;
        if (gdiInfo && cjGdiInfo >= offsetof(GDIINFO, ulVertRes) + sizeof(ULONG)) {
            pdev->surfaceWidth = static_cast<int>(gdiInfo->ulHorzRes);
            pdev->surfaceHeight = static_cast<int>(gdiInfo->ulVertRes);
        }
        pdev->helper = helper_;
        const DEVMODEW* dm = pdevobj ? pdevobj->pPublicDM : nullptr;
        pdev->landscape = dm && (dm->dmFields & DM_ORIENTATION) && dm->dmOrientation == DMORIENT_LANDSCAPE;
        try {
            pdev->usbPort = IsUsbPort(pdevobj);
        } catch (const std::bad_alloc&) {
            pdev->usbPort = true;
        }
        try {
            const std::wstring form = FormName(pdevobj, dm);
            const std::vector<ql::LabelFormat> labels = ql::LoadLabelsTsv();
            if (const ql::LabelFormat* f = ql::FindLabelFormat(labels, form.c_str())) {
                pdev->label = *f;
                pdev->haveLabel = true;
            }
            Log("EnablePDEV form '%ls' (paper %d, fields 0x%lx): %s (%u label formats)", form.c_str(),
                dm ? dm->dmPaperSize : 0, dm ? dm->dmFields : 0UL,
                pdev->haveLabel ? "label format" : "no label format", static_cast<unsigned>(labels.size()));
        } catch (const std::bad_alloc&) {
            pdev->haveLabel = false;
        }
        for (ULONG i = 0; unidrv && i < unidrv->c; ++i) {
            if (unidrv->pdrvfn[i].iFunc == INDEX_DrvSendPage) pdev->unidrvSendPage = unidrv->pdrvfn[i].pfn;
            if (unidrv->pdrvfn[i].iFunc == INDEX_DrvEndDoc) pdev->unidrvEndDoc = unidrv->pdrvfn[i].pfn;
        }
        Log("EnablePDEV surface %dx%d, helper %p, unidrv SendPage %p EndDoc %p", pdev->surfaceWidth,
            pdev->surfaceHeight, helper_, pdev->unidrvSendPage, pdev->unidrvEndDoc);
        if (!pdev->unidrvSendPage || !pdev->unidrvEndDoc) {
            delete pdev;
            return E_FAIL;
        }
        *devOem = pdev;
        return S_OK;
    }
    STDMETHODIMP DisablePDEV(PDEVOBJ pdevobj) override {
        delete static_cast<QlPdev*>(pdevobj->pdevOEM);
        return S_OK;
    }
    // A new PDEV is created when the DEVMODE changes mid-job: carry the job state over.
    STDMETHODIMP ResetPDEV(PDEVOBJ oldObj, PDEVOBJ newObj) override {
        auto* oldP = static_cast<QlPdev*>(oldObj->pdevOEM);
        auto* newP = static_cast<QlPdev*>(newObj->pdevOEM);
        if (oldP && newP) {
            newP->settings = oldP->settings;
            newP->redBlack = oldP->redBlack;
            newP->rollChecked = oldP->rollChecked;
            newP->blackRoll = oldP->blackRoll;
            newP->media = oldP->media;
            newP->feedDots = oldP->feedDots;
            newP->customLength01 = oldP->customLength01;
            newP->surfaceTopDots = oldP->surfaceTopDots;
            newP->copies = oldP->copies;
            newP->pending.swap(oldP->pending);
            newP->havePending = oldP->havePending;
            oldP->havePending = false;
            newP->pagesEncoded = oldP->pagesEncoded;
            Log("ResetPDEV");
        }
        return S_OK;
    }

    // IPrintOemUni
    STDMETHODIMP PublishDriverInterface(IUnknown* unk) override {
        if (!helper_ && unk) {
            void* p = nullptr;
            if (FAILED(unk->QueryInterface(IID_IPrintOemDriverUni, &p))) return E_FAIL;
            helper_ = static_cast<IPrintOemDriverUni*>(p);
        }
        return S_OK;
    }

    STDMETHODIMP GetImplementedMethod(PSTR name) override {
        static const char* const kImplemented[] = {
            "GetInfo", "PublishDriverInterface", "EnableDriver", "DisableDriver", "EnablePDEV",
            "DisablePDEV", "ResetPDEV", "GetImplementedMethod", "CommandCallback", "ImageProcessing",
        };
        if (!name) return S_FALSE;
        for (const char* m : kImplemented)
            if (lstrcmpA(name, m) == 0) return S_OK;
        return S_FALSE;
    }

    STDMETHODIMP CommandCallback(PDEVOBJ pdevobj, DWORD id, DWORD count, PDWORD params,
                                 INT* result) override {
        if (result) *result = 0;
        auto* p = static_cast<QlPdev*>(pdevobj->pdevOEM);
        if (!p) return E_FAIL;
        Log("CommandCallback %lu (%lu params)", id, count);
        try {
            switch (id) {
            case kStartDoc:
            case kStartDocModelBase + 1:
            case kStartDocModelBase + 2:
            case kStartDocModelBase + 3:
            case kStartDocModelBase + 4:
            case kStartDocModelBase + 5:
            case kStartDocModelBase + 6:
            case kStartDocModelBase + 7:
            case kStartDocModelBase + 8:
            case kStartDocModelBase + 9:
            case kStartDocModelBase + 10:
            case kStartDocModelBase + 11:
            case kStartDocModelBase + 12:
            case kStartDocModelBase + 13:
            case kStartDocModelBase + 14:
            case kStartDocModelBase + 15:
            case kStartDocModelBase + 16:
            case kStartDocLast: {
                p->settings.model = id == kStartDoc ? ql::Model::QL800 : static_cast<ql::Model>(id - kStartDocModelBase);
                p->settings.compress = ql::Traits(p->settings.model).packOffUsb && !p->usbPort;
                Log("StartDoc model %d, %s port%s", static_cast<int>(p->settings.model),
                    p->usbPort ? "USB" : "non-USB", p->settings.compress ? ", PackBits" : "");
                if (p->pagesEncoded == 0) CheckRoll(pdevobj, *p);  // before the job header is written
                std::vector<uint8_t> out;
                ql::AppendJobHeader(out, p->settings.model);
                Write(pdevobj, *p, out);
                p->pagesEncoded = 0;
                break;
            }
            case kCopies:
                p->copies = (count >= 1 && params && params[0] >= 1 && params[0] <= 999) ? static_cast<int>(params[0]) : 1;
                break;
            case kStartPage: p->pageActive = false; break;
            case kEndPage:  // pages end in HookSendPage, documents in HookEndDoc
            case kEndDoc:
                break;
            case kAutoCutOn: p->settings.autoCut = true; break;
            case kAutoCutOff: p->settings.autoCut = false; break;
            case kCutAtEndOn: p->settings.cutAtEnd = true; break;
            case kCutAtEndOff: p->settings.cutAtEnd = false; break;
            case kResNormal: p->settings.highRes = false; break;
            case kResHigh: p->settings.highRes = true; break;
            case kSpeed: p->settings.qualityPriority = false; break;
            case kQuality: p->settings.qualityPriority = true; break;
            case kMirrorOff: p->settings.mirror = false; break;
            case kMirrorOn: p->settings.mirror = true; break;
            case kTrimOff: p->settings.trimTrailingBlank = false; break;
            case kTrimOn: p->settings.trimTrailingBlank = true; break;
            case kRotateOff: p->settings.rotate180 = false; break;
            case kRotateOn: p->settings.rotate180 = true; break;
            case kColorMono: p->redBlack = false; break;
            case kColorRedBlack: p->redBlack = true; break;
            case kCustomSize:
                if (count >= 2 && params) {
                    const double widthMm = params[0] * 25.4 / kMasterUnits;
                    const double lengthMm = params[1] * 25.4 / kMasterUnits;
                    // Like Brother's driver, a custom size is always continuous tape.
                    p->feedDots = kDefaultFeedDots;
                    p->surfaceTopDots = kDefaultFeedDots;  // CUSTOMSIZE *TopMargin
                    p->media = ql::ContinuousForWidth(widthMm, p->settings.model);
                    const DEVMODEW* dm = pdevobj->pPublicDM;
                    p->customLength01 = (dm && dm->dmPaperSize == DMPAPER_USER && (dm->dmFields & DM_PAPERLENGTH) &&
                                         dm->dmPaperLength > 0)
                                            ? dm->dmPaperLength
                                            : static_cast<int>(lengthMm * 10 + 0.5);
                }
                ApplyLabel(*p);
                break;
            default:
                if (id > kCutEveryBase && id < kCutEveryBase + 100) {
                    p->settings.cutEvery = static_cast<uint8_t>(id - kCutEveryBase);
                } else if (id >= kPaperBase) {
                    const DWORD media = (id - kPaperBase) % 100, feed = (id - kPaperBase) / 100;
                    if (const ql::Media* m = ql::MediaByIndex(media)) {
                        p->media = m;
                        p->feedDots = static_cast<uint16_t>(feed);
                        p->surfaceTopDots = static_cast<int>(feed);  // the paper's printable origin
                        p->customLength01 = 0;
                        // Unidrv replaces a custom size by a paper size with the same dimensions, e.g.
                        // custom 62 x 100 mm by the 62 x 100 mm die-cut label. Brother's driver keeps a
                        // custom size on continuous tape, so do the same.
                        const DEVMODEW* dm = pdevobj->pPublicDM;
                        if (m->kind == ql::MediaKind::DieCut && dm && dm->dmPaperSize == DMPAPER_USER &&
                            (dm->dmFields & DM_PAPERLENGTH) && (dm->dmFields & DM_PAPERWIDTH) &&
                            dm->dmPaperLength > 0 && dm->dmPaperWidth > 0) {
                            p->media = ql::ContinuousForWidth(dm->dmPaperWidth / 10.0, p->settings.model);
                            p->feedDots = kDefaultFeedDots;
                            p->customLength01 = dm->dmPaperLength;
                            Log("custom size %d x %d (0.1 mm) kept on continuous tape", dm->dmPaperWidth,
                                dm->dmPaperLength);
                        }
                        ApplyLabel(*p);
                    }
                }
                break;
            }
        } catch (const std::bad_alloc&) {
            return E_OUTOFMEMORY;
        }
        return S_OK;
    }

    STDMETHODIMP ImageProcessing(PDEVOBJ pdevobj, PBYTE src, PBITMAPINFOHEADER bih, PBYTE colorTable,
                                 DWORD, PIPPARAMS ip, PBYTE* result) override {
        if (result) *result = nullptr;
        auto* p = static_cast<QlPdev*>(pdevobj->pdevOEM);
        if (!p || !bih || !ip) return E_FAIL;
        Log("ImageProcessing %ldx%ld bpp %u at y %ld blank %d table %p", bih->biWidth, bih->biHeight,
            bih->biBitCount, ip->ptOffset.y, ip->bBlankBand, colorTable);
        try {
            EnsurePage(*p, static_cast<int>(bih->biWidth));
            if (colorTable && bih->biBitCount == 1) {
                // Palette entry 0 is black -> a 1 bit means white.
                const RGBQUAD* pal = reinterpret_cast<const RGBQUAD*>(colorTable);
                p->inkIsOne = (pal[0].rgbRed | pal[0].rgbGreen | pal[0].rgbBlue) != 0;
            }
            const int bpp = bih->biBitCount;
            if (!ip->bBlankBand && src && (bpp == 1 || bpp == 24)) {
                const int width = static_cast<int>(bih->biWidth);
                const int stride = ((width * bpp + 31) / 32) * 4;
                const int height = bih->biHeight < 0 ? -bih->biHeight : bih->biHeight;
                // 24 bpp (black and red): separated into two 1 bpp lines; without a red plane
                // (other paper or model) red is added to black.
                const bool rgb = bpp == 24, twoColor = p->page.TwoColor();
                std::vector<uint8_t> black, red;
                if (rgb) {
                    black.resize((width + 7) / 8);
                    red.resize(black.size());
                    LogBandColours(src, width, height, stride, bih->biCompression);
                }
                // Unidrv delivers top-down DIBs (IPrintOemUni::ImageProcessing documentation).
                for (int row = 0; row < height; ++row) {
                    const uint8_t* line = src + static_cast<size_t>(row) * stride;
                    bool ink = p->inkIsOne;
                    if (rgb) {
                        ql::SeparateRgbLine(line, width, black.data(), red.data());
                        if (!twoColor)
                            for (size_t i = 0; i < black.size(); ++i) black[i] |= red[i];
                        line = black.data();
                        ink = true;
                    }
                    const int y = ip->ptOffset.y + row;
                    if (!p->landscape) {
                        p->page.SetSourceLine(y + p->lineOffset, line, ink, width);
                        if (rgb && twoColor) p->page.SetRedLine(y + p->lineOffset, red.data(), width);
                    } else if (y >= 0 && y < p->surfaceHeight) {
                        // Landscape planes are stored with 1 = ink (rgb) or Unidrv's convention (1 bpp).
                        const size_t n = std::min<size_t>(p->landscapeStride, rgb ? black.size() : static_cast<size_t>(stride));
                        const size_t at = static_cast<size_t>(y) * p->landscapeStride;
                        std::copy(line, line + n, &p->landscapeBits[at]);
                        if (rgb && twoColor) std::copy(red.data(), red.data() + n, &p->landscapeRed[at]);
                    }
                }
                if (rgb) p->inkIsOne = true;
            }
        } catch (const std::bad_alloc&) {
            return E_OUTOFMEMORY;
        }
        // The plug-in owns the output; tell Unidrv not to send this band itself.
        if (result) *result = reinterpret_cast<PBYTE>(TRUE);
        return S_OK;
    }

    STDMETHODIMP DriverDMS(PVOID, PVOID, DWORD, PDWORD) override { return E_NOTIMPL; }
    STDMETHODIMP FilterGraphics(PDEVOBJ, PBYTE, DWORD) override { return E_NOTIMPL; }
    STDMETHODIMP Compression(PDEVOBJ, PBYTE, PBYTE, DWORD, DWORD, INT*) override { return E_NOTIMPL; }
    STDMETHODIMP HalftonePattern(PDEVOBJ, PBYTE, DWORD, DWORD, DWORD, DWORD, PBYTE, DWORD) override {
        return E_NOTIMPL;
    }
    STDMETHODIMP MemoryUsage(PDEVOBJ, POEMMEMORYUSAGE) override { return E_NOTIMPL; }
    STDMETHODIMP TTYGetInfo(PDEVOBJ, DWORD, PVOID, DWORD, DWORD*) override { return E_NOTIMPL; }
    STDMETHODIMP DownloadFontHeader(PDEVOBJ, PUNIFONTOBJ, DWORD*) override { return E_NOTIMPL; }
    STDMETHODIMP DownloadCharGlyph(PDEVOBJ, PUNIFONTOBJ, HGLYPH, PDWORD, DWORD*) override {
        return E_NOTIMPL;
    }
    STDMETHODIMP TTDownloadMethod(PDEVOBJ, PUNIFONTOBJ, DWORD*) override { return E_NOTIMPL; }
    STDMETHODIMP OutputCharStr(PDEVOBJ, PUNIFONTOBJ, DWORD, DWORD, PVOID) override { return E_NOTIMPL; }
    STDMETHODIMP SendFontCmd(PDEVOBJ, PUNIFONTOBJ, PFINVOCATION) override { return E_NOTIMPL; }
    STDMETHODIMP TextOutAsBitmap(SURFOBJ*, STROBJ*, FONTOBJ*, CLIPOBJ*, RECTL*, RECTL*, BRUSHOBJ*,
                                 BRUSHOBJ*, POINTL*, MIX) override {
        return E_NOTIMPL;
    }

private:
    LONG refs_ = 1;
    IPrintOemDriverUni* helper_ = nullptr;
};

class QlFactory final : public IClassFactory {
public:
    STDMETHODIMP QueryInterface(REFIID riid, LPVOID* ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override {
        const ULONG r = InterlockedDecrement(&refs_);
        if (r == 0) delete this;
        return r;
    }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, LPVOID* ppv) override {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* obj = new (std::nothrow) QlRender();
        if (!obj) return E_OUTOFMEMORY;
        const HRESULT hr = obj->QueryInterface(riid, ppv);
        obj->Release();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL lock) override {
        if (lock) InterlockedIncrement(&g_locks); else InterlockedDecrement(&g_locks);
        return S_OK;
    }

private:
    LONG refs_ = 1;
};

}  // namespace

extern "C" BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(inst);
    return TRUE;
}

STDAPI DllCanUnloadNow() { return (g_objects == 0 && g_locks == 0) ? S_OK : S_FALSE; }

_Check_return_ STDAPI DllGetClassObject(_In_ REFCLSID clsid, _In_ REFIID riid, _Outptr_ LPVOID* ppv) {
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_OEMRENDER) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) QlFactory();
    if (!factory) return E_OUTOFMEMORY;
    const HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}
