#include "labels.h"

#include <windows.h>

namespace ql {
namespace {

std::wstring Utf8ToWide(const char* s, int n) {
    if (n <= 0) return std::wstring();
    const int len = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s, n, nullptr, 0);
    if (len <= 0) return std::wstring();
    std::wstring w(static_cast<size_t>(len), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s, n, &w[0], len);
    return w;
}

bool ParseInt(const std::string& s, long lo, long hi, long* out) {
    if (s.empty() || s.size() > 9) return false;
    long v = 0;
    for (char c : s) {
        if (c < '0' || c > '9') return false;
        v = v * 10 + (c - '0');
    }
    if (v < lo || v > hi) return false;
    *out = v;
    return true;
}

}  // namespace

std::vector<LabelFormat> ParseLabelsTsv(const std::string& utf8) {
    std::vector<LabelFormat> formats;
    size_t pos = 0;
    if (utf8.compare(0, 3, "\xEF\xBB\xBF") == 0) pos = 3;
    while (pos < utf8.size()) {
        size_t end = utf8.find('\n', pos);
        if (end == std::string::npos) end = utf8.size();
        std::string line = utf8.substr(pos, end - pos);
        pos = end + 1;
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty() || line[0] == '#') continue;

        std::vector<std::string> cols;
        size_t start = 0;
        for (;;) {
            const size_t tab = line.find('\t', start);
            cols.push_back(line.substr(start, tab == std::string::npos ? std::string::npos : tab - start));
            if (tab == std::string::npos) break;
            start = tab + 1;
        }
        long paper = 0, length = 0, feed = 0;
        if (cols.size() < 4 || !ParseInt(cols[1], 1, 65535, &paper) || !ParseInt(cols[2], 0, 100000, &length) ||
            !ParseInt(cols[3], 0, 1000, &feed))
            continue;
        LabelFormat f;
        f.name = Utf8ToWide(cols[0].data(), static_cast<int>(cols[0].size()));
        if (f.name.empty() || f.name.size() > 31) continue;  // dmFormName holds 31 characters + NUL
        f.paperId = static_cast<uint16_t>(paper);
        f.length01 = static_cast<int>(length);
        f.feedDots = static_cast<uint16_t>(feed);
        formats.push_back(f);
    }
    return formats;
}

std::vector<LabelFormat> LoadLabelsTsv(const wchar_t* path) {
    HANDLE h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
                           OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return {};
    std::string data;
    LARGE_INTEGER size;
    if (GetFileSizeEx(h, &size) && size.QuadPart > 0 && size.QuadPart <= 1024 * 1024) {
        data.resize(static_cast<size_t>(size.QuadPart));
        DWORD read = 0;
        if (!ReadFile(h, &data[0], static_cast<DWORD>(data.size()), &read, nullptr)) read = 0;
        data.resize(read);
    }
    CloseHandle(h);
    return ParseLabelsTsv(data);
}

const LabelFormat* FindLabelFormat(const std::vector<LabelFormat>& formats, const wchar_t* name) {
    if (!name || !*name) return nullptr;
    const int len = lstrlenW(name);
    for (const LabelFormat& f : formats)
        if (CompareStringOrdinal(f.name.c_str(), static_cast<int>(f.name.size()), name, len, TRUE) == CSTR_EQUAL)
            return &f;
    return nullptr;
}

}  // namespace ql
