// QL-800 raster protocol encoder. Pure C++ (no Windows print headers) so it
// can be unit-tested and reused by tools.
#pragma once
#include <climits>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace ql {

enum class MediaKind : uint8_t { Continuous = 0x0A, DieCut = 0x0B };

// The QL-810W and QL-820NWB get the same commands as the QL-800, except: ESC i K has bit 0x02 in
// 300 x 600 mode, and on non-USB ports (network, file) the raster lines are PackBits-compressed
// (M 02, blank lines as Z) as Brother's driver does it.
// The QL-570 gets the QL-800's commands without ESC i a, ESC i ! and M, never compressed, and a
// 200-byte invalidate. The QL-560/550/500/650TD are QL-570s with fewer
// commands. The QL-700/710W/720NW are QL-570s with ESC i a and a few
// extras; the QL-580N gets the QL-710W's commands. The QL-1050/1060N have a 1296-dot head (162-byte raster lines). The QL-1100/1110NWB/1115NWB are QL-1060Ns with ESC i ! and their own 103 mm
// geometry, the QL-1115NWB sends ESC i U w on direct-thermal papers. The
// QL-600 is a QL-700 without the density byte and ESC i J, with ESC i a FF after the job.
// ModelTraits says which.
// Listed by model number; the values are fixed IDs: StartDoc callback 40 + value (the QL-800's is 1) in
// the GPDs and the bit in Media::models (MODELS in gen/media.py). Never renumber.
enum class Model : uint8_t {
    QL500 = 6, QL550 = 5, QL560 = 4, QL570 = 3, QL580N = 13, QL600 = 17, QL650TD = 7, QL700 = 8, QL710W = 9,
    QL720NW = 10, QL800 = 0, QL810W = 1, QL820NWB = 2, QL1050 = 11, QL1060N = 12, QL1100 = 14, QL1110NWB = 15,
    QL1115NWB = 16
};

// What a model's stream contains, from Brother's drivers and captures.
struct ModelTraits {
    bool series800;      // QL-800/810W/820NWB: ESC i ! 01
    bool rasterModeCmd;  // ESC i a 01 (series 800, QL-650TD, QL-700 series)
    bool compressionCmd; // M 00/02 after ESC i d (series 800, QL-650TD, QL-710W/720NW)
    bool packOffUsb;     // PackBits (M 02, Z) on non-USB ports (QL-810W/820NWB, QL-710W/720NW)
    bool highResMargin;  // ESC i K 0x02 in 300 x 600 mode (QL-810W/820NWB; feed 70 < 71 dots)
    bool lineDensity;    // QL-700: the byte after 'g' is a density value, ESC i J 01
    bool cutter;         // ESC i M bit 6 (auto cut); the QL-500 has a manual cutter only
    bool cutEveryCmd;    // ESC i A n
    bool expandedCmd;    // ESC i K (cut at end, 300 x 600, two-colour)
    bool highRes;        // 300 x 600 dpi
    bool twoColor;       // red/black DK-22251
    bool roundTopMargin; // ESC i d 35 for the 12 mm round label (QL-650TD: 0 like every die-cut label)
    int minLength01;     // shortest continuous label, 0.1 mm
    int headDots;        // print head: 720 dots (90-byte raster lines); QL-1050/1060N 1296 (162 bytes)
    int invalidateBytes; // zero bytes before ESC @: series 800 400, QL-1060N/1100 series 350 (200 + 150), else 200
    bool rasterOffAtEnd; // ESC i a FF after the job's last page (QL-600)
};
const ModelTraits& Traits(Model model);

// One entry of Brother's PTD paper table, generated into media_table.inc.
struct Media {
    uint16_t ptdId;            // Brother paper id (dmPaperSize of the original driver)
    uint8_t widthMm;           // ESC i z n3
    uint8_t lengthMm;          // ESC i z n4 (0 for continuous)
    MediaKind kind;            // ESC i z n2
    uint16_t printableDots;    // data bits per raster line (GDI HORZRES; multi-up: per strip)
    uint16_t printableLines;   // die-cut: raster lines at 300 dpi; continuous: 0
    uint16_t padStartBits;     // zero bits before the printable pixels in each raster line
    uint8_t strips;            // multi-up ("62mm x 2"): the page is this many tapes wide, sent as
                               // one label per strip, left strip first; 1 for normal papers
    uint32_t models;           // bit (1 << ql::Model) set for each model whose paper table has it
    uint16_t padStartWide;     // padStartBits on the 1296-dot head (QL-1050/1060N/1100 series)
    uint16_t widthDots;        // paper width at 300 dpi (widthMm is the ESC i z byte: 103 mm tape sends 104)
};

size_t MediaCount();
const Media* MediaByIndex(size_t index);
// A paper id can have several entries when models place it differently on the head (103 mm on the
// QL-1050/1060N and on the QL-1100 series): the model's own entry first, else the first one.
const Media* MediaByPtdId(uint16_t ptdId, Model model = Model::QL800);
// Continuous roll (not multi-up) whose paper width is closest to widthMm (also one the model lacks: a
// paper it doesn't have prints on tape of the same width); of equal widths, the model's own entry.
const Media* ContinuousForWidth(double widthMm, Model model = Model::QL800);
// ESC i U w payload (128 bytes: 01 + Brother's 127-byte media block) of a direct-thermal paper
// (QL-1115NWB), as Brother's driver sends it; nullptr for other papers.
const uint8_t* MediaInfoBlock(Model model, uint16_t ptdId);
constexpr int kMediaInfoBytes = 128;

struct JobSettings {
    Model model = Model::QL800;
    bool compress = false;         // models with packOffUsb: M 02 PackBits (Brother: non-USB ports)
    bool autoCut = true;           // ESC i M bit 6, ESC i A
    uint8_t cutEvery = 1;          // ESC i A n (1..99)
    bool cutAtEnd = true;          // ESC i K bit 3
    bool highRes = false;          // 300 x 600 dpi: ESC i K bit 6, feed margin x2
    bool qualityPriority = false;  // ESC i z n1 bit 6
    bool mirror = false;           // ESC i M bit 7, pixels in natural order
    bool trimTrailingBlank = false;  // continuous only: drop blank lines at the end
    bool rotate180 = false;        // reverse line and pixel order
    bool twoColor = false;         // red/black DK-22251 (series 800 only): ESC i K bit 0, 'w 01' + 'w 02' lines;
                                   // the page must have been Reset with twoColor
};

// Two-colour separation of one RGB pixel: the nearest of white, black and pure red.
enum class Ink : uint8_t { None, Black, Red };
inline Ink ClassifyRgb(int r, int g, int b) {
    const int white = (255 - r) * (255 - r) + (255 - g) * (255 - g) + (255 - b) * (255 - b);
    const int black = r * r + g * g + b * b;
    const int red = (255 - r) * (255 - r) + g * g + b * b;
    if (white <= black && white <= red) return Ink::None;
    return red < black ? Ink::Red : Ink::Black;
}

// One 24 bpp scanline in R,G,B byte order (as Unidrv hands it over; measured 2026-10-17, not BGR) -> a black and a red 1 bpp line (MSB first,
// 1 = ink), widthPx pixels each.
void SeparateRgbLine(const uint8_t* rgb, int widthPx, uint8_t* black, uint8_t* red);

// Raster lines of a page and where the rendered surface lands on the label.
struct PageGeometry {
    int lines = 0;       // raster lines (ESC i z)
    int lineOffset = 0;  // label line = surface line + lineOffset (in device lines)
};

constexpr int kMinContinuousLength01 = 127;  // Brother's shortest continuous label, 12.7 mm (QL-500/550/560/650TD: 25 mm)

// surfaceLines: height of the rendered page in device lines; surfaceTopDots: its top margin at
// 300 dpi (GPD paper: its printable origin; custom size: 35). length01: continuous label length
// in 0.1 mm, 0 = the surface is the label (a GPD paper size). labelTopDots: the label's own top
// margin at 300 dpi (continuous: the feed margin; die-cut: the paper's top offset).
// Continuous lengths use Brother's line count, round(L * ydpi / 254) + 1 - 2 * feed; die-cut labels
// always have the paper's printable lines.
PageGeometry ComputePageGeometry(const Media& media, bool highRes, int surfaceLines, int surfaceTopDots,
                                 int length01, int labelTopDots, int minLength01 = kMinContinuousLength01);

constexpr int kHeadDots = 720;              // the usual head; the wide models: ModelTraits::headDots
constexpr int kHeadBytes = kHeadDots / 8;  // 90
constexpr int kWideHeadDots = 1296;
constexpr int kMaxHeadBytes = kWideHeadDots / 8;  // 162

// Zero bits before the printable pixels of a raster line on the model's head.
inline int PadStart(const Media& media, Model model) {
    return Traits(model).headDots == kWideHeadDots ? media.padStartWide : media.padStartBits;
}

// Invalidate + ESC @. Sent once, before the first page of a job.
void AppendJobHeader(std::vector<uint8_t>& out, Model model = Model::QL800);
// After the last page's terminator: ESC i a FF on the QL-600, nothing on the other models.
void AppendJobTrailer(std::vector<uint8_t>& out, Model model);

inline bool ModelHasMedia(Model model, const Media& media) {
    return (media.models >> static_cast<int>(model)) & 1;
}

// One page of label pixels: printableDots x strips bits per line, natural left-to-right order. A
// two-colour page has a second (red) plane.
class Page {
public:
    void Reset(const Media& media, int sourceWidthPx, int lines, bool twoColor = false);
    int Lines() const { return lines_; }
    bool TwoColor() const { return !red_.empty(); }
    const Media& GetMedia() const { return *media_; }
    // Places one source scanline (1 bpp, MSB first) at line y; the media's printable width is
    // taken from the centre of the source line. inkIsOne: a 1 bit means black. srcWidthPx: pixels
    // actually in src (bits beyond it are not read).
    void SetSourceLine(int y, const uint8_t* src, bool inkIsOne, int srcWidthPx = INT_MAX);
    // The same for the red plane of a two-colour page (a 1 bit means red).
    void SetRedLine(int y, const uint8_t* src, int srcWidthPx = INT_MAX);
    bool Pixel(int y, int x) const { return Bit(data_, y, x); }
    bool RedPixel(int y, int x) const { return !red_.empty() && Bit(red_, y, x); }
    bool LineBlank(int y) const;

private:
    bool Bit(const std::vector<uint8_t>& plane, int y, int x) const {
        return (plane[static_cast<size_t>(y) * stride_ + (x >> 3)] >> (7 - (x & 7))) & 1;
    }
    void PlaceLine(std::vector<uint8_t>& plane, int y, const uint8_t* src, bool inkIsOne, int srcWidthPx);

    const Media* media_ = nullptr;
    int sourceWidth_ = 0;
    int lines_ = 0;
    int stride_ = 0;
    std::vector<uint8_t> data_;
    std::vector<uint8_t> red_;  // empty: one colour
};

// Header commands and raster lines for one page (multi-up: one strip of it, 0 = leftmost; every
// strip is a label of its own). The page terminator (FF for more pages, Ctrl-Z for the last page) is
// NOT appended. feedDots: ESC i d for continuous media at 300 dpi.
void AppendPage(std::vector<uint8_t>& out, const JobSettings& settings, const Page& page,
                uint16_t feedDots, bool firstPage, int strip = 0);

// PackBits as Brother's driver does it: runs of 2+ equal bytes as repeats, other bytes as literals
// (at most 128 per run); a line that would not get shorter is sent as one literal.
void AppendPackBits(std::vector<uint8_t>& out, const uint8_t* line, int n);

// QL-700: second byte of a 'g' line, from the black dots of the 90 bytes in 18-byte blocks, last
// block first.
uint8_t Ql700LineDensity(const uint8_t* line);

constexpr uint8_t kNextPage = 0x0C;  // FF: print, more pages follow
constexpr uint8_t kLastPage = 0x1A;  // Ctrl-Z: print, last page

}  // namespace ql
