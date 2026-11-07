"""Generates the media-dependent parts of the driver and the label tool from Brother's paper table.

Input
  docs/data/bsq80AD.json          Brother QL-800 PTD paper table
  docs/data/bsq81wAD.json         QL-810W (the QL-800's papers minus 62x60 / 62x75 die-cut)
  docs/data/bsq82nwbAD.json       QL-820NWB (same papers as the QL-800)
  docs/data/ql57ad.json           QL-570 (the QL-800's papers minus 29x42, 54x29, 60x86, 62x60, 62x75)
  docs/data/ql56ad.json           QL-560, ptql5ad.json QL-500/550, ptql65ad.json QL-650TD (the
                                  QL-570's papers; continuous minimum 25 mm)
  docs/data/ptql10ad.json         QL-1050, ql106nad.json QL-1060N: 1296-dot head; the QL-570's papers
                                  (same sizes, other raster padding) plus 102/103 mm wide papers,
                                  continuous 25 mm - 3 m
  docs/data/bsq110ad.json         QL-1100, bsq111nwbad.json QL-1110NWB (the same), bsq115nwbad.json
                                  QL-1115NWB: wide head; the 103 mm papers and 102 x 51 placed
                                  differently from the QL-1050/1060N (own entries), direct-thermal
                                  papers on the QL-1115NWB; continuous 25.4 mm - 3 m
  docs/data/bsq60ad.json          QL-600 (= the QL-700's papers)
  reference/captures/**/*.prn     Brother's ESC i U w media blocks (per model and paper, media_info_blocks)
Outputs
  driver/src/media_table.inc      C++ media table for the render plug-in (the QL-800's papers,
                                  a superset of the 720-dot models', then the wide models' own
                                  papers; a bit mask says which models have each entry; two pad
                                  start columns: 720-dot and 1296-dot head). A paper id has a second
                                  entry where a model places it differently (QL-1100 103 mm).
  driver/src/media_info.inc       ESC i U w blocks (direct-thermal papers and a few die-cut papers per model)
  driver/gpd/QLUnidrv<model>.GPD  one GPD per model from template.GPD.in (@@MODELNAME@@, @@GPDFILE@@,
                                  @@STARTDOC@@, @@PAPERSIZES@@, @@CUTEVERY@@, @@DEFAULT_PAPER@@,
                                  @@COLORMODES@@ = the black/red option for two-colour models,
                                  @@IF option@@ blocks); each lists only its model's papers
  labeltool/src/MediaTable.g.cs   media table and model list for the label tool (each model's
                                  driver name, status model code and papers)

User label formats are not part of the driver: the label tool turns them into Windows forms
(offered through the GPD's custom-size range) plus C:\\ProgramData\\QLUnidrv\\labels.tsv, which the
plug-in reads at print time. The GPD only has Brother's papers and the custom size.

Paper callback IDs encode what the plug-in needs:  id = 10000 + feedDots * 100 + mediaIndex.

Multi-up papers ("62mm x 2/3/4", PTD multi_up_minus_1 > 0): a page k tapes wide; the plug-in sends
it as k strips, each a label of the base tape ("62mm"), left strip first. They
are GPD papers but not offered to the label tool (no label formats based on them).

Run:  python -I driver/gen/media.py
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
DRIVER = os.path.dirname(HERE)
ROOT = os.path.dirname(DRIVER)

PTD_JSON = os.path.join(ROOT, "docs", "data", "bsq80AD.json")
# The wide models' paper tables (1296-dot head), in this order: QL-1050/1060N, QL-1100/1110NWB, QL-1115NWB
WIDE_JSON = [os.path.join(ROOT, "docs", "data", t) for t in ("ptql10ad.json", "bsq110ad.json", "bsq115nwbad.json")]
CAPTURES = os.path.join(ROOT, "reference", "captures")
NARROW_HEAD, WIDE_HEAD = 720, 1296
# Not printer papers: 472 is Brother's "crop print" page (A4/Letter, the driver cuts barcodes out of it);
# 386/387 "Shipping label" send the commands of 385/265.
SKIPPED_PAPERS = {386, 387, 472}
# PTD flag 0x02 on a die-cut paper (QL-1100 series 102 x 51, DT 102 x 51): Brother's driver sends two raster
# lines more than the PTD's printable length (528 instead of 526; captures 102x51-diecut, dt102x51-diecut).
FLAG_TWO_MORE_LINES = 0x02
# One row per model, sorted by model number (QL-500 ... QL-1060N). The row order is only the order of
# the label tool's model list; a model's identity is its ql::Model value (src/ql_raster.h), taken from
# "start" (the bit in the media table's model mask):
#   gpd     GPD base name          table   the model's paper table
#   driver  model name (the INF's model strings must match)
#   start   StartDoc callback ID (tells the plug-in the model): 40 + ql::Model; the QL-800 (ql::Model 0)
#           has 1. Fixed per model - never renumber (GPDs and plug-in must agree)
#   short   short name             status  model code in the printer's status reply (byte 4)
#   red     two-colour (red/black DK-22251)
#   options GPD option blocks (@@IF name@@ in template.GPD.in): highres = 300 x 600, autocut, cutevery,
#           cutatend; what Brother's driver dialog offers
#   min     shortest continuous label (0.1 mm; the plug-in lengthens shorter ones, src/ql_raster.cpp)
#   head    print head dots (720; QL-1050/1060N/1100 series 1296: 162-byte raster lines)
# A new model is one more row here (plus its INF line and plug-in traits); the label tool takes its
# model list from this table.
ALL_OPTIONS = {"highres", "autocut", "cutevery", "cutatend"}
MODELS = [
    dict(gpd="QLUnidrv500", table="ptql5ad.json", driver="Brother QL-500 (Unidrv)", start=46, short="QL-500",
         status=0x4F, red=False, options=set(), min=250),
    dict(gpd="QLUnidrv550", table="ptql5ad.json", driver="Brother QL-550 (Unidrv)", start=45, short="QL-550",
         status=0x4F, red=False, options={"autocut"}, min=250),
    dict(gpd="QLUnidrv560", table="ql56ad.json", driver="Brother QL-560 (Unidrv)", start=44, short="QL-560",
         status=0x31, red=False, options={"autocut", "cutevery", "cutatend"}, min=250),
    dict(gpd="QLUnidrv570", table="ql57ad.json", driver="Brother QL-570 (Unidrv)", start=43, short="QL-570",
         status=0x32, red=False, options=ALL_OPTIONS, min=127),
    # QL-580N: the QL-570's papers (ql58nad.ptd), the QL-710W's options and commands
    dict(gpd="QLUnidrv580", table="ql58nad.json", driver="Brother QL-580N (Unidrv)", start=53, short="QL-580N",
         status=0x33, red=False, options=ALL_OPTIONS, min=127),
    # QL-600: the QL-700's papers and commands without density / ESC i J
    dict(gpd="QLUnidrv600", table="bsq60ad.json", driver="Brother QL-600 (Unidrv)", start=57, short="QL-600",
         status=0x47, red=False, options=ALL_OPTIONS, min=127),
    dict(gpd="QLUnidrv650", table="ptql65ad.json", driver="Brother QL-650TD (Unidrv)", start=47, short="QL-650TD",
         status=0x51, red=False, options={"autocut", "cutatend"}, min=250),
    dict(gpd="QLUnidrv700", table="bsq70AD.json", driver="Brother QL-700 (Unidrv)", start=48, short="QL-700",
         status=0x35, red=False, options=ALL_OPTIONS, min=127),
    dict(gpd="QLUnidrv710", table="bsq71wAD.json", driver="Brother QL-710W (Unidrv)", start=49, short="QL-710W",
         status=0x36, red=False, options=ALL_OPTIONS, min=127),
    dict(gpd="QLUnidrv720", table="bsq72nwAD.json", driver="Brother QL-720NW (Unidrv)", start=50, short="QL-720NW",
         status=0x37, red=False, options=ALL_OPTIONS, min=127),
    dict(gpd="QLUnidrv800", table="bsq80AD.json", driver="Brother QL-800 (Unidrv)", start=1, short="QL-800",
         status=0x38, red=True, options=ALL_OPTIONS, min=127),
    dict(gpd="QLUnidrv810", table="bsq81wAD.json", driver="Brother QL-810W (Unidrv)", start=41, short="QL-810W",
         status=0x39, red=True, options=ALL_OPTIONS, min=127),
    dict(gpd="QLUnidrv820", table="bsq82nwbAD.json", driver="Brother QL-820NWB (Unidrv)", start=42, short="QL-820NWB",
         status=0x41, red=True, options=ALL_OPTIONS, min=127),
    dict(gpd="QLUnidrv1050", table="ptql10ad.json", driver="Brother QL-1050 (Unidrv)", start=51, short="QL-1050",
         status=0x50, red=False, options={"autocut", "cutevery", "cutatend"}, min=250, head=WIDE_HEAD),
    dict(gpd="QLUnidrv1060", table="ql106nad.json", driver="Brother QL-1060N (Unidrv)", start=52, short="QL-1060N",
         status=0x34, red=False, options={"autocut", "cutevery", "cutatend"}, min=250, head=WIDE_HEAD),
    # QL-1100 series: 300 x 300 only, the QL-800's other options
    dict(gpd="QLUnidrv1100", table="bsq110ad.json", driver="Brother QL-1100 (Unidrv)", start=54, short="QL-1100",
         status=0x43, red=False, options={"autocut", "cutevery", "cutatend"}, min=254, head=WIDE_HEAD),
    dict(gpd="QLUnidrv1110", table="bsq111nwbad.json", driver="Brother QL-1110NWB (Unidrv)", start=55, short="QL-1110NWB",
         status=0x44, red=False, options={"autocut", "cutevery", "cutatend"}, min=254, head=WIDE_HEAD),
    dict(gpd="QLUnidrv1115", table="bsq115nwbad.json", driver="Brother QL-1115NWB (Unidrv)", start=56, short="QL-1115NWB",
         status=0x45, red=False, options={"autocut", "cutevery", "cutatend"}, min=254, head=WIDE_HEAD),
]

# ColorMode option of the two-colour models: Unidrv renders 24 bpp, the plug-in separates black and
# red (src/ql_raster.h ClassifyRgb). Used on 62 mm continuous only; on other papers red prints black.
GPD_RED_BLACK = """    *Option: RedBlack
    {
        *Name: "Black and red (DK-22251, 62 mm)"
        *DevNumOfPlanes: 0
        *DevBPP: 0
        *DrvBPP: 24
        *IPCallbackID: 1
        *Color? : TRUE
        *Command: CmdSelect
        {
            *Order: DOC_SETUP.6
            *CallbackID: 21
        }
    }"""
APP_CS = os.path.join(ROOT, "labeltool", "src", "MediaTable.g.cs")

DOT_MU = 2               # GPD master units (600 dpi) per 300-dpi dot
PAPER_CALLBACK_BASE = 10000
CUSTOM_CALLBACK = 199
CUSTOM_MIN_LEFT_DOTS = 0  # GPD CUSTOMSIZE *MinLeftMargin (dots): 0 keeps the custom page centred (gpd_papers)
CUT_EVERY_BASE = 200
DEFAULT_PAPER_ID = 259   # "62mm" continuous


def mm01_to_dots(v):
    """0.1 mm -> 300 dpi dots, rounded like Brother's driver (1000 -> 1181, 898 -> 1061)."""
    return (v * 300 * 10 // 254 + 5) // 10


def dots_to_um(dots):
    """300 dpi dots -> 0.001 mm (forms database unit), as Windows lists the GPD's paper sizes."""
    return (dots * 254000 + 1500) // 3000


def load_media(path=PTD_JSON):
    papers = json.load(open(path, encoding="utf-8"))["papers"]
    media = []
    for p in papers:
        if p["id"] in SKIPPED_PAPERS:
            continue
        lanes = p["multi_up_minus_1"] + 1
        kind = p["kind"] & 0x7F  # direct-thermal papers: 0x8A / 0x8B in the PTD, ESC i z sends 0x0A / 0x0B
        cont = kind == 10
        rounding = 5
        flags = int(p["flags_f8"], 16)
        lines = p["printable_length_dots"] + (2 if not cont and flags & FLAG_TWO_MORE_LINES else 0)
        if flags & 0x100:
            rounding = 10
        elif flags & 0x200:
            rounding = 0
        media.append({
            "ptd_id": p["id"], "name": p["name"], "kind": kind,
            "width_mm": (p["width_0_1mm"] + rounding) // 10,
            "length_mm": 0 if cont else (p["length_0_1mm"] + 5) // 10,
            "width01": p["width_0_1mm"], "length01": 0 if cont else p["length_0_1mm"],
            "width_dots": p["width_dots"], "printable_dots": p["printable_width_dots"],
            "length_dots": p["length_dots"], "printable_lines": lines,
            "offx": p["offset_x_dots"], "offy": p["offset_y_dots"],
            "pad_start": p["raster_pad_start_bits"],
            "cont_min": p["cont_min_length_0_1mm"], "cont_max": p["cont_max_length_0_1mm"],
            "cont_default": p["cont_default_length_0_1mm"],
            "lanes": lanes,
        })
    # A multi-up paper's strips are labels of its base tape: "62mm x 2" -> "62mm" (ESC i z width).
    by_name = {m["name"]: m for m in media}
    for m in media:
        if m["lanes"] > 1:
            base = by_name[m["name"].split(" x ")[0]]
            if base["printable_dots"] * m["lanes"] != m["printable_dots"] or base["pad_start"] != m["pad_start"]:
                raise SystemExit(f"{m['name']}: strips don't match {base['name']}")
            m["width_mm"] = base["width_mm"]
    return media


# Fields that may differ between models for the same entry: the raster padding (one column per head) and
# the continuous minimum / maximum length (per model: MODELS "min", returned with the model's papers).
PER_MODEL_KEYS = ("pad_start", "pad_wide", "cont_min", "cont_max")


def same_paper(a, b):
    return all(a[k] == b[k] for k in a if k not in PER_MODEL_KEYS and k in b)


def all_media():
    """The QL-800's papers, then the wide models' own; "pad_wide" = pad start on the 1296-dot head
    (0 where no wide model has the paper), "pad_start" on the 720-dot head (0 for wide-only entries).
    A wide paper that differs from the existing entries of its id (other printable width, offset, lines)
    gets an entry of its own."""
    media = [dict(m, pad_wide=0) for m in load_media()]
    for table in WIDE_JSON:
        for w in load_media(table):
            entry = next((m for m in media if m["ptd_id"] == w["ptd_id"] and same_paper(m, w)
                          and m["pad_wide"] in (0, w["pad_start"])), None)
            if entry:
                entry["pad_wide"] = w["pad_start"]
            else:
                media.append(dict(w, pad_start=0, pad_wide=w["pad_start"]))
    return media


def capture_dir(md):
    """A model's capture folder name: "QL-1115NWB" -> "ql1115nwb"."""
    return md["short"].lower().replace("-", "")


def media_info_blocks():
    """ESC i U w blocks (01 + 127 bytes) as {(paper id, block): model mask}, from every capture of Brother's
    drivers (reference/captures/<model> and .../testkit/<model>; the paper id is in bytes 22-23 of the block).
    Which papers get one is the model's driver's choice, not the PTD's (the QL-800's table has media data for
    54 x 29 and 60 x 86 too, its driver sends none), and the block differs between models (byte 11), so it is
    sent per model and paper exactly as captured: QL-1115NWB direct-thermal papers, QL-700 29 x 42, QL-700/710W/
    720NW 60 x 86, QL-800/820NWB 62 x 60 and 62 x 75 (captures of every single-label paper, 2026-11-04)."""
    found = {}
    for md in MODELS:
        bit = model_bit(md)
        for folder in (os.path.join(CAPTURES, capture_dir(md)), os.path.join(CAPTURES, "testkit", capture_dir(md))):
            if not os.path.isdir(folder):
                continue
            for name in sorted(os.listdir(folder)):
                if not name.endswith(".prn"):
                    continue
                b = open(os.path.join(folder, name), "rb").read()
                i = b.find(b"\x1biUw")
                if i < 0:
                    continue
                block = b[i + 4:i + 4 + 128]
                key = (bit, block[22] | block[23] << 8)
                if found.get(key, block) != block:
                    raise SystemExit(f"{md['short']}: two different ESC i U w blocks for paper {key[1]} ({name})")
                found[key] = block
    blocks = {}
    for (bit, ptd_id), block in found.items():
        blocks[(ptd_id, block)] = blocks.get((ptd_id, block), 0) | (1 << bit)
    return blocks


def single(media):
    """Papers without multi-up (label tool, custom-size range)."""
    return [m for m in media if m["lanes"] == 1]


def gpd_option(key, name, page_w, page_l, area_w, area_l, org_x, org_y, callback):
    return f"""    *Option: {key}
    {{
        *Name: "{name}"
        *PageDimensions: PAIR({page_w * DOT_MU}, {page_l * DOT_MU})
        *PrintableArea: PAIR({area_w * DOT_MU}, {area_l * DOT_MU})
        *PrintableOrigin: PAIR({org_x * DOT_MU}, {org_y * DOT_MU})
        *CursorOrigin: PAIR({org_x * DOT_MU}, {org_y * DOT_MU})
        *Command: CmdSelect
        {{
            *Order: PAGE_SETUP.2
            *CallbackID: {callback}
        }}
    }}"""


def paper_option(m, index):
    feed = m["offy"]
    callback = PAPER_CALLBACK_BASE + feed * 100 + index
    if m["kind"] == 10:  # continuous: default length, feed margin top and bottom
        # Brother's page is one dot longer than the rounded length (captures: 89.8 / 100 / 180 mm
        # -> 992 / 1112 / 2057 raster lines = dots + 1 - 2 * 35).
        length = mm01_to_dots(m["cont_default"]) + 1
        return gpd_option(f"P{m['ptd_id']}", m["name"], m["width_dots"], length, m["printable_dots"],
                          length - 2 * feed, m["offx"], feed, callback)
    return gpd_option(f"P{m['ptd_id']}", m["name"], m["width_dots"], m["length_dots"], m["printable_dots"],
                      m["printable_lines"], m["offx"], m["offy"], callback)


def model_media(media, table, head=NARROW_HEAD):
    """(media index, paper) for the papers of one model: the shared entry that matches exactly, except
    the continuous minimum length (QL-500/550/560/650TD/1050/1060N: 25 mm, QL-1100 series 25.4 mm; MODELS
    "min") and maximum (wide models: 3 m; returned with the paper, for the GPD's custom-size range). The
    raster padding is compared with the shared table's column for the model's head."""
    pad = "pad_wide" if head == WIDE_HEAD else "pad_start"
    out = []
    for m in load_media(os.path.join(ROOT, "docs", "data", table)):
        match = next(((i, s) for i, s in enumerate(media) if s["ptd_id"] == m["ptd_id"] and
                      s[pad] == m["pad_start"] and same_paper(s, m)), None)
        if not match:
            raise SystemExit(f"{table}: paper {m['ptd_id']} {m['name']} differs from the shared table")
        i, s = match
        out.append((i, dict(s, cont_max=m["cont_max"])))
    return out


def gpd_papers(indexed):
    media = single([m for _, m in indexed])
    out = [paper_option(m, i) for i, m in indexed]
    cont = [m for m in media if m["kind"] == 10]
    max_area = max(m["printable_dots"] for m in cont)
    # The custom-size range decides which Windows forms Unidrv offers: wide enough for every tape,
    # and short enough for the smallest die-cut label (12 mm round), so that user label formats
    # based on any Brother paper are listed. Continuous lengths below Brother's minimum (12.7 mm)
    # are lengthened by the plug-in.
    # Unidrv's custom-size printable area (measured): left = max(MinLeftMargin, (width - MaxPrintableWidth) / 2),
    # printable width = min(MaxPrintableWidth, width - left), no right margin of its own. The plug-in centres the
    # surface on the tape's band (ql_raster.cpp PlaceLine), so the surface must be centred on the paper too:
    # MinLeftMargin 0 does that at every width (62 mm on the 720-dot head: 18 / 696 / 18 as Brother; narrower
    # tapes and 62 mm on the wide head: the whole paper width, 0 / 0, the plug-in crops the tape's side margins).
    # A margin of 36 dots would give Brother's sizes (62 mm 732 -> 696, 29 mm 342 -> 306) but reports 36 / 0, so
    # applications that centre on the paper print 1.5 mm too far left (measured 2026-11-01).
    # check_custom_margins() below checks the centring for every model and tape.
    min_len = min(min(mm01_to_dots(m["cont_min"]) for m in cont),
                  min(m["length_dots"] for m in media if m["kind"] != 10))
    # Widest custom size: the widest tape rounded up to whole millimetres, plus a dot. A tape's own width in dots
    # is a little below its nominal size ("102mm" = 1200 dots = 101.6 mm, 62 mm = 732 dots = 61.98 mm), and Unidrv
    # rejects a custom size even 0.1 mm wider than MaxSize (QL-1115NWB, x64 2026-10-21: 102 x 100 mm became the
    # default paper).
    max_width = mm01_to_dots((max(m["width01"] for m in cont) + 9) // 10 * 10) + 1
    out.append(f"""    *Option: CUSTOMSIZE
    {{
        *rcNameID: =USER_DEFINED_SIZE_DISPLAY
        *MinSize: PAIR({min(m['width_dots'] for m in cont) * DOT_MU}, {min_len * DOT_MU})
        *MaxSize: PAIR({max_width * DOT_MU}, {mm01_to_dots(max(m['cont_max'] for m in cont)) * DOT_MU})
        *MaxPrintableWidth: {max_area * DOT_MU}
        *MinLeftMargin: {CUSTOM_MIN_LEFT_DOTS * DOT_MU}
        *CenterPrintable?: TRUE
        *TopMargin: {35 * DOT_MU}
        *BottomMargin: {35 * DOT_MU}
        *Command: CmdSelect
        {{
            *Order: PAGE_SETUP.2
            *CallbackID: {CUSTOM_CALLBACK}
            *Params: LIST(PhysPaperWidth, PhysPaperLength)
        }}
    }}""")
    return "\n".join(out)


def check_custom_margins(indexed, model):
    """Every continuous tape as a custom size: Unidrv's printable area (rule in gpd_papers) must be centred on the
    paper (left = right margin), and the plug-in's centring (PlaceLine: crop = (surface - band) / 2) must then put
    surface x = 0 where the application thinks it is: band dot = paper dot - the tape's left margin."""
    cont = [m for _, m in indexed if m["kind"] == 10 and m["lanes"] == 1]
    max_area = max(m["printable_dots"] for m in cont)
    for m in cont:
        w, band = m["width_dots"], m["printable_dots"]
        left = max(CUSTOM_MIN_LEFT_DOTS, (w - max_area) // 2)
        surface = min(max_area, w - left)
        right = w - left - surface
        tape_left = (w - band) // 2
        crop = (surface - band) // 2
        if abs(left - right) > 1 or left + crop != tape_left:
            raise SystemExit(f"{model}: custom size {m['name']} ({w} dots): margins {left} / {right}, surface {surface}, "
                             f"plug-in crop {crop}: x = 0 lands {left + crop - tape_left} dots off")


def gpd_options(tpl, options):
    """Keeps the template's @@IF name@@ ... @@END@@ blocks of the model's options, drops the others."""
    out, keep = [], True
    for line in tpl.splitlines(keepends=True):
        s = line.strip()
        if s.startswith("*% @@IF ") and s.endswith("@@"):
            keep = s[len("*% @@IF "):-2] in options
        elif s == "*% @@END@@":
            keep = True
        elif keep:
            out.append(line)
    return "".join(out)


def gpd_cut_every():
    out = []
    for n in range(1, 100):
        out.append(f"""    *Option: N{n}
    {{
        *Name: "{n}"
        *Command: CmdSelect
        {{
            *Order: DOC_SETUP.11
            *CallbackID: {CUT_EVERY_BASE + n}
        }}
    }}""")
    return "\n".join(out)


def cpp_table(media, models):
    """models: media index -> bit mask of the models that have the entry (bit = ql::Model)."""
    lines = ["// Generated by driver/gen/media.py from docs/data/*.json (MODELS) - do not edit.",
             "// { ptd id, width mm, length mm, kind, printable dots (per strip), printable lines, pad start bits,",
             "//   strips (multi-up), models (bit mask of ql::Model), pad start bits on the 1296-dot head,",
             "//   paper width dots }"]
    for i, m in enumerate(media):
        kind = "MediaKind::Continuous" if m["kind"] == 10 else "MediaKind::DieCut"
        lines.append(f'{{ {m["ptd_id"]}, {m["width_mm"]}, {m["length_mm"]}, {kind}, {m["printable_dots"] // m["lanes"]}, '
                     f'{m["printable_lines"]}, {m["pad_start"]}, {m["lanes"]}, 0x{models.get(i, 0):X}, {m["pad_wide"]}, {m["width_dots"]} }},  // {m["name"]}')
    return "\n".join(lines) + "\n"


def cpp_media_info(blocks):
    lines = ["// Generated by driver/gen/media.py from Brother's captures (reference/captures) - do not edit.",
             "// { model mask (1 << ql::Model), ptd id, ESC i U w payload: 01 + Brother's 127-byte media block }"]
    for (ptd_id, block), mask in sorted(blocks.items(), key=lambda e: (e[0][0], e[1])):
        lines.append(f"{{ 0x{mask:05X}, {ptd_id}, {{ " + ", ".join(f"0x{x:02X}" for x in block) + " } },")
    return "\n".join(lines) + "\n"


def cs_table(media, models):
    def kind(m):
        if m["kind"] == 10:
            return "MediaKind.Continuous"
        return "MediaKind.Round" if "Dia" in m["name"] else "MediaKind.DieCut"

    rows, seen = [], set()
    for m in single(media):
        if m["ptd_id"] in seen:  # a model's own entry of a paper: same size and name for the label tool
            continue
        seen.add(m["ptd_id"])
        cont = m["kind"] == 10
        rows.append(f'            new Media({m["ptd_id"]}, "{m["name"]}", {kind(m)}, {m["width01"]}, {m["length01"]}, '
                    f'{dots_to_um(m["width_dots"])}, {0 if cont else dots_to_um(m["length_dots"])}, {m["offy"]}, '
                    f'{m["cont_min"] if cont else 0}, {m["cont_max"] if cont else 0}, {m["cont_default"] if cont else 0}, '
                    f'{m["printable_dots"]}),')
    return ("// Generated by driver/gen/media.py from docs/data/*.json - do not edit.\n"
            "namespace QlLabels\n{\n"
            "    static partial class MediaTable\n    {\n"
            "        // id, name, kind, width / length (0.1 mm, length 0 = continuous), page width / length as\n"
            "        // the driver's GPD defines it (0.001 mm), top offset (300-dpi dots; continuous: the default\n"
            "        // feed), continuous min / max / default length (0.1 mm), printable width (300-dpi dots)\n"
            "        public static readonly Media[] All =\n        {\n"
            + "\n".join(rows) +
            "\n        };\n\n"
            "        // the driver's multi-up papers: no label formats on them, but their names are taken\n"
            "        public static readonly string[] MultiUpNames = { "
            + ", ".join(dict.fromkeys(f'"{m["name"]}"' for m in media if m["lanes"] > 1)) + " };\n\n"
            "        // short name, driver name, status model code (byte 4 of the status reply), two-colour,\n"
            "        // shortest and longest continuous label (0.1 mm), the model's papers\n"
            "        public static readonly QlModel[] Models =\n        {\n"
            + "\n".join(models) +
            "\n        };\n    }\n}\n")


def model_bit(md):
    """ql::Model value of a MODELS row (bit in the media table's model mask)."""
    return 0 if md["start"] == 1 else md["start"] - 40


def main():
    media = all_media()
    cs_models = []
    model_bits = {}
    with open(os.path.join(DRIVER, "gpd", "template.GPD.in"), encoding="ascii") as f:
        tpl = f.read()
    bits = [model_bit(md) for md in MODELS]
    if len(set(bits)) != len(bits) or sorted(bits) != list(range(len(bits))):
        raise SystemExit("MODELS: StartDoc callbacks must be 1 (QL-800) and 41, 42, ... without gaps")
    for md in MODELS:
        bit = model_bit(md)
        indexed = model_media(media, md["table"], md.get("head", NARROW_HEAD))
        check_custom_margins(indexed, md["short"])
        for i, _ in indexed:
            model_bits[i] = model_bits.get(i, 0) | (1 << bit)
        ids = ", ".join(str(m["ptd_id"]) for _, m in indexed if m["lanes"] == 1)
        max_len = max(m["cont_max"] for _, m in indexed if m["kind"] == 10)   # the GPD's custom-size maximum
        cs_models.append(f'            new QlModel("{md["short"]}", "{md["driver"]}", 0x{md["status"]:02X}, '
                         f'{str(md["red"]).lower()}, {md["min"]}, {max_len}, new[] {{ {ids} }}),')
        gpd = (gpd_options(tpl, md["options"])
               .replace("@@MODELNAME@@", md["driver"])
               .replace("@@STARTDOC@@", str(md["start"]))
               .replace("@@GPDFILE@@", md["gpd"] + ".GPD")
               .replace("@@PAPERSIZES@@", gpd_papers(indexed))
               .replace("@@CUTEVERY@@", gpd_cut_every())
               .replace("@@DEFAULT_PAPER@@", f"P{DEFAULT_PAPER_ID}")
               .replace("@@COLORMODES@@", GPD_RED_BLACK if md["red"] else ""))
        with open(os.path.join(DRIVER, "gpd", md["gpd"] + ".GPD"), "w", encoding="ascii", newline="\r\n") as f:
            f.write(gpd)
        print(f"{md['gpd']}.GPD: {md['driver']}, {len(indexed)} Brother papers{', black/red' if md['red'] else ''}, "
              f"options: {', '.join(sorted(md['options'])) or '-'}")
    with open(os.path.join(DRIVER, "src", "media_table.inc"), "w", newline="\r\n") as f:
        f.write(cpp_table(media, model_bits))
    with open(os.path.join(DRIVER, "src", "media_info.inc"), "w", newline="\r\n") as f:
        f.write(cpp_media_info(media_info_blocks()))
    if len(media) >= 100:
        raise SystemExit(f"{len(media)} media entries: the paper callback ID (10000 + feed * 100 + index) needs < 100")
    with open(APP_CS, "w", encoding="utf-8", newline="\n") as f:
        f.write(cs_table(media, cs_models))
    print(f"generated {len(media)} Brother papers ({len(single(media))} without multi-up for the label tool)")


if __name__ == "__main__":
    main()
