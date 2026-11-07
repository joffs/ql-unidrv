// Replays Brother's reference captures through our encoder.
//
// For every capture listed below: decode the raster lines back into label pixels, re-encode them
// with ql::AppendPage using the settings of each capture, and require the result
// to be byte-identical to the capture (Brother's "ESC i U J" job-id block removed).
//
// The QL-810W / QL-820NWB captures (PackBits, Z) are decoded the same way and re-encoded with
// that model's settings.
//
// Two-colour captures ('w 01' / 'w 02' lines, QL-810W / QL-820NWB): Brother packs some lines
// differently from run to run, so when the bytes differ, the commands and
// the decoded black and red planes must match instead ("same planes").
//
// The QL-570/560/550/500/650TD captures (reference/captures/ql570/*.prn etc.) start at the first page
// command: Brother's language monitor sends the invalidate + ESC @ (+ a status request), so they are
// compared without our job header. The QL-700/710W/720NW drivers write the job header themselves, like the
// QL-800's.
//
// usage: replay_test <captures dir> [500|550|560|570|580|600|650|700|710|720|800|810|820|1050|1060|1100|1110|1115]
//        [output dir [usb|net [quality]]]
// With an output dir, every re-encoded stream is also written there (with the job header), e.g. to send
// it RAW to a real printer: the form our driver writes on a non-USB port ("net" or nothing), or with "usb"
// the form it writes on a USB port (no compression on any model). "quality" writes them with print priority
// Quality (ESC i z bit 6, the driver's "Print priority" option; slower, sharper bars); the comparison with
// Brother's captures is unchanged. tools/testkit/make-testkit.ps1 uses this.
#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

#include "../src/ql_raster.h"

namespace {

struct Case {
    const char* file;
    uint16_t ptdId;
    ql::JobSettings settings;
    bool twoColor = false;
};

ql::JobSettings S(bool autoCut = true, int cutEvery = 1, bool cutAtEnd = true, bool highRes = false,
                  bool mirror = false) {
    ql::JobSettings s;
    s.autoCut = autoCut;
    s.cutEvery = static_cast<uint8_t>(cutEvery);
    s.cutAtEnd = cutAtEnd;
    s.highRes = highRes;
    s.mirror = mirror;
    return s;
}

const Case kCases[] = {
    {"62mm-continuous_default-length.prn", 259, S()},
    {"62mm-continuous_100mm-custom.prn", 259, S()},
    {"29x90-diecut.prn", 271, S()},
    {"29x90-diecut_portrait.prn", 271, S()},
    {"29x90_userformat-317.prn", 271, S()},
    {"62mm_userformat-332-DHL-180mm.prn", 259, S()},
    {"62mm_autocut-off.prn", 259, S(false)},
    {"62mm_cut-every-2.prn", 259, S(true, 2)},
    {"62mm_cut-at-end.prn", 259, S(false, 1, true)},
    {"62mm_cut-at-end-off.prn", 259, S(false, 1, false)},
    {"62mm_highres.prn", 259, S(true, 1, true, true)},
    {"62mm_2pages.prn", 259, S()},
    {"62mm_copies-2.prn", 259, S()},
    {"62mm_landscape.prn", 259, S()},
    {"62mm_mirror.prn", 259, S(true, 1, true, false, true)},
    // QL-810W / QL-820NWB only: Brother's factory list, blank-line (Z) rule, red content in mono
    {"62mm_userformat-332-209mm.prn", 259, S()},
    {"62mm_mark_z-rule.prn", 259, S()},
    {"62mm_mark_z-rule_highres.prn", 259, S(true, 1, true, true)},
    {"29x90_mark_z-rule.prn", 271, S()},
    {"62mm_red-content_mono.prn", 259, S()},
    // QL-570 and older (2026-10-17): 12 mm round (ESC i d 35, QL-650TD 0), trim (lines already dropped), 600 x 600
    {"12mm-dia_diecut.prn", 362, S()},
    {"62mm_mark_trim-trailing.prn", 259, S()},
    {"62mm_highres_600x600.prn", 259, S(true, 1, true, true)},
    // QL-810W / QL-820NWB two-colour (DK-22251)
    {"62mm_2color.prn", 259, S(), true},
    {"62mm_2color_highres.prn", 259, S(true, 1, true, true), true},
    {"62mm_2color_mark.prn", 259, S(), true},
    {"29x90_2color_forced.prn", 271, S(), true},
    // QL-1050 / QL-1060N (2026-10-17): wide papers, Brother's 72-dot default feed, factory formats 332 / 334
    {"102mm-continuous_default-length.prn", 260, S()},
    {"102mm-continuous_100mm-custom.prn", 260, S()},
    {"103mm-continuous.prn", 265, S()},
    {"102x152-diecut.prn", 366, S()},
    {"102x152_userformat-332.prn", 366, S()},
    {"102x51-diecut.prn", 365, S()},
    {"103x164-diecut.prn", 385, S()},
    {"62mm-continuous_default-length_feed-72.prn", 259, S()},
    {"62mm_userformat-334-209mm.prn", 259, S()},
    // QL-1100 / QL-1110NWB / QL-1115NWB (2026-10-21): new papers, 103 mm (shipping papers 386/387 are sent as
    // 385/265), factory formats 328/330/331, direct-thermal papers (QL-1115NWB, ESC i U w)
    {"29x42-diecut.prn", 358, S()},
    {"60x86-diecut.prn", 383, S()},
    {"103x164-shipping-diecut.prn", 385, S()},
    {"103mm-shipping-continuous.prn", 265, S()},
    {"103x164_userformat-328.prn", 385, S()},
    {"103x211_userformat-331.prn", 265, S()},
    {"102x152_userformat-328.prn", 366, S()},
    {"62mm_userformat-330-209mm.prn", 259, S()},
    {"dt90mm-continuous.prn", 465, S()},
    {"dt102mm-continuous.prn", 466, S()},
    {"dt102x51-diecut.prn", 467, S()},
    {"dt102x152-diecut.prn", 468, S()},
    // Test kit (2026-11-04, reference/captures/testkit): one label for every further single-label paper of a model
    {"12mm-continuous_default-length.prn", 257, S()},
    {"29mm-continuous_default-length.prn", 258, S()},
    {"38mm-continuous_default-length.prn", 264, S()},
    {"50mm-continuous_default-length.prn", 262, S()},
    {"54mm-continuous_default-length.prn", 261, S()},
    {"17x54-diecut.prn", 269, S()},
    {"17x87-diecut.prn", 270, S()},
    {"23x23-diecut.prn", 370, S()},
    {"38x90-diecut.prn", 272, S()},
    {"39x48-diecut.prn", 367, S()},
    {"52x29-diecut.prn", 374, S()},
    {"54x29-diecut.prn", 382, S()},
    {"62x29-diecut.prn", 274, S()},
    {"62x60-diecut.prn", 388, S()},
    {"62x75-diecut.prn", 389, S()},
    {"62x100-diecut.prn", 275, S()},
    {"24mm-dia_diecut.prn", 363, S()},
    {"58mm-dia_diecut.prn", 273, S()},
    {"dt-90mm-continuous_default-length.prn", 465, S()},
    {"dt-102mm-continuous_default-length.prn", 466, S()},
    {"dt-102x51-diecut.prn", 467, S()},
    {"dt-102x152-diecut.prn", 468, S()},
};

std::vector<uint8_t> ReadFile(const std::string& path) {
    std::ifstream f(path, std::ios::binary);
    return std::vector<uint8_t>(std::istreambuf_iterator<char>(f), {});
}

using Lines = std::vector<std::vector<uint8_t>>;

// Parsed capture: pages of raw 90-byte head lines (black; two-colour also red), the stream without
// ESC i U J blocks, and its commands without the raster lines.
struct Capture {
    std::vector<Lines> pages, redPages;
    std::vector<uint8_t> normalized, commands;
    std::string error;
    uint16_t feed = 35;  // continuous feed from ESC i d (die-cut papers send 0)
};

Capture Parse(const std::vector<uint8_t>& b, int headBytes) {
    Capture c;
    Lines page, red;
    bool packBits = false, twoColor = false;
    size_t i = 0;
    auto copy = [&](size_t n, bool raster = false) {
        c.normalized.insert(c.normalized.end(), b.begin() + i, b.begin() + i + n);
        if (!raster) c.commands.insert(c.commands.end(), b.begin() + i, b.begin() + i + n);
        i += n;
    };
    auto unpack = [&](size_t from, size_t n) {
        std::vector<uint8_t> line;
        for (size_t k = from, end = from + n; k < end;) {
            const int c8 = static_cast<int8_t>(b[k++]);
            if (c8 >= 0) {
                line.insert(line.end(), b.begin() + k, b.begin() + k + c8 + 1);
                k += c8 + 1;
            } else {
                line.insert(line.end(), static_cast<size_t>(1 - c8), b[k++]);
            }
        }
        line.resize(static_cast<size_t>(headBytes));
        return line;
    };
    while (i < b.size() && b[i] == 0) copy(1);
    while (i < b.size()) {
        const uint8_t x = b[i];
        if (x == 0x67 || x == 0x77) {
            const size_t n = b[i + 2];
            std::vector<uint8_t> line = packBits ? unpack(i + 3, n)
                                                 : std::vector<uint8_t>(b.begin() + i + 3, b.begin() + i + 3 + n);
            (x == 0x77 && b[i + 1] == 0x02 ? red : page).push_back(line);
            copy(3 + n, true);
        } else if (x == 0x5A) {
            page.emplace_back(static_cast<size_t>(headBytes), uint8_t(0));
            if (twoColor) red.emplace_back(static_cast<size_t>(headBytes), uint8_t(0));
            copy(1, true);
        } else if (x == 0x0C || x == 0x1A) {
            c.pages.push_back(page);
            c.redPages.push_back(red);
            page.clear();
            red.clear();
            copy(1);
        } else if (x == 0x4D) {
            packBits = b[i + 1] == 0x02;
            copy(2);
        } else if (x == 0x1B && b[i + 1] == 0x40) {
            copy(2);
        } else if (x == 0x1B && b[i + 1] == 0x69) {
            switch (b[i + 2]) {
            case 0x55:
                if (b[i + 3] == 0x77) copy(4 + ql::kMediaInfoBytes);  // ESC i U w 01 <127 bytes>: kept
                else i += 6 + b[i + 5];                              // ESC i U J 00 len <len bytes>: dropped
                break;
            case 0x7A: copy(13); break;
            case 0x64:
                if (b[i + 3] | b[i + 4]) c.feed = static_cast<uint16_t>(b[i + 3] | b[i + 4] << 8);
                copy(5);
                break;
            case 0x4B: twoColor = (b[i + 3] & 0x01) != 0; copy(4); break;
            default: copy(4); break;              // ESC i a / ! / M / A / K (QL-600: ESC i a FF after the job)
            }
        } else {
            c.error = "unknown byte at " + std::to_string(i);
            return c;
        }
    }
    return c;
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 2) {
        std::printf("usage: replay_test <captures dir> [500|550|560|570|580|600|650|700|710|720|800|810|820|1050|1060|1100|1110|1115] [output dir [usb|net [quality]]]\n");
        return 2;
    }
    const std::string modelArg = argc > 2 ? argv[2] : "800";
    const bool usbForm = argc > 4 && std::string(argv[4]) == "usb";
    const bool quality = argc > 5 && std::string(argv[5]) == "quality";
    const ql::Model model = modelArg == "500"    ? ql::Model::QL500
                            : modelArg == "550"  ? ql::Model::QL550
                            : modelArg == "560"  ? ql::Model::QL560
                            : modelArg == "570"  ? ql::Model::QL570
                            : modelArg == "580"  ? ql::Model::QL580N
                            : modelArg == "600"  ? ql::Model::QL600
                            : modelArg == "650"  ? ql::Model::QL650TD
                            : modelArg == "700"  ? ql::Model::QL700
                            : modelArg == "710"  ? ql::Model::QL710W
                            : modelArg == "720"  ? ql::Model::QL720NW
                            : modelArg == "810"  ? ql::Model::QL810W
                            : modelArg == "820"  ? ql::Model::QL820NWB
                            : modelArg == "1050" ? ql::Model::QL1050
                            : modelArg == "1060" ? ql::Model::QL1060N
                            : modelArg == "1100" ? ql::Model::QL1100
                            : modelArg == "1110" ? ql::Model::QL1110NWB
                            : modelArg == "1115" ? ql::Model::QL1115NWB
                                                 : ql::Model::QL800;
    int failures = 0, run = 0;
    for (const Case& tc : kCases) {
        const std::vector<uint8_t> raw = ReadFile(std::string(argv[1]) + "\\" + tc.file);
        if (raw.empty()) {
            std::printf("SKIP %s (missing)\n", tc.file);
            continue;
        }
        ++run;
        const int headBytes = ql::Traits(model).headDots / 8;
        const Capture cap = Parse(raw, headBytes);
        const ql::Media* m = ql::MediaByPtdId(tc.ptdId, model);
        if (!cap.error.empty() || !m) {
            std::printf("FAIL %s: %s\n", tc.file, cap.error.empty() ? "unknown media" : cap.error.c_str());
            ++failures;
            continue;
        }
        // Rebuild label pixels from the head lines, then re-encode.
        std::vector<uint8_t> headerBytes;
        ql::AppendJobHeader(headerBytes, model);
        const size_t header = headerBytes.size();
        const bool reverse = !tc.settings.mirror;
        const int stride = (m->printableDots + 7) / 8;
        auto toSource = [&](const std::vector<uint8_t>& head, std::vector<uint8_t>& src) {
            std::fill(src.begin(), src.end(), uint8_t(0));
            for (int x = 0; x < m->printableDots; ++x) {
                const int dot = ql::PadStart(*m, model) + (reverse ? m->printableDots - 1 - x : x);
                if ((head[dot >> 3] >> (7 - (dot & 7))) & 1) src[x >> 3] |= uint8_t(0x80 >> (x & 7));
            }
        };
        // The whole job as our driver writes it on a non-USB port (compress) or on USB.
        auto encode = [&](bool compress, bool qualityPriority) {
            std::vector<uint8_t> job = headerBytes;
            for (size_t p = 0; p < cap.pages.size(); ++p) {
                const auto& lines = cap.pages[p];
                const auto& red = cap.redPages[p];
                ql::Page page;
                page.Reset(*m, m->printableDots, static_cast<int>(lines.size()), tc.twoColor);
                std::vector<uint8_t> src(stride);
                for (size_t y = 0; y < lines.size(); ++y) {
                    toSource(lines[y], src);
                    page.SetSourceLine(static_cast<int>(y), src.data(), true);
                    if (tc.twoColor && y < red.size()) {
                        toSource(red[y], src);
                        page.SetRedLine(static_cast<int>(y), src.data());
                    }
                }
                ql::JobSettings settings = tc.settings;
                settings.twoColor = tc.twoColor;
                settings.model = model;
                settings.compress = compress;
                if (qualityPriority) settings.qualityPriority = true;
                const bool scaled = settings.highRes && ql::Traits(model).highRes;
                ql::AppendPage(job, settings, page, static_cast<uint16_t>(scaled ? cap.feed / 2 : cap.feed), p == 0);
                job.push_back(p + 1 == cap.pages.size() ? ql::kLastPage : ql::kNextPage);
            }
            ql::AppendJobTrailer(job, model);
            return job;
        };
        std::vector<uint8_t> out = encode(true, false);  // captured on file ports: Brother's network form
        if (argc > 3) {
            const std::vector<uint8_t> written = usbForm || quality ? encode(!usbForm, quality) : out;
            std::ofstream f(std::string(argv[3]) + "\\" + tc.file, std::ios::binary);
            f.write(reinterpret_cast<const char*>(written.data()), static_cast<std::streamsize>(written.size()));
        }
        if (raw[0] != 0) out.erase(out.begin(), out.begin() + static_cast<std::ptrdiff_t>(header));
        size_t diff = 0;
        while (diff < out.size() && diff < cap.normalized.size() && out[diff] == cap.normalized[diff]) ++diff;
        const Capture ours = tc.twoColor ? Parse(out, headBytes) : Capture();
        if (diff == out.size() && diff == cap.normalized.size()) {
            std::printf("OK   %-40s %zu pages, %zu bytes\n", tc.file, cap.pages.size(), out.size());
        } else if (tc.twoColor && ours.error.empty() && ours.commands == cap.commands && ours.pages == cap.pages &&
                   ours.redPages == cap.redPages) {
            std::printf("OK   %-40s %zu pages, same commands and planes (Brother's packing differs)\n",
                        tc.file, cap.pages.size());
        } else {
            ++failures;
            std::printf("FAIL %-40s first difference at byte %zu (ours %zu bytes, capture %zu bytes)\n", tc.file,
                        diff, out.size(), cap.normalized.size());
            for (size_t k = diff > 8 ? diff - 8 : 0; k < diff + 8; ++k)
                std::printf("  %06zx  ours %s  capture %s\n", k,
                            k < out.size() ? (std::to_string(out[k])).c_str() : "-",
                            k < cap.normalized.size() ? (std::to_string(cap.normalized[k])).c_str() : "-");
        }
    }
    std::printf(failures ? "QL-%s: %d of %d captures FAILED\n" : "QL-%s: all %d captures reproduced (byte-for-byte; two-colour: same planes)\n",
                modelArg.c_str(), failures ? failures : run, run);
    return failures ? 1 : 0;
}
