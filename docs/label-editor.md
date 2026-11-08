# QL Label Maker

A simple editor for designing and printing labels on the QL printers: text, lines, boxes and pictures, shown on
screen exactly as they will print. The name is the same in German; the UI follows the label tool's language.

## Starting it

- Start menu **QL Label Maker**, or
- in **QL Label Formats**: the button **Label Maker** (also in German; also menu *Tools*), or right-click a label
  format > **Design a label on this format...** (*Etikett mit diesem Format gestalten...*),
- or `QlLabelMaker.exe [file.qllabel] [--printer "name"] [--format "label format"]` (the editor's own program next to
  `QlLabelFormats.exe`; `QlLabelFormats.exe --editor ...` starts it too).

It opens on the printer chosen in the label tool (else the first QL printer) and on that printer's default label
format. On a USB printer, a new empty label switches to a format that fits the loaded roll.

## The window

The window has a tool palette on the left (*Text, Linie, Rahmen, Bild, Barcode*), one row with the printer, the
label, the copies and **Print** (*Drucken*), the **Arrange toolbar** above the canvas and the settings panel, the canvas,
and a settings panel on the right. Nothing is hidden behind an overflow arrow, also at 150 % display scaling.
The toolbar (always visible) holds, from left to right: duplicate, delete; turn 90° left / right; align left, centre,
right, top, middle, bottom; distribute horizontally / vertically; bring to front, forward, backward, to back. Each
button's tooltip gives its name, its key and, for aligning, what it aligns with (*an der Auswahl* / *am druckbaren
Bereich*); a button that can't do anything for the selection is greyed.

- **Printer** and **Label** (*Drucker*, *Etikett*, top row): the label is one of your label formats (listed first) or one of Brother's
  papers that the printer's model offers.
- **Length** (*Länge*, right panel while nothing is selected; continuous tape only): any length the printer can
  print (QL-800 and other narrow-head models 12.7–1000 mm, the wide-head models 25.4–3000 mm, the older models from 25
  mm). Type it, or drag the grip on the label's end (1 mm steps, Alt: 0.1 mm); a measure under the label shows it.
  The label list then says *own length* (*eigene Länge*). A length outside the printer's range is refused with
  the range under the field. **Automatic from the content** (*Automatisch nach dem Inhalt*): the end follows the
  last object, with the same margin as at the start. **Save as label format...** (*Als Etikettenformat
  speichern...*) opens *QL Label Formats* with a new format of this paper and length, which is kept after *Save &
  apply* (needed only for printing from other programs). Die-cut and round labels show their fixed length instead.
  The editor asks the driver for this page directly (a custom size, no Windows form), so nothing needs to be saved
  first.
- **Direction** (*Richtung*, right panel while nothing is selected; also **View > Turn view**, *Ansicht > Ansicht
  drehen*, Ctrl+Shift+R): write **along the label** (its length runs left to right; for long labels) or
  **across the tape** (the tape's width runs left to right; for short, wide labels such as 62 × 29 mm and for round
  labels). A new label starts with the usual one for its paper. The objects keep their places on the screen.
- **Resolution 300 × 600 dpi** (*Auflösung*, right panel while nothing is selected; also in the **Printer** menu):
  twice the dots along the label (the driver's high resolution); starts with the printer's setting.
- The canvas shows the label at its real proportions. **Hatched** = the printer can't print there (feed margin,
  sides). Continuous tape shows the cut lines (dashed). **Zoom** (status bar, bottom right, as in Office): *Fit*
  (*Einpassen*, a toggle: the label follows the window, Ctrl+0), − / slider / + (25 % to 800 %; also Ctrl+Plus /
  Ctrl+Minus and the mouse wheel around the pointer over the label and the grey area around it, with or without Ctrl, also while a panel field has the focus; Shift+wheel or a tilted wheel scrolls left and right; over the status bar the wheel zooms too), the percentage opens the menu that is also in **View**: *Fit*,
  100 % (real size on the screen, Ctrl+1), **Dot for dot** (*Punktgenau*): 1 or 2 screen pixels per printer dot (Ctrl+2,
  Ctrl+4; 208 % / 417 % at 150 % display scaling, 312 % / 625 % at 100 %). There the label is shown sharp, dot for dot;
  at every other zoom it is smoothed (uneven pixels per dot would show stripes in dithered pictures). The slider has
  ticks at 100 % and at the dot-for-dot zooms and snaps to them.
- **Printer** menu (*Drucker*): check the loaded roll, **Printer status** (F6; the one status window of all QL programs),
  **Notify me about printer errors** and **Printer setup** (as in QL Label Formats).
  **Tools** (*Extras*): **QL Label Formats** (the only way there, as QL Label Formats opens QL Label Maker from its Tools menu), **Appearance** (*Darstellung*: like Windows, light or dark, for all QL programs, live) and **Language / Sprache**, as in QL Label
  Formats: the language of all QL programs; QL Label Maker restarts in it with the same file (saved first when
  changed), or a new label on the same printer and label format. **Help** (*Hilfe*): **QL Label Maker Help** (*Hilfe zu QL Label Maker*) opens the help
  contents in the default browser; **F1** opens the page of what is selected (text, picture, code, line/box; nothing or
  several kinds: the contents), in the dialogs the page of their window (the problems dialog before printing: codes);
  then **About QL Unidrv**. An ellipsis ("...") only on commands that ask
  something before they act (Open, Save as, Picture, Replace); entries that open a window or a program have none.
- The picture on the canvas **is** the print: the label is rendered in black and white at the printer's own dots,
  and printing sends exactly these dots.

## Objects

| Insert (menu / palette) | Settings (right panel) |
|---|---|
| **Artistic text** (Ctrl+T, *Grafiktext*) | one line (**Kind** *Grafiktext*): the box always fits the text; drag any handle to make the text larger or smaller (in proportion, the opposite side stays), or type the size in points. Font, bold / italic / underline, **letter spacing** in points (*Zeichenabstand*, −5 to +20). **Invert** (*Invertieren*): white on a black box 1 mm larger than the text's box; the box covers what lies below and turns with the text, the selection is drawn light on it. Enter does nothing, pasted line breaks become spaces |
| **Paragraph text** (Ctrl+Shift+T, *Mengentext*) | text over several lines in a box you set with the handles: it wraps at the box's edge at its font size, a word longer than a line breaks at characters. Line alignment, **line spacing** (*Zeilenabstand*, 0.5 to 3 × the font's), letter spacing, **Invert** as for artistic text. A text that doesn't fit is cut off at the box; on screen the box gets a red edge and the panel says so in red. **Kind** switches between the two: paragraph text becomes one line (line breaks become spaces), artistic text becomes paragraph text in a box around its text. **Words in their own format** (*Wörter mit eigenem Format*, frame only): select words in the **Text** box, then **B / I / U** above it (*F / K / U*; Ctrl+B/I/U, also Ctrl+Shift+F/K/U), **Font** or **Size** apply to them; with nothing selected B/I/U apply to what is typed next, and with the box not active the rows apply to the whole text; mixed values show blank or ■. A paste is the text only, in the format at the cursor. Switching such a paragraph text to artistic text asks first and keeps the first word's format. In the file: `"runs"` on the text object, each run with only what differs (`{"t":"M4","bold":true}`); `"text"` stays the whole text, so older versions show it in one format |
| **Line** (Ctrl+L) | line width, **angle** (*Winkel*, 0–180°, 0° level) and **length** (*Länge*); typed values keep the left end (a vertical line: the lower end); drag an end to move it (Shift: horizontal or vertical) |
| **Box** (Ctrl+B) | line width, filled, ellipse |
| **Picture** (Ctrl+I) | **Dither** for photos and shades, or **Threshold** (slider) for logos and drawings; **Invert**; *Replace...*, *Original proportions*, *Fit to label* (as large as the printable area, in proportion, centred; also in the object menu, for several selected pictures together; `EditorForm.Fit.cs`). Transparent parts print white. Files: PNG, JPG, BMP, GIF, TIFF, WebP, HEIC/HEIF, a PDF's first page, **SVG**: kept as a vector (`SvgPicture.cs`, Windows' own Direct2D SVG renderer, no DLL) and drawn at the printer's dots for the picture's size, so it stays sharp at any size; the stored 2000-px PNG is only the editor's draft. The file is cleaned on insert and on open (no DTD, scripts, event attributes or references to other files or the web; only `#id` and embedded `data:image/` pictures). CSS in `<style>` (Illustrator and others keep their colours there) becomes the elements' attributes on insert and on open (selectors: element, `.class`, `#id`, combinations such as `path.cls-1`, and lists of those; CSS's order, `style="..."` wins); other selectors and `@media` stay unapplied. Text becomes paths of the installed fonts on insert and on open, as a drawing program's "object to path" does (`SvgPicture.TextToPaths`: x/y, font-family with fallbacks, size, weight, style, text-anchor, letter-spacing; a missing font is replaced by a substitute, and the panel says "This picture prints with a substitute font.", its (i) naming the font); text along a path, vertical text and per-letter positions stay text. Direct2D doesn't draw such text, filters or masks; while such an SVG is selected the panel says so in one plain line ("Parts of this picture can't be printed – tip: insert it as a PNG."), its (i) names what is missing in plain words and how to print it all (`SvgPicture.Missing`) |
| **Barcode** (Ctrl+K) | **Type**: Code 128 (letters, digits, ASCII symbols; control characters as tokens `{tab}` `{cr}` `{lf}` `{gs}` `{rs}` `{eot}` `{fnc1}` `{xNN}`, shown with an (i) under the content, encoded but not printed under the bars), GS1-128 (elements as `(AI)data`, e.g. `(01)00000000000000(17)261231(10)AB-12`: the brackets are printed under the bars, not encoded; FNC1 separators are added; wrong lengths and check digits show in the problem line; file name `gs1-128`), EAN-13 (12 digits, the check digit is added; or 13); **content**; text under the bars. No *Invert*: the scanners of these codes read dark on light only. The code fills its box as far as whole printer dots per bar/module allow; the quiet zone scanners need lies around the box (see *Quiet zone* below). A content that can't be encoded, or a box so narrow that the bars at one printer dot each don't fit even with both quiet zones (*Zu schmal: mindestens N mm*), is shown in red in the panel and with a red dashed frame on the label. Printing then asks first (*Das Etikett hat Fehler*: the codes and their errors, *Trotzdem drucken* / *Abbrechen*, Abbrechen is the default): a code that can't be encoded prints blank, a too narrow one prints cut off, as on the canvas. Warnings in the same line, printed without asking: a box narrower than one dot per bar whose code still fits with its quiet zones (drawn whole over them: *Der Code ist breiter als sein Rahmen – besser mindestens N mm*), the code lies partly outside the printable area (cut off), bars narrower than 3 dots (an EAN-13 below 80 % of its nominal 0.33 mm, with the width in mm), the text under the bars doesn't fit |
| **QR code** (Ctrl+Q, *QR-Code*) | a matrix code, its own tool: **error correction** L/M/Q/H; **Contains** (*Enthält*): *Text* (any text, also umlauts), *Web address* (`https://` added if missing), *Contact* (name, company, phone, e-mail, address, website: a vCard 3.0 that phones offer to save), *Wi-Fi* (network name, password, WPA/WEP/open, hidden: phones offer to join), *E-mail* (address, subject, message), *Phone number*, *SMS* (number, message); each with its own fields in the panel, date fields allowed in all of them. **Invert**: white modules on black, the quiet zone printed black too (the standard allows it; phone cameras usually read it, older or simple scanners often don't, (i) in the panel). It fills its square box (width and height follow each other) as far as whole printer dots per module allow, the quiet zone around it; missing details or too much content are shown in red, and printing asks first (as for barcodes) |

### Quiet zone

Scanners need a clear margin around a code (the quiet zone): 4 modules on every side of a QR code, 10 left and right of
a Code 128, 11 left and 7 right of an EAN-13. The handles and the box are the code itself; the zone lies around it and
doesn't print. **While a code is selected** the canvas shows the zone as a light margin (tooltip *Ruhezone: bleibt frei,
wird nicht gedruckt.*), and the panel says *Heller Rand: Ruhezone, bleibt frei, wird nicht gedruckt.* with an (i) that
explains it. Unselected, the canvas shows only what prints. Something printed in the zone (you may let an object reach
in on purpose) turns the margin orange, the panel line names the object (*„Regal 3“ ragt in die Ruhezone ...*), and the
strip above the canvas says how many codes have objects in their quiet zone.

### Date and time fields

**Insert field ▾** (*Feld einfügen*, under the text and under a barcode's content) adds a field that is filled in
whenever the label is shown or printed: `{date}` today, `{date+30}` in 30 days (`{date-1}` yesterday), `{time}`
the time; a format after a colon, e.g. `{date:yyyy-MM-dd}`, `{date+7:dd.MM.yy}`, `{time:HH:mm}`. Without a format
the date and time look as in your Windows settings. `{{` and `}}` print single braces. Example: a freezer label
"Frozen {date} – use by {date+90}", or a Code 128 batch number `LOT{date:yyMMdd}`.

All objects: position and size in mm. **Rotation** (*Drehung*): text, text frames, boxes and pictures turn to any angle
(−180° to 180°): drag the **round grip** above the selected object's top edge (it turns with the object; it snaps to 0°, 90°, 180°, 270° with a dotted
guide; Shift: 15° steps; Alt: free; the angle is shown next to the mouse) or type the angle in the panel; its two
buttons and **Rotate** (Ctrl+R) turn by 90°; **Reset rotation** (*Drehung zurücksetzen*, the **0°** button between the
two in the toolbar and the panel, also in **Arrange** and the context menu; grey at 0°) sets it back to 0°. A turned object's handles still resize along its own sides. Pictures are
turned first and then made black and white (no moiré). Barcodes and QR codes turn in 90° steps only (a list in the
panel; scanners read them best straight), lines by their two ends. Drag to move; handles resize
(pictures keep their proportions at the corners, Shift: free; a single-line text changes its font size). Moving snaps to the label's edges and centre lines
and to the printable area's edges (pink guide; Alt: no snapping). Arrow keys move by 0.1 mm, Shift+arrow by 1 mm.
**Several objects**: Ctrl+click or Shift+click adds or removes one, dragging from an empty spot draws a frame
(everything it touches is selected), **Select all** (*Alles auswählen*, Ctrl+A). They get dashed frames without
handles; drag one or use the arrow keys to move them together, Del deletes them, Duplicate copies them; the panel shows
how many are selected. Click a single object to edit its properties.
**Arrange** (*Anordnen*) aligns one selected object with the printable area (left, centre, right, top, middle,
bottom); several objects align with each other (left = the leftmost object's left edge, and so on). The menu's grey
first line says which (*Richtet am druckbaren Bereich aus* / *Richtet an der Auswahl aus*). **Distribute horizontally /
vertically** (*Horizontal / Vertikal verteilen*, three or more objects): equal gaps, the first and last stay (objects
that overlap get equal distances between their centres).
**Layers**: a later object covers the earlier ones. *Bring to front* / *Bring forward* / *Send backward* / *Send to back*
(*In den Vordergrund* / *Eine Ebene nach vorne* / *Eine Ebene nach hinten* / *In den Hintergrund*) in **Arrange** and in
the object's context menu; keys Ctrl+PgUp / Ctrl+PgDn (one layer), with Shift all the way; on keyboards that type `]` and
`[` without AltGr also Ctrl+] / Ctrl+[ (the English menu shows these). **Right-click an object** (or the menu key,
Shift+F10) for its commands: Cut, Copy, Paste, Paste as plain text, Duplicate, Delete, Rotate 90°, Reset rotation,
Align, the four layer commands; on an empty spot: Paste, Paste as plain text (both at that spot), Select all.
**Edit**: Undo (Ctrl+Z), Redo (Ctrl+Y), Cut (Ctrl+X), Copy (Ctrl+C), Paste (Ctrl+V), Paste as plain text (Ctrl+Shift+V,
*Unformatierten Text einfügen*), Duplicate (Ctrl+D), Delete (Del). In a panel field they work on its text.
**Clipboard and drag and drop** (`EditorClipboard.cs`, `EditorForm.Clipboard.cs`): Copy writes the objects in our own
format `QLUnidrv.LabelObjects` (JSON as in the file) plus a 300 dpi DIB of them for programs without EMF (Paint), and an EMF
(`EditorRender.RenderEmf`: the print's Draw onto a GDI+ EmfPlusDual metafile in device pixels at the reference DC's
HORZRES/HORZSIZE, since GDI+'s own millimetres go by 96 dpi; text stays text with its fonts, lines and shapes vectors;
pictures and barcodes are rendered on their own at 300 dpi and placed in mm, white transparent). `EditorClipboard.SetCopy`
puts them on the clipboard with the Windows clipboard functions in the order EMF, own format, DIB (Windows adds
METAFILEPICT, BITMAP and DIBV5; WinForms' DataObject can't offer an EMF handle). No PNG: Word pastes a PNG whenever
one is offered, also behind the EMF (`labeltool\test\word-paste.ps1`, 2026-11-08). Paste takes
the first of: our own objects (at their place, 2 mm on while the spot is taken), SVG with real vectors (`image/svg+xml`,
`image/x-inkscape-svg`, `Scalable Vector Graphics`, or SVG source as the plain text), EMF (unless it only wraps one
bitmap), PNG, DIBV5, DIB, files (CF_HDROP: side by side; a `.qllabel` opens), RTF, plain text (a
text frame if it has line breaks). RTF that is only text (no `\pict`, `\object`, `\shp`, `\trowd`) comes before the
EMF and the bitmaps, because Word offers its text as an EMF too (dropped files still win). **RTF → runs**
(`RtfRuns.cs`, no RichTextBox): font table (names, `\fcharset`/`\cpg` code pages), `\f`, `\fs` (half points), `\b`,
`\i`, `\ul` and its kinds (`\ulc` is a colour), `\plain`, `\v` (hidden, left out), `\par`/`\line` ("\n"), `\tab`,
`\cell`/`\row`, `\ql`/`\qc`/`\qr` (used when every paragraph has the same, else left), `\'hh`, `\uN` with `\uc`.
One text object: `SetRuns` makes the most frequent format the object's own; a text frame for several lines or several
formats, else a single-line text. Colours, spacing and lists are dropped; a missing font gets Segoe UI and the status
bar names it. Line breaks in pasted runs are "\n" (EMF text too), as the text box writes them. HTML, URLs, PDF, OLE and program-native formats are never
used; all formats are read as bytes through COM (never WinForms' GetData, which would deserialize .NET objects). An EMF
picture stays a vector like SVG (file key `emf`, base64; `EmfPicture.Check` on paste and on open: header, record sizes,
20 MB), drawn with GDI+ at the printer's dots, its text without font smoothing there (smoothed edges are greys that
Dither scatters into dots: CorelDRAW's white text on white came out as a dotted patch; the preview stays smoothed); a pasted EMF from a program that also offered text says in the status
bar how to paste the text instead. Drops: the same order, only the drop's own data, placed at the drop point.
**Pasted and dropped pictures keep their size (1:1)** (since 2026-11-08), never shrunk, also when bigger than the
label (they hang over its edges; the print warning covers that; a message bar over the canvas and the panel says "Larger than the label." with a *Fit to label* link and ×, gone on ×, the next edit, an undo or after fitting; pictures only): EMF at its `rclFrame` (0.01 mm; CorelDRAW's reference
device is wrong, its frame right), SVG at its width/height as a browser reads them (mm, cm, in, pt, pc; px or no unit
= 1/96 in; else the viewBox in px), a bitmap at the frame of an EMF that only wraps it (Word's photo: the size Word
showed it at), else at its resolution (PNG `pHYs`, DIB `biXPelsPerMeter`, a file's own;
96 dpi without one or with an unlikely one under 10 or over 10000), a dropped PDF page at its page size. Placement:
the middle of the label (a paste), the drop or right-click point, kept on the label along each side it fits. Only
an EMF with an empty frame falls back to Insert > Picture's size (80 % of the printable area), which stays as it is.
**Text from EMF** (`EmfText.cs`): an EMF that holds only text (CorelDRAW keeps text as ExtTextOutW records with their
font) pastes as editable text, read without playing the EMF: map mode, window/viewport extents and origins, world
transforms, SaveDC/RestoreDC, ExtCreateFontIndirectW + SelectObject, SetTextAlign, ExtTextOutW/A with the dx array for
the width. The font size is lfHeight through the transforms to mm, then points (a positive lfHeight is a cell height:
scaled by the font's em/cell ratio). Runs on one baseline next to each other make a line (a space where the gap is a
word's); lines a line pitch apart with a common left edge, centre or right edge make a paragraph, which becomes one
text frame with that alignment (one line in one format: a single-line text); other fonts, sizes or styles inside become
its runs (`TextObject.SetRuns`), and then it is a text frame even on one line (a single-line text has one format). Turned text (escapement or world transform) keeps its angle. A font that isn't installed is
replaced by Segoe UI and the status bar names it. CorelDRAW sets a 0.01 mm page, then resets it to 1:1 before its
text but keeps the text's size and place in 0.01 mm, so Windows draws that text far outside the picture (a whole
label pasted as a picture lost all its text). A run outside the frame that the mapping from before the first SaveDC
puts inside is read with that mapping, and `EmfText.Repair` (called by `ImageObject.FromEmf`, so for pastes, drops
and .emf files) writes that mapping around each such text record and the old one back after it.
**Objects beyond the label** (after *Turn view* or a narrower or shorter label format): the canvas grows so that every
object stays visible; the parts outside the label are shown grey and hatched with a dashed edge (they don't print) and
can still be clicked and dragged back (by the object's box). A strip above the canvas says how many objects have printed
dots beyond the label (*... ragen über das Etikett hinaus*) and how many only in the hatched unprintable margin (*...
reicht in den schraffierten Rand*); it goes away when none does. Both lines go by the object's own dots as printed
(glyphs, an inverted text's or a filled box's black, a line's width, a dithered picture, a turned corner;
`EditorRender.CutOff`), so an empty part of a box there gives no hint. Only the strip: no question before printing
(cutting something off can be intended).

## Printing

**Print** (Ctrl+P) with the number of **Copies**. Before printing on a USB printer the editor asks the printer for
the loaded roll, as the label tool's test print does: a printer error, a roll that doesn't fit the label (the
printer would stop with a red LED), or *Use Printer Offline* is shown first. A label format that Windows doesn't
know yet (not saved with *Save & apply*) can't be printed: the status bar then says "approximate".

## Files

**File > Save** stores the label as a `.qllabel` file wherever you choose (JSON: the label format's name, its
paper and length, the view, and the objects; pictures are embedded). Opening a file whose label format is gone uses
a format or paper with the same paper and length, and says so. Files from a newer QL Label Maker version open too; objects
it doesn't know are left out (with a note), unknown settings are kept when saving. The file carries a format version
(`"version": 1` = the format of 0.18, the base); a later version that changes the format raises it and converts older
files on opening.

## Limits

Black only (no red on DK-22251 two-colour tape yet), barcodes Code 128 / GS1-128 / EAN-13 / QR only, no serial numbers or
printing from a list. See
[differences-from-brother.md](differences-from-brother.md) for what Brother's P-touch Editor offers beyond this.
