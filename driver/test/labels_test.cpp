// Unit tests for labels.tsv parsing/lookup and the page geometry of label formats.
#include <cstdio>
#include <string>

#include "../src/labels.h"
#include "../src/ql_raster.h"

static int g_failures = 0;
#define CHECK(cond)                                                    \
    do {                                                               \
        if (!(cond)) {                                                 \
            std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); \
            ++g_failures;                                              \
        }                                                              \
    } while (0)

int main() {
    // --- labels.tsv
    const std::string tsv =
        "\xEF\xBB\xBF# QLUnidrv labels 1\r\n"
        "Versand-Etikett 62mmx180mm\t259\t1800\t35\r\n"
        "Adress-Etikett (gro\xC3\x9F)\t272\t898\t35\r\n"
        "\r\n"
        "# comment\r\n"
        "bad paper\tx\t100\t35\r\n"
        "too few\t259\t100\r\n"
        "This name is far too long for dmFormName\t259\t100\t35\r\n"
        "Rundes Etikett \xC3\xB8 12 mm\t362\t120\t24";  // no final newline
    const std::vector<ql::LabelFormat> f = ql::ParseLabelsTsv(tsv);
    CHECK(f.size() == 3);
    CHECK(f.size() > 0 && f[0].name == L"Versand-Etikett 62mmx180mm" && f[0].paperId == 259 &&
          f[0].length01 == 1800 && f[0].feedDots == 35);
    CHECK(f.size() > 1 && f[1].name == L"Adress-Etikett (gro\u00DF)" && f[1].paperId == 272);
    CHECK(f.size() > 2 && f[2].name == L"Rundes Etikett \u00F8 12 mm" && f[2].feedDots == 24);
    CHECK(ql::FindLabelFormat(f, L"versand-etikett 62MMX180MM") == &f[0]);
    CHECK(ql::FindLabelFormat(f, L"ADRESS-ETIKETT (GRO\u00DF)") == &f[1]);
    CHECK(ql::FindLabelFormat(f, L"62mm") == nullptr);
    CHECK(ql::FindLabelFormat(f, L"") == nullptr);
    CHECK(ql::FindLabelFormat(f, nullptr) == nullptr);
    CHECK(ql::ParseLabelsTsv("").empty());
    CHECK(ql::LoadLabelsTsv(L"C:\\does-not-exist\\labels.tsv").empty());

    // --- page geometry
    const ql::Media& m62 = *ql::MediaByPtdId(259);
    const ql::Media& m29x90 = *ql::MediaByPtdId(271);
    // Forms gate test: 62 x 77 mm form through the custom-size path -> 840 lines.
    ql::PageGeometry g = ql::ComputePageGeometry(m62, false, 839, 35, 770, 35);
    CHECK(g.lines == 840 && g.lineOffset == 0);
    // DHL 180 mm (capture 62mm_userformat-332-DHL-180mm.prn: 2057 lines).
    g = ql::ComputePageGeometry(m62, false, 2056, 35, 1800, 35);
    CHECK(g.lines == 2057 && g.lineOffset == 0);
    // 89.8 mm default length (capture 62mm-continuous_default-length.prn: 992 lines), 300 x 600.
    g = ql::ComputePageGeometry(m62, false, 990, 35, 898, 35);
    CHECK(g.lines == 992);
    g = ql::ComputePageGeometry(m62, true, 1980, 35, 898, 35);
    CHECK(g.lines == 1982 && g.lineOffset == 0);  // capture 62mm_highres.prn
    // Feed 2 mm (24 dots) on a custom-size page with a 35-dot top margin: drawing moves down 11 lines.
    g = ql::ComputePageGeometry(m62, false, 839, 35, 770, 24);
    CHECK(g.lines == 909 + 1 - 48 && g.lineOffset == 11);
    g = ql::ComputePageGeometry(m62, true, 1678, 35, 770, 24);
    CHECK(g.lineOffset == 22);
    // Shorter than Brother's minimum: clamped to 12.7 mm.
    g = ql::ComputePageGeometry(m62, false, 100, 35, 100, 35);
    CHECK(g.lines == 150 + 1 - 70);
    // GPD continuous paper (no length): the surface is the label.
    g = ql::ComputePageGeometry(m62, false, 1633, 35, 0, 35);
    CHECK(g.lines == 1633 && g.lineOffset == 0);
    // Die-cut: the paper's printable lines, wherever the surface came from.
    g = ql::ComputePageGeometry(m29x90, false, 991, 35, 0, 35);
    CHECK(g.lines == 991 && g.lineOffset == 0);
    g = ql::ComputePageGeometry(m29x90, true, 1982, 35, 0, 35);
    CHECK(g.lines == 1982);
    const ql::Media& round12 = *ql::MediaByPtdId(362);
    g = ql::ComputePageGeometry(round12, false, 72, 35, 0, 24);  // via the custom-size path
    CHECK(g.lines == round12.printableLines && g.lineOffset == 11);

    std::printf(g_failures ? "%d FAILED\n" : "all label tests passed\n", g_failures);
    return g_failures ? 1 : 0;
}
