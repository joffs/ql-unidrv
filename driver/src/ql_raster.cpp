#include "ql_raster.h"

#include <algorithm>
#include <bitset>
#include <cmath>

namespace ql {
namespace {

const Media kMedia[] = {
#include "media_table.inc"
};

struct MediaInfo {
    uint32_t models;  // bit (1 << ql::Model) for each model whose Brother driver sends this block
    uint16_t ptdId;
    uint8_t block[kMediaInfoBytes];
};
const MediaInfo kMediaInfo[] = {
#include "media_info.inc"
};

void Put(std::vector<uint8_t>& out, std::initializer_list<uint8_t> bytes) {
    out.insert(out.end(), bytes);
}

// series800, ESC i a, M, PackBits off USB, K 0x02 at 300 x 600, density + ESC i J,
// cutter, ESC i A, ESC i K, 300 x 600, two-colour, round top margin, min length, head dots, invalidate,
// ESC i a FF after the job
constexpr bool T = true, F = false;
constexpr int N = kHeadDots, W = kWideHeadDots;
struct ModelEntry {
    Model model;
    ModelTraits traits;
};
const ModelEntry kTraits[] = {
    {Model::QL500, {F, F, F, F, F, F, F, F, F, F, F, T, 250, N, 200}},
    {Model::QL550, {F, F, F, F, F, F, T, F, F, F, F, T, 250, N, 200}},
    {Model::QL560, {F, F, F, F, F, F, T, T, T, F, F, T, 250, N, 200}},
    {Model::QL570, {F, F, F, F, F, F, T, T, T, T, F, T, 127, N, 200}},
    {Model::QL580N, {F, T, T, T, F, F, T, T, T, T, F, T, 127, N, 200}},  // = QL-710W
    {Model::QL600, {F, T, F, F, F, F, T, T, T, T, F, T, 127, N, 200, T}},  // QL-700 without density / ESC i J
    {Model::QL650TD, {F, T, T, F, F, F, T, F, T, F, F, F, 250, N, 200}},
    {Model::QL700, {F, T, F, F, F, T, T, T, T, T, F, T, 127, N, 200}},
    {Model::QL710W, {F, T, T, T, F, F, T, T, T, T, F, T, 127, N, 200}},
    {Model::QL720NW, {F, T, T, T, F, F, T, T, T, T, F, T, 127, N, 200}},
    {Model::QL800, {T, T, T, F, F, F, T, T, T, T, T, T, 127, N, 400}},
    {Model::QL810W, {T, T, T, T, T, F, T, T, T, T, T, T, 127, N, 400}},
    {Model::QL820NWB, {T, T, T, T, T, F, T, T, T, T, T, T, 127, N, 400}},
    {Model::QL1050, {F, T, T, T, F, F, T, T, T, F, F, F, 250, W, 200}},
    {Model::QL1060N, {F, T, T, T, F, F, T, T, T, F, F, F, 250, W, 350}},  // writes the stream itself: 200 + 150
    // QL-1060N + ESC i !, round top margin, 25.4 mm minimum
    {Model::QL1100, {T, T, T, T, F, F, T, T, T, F, F, T, 254, W, 350}},
    {Model::QL1110NWB, {T, T, T, T, F, F, T, T, T, F, F, T, 254, W, 350}},
    {Model::QL1115NWB, {T, T, T, T, F, F, T, T, T, F, F, T, 254, W, 350}},
};

}  // namespace

const ModelTraits& Traits(Model model) {
    for (const ModelEntry& e : kTraits)
        if (e.model == model) return e.traits;
    for (const ModelEntry& e : kTraits)  // unknown value: the QL-800's, as before
        if (e.model == Model::QL800) return e.traits;
    return kTraits[0].traits;
}

size_t MediaCount() { return sizeof(kMedia) / sizeof(kMedia[0]); }

const Media* MediaByIndex(size_t index) { return index < MediaCount() ? &kMedia[index] : nullptr; }

const Media* MediaByPtdId(uint16_t ptdId, Model model) {
    const Media* first = nullptr;
    for (const Media& m : kMedia) {
        if (m.ptdId != ptdId) continue;
        if (ModelHasMedia(model, m)) return &m;
        if (!first) first = &m;
    }
    return first;
}

const Media* ContinuousForWidth(double widthMm, Model model) {
    // Compared in dots of the real paper width: the ESC i z byte would tie 103 mm between the 102 mm
    // (byte 102) and the 103 mm tape (byte 104).
    const double dots = widthMm * 300 / 25.4;
    // Between entries of the same width (103 mm on the QL-1050/1060N and on the QL-1100 series) the model's own.
    const Media* best = nullptr;
    for (const Media& m : kMedia) {
        if (m.kind != MediaKind::Continuous || m.strips != 1) continue;
        const double d = std::fabs(m.widthDots - dots), b = best ? std::fabs(best->widthDots - dots) : 0;
        if (!best || d < b || (d == b && ModelHasMedia(model, m) && !ModelHasMedia(model, *best))) best = &m;
    }
    return best;
}

const uint8_t* MediaInfoBlock(Model model, uint16_t ptdId) {
    for (const MediaInfo& i : kMediaInfo)
        if (i.ptdId == ptdId && (i.models >> static_cast<int>(model)) & 1) return i.block;
    return nullptr;
}

PageGeometry ComputePageGeometry(const Media& media, bool highRes, int surfaceLines, int surfaceTopDots,
                                 int length01, int labelTopDots, int minLength01) {
    const int scale = highRes ? 2 : 1;
    PageGeometry g;
    if (media.kind == MediaKind::DieCut) {
        g.lines = media.printableLines * scale;
        g.lineOffset = (surfaceTopDots - labelTopDots) * scale;
    } else if (length01 > 0) {
        const int ydpi = 300 * scale;
        const int length = std::max(length01, minLength01);
        g.lines = std::max((length * ydpi * 10 / 254 + 5) / 10 + 1 - 2 * labelTopDots * scale, 1);
        g.lineOffset = (surfaceTopDots - labelTopDots) * scale;
    } else {
        g.lines = surfaceLines;
    }
    return g;
}

void AppendPackBits(std::vector<uint8_t>& out, const uint8_t* line, int n) {
    const size_t start = out.size();
    int i = 0, lit = -1;  // lit: index of the open literal's count byte in out (relative to start)
    while (i < n) {
        int run = 1;
        while (i + run < n && run < 128 && line[i + run] == line[i]) ++run;
        if (run >= 2) {
            out.push_back(static_cast<uint8_t>(257 - run));
            out.push_back(line[i]);
            lit = -1;
            i += run;
        } else {
            if (lit < 0 || out[start + lit] == 127) {
                lit = static_cast<int>(out.size() - start);
                out.push_back(0xFF);  // becomes 0 with the first byte
            }
            ++out[start + lit];
            out.push_back(line[i++]);
        }
    }
    if (static_cast<int>(out.size() - start) > n) {
        out.resize(start);
        out.push_back(static_cast<uint8_t>(n - 1));
        out.insert(out.end(), line, line + n);
    }
}

void AppendJobHeader(std::vector<uint8_t>& out, Model model) {
    out.insert(out.end(), Traits(model).invalidateBytes, 0x00);  // invalidate
    Put(out, {0x1B, 0x40});            // ESC @ initialize
}

void AppendJobTrailer(std::vector<uint8_t>& out, Model model) {
    if (Traits(model).rasterOffAtEnd) Put(out, {0x1B, 0x69, 0x61, 0xFF});  // ESC i a FF (QL-600; meaning unknown)
}

void SeparateRgbLine(const uint8_t* rgb, int widthPx, uint8_t* black, uint8_t* red) {
    const int bytes = (widthPx + 7) / 8;
    std::fill(black, black + bytes, uint8_t(0));
    std::fill(red, red + bytes, uint8_t(0));
    for (int x = 0; x < widthPx; ++x, rgb += 3) {
        const Ink ink = ClassifyRgb(rgb[0], rgb[1], rgb[2]);
        if (ink == Ink::Black) black[x >> 3] |= static_cast<uint8_t>(0x80 >> (x & 7));
        else if (ink == Ink::Red) red[x >> 3] |= static_cast<uint8_t>(0x80 >> (x & 7));
    }
}

void Page::Reset(const Media& media, int sourceWidthPx, int lines, bool twoColor) {
    media_ = &media;
    sourceWidth_ = sourceWidthPx;
    lines_ = lines;
    stride_ = (media.printableDots * media.strips + 7) / 8;
    data_.assign(static_cast<size_t>(lines) * stride_, 0);
    if (twoColor) red_.assign(data_.size(), 0);
    else red_.clear();
}

void Page::SetSourceLine(int y, const uint8_t* src, bool inkIsOne, int srcWidthPx) {
    PlaceLine(data_, y, src, inkIsOne, srcWidthPx);
}

void Page::SetRedLine(int y, const uint8_t* src, int srcWidthPx) {
    if (!red_.empty()) PlaceLine(red_, y, src, true, srcWidthPx);
}

void Page::PlaceLine(std::vector<uint8_t>& plane, int y, const uint8_t* src, bool inkIsOne, int srcWidthPx) {
    if (y < 0 || y >= lines_ || !media_) return;
    uint8_t* line = &plane[static_cast<size_t>(y) * stride_];
    const int printable = media_->printableDots * media_->strips;
    const int crop = (sourceWidth_ - printable) / 2;  // centre the printable width (crop or pad)
    const int readable = std::min(sourceWidth_, srcWidthPx);
    for (int x = 0; x < printable; ++x) {
        const int sx = x + crop;
        if (sx < 0 || sx >= readable) continue;
        const bool bit = (src[sx >> 3] >> (7 - (sx & 7))) & 1;
        if (bit == inkIsOne) line[x >> 3] |= static_cast<uint8_t>(0x80 >> (x & 7));
    }
}

bool Page::LineBlank(int y) const {
    const size_t at = static_cast<size_t>(y) * stride_;
    for (int i = 0; i < stride_; ++i)
        if (data_[at + i] || (!red_.empty() && red_[at + i])) return false;
    return true;
}

void AppendPage(std::vector<uint8_t>& out, const JobSettings& s, const Page& page, uint16_t feedDots,
                bool firstPage, int strip) {
    const Media& media = page.GetMedia();
    const bool continuous = media.kind == MediaKind::Continuous;
    const int printable = media.printableDots;
    // Multi-up: this strip's first pixel in the page (rotate 180 also reverses the strip order).
    const int stripX = printable * (s.rotate180 ? media.strips - 1 - strip : strip);
    const ModelTraits& t = Traits(s.model);
    const bool packBits = t.packOffUsb && s.compress;
    const bool twoColor = s.twoColor && page.TwoColor() && t.twoColor;
    const bool highRes = s.highRes && t.highRes;
    const bool autoCut = s.autoCut && t.cutter;
    const int headDots = t.headDots, headBytes = headDots / 8, padStart = PadStart(media, s.model);

    // Output order of the source lines (rotate 180 reverses it); optionally drop trailing blanks.
    int lines = page.Lines();
    auto srcLine = [&](int i) { return s.rotate180 ? page.Lines() - 1 - i : i; };
    if (continuous && s.trimTrailingBlank)
        while (lines > 0 && page.LineBlank(srcLine(lines - 1))) --lines;
    const uint32_t count = static_cast<uint32_t>(lines);

    if (t.rasterModeCmd) Put(out, {0x1B, 0x69, 0x61, 0x01});  // ESC i a 01: raster mode
    if (t.series800) Put(out, {0x1B, 0x69, 0x21, 0x01});      // ESC i ! 01: automatic status notification off
    // ESC i U w: Brother's media block (QL-1115NWB direct-thermal papers, a few die-cut papers of the QL-700
    // series and the QL-800/820NWB), sent as captured for the model and paper
    if (const uint8_t* info = MediaInfoBlock(s.model, media.ptdId)) {
        Put(out, {0x1B, 0x69, 0x55, 0x77});
        out.insert(out.end(), info, info + kMediaInfoBytes);
    }
    // ESC i z: print information
    const uint8_t zFlags = static_cast<uint8_t>(0x80 | 0x04 | 0x02 | (continuous ? 0x00 : 0x08) |
                                                (s.qualityPriority ? 0x40 : 0x00));
    Put(out, {0x1B, 0x69, 0x7A, zFlags, static_cast<uint8_t>(media.kind), media.widthMm,
              continuous ? uint8_t(0) : media.lengthMm, static_cast<uint8_t>(count),
              static_cast<uint8_t>(count >> 8), static_cast<uint8_t>(count >> 16),
              static_cast<uint8_t>(count >> 24), static_cast<uint8_t>(firstPage && strip == 0 ? 0 : 1), 0x00});
    // ESC i M: various mode
    Put(out, {0x1B, 0x69, 0x4D,
              static_cast<uint8_t>((autoCut ? 0x40 : 0x00) | (s.mirror ? 0x80 : 0x00))});
    if (autoCut && t.cutEveryCmd) Put(out, {0x1B, 0x69, 0x41, s.cutEvery ? s.cutEvery : uint8_t(1)});  // ESC i A n
    // ESC i K: expanded mode. QL-810W/820NWB: 0x02 = top margin below 3 mm, i.e. in 300 x 600 mode
    // (feed 70 lines < 71), except for the round 12 / 24 mm labels.
    const bool smallMargin = t.highResMargin && highRes && media.ptdId != 362 && media.ptdId != 363;
    if (t.expandedCmd)
        Put(out, {0x1B, 0x69, 0x4B,
                  static_cast<uint8_t>((twoColor ? 0x01 : 0x00) | (s.cutAtEnd ? 0x08 : 0x00) |
                                       (highRes ? 0x40 : 0x00) | (smallMargin ? 0x02 : 0x00))});
    // ESC i d: feed margin in dots (continuous; the 12 mm round label 35), doubled in 300 x 600 mode
    const int scale = highRes ? 2 : 1;
    const uint16_t margin = continuous                                  ? static_cast<uint16_t>(feedDots * scale)
                            : media.ptdId == 362 && t.roundTopMargin ? static_cast<uint16_t>(35 * scale)
                                                                       : uint16_t(0);
    Put(out, {0x1B, 0x69, 0x64, static_cast<uint8_t>(margin), static_cast<uint8_t>(margin >> 8)});
    if (t.lineDensity) Put(out, {0x1B, 0x69, 0x4A, 0x01});  // ESC i J 01 (QL-700; meaning unknown)
    if (t.compressionCmd) Put(out, {0x4D, static_cast<uint8_t>(packBits ? 0x02 : 0x00)});  // M: compression (02 = PackBits)

    // Raster lines: g 00 5A + 90 bytes (wide head: g 00 A2 + 162) = pad-start zero bits, printable
    // pixels, zero fill.
    // Normally the pixels go right-to-left (the head prints the line mirrored); with mirror
    // printing the printer mirrors, so they go left-to-right. Rotate 180 flips both orders.
    const bool reversePixels = s.mirror == s.rotate180;
    // With PackBits, blank lines are sent as Z from line (shortest length in dots - 2 * feed) on, x2 at
    // 300 x 600; earlier blank lines as packed g lines. Shortest length 12.7 mm = 150 dots (80 at feed
    // 35), QL-1050/1060N 25 mm = 295 (225). The
    // feed is Brother's DEVMODE feed: the continuous feed, 35 (the default) for die-cut papers.
    const int minDots = (t.minLength01 * 3000 / 254 + 5) / 10;
    const int zFrom = scale * (minDots - 2 * (continuous ? feedDots : 35));
    out.reserve(out.size() + count * (twoColor ? 2 : 1) * (3 + headBytes) + 1);
    uint8_t line[kMaxHeadBytes], red[kMaxHeadBytes];
    auto blank = [&](const uint8_t* l) { return std::all_of(l, l + headBytes, [](uint8_t b) { return b == 0; }); };
    // Two-colour: 'w 01' (black) + 'w 02' (red) per line, packed like g lines; a line blank in both
    // planes is a Z at any position when packed.
    auto plane = [&](uint8_t n, const uint8_t* l) {
        if (!packBits) {
            Put(out, {0x77, n, static_cast<uint8_t>(headBytes)});
            out.insert(out.end(), l, l + headBytes);
            return;
        }
        Put(out, {0x77, n, 0x00});
        const size_t len = out.size() - 1;
        AppendPackBits(out, l, headBytes);
        out[len] = static_cast<uint8_t>(out.size() - len - 1);
    };
    for (int i = 0; i < lines; ++i) {
        const int y = srcLine(i);
        std::fill(line, line + headBytes, uint8_t(0));
        std::fill(red, red + headBytes, uint8_t(0));
        for (int x = 0; x < printable; ++x) {
            const bool k = page.Pixel(y, stripX + x), r = twoColor && page.RedPixel(y, stripX + x);
            if (!k && !r) continue;
            const int dot = padStart + (reversePixels ? printable - 1 - x : x);
            if (dot < 0 || dot >= headDots) continue;
            if (k) line[dot >> 3] |= static_cast<uint8_t>(0x80 >> (dot & 7));
            if (r) red[dot >> 3] |= static_cast<uint8_t>(0x80 >> (dot & 7));
        }
        if (twoColor) {
            if (packBits && blank(line) && blank(red)) {
                out.push_back(0x5A);
            } else {
                plane(0x01, line);
                plane(0x02, red);
            }
        } else if (!packBits) {
            Put(out, {0x67, t.lineDensity ? Ql700LineDensity(line) : uint8_t(0), static_cast<uint8_t>(headBytes)});
            out.insert(out.end(), line, line + headBytes);
        } else if (i >= zFrom && blank(line)) {
            out.push_back(0x5A);  // Z: blank line
        } else {
            Put(out, {0x67, 0x00, 0x00});
            const size_t len = out.size() - 1;
            AppendPackBits(out, line, headBytes);
            out[len] = static_cast<uint8_t>(out.size() - len - 1);
        }
    }
}

uint8_t Ql700LineDensity(const uint8_t* line) {
    auto dots = [&](int k) {  // black dots in the k-th 18-byte block from the end
        int n = 0;
        for (int i = kHeadBytes - 18 * (k + 1); i < kHeadBytes - 18 * k; ++i) n += static_cast<int>(std::bitset<8>(line[i]).count());
        return n;
    };
    int flags = 0, count = 0, acc = dots(0);
    for (int k = 1; k < 5; ++k) {
        const int d = dots(k);
        if (acc + d > 144) {
            flags |= 1 << (k - 1);
            ++count;
            acc = d;
        } else {
            acc += d;
        }
    }
    return static_cast<uint8_t>((flags << 4) ^ (count & 0x0F));
}

}  // namespace ql
