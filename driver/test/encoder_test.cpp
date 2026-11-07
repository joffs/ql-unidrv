// Unit tests for the QL raster encoder (geometry, header bytes, options).
#include <cstdio>
#include <cstring>
#include <vector>

#include <cstdlib>
#include <string>

#include "../src/ql_raster.h"
#include "../src/usb_status.h"

static int g_failures = 0;
#define CHECK(cond)                                                    \
    do {                                                               \
        if (!(cond)) {                                                 \
            std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); \
            ++g_failures;                                              \
        }                                                              \
    } while (0)

// Head dot `dot` of raster line `line` in an encoded page starting at `rasterStart`.
static bool HeadDot(const std::vector<uint8_t>& out, size_t rasterStart, int line, int dot) {
    const size_t o = rasterStart + static_cast<size_t>(line) * 93 + 3;
    return (out[o + (dot >> 3)] >> (7 - (dot & 7))) & 1;
}

int main() {
    const ql::Media* m62 = ql::MediaByPtdId(259);
    const ql::Media* m29x90 = ql::MediaByPtdId(271);
    CHECK(m62 && m62->printableDots == 696 && m62->padStartBits == 12 && m62->widthMm == 62);
    CHECK(m29x90 && m29x90->printableLines == 991 && m29x90->lengthMm == 90 && m29x90->padStartBits == 6);
    CHECK(ql::ContinuousForWidth(61.8) == m62);
    CHECK(ql::ContinuousForWidth(29.4)->widthMm == 29);
    CHECK(ql::ContinuousForWidth(102)->ptdId == 260);  // 102 mm tape
    CHECK(ql::ContinuousForWidth(103)->ptdId == 265);  // 103 mm tape (ESC i z byte 104)
    CHECK(ql::ContinuousForWidth(104)->ptdId == 265);  // a 103 x 164 mm label's ESC i z width

    // Leftmost and rightmost label pixel on line 1 of a 3-line page; source wider by 4 px (centre crop).
    const int w = 700, stride = (w + 31) / 32 * 4;
    std::vector<uint8_t> src(stride, 0);
    src[0] = 0x20;                                         // source x 2 = label x 0
    src[(w - 3) >> 3] |= uint8_t(0x80 >> ((w - 3) & 7));  // source x 697 = label x 695
    ql::Page page;
    page.Reset(*m62, w, 3);
    page.SetSourceLine(1, src.data(), true);
    CHECK(page.Pixel(1, 0) && page.Pixel(1, 695) && !page.Pixel(1, 1) && !page.Pixel(0, 0));

    std::vector<uint8_t> out;
    ql::AppendJobHeader(out);
    CHECK(out.size() == 402 && out[400] == 0x1B && out[401] == 0x40);

    const size_t pageStart = out.size();
    ql::JobSettings s;
    ql::AppendPage(out, s, page, 35, true);
    const uint8_t expectHead[] = {0x1B, 0x69, 0x61, 0x01, 0x1B, 0x69, 0x21, 0x01,
                                  0x1B, 0x69, 0x7A, 0x86, 0x0A, 62, 0, 3, 0, 0, 0, 0, 0,
                                  0x1B, 0x69, 0x4D, 0x40, 0x1B, 0x69, 0x41, 0x01, 0x1B, 0x69, 0x4B, 0x08,
                                  0x1B, 0x69, 0x64, 35, 0, 0x4D, 0x00};
    CHECK(std::memcmp(&out[pageStart], expectHead, sizeof expectHead) == 0);
    const size_t raster = pageStart + sizeof expectHead;
    CHECK(out.size() == raster + 3 * 93);
    // Normal: pixels right-to-left after 12 pad bits -> label x 0 at dot 707, x 695 at dot 12.
    CHECK(HeadDot(out, raster, 1, 707) && HeadDot(out, raster, 1, 12) && !HeadDot(out, raster, 1, 11));

    // Mirror: natural order; rotate 180: line order and pixel order flipped.
    std::vector<uint8_t> mir;
    s.mirror = true;
    ql::AppendPage(mir, s, page, 35, false);
    CHECK(mir[24] == 0xC0);  // ESC i M: auto cut + mirror
    CHECK(mir[19] == 1);  // ESC i z n9: not the first page
    CHECK(HeadDot(mir, sizeof expectHead, 1, 12) && HeadDot(mir, sizeof expectHead, 1, 707));
    std::vector<uint8_t> rot;
    s.mirror = false;
    s.rotate180 = true;
    page.SetSourceLine(0, src.data(), true);  // lines 0 and 1 set, line 2 blank
    ql::AppendPage(rot, s, page, 35, true);
    CHECK(!HeadDot(rot, sizeof expectHead, 0, 12) && HeadDot(rot, sizeof expectHead, 1, 12));

    // Die-cut, high resolution, quality priority, no auto cut, trim (ignored for die-cut).
    ql::Page die;
    die.Reset(*m29x90, 306, 4);
    ql::JobSettings d;
    d.autoCut = false;
    d.highRes = true;
    d.qualityPriority = true;
    d.trimTrailingBlank = true;
    std::vector<uint8_t> dc;
    ql::AppendPage(dc, d, die, 35, true);
    const uint8_t expectZ[] = {0x1B, 0x69, 0x7A, 0xCE, 0x0B, 29, 90, 4, 0, 0, 0, 0, 0};
    CHECK(std::memcmp(&dc[8], expectZ, sizeof expectZ) == 0);
    const uint8_t expectRest[] = {0x1B, 0x69, 0x4D, 0x00, 0x1B, 0x69, 0x4B, 0x48, 0x1B, 0x69, 0x64, 0, 0};
    CHECK(std::memcmp(&dc[21], expectRest, sizeof expectRest) == 0);

    // Trim trailing blank lines on continuous media, margin doubled at 300 x 600.
    std::vector<uint8_t> tr;
    ql::JobSettings t;
    t.trimTrailingBlank = true;
    t.highRes = true;
    ql::Page cont;
    cont.Reset(*m62, 696, 5);
    std::vector<uint8_t> one((696 + 7) / 8, 0);
    one[0] = 0x80;
    cont.SetSourceLine(1, one.data(), true);
    ql::AppendPage(tr, t, cont, 35, true);
    CHECK(tr[15] == 2);                    // ESC i z line count: lines 0..1
    CHECK(tr[36] == 70 && tr[37] == 0);    // ESC i d 70 at 300 x 600

    // QL-810W/820NWB: on USB (no compression) M 00 and plain lines, ESC i K 0x02 in 300 x 600;
    // on other ports M 02 with packed lines (line 0 blank: g 00 02 A7 00).
    ql::JobSettings u;
    u.model = ql::Model::QL810W;
    u.highRes = true;
    std::vector<uint8_t> usb;
    ql::AppendPage(usb, u, cont, 35, true);
    CHECK(usb[32] == 0x4A && usb[38] == 0x4D && usb[39] == 0x00 && usb.size() == 40 + 5 * 93);
    u.compress = true;
    std::vector<uint8_t> net;
    ql::AppendPage(net, u, cont, 35, true);
    const uint8_t blank[] = {0x4D, 0x02, 0x67, 0x00, 0x02, 0xA7, 0x00};
    CHECK(net[32] == 0x4A && std::memcmp(&net[38], blank, sizeof blank) == 0);
    u.highRes = false;
    std::vector<uint8_t> low;
    ql::AppendPage(low, u, cont, 35, true);
    CHECK(low[32] == 0x08);  // 300 dpi: feed 35 is not below 3 mm

    // QL-570: 200-byte invalidate; pages without ESC i a, ESC i ! and M, never compressed, no ESC i K 0x02.
    std::vector<uint8_t> h570;
    ql::AppendJobHeader(h570, ql::Model::QL570);
    CHECK(h570.size() == 202 && h570[199] == 0x00 && h570[200] == 0x1B && h570[201] == 0x40);
    u.model = ql::Model::QL570;
    u.highRes = true;
    std::vector<uint8_t> p570;
    ql::AppendPage(p570, u, cont, 35, true);
    CHECK(p570[0] == 0x1B && p570[2] == 0x7A && p570[24] == 0x48 && p570[30] == 0x67 && p570.size() == 30 + 5 * 93);

    // QL-560/550/500/650TD: 200-byte invalidate, 300 dpi only.
    std::vector<uint8_t> h650;
    ql::AppendJobHeader(h650, ql::Model::QL650TD);
    CHECK(h650.size() == 202);
    u.highRes = true;  // ignored: no 300 x 600
    u.model = ql::Model::QL560;  // = QL-570: ESC i z, M 40, A 01, K 08, d 35
    std::vector<uint8_t> p560;
    ql::AppendPage(p560, u, cont, 35, true);
    CHECK(p560[16] == 0x40 && p560[20] == 0x01 && p560[24] == 0x08 && p560[28] == 35 && p560[30] == 0x67 &&
          p560.size() == 30 + 5 * 93);
    u.model = ql::Model::QL550;  // ESC i z, M 40, d 35
    std::vector<uint8_t> p550;
    ql::AppendPage(p550, u, cont, 35, true);
    CHECK(p550[16] == 0x40 && p550[17] == 0x1B && p550[19] == 0x64 && p550[20] == 35 && p550.size() == 22 + 5 * 93);
    u.model = ql::Model::QL500;  // no cutter: M 00
    std::vector<uint8_t> p500;
    ql::AppendPage(p500, u, cont, 35, true);
    CHECK(p500[16] == 0x00 && p500[19] == 0x64 && p500.size() == 22 + 5 * 93);
    u.model = ql::Model::QL650TD;  // ESC i a 01, ESC i z, M 40, K 08, d 35, M 00
    std::vector<uint8_t> p650;
    ql::AppendPage(p650, u, cont, 35, true);
    CHECK(p650[2] == 0x61 && p650[3] == 0x01 && p650[6] == 0x7A && p650[20] == 0x40 && p650[23] == 0x4B &&
          p650[24] == 0x08 && p650[27] == 0x64 && p650[28] == 35 && p650[30] == 0x4D && p650[31] == 0x00 &&
          p650.size() == 32 + 5 * 93);
    // QL-700/710W/720NW: ESC i a 01 first, no ESC i K 0x02 in 300 x 600. QL-700:
    // ESC i J 01 after ESC i d, no M, never packed, density byte in g; QL-710W: M 02 + PackBits off USB.
    u.highRes = true;
    u.compress = true;
    u.model = ql::Model::QL700;
    std::vector<uint8_t> p700;
    ql::AppendPage(p700, u, cont, 35, true);
    CHECK(p700[2] == 0x61 && p700[6] == 0x7A && p700[20] == 0x40 && p700[28] == 0x48 && p700[32] == 70 &&
          p700[34] == 0x1B && p700[36] == 0x4A && p700[37] == 0x01 && p700[38] == 0x67 && p700[39] == 0x00 &&
          p700.size() == 38 + 5 * 93);
    u.model = ql::Model::QL710W;
    std::vector<uint8_t> p710;
    ql::AppendPage(p710, u, cont, 35, true);
    CHECK(p710[2] == 0x61 && p710[28] == 0x48 && std::memcmp(&p710[34], blank, sizeof blank) == 0);
    std::vector<uint8_t> full(ql::kHeadBytes, 0xFF), first(ql::kHeadBytes, 0);
    std::fill(first.begin(), first.begin() + 18, uint8_t(0xFF));
    CHECK(ql::Ql700LineDensity(full.data()) == 0xF4 && ql::Ql700LineDensity(first.data()) == 0x00);
    // 12 mm round label: ESC i d 35 (QL-650TD: 0 like every die-cut label).
    const ql::Media* round12 = ql::MediaByPtdId(362);
    ql::Page rp;
    rp.Reset(*round12, 94, 2);
    ql::JobSettings rs;
    rs.model = ql::Model::QL570;
    std::vector<uint8_t> r570, r650;
    ql::AppendPage(r570, rs, rp, 35, true);
    rs.model = ql::Model::QL650TD;
    ql::AppendPage(r650, rs, rp, 35, true);
    CHECK(r570[27] == 0x64 && r570[28] == 35 && r650[27] == 0x64 && r650[28] == 0);
    // Shortest continuous label: 12.7 mm, 25 mm on the older models.
    CHECK(ql::Traits(ql::Model::QL570).minLength01 == 127 && ql::Traits(ql::Model::QL560).minLength01 == 250);
    CHECK(ql::ComputePageGeometry(*m62, false, 100, 35, 150, 35, 250).lines ==
          ql::ComputePageGeometry(*m62, false, 100, 35, 250, 35).lines);

    // Two-colour separation: nearest of white, black and pure red.
    CHECK(ql::ClassifyRgb(255, 0, 0) == ql::Ink::Red && ql::ClassifyRgb(180, 20, 20) == ql::Ink::Red);
    CHECK(ql::ClassifyRgb(0, 0, 0) == ql::Ink::Black && ql::ClassifyRgb(60, 60, 60) == ql::Ink::Black);
    CHECK(ql::ClassifyRgb(255, 255, 255) == ql::Ink::None && ql::ClassifyRgb(200, 200, 200) == ql::Ink::None);
    const uint8_t rgb[] = {255, 0, 0, 0, 0, 0, 255, 255, 255};  // red, black, white (R,G,B order)
    uint8_t kLine[1], rLine[1];
    ql::SeparateRgbLine(rgb, 3, kLine, rLine);
    CHECK(kLine[0] == 0x40 && rLine[0] == 0x80);

    // QL-800 two-colour on 62 mm: ESC i K bit 0, uncompressed 'w 01' + 'w 02' lines, no Z.
    ql::Page two;
    two.Reset(*m62, 696, 2, true);
    const uint8_t left[87] = {0x80};
    two.SetSourceLine(0, left, true);  // black at label x 0
    two.SetRedLine(1, left);           // red at label x 0
    CHECK(two.TwoColor() && two.Pixel(0, 0) && !two.RedPixel(0, 0) && two.RedPixel(1, 0) && !two.Pixel(1, 0));
    ql::JobSettings tc;
    tc.twoColor = true;
    std::vector<uint8_t> p2;
    ql::AppendPage(p2, tc, two, 35, true);
    CHECK(p2.size() == 40 + 2 * 2 * 93 && p2[32] == 0x09 && p2[38] == 0x4D && p2[39] == 0x00);
    const size_t l0 = 40, l1 = 40 + 2 * 93;
    CHECK(p2[l0] == 0x77 && p2[l0 + 1] == 0x01 && p2[l0 + 2] == 90 && p2[l0 + 93] == 0x77 && p2[l0 + 94] == 0x02);
    // label x 0 = head dot 12 + 695 = 707 (byte 88, mask 0x80 >> 3 = 0x10)
    CHECK(p2[l0 + 3 + 88] == 0x10 && p2[l0 + 93 + 3 + 88] == 0x00);
    CHECK(p2[l1 + 3 + 88] == 0x00 && p2[l1 + 93 + 3 + 88] == 0x10);
    // QL-810W on a network port: a line blank in both planes is Z even at line 0.
    ql::Page blank2;
    blank2.Reset(*m62, 696, 1, true);
    tc.model = ql::Model::QL810W;
    tc.compress = true;
    std::vector<uint8_t> p810;
    ql::AppendPage(p810, tc, blank2, 35, true);
    CHECK(p810.back() == 0x5A && p810[39] == 0x02);
    // The QL-570 has no two-colour: g lines, no ESC i K bit 0.
    tc.model = ql::Model::QL570;
    tc.compress = false;
    std::vector<uint8_t> p570two;
    ql::AppendPage(p570two, tc, two, 35, true);
    CHECK(p570two[24] == 0x08 && p570two[30] == 0x67 && p570two.size() == 30 + 2 * 93);

    // Multi-up "62mm x 2": a page two tapes wide, each strip a 62 mm label (ESC i z width 62), left
    // strip first; the second strip is a later page (n9 = 1). Rotate 180 also swaps the strips.
    const ql::Media* m62x2 = ql::MediaByPtdId(279);
    CHECK(m62x2 && m62x2->strips == 2 && m62x2->printableDots == 696 && m62x2->widthMm == 62 && m62x2->padStartBits == 12);
    CHECK(ql::ContinuousForWidth(62) == m62);
    ql::Page mu;
    mu.Reset(*m62x2, 1392, 1);
    std::vector<uint8_t> wide(1392 / 8, 0);
    wide[0] = 0x80;          // page x 0 = strip 0, x 0
    wide[696 / 8] = 0x40;    // page x 697 = strip 1, x 1
    mu.SetSourceLine(0, wide.data(), true);
    ql::JobSettings ms;
    std::vector<uint8_t> s0, s1, r0;
    ql::AppendPage(s0, ms, mu, 35, true, 0);
    ql::AppendPage(s1, ms, mu, 35, true, 1);
    CHECK(s0[8] == 0x1B && s0[10] == 0x7A && s0[13] == 62 && s0[19] == 0 && s1[19] == 1);
    // label x 0 -> head dot 707 (byte 88, 0x10); label x 1 -> head dot 706 (byte 88, 0x20)
    CHECK(s0[40 + 3 + 88] == 0x10 && s1[40 + 3 + 88] == 0x20);
    ms.rotate180 = true;
    ql::AppendPage(r0, ms, mu, 35, true, 0);  // strip 0 of the rotated page = strip 1 reversed
    CHECK(r0[40 + 3 + 1] == 0x04 && r0[40 + 3 + 88] == 0x00);  // not reversed: strip x 1 -> dot 13 (byte 1, 0x04)

    // Papers per model: 62 x 60 mm (388) is not in the QL-810W's or the QL-570's table.
    const ql::Media* m388 = ql::MediaByPtdId(388);
    CHECK(m388 && ql::ModelHasMedia(ql::Model::QL800, *m388) && !ql::ModelHasMedia(ql::Model::QL810W, *m388) &&
          !ql::ModelHasMedia(ql::Model::QL570, *m388) && ql::ModelHasMedia(ql::Model::QL570, *m62));

    // Wide head (QL-1050/1060N): 162-byte lines, the paper's 1296-dot padding (102 mm: 56, 62 mm: 56).
    const ql::Media* m102 = ql::MediaByPtdId(260);
    CHECK(m102 && m102->printableDots == 1164 && m102->padStartWide == 56 && m62->padStartWide == 56 &&
          ql::ModelHasMedia(ql::Model::QL1050, *m102) && !ql::ModelHasMedia(ql::Model::QL800, *m102) &&
          ql::ModelHasMedia(ql::Model::QL1060N, *m62) && ql::PadStart(*m62, ql::Model::QL1050) == 56 &&
          ql::PadStart(*m62, ql::Model::QL800) == 12);
    if (m102) {
        ql::JobSettings ws;
        ws.model = ql::Model::QL1050;
        ql::Page pageW;
        pageW.Reset(*m102, m102->printableDots, 2);
        std::vector<uint8_t> srcW((m102->printableDots + 7) / 8, 0);
        srcW[0] = 0x80;  // leftmost pixel -> last printable dot (lines go right to left): 56 + 1163 = 1219
        pageW.SetSourceLine(0, srcW.data(), true);
        std::vector<uint8_t> pw;
        ql::AppendPage(pw, ws, pageW, 35, true);
        const size_t g = pw.size() - 2 * 165;
        CHECK(pw[g] == 0x67 && pw[g + 2] == 162 && pw[g + 3 + 152] == 0x10 && pw[g + 165] == 0x67 &&
              pw[g + 165 + 2] == 162);
    }

    // Roll check: replies measured on the QL-800 2026-10-17.
    auto status = [](const char* hex) {
        ql::StatusBytes s = {};
        for (size_t i = 0; i < s.size(); ++i) s[i] = static_cast<uint8_t>(std::strtoul(std::string(hex + i * 3, 2).c_str(), nullptr, 16));
        return s;
    };
    const auto blackIdle = status("80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00");
    const auto redIdle = status("80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 23 00 00 00 00 00 00 00 00 00 00 81 00 00 00 00 00 00");
    const auto printing = status("80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 06 01 00 00 00 00 00 01 00 00 00 00 00 00");
    const auto noRoll = status("80 20 42 34 38 30 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00");
    CHECK(ql::BlackOnlyRoll(blackIdle) && !ql::BlackOnlyRoll(redIdle) && !ql::BlackOnlyRoll(printing) &&
          !ql::BlackOnlyRoll(noRoll) && !ql::BlackOnlyRoll(ql::StatusBytes{}));

    std::printf(g_failures ? "%d FAILED\n" : "all encoder tests passed\n", g_failures);
    return g_failures ? 1 : 0;
}
