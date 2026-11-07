// User label formats at print time (labels v2): the label tool writes every format as a Windows
// form plus one line in C:\ProgramData\QLUnidrv\labels.tsv; the plug-in looks up the selected form
// (DEVMODE dmFormName) there to get the Brother media, length and feed.
//
// labels.tsv, UTF-8 (optional BOM), one format per line, '#' starts a comment line:
//   <form name> TAB <Brother paper id> TAB <length in 0.1 mm> TAB <feed in 300-dpi dots>
// The first line is "# QLUnidrv labels 1" (a comment line; not checked).
#pragma once
#include <cstdint>
#include <string>
#include <vector>

namespace ql {

struct LabelFormat {
    std::wstring name;
    uint16_t paperId = 0;   // Brother PTD paper id (media_table.inc)
    int length01 = 0;       // label length in 0.1 mm (used for continuous media)
    uint16_t feedDots = 0;  // continuous: feed margin (ESC i d); die-cut: the paper's top offset
};

constexpr wchar_t kLabelsTsvPath[] = L"C:\\ProgramData\\QLUnidrv\\labels.tsv";

// Malformed lines are skipped.
std::vector<LabelFormat> ParseLabelsTsv(const std::string& utf8);
// Reads and parses the file; empty if it is missing, unreadable or larger than 1 MB.
std::vector<LabelFormat> LoadLabelsTsv(const wchar_t* path = kLabelsTsvPath);
// Form names compare case-insensitively, like the Windows forms database.
const LabelFormat* FindLabelFormat(const std::vector<LabelFormat>& formats, const wchar_t* name);

}  // namespace ql
