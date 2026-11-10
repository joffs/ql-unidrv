# QL Label Maker

A simple editor for designing and printing labels on the QL printers: text, lines, boxes and pictures, shown on
screen exactly as they will print. The name is the same in German; the UI follows the label tool's language.

## Starting it

- Start menu **QL Label Maker**, or
- in **QL Label Formats**: the button **Label Maker** (also in German; also menu *Tools*), or right-click a label
  format > **Design a label on this format...** (*Etikett mit diesem Format gestalten...*),
- or `QlLabelMaker.exe [file.qllabel] [--printer "name"] [--format "label format"]` (the editor's own program next to
  `QlLabelFormats.exe`; `QlLabelFormats.exe --editor ...` starts it too).

**One window** (`SingleInstance.cs`, `EditorForm.SingleInstance.cs`): a second start while QL Label Maker is open (in
this user session) hands its command line to the open window and ends; the window comes to the front (restored when
minimised). A file opens there after the usual *Save the changes?* question (Cancel keeps the label; the same file
already open: only to the front); `--printer`/`--format` without a file start a new label after the same question;
without arguments only to the front. While a dialog of the window is open (Save as, Print with data, a question) or a
print is being sent, the file waits until that is over. A local mutex `Local\QLUnidrv.LabelMaker.Instance` marks the
open window, which listens on a named pipe of this session and user (only this user may connect; the second start
checks that the listener is the same program in this session). An elevated window can't be reached from a normal start:
then a second window opens, as before. The language switch lets go before it restarts. QL Label Formats works the same
for a plain start and `--printer` (Printer setup's test print, *Save as label format*: the printer is chosen there and
the new format added); `--apply`, `--sync`, `--plan`, `--remove-forms`, `--setup`, `--install` and Setup.exe never take
part.

It opens on the printer chosen in the label tool (else the first QL printer) and on that printer's default label
format. On a USB printer, a new empty label switches to a format that fits the loaded roll.

## The window

The window has a tool palette on the left (*Text, Linie, Rahmen, Bild, Barcode*), one row with the printer, the
label, the copies and **Print** (*Drucken*), the **Arrange toolbar** above the canvas and the settings panel, the canvas,
and a settings panel on the right. Nothing is hidden behind an overflow arrow from 1024 px window width, also at 150 %
display scaling (the window opens at 1100 px; narrowed towards its 780 px minimum, the toolbar's last fields go behind
»).
**Drawing objects** (since 0.23; `EditorForm.Draw.cs`, `EditorCanvas.Draw.cs`):
a click on a tool in the palette switches it on (`SelectTool`, event `ToolChanged`; one tool per window, a tab switch
keeps it); the cursor is a crosshair and a drag on the label, or the grey area around it, draws the object with a live
outline (a shape tool shows the shape itself: triangle, ellipse, polygon, star; the line a thin line): a line from A
to Z, a shape, paragraph text or a code in the dragged area (a QR code square inside it), artistic text with the dragged
height as its line (font size; under 6 pt the default size). A drawn text (artistic or paragraph, by click or drag)
starts empty with the blinking caret, no placeholder (CorelDRAW); left empty (Esc, a click beside it, the arrow,
another tool, a tab switch; also typed and deleted again) it goes again, without an undo step; typed in, Esc or the
arrow ends the editing and leaves it selected. Insert (Ctrl+T, Ctrl+Shift+T, Enter on a tool) keeps the placeholder
text, selected, as before. Choosing the arrow or another tool always ends a text's editing. A wheel zoom or scroll
during a drag keeps its start on the label. A plain click (within the system's drag size) places the default size at
the click point. Shift while dragging: a line horizontal, vertical or at 45°, a shape square, round or regular
(equilateral triangle, regular polygon or star); pressing or releasing Shift during the drag switches at once, also
without a mouse move (the outline and the status bar follow; the canvas reads the keyboard's Shift state itself, on
every mouse move and with a short timer during the drag). Tip: in a Parallels VM on a Mac a Shift pressed during
a drag doesn't reach Windows until another key comes; tap Ctrl once while holding Shift. A triangle, a polygon with
an odd number of corners or a star with an odd number of points dragged upwards from its start point is drawn
upside down (turned 180°; every shape is symmetric left to right, so a drag to the left changes nothing), as in
PowerPoint; the outline shows it. The drawn corners and line ends snap to the label's edges, centre lines and printable area
edges as moving does (same tolerance, the dotted guides show; with Shift only along the constrained direction, so a
line stays level and a shape regular); Alt held is free (read live like Shift). The right mouse button during a drag
cancels it like Esc (no object, no context menu, the tool stays on). The tool stays on for the next object; the picture tool asks for the
file first and goes back to the arrow after one picture. With a text tool, a click on a text edits it; other tools draw
over objects. Each drawn object is one undo step. Esc: a drag in progress ends without an object, then a text edit
ends, then the arrow is back. Enter on a tool button, the *Insert* menu and the keys (Ctrl+T, Ctrl+L, ...) still insert
at a free place and leave the active tool as it is.
The toolbar (always visible) holds, from left to right: duplicate, delete; turn 90° left / right; align left, centre,
right, top, middle, bottom; distribute horizontally / vertically; bring to front, forward, backward, to back; then the
**text group** (`EditorForm.TextBar.cs`): **B I U** (German *F K U*) and **Invert**; align text left, centre, right,
**justify** (*Blocksatz*); **line spacing** (an icon and a box: 1.0 / 1.15 / 1.5 / 2.0 or a value of one's own, 0.5 to
3) and **letter spacing** (an icon and a number with arrows, pt). Each button's tooltip gives its name, its key and,
for aligning, what it aligns with (*an der Auswahl* / *am druckbaren Bereich*); a button that can't do anything for
the selection is greyed, nothing is hidden or moves. The text group is greyed without a single selected text; for
artistic text the alignment and the line spacing are greyed (*nur bei Mengentext*). It stays usable while a text is
edited (the object commands are greyed then). B, I, U, Invert and the letter spacing act on the selected characters
while editing (nothing selected: on what is typed next), else on the whole text; a format that differs inside is
**hatched** (tooltip *teilweise* / *partly*): the first click sets it for all, the next removes it everywhere, as in
Word; a letter spacing that differs leaves the field blank. Alignment and line spacing always apply to the whole text.
Inverted characters (a run's `"invert": true`) get a black band behind them, the height of their line (Word's
shading); the whole text inverted is the object's `"invert"`: an artistic text in a black box 1 mm beyond the frame, a
paragraph text in bands behind its lines (from 0.3 mm before the first to 0.3 mm after the last character; the bands of
two lines meet halfway between them, at any line spacing; an empty line and the rest of the frame stay white). A run's
`"spacing"` (pt) is its letter spacing; RTF copies and pastes them as `\expnd`/`\expndtw` and black `\chcbpat` /
`\highlight` with white `\cf` (a dark highlight or shading pastes as inverted).
Keys: while editing Ctrl+B/I/U and Ctrl+L/E/R/J (alignment; for artistic text they do nothing); with the text only
selected Ctrl+Shift+F (bold: Ctrl+B adds a rectangle then), Ctrl+I, Ctrl+U and Ctrl+J. The whole bar needs about 870 px (100 %).

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

**Text is edited on the label** (since 0.22; `EditorCanvas.Editing.cs`, `EditorForm.TextEdit.cs`, `TextEditing`): a
double-click into a text starts editing with the caret there; F2, Enter, the panel's
**Edit text (F2)** (*Text bearbeiten (F2)*) or typing a character on the selected text start it with the caret at the
end; a new text from the palette is only selected (it can be moved first); typing on it, or a field inserted before any
editing, replaces its placeholder "Text". While editing: a thin solid frame without
handles, the status bar says "Editing text – Esc or a click beside it ends.", fields show as codes in grey chips, the
object commands are greyed and Edit > Cut/Copy/Paste/Select all/Undo act on the characters (one undo step per editing
session afterwards). Esc, a click beside the text or another tool ends it; a text left empty is removed. Enter: a new
paragraph in paragraph text, the end of editing in artistic text; Tab and PgUp/PgDn do nothing. A paragraph text's
cut-off lines show light grey below its box while editing (**View > Show overflow while editing**, *Überlauf beim
Bearbeiten zeigen*, on by default, per user); off, the text scrolls inside the box to the caret's line. The panel has
no text box: one hint line (*Doppelklicken Sie den Text auf dem Etikett, um ihn zu bearbeiten.*) and the button.

| Insert (menu / palette) | Settings (right panel) |
|---|---|
| **Artistic text** (Ctrl+T, *Grafiktext*) | one line (**Kind** *Grafiktext*): the box always fits the text; drag any handle to make the text larger or smaller (in proportion, the opposite side stays), or type the size in points. Font and size in the panel; bold / italic / underline, **Invert** and **letter spacing** in points (*Zeichenabstand*, −5 to +20) in the toolbar's text group, for the whole text or the selected characters. **Invert** (*Invertieren*) on the whole text: white on a black box 1 mm larger than the text's box; the box covers what lies below and turns with the text, the selection is drawn light on it. On selected characters: white on a black band as high as their line. Enter does nothing, pasted line breaks become spaces |
| **Paragraph text** (Ctrl+Shift+T, *Mengentext*) | text over several lines in a box you set with the handles: it wraps at the box's edge at its font size, a word longer than a line breaks at characters. Line alignment left, centre, right or **justify** (*Blocksatz*: the spaces of a wrapped line widen so that it reaches both edges of the box; the last line of a paragraph, and a line of one word, stay left, as in Word; drawn, edited and printed alike, `EditorRender.Gap`), **line spacing** (*Zeilenabstand*, 0.5 to 3 × the font's), letter spacing, **Invert** as for artistic text (but the whole text inverted gets a black band behind each line, no box over the frame): all in the toolbar's text group. In the file `"align": "justify"`; RTF `\qj` pastes as justify. A text that doesn't fit is cut off at the box; on screen the box gets a red edge and the panel says so in red. **Kind** switches between the two: paragraph text becomes one line (line breaks become spaces), artistic text becomes paragraph text in a box around its text. **Words in their own format** (*Wörter mit eigenem Format*, artistic and paragraph text): select words while editing on the label, then Ctrl+B/I/U (also Ctrl+Shift+F/K/U) or the toolbar's **B / I / U**, the panel's **Font** or **Size** apply to them; with nothing selected they apply to what is typed next, and when the text isn't being edited they apply to the whole text; mixed values show blank (font, size) or hatched (B, I, U). A paste from Word or WordPad (RTF) keeps its formats, plain text takes the format at the cursor; in artistic text line breaks become spaces. **Kind** keeps the words' formats both ways (line breaks become spaces); the handles of an artistic text scale every word's size in proportion. In the file: `"runs"` on the text object, each run with only what differs (`{"t":"M4","bold":true}`); `"text"` stays the whole text, so older versions show it in one format |
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
One text object: `SetRuns` makes the most frequent format the object's own; a text frame for several lines, else a
single-line text (other formats are its runs in both). Colours, spacing and lists are dropped; a missing font gets Segoe UI and the status
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
text frame with that alignment (one line: a single-line text); other fonts, sizes or styles inside become
its runs (`TextObject.SetRuns`), in both kinds. Turned text (escapement or world transform) keeps its angle. A font that isn't installed is
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

**File > Recent labels** (*Zuletzt geöffnet*, `EditorForm.Recent.cs`, `RecentLabels.cs`): the last 15 labels opened
or saved, the newest first, the digits 1-9 as access keys, the folder grey beside the name (labels of the same name
differ there). The x on the marked entry (mouse) or Del removes one and the menu stays open; *Clear list...* asks
first. A missing local file is grey with "not found" when the menu opens and a click asks whether to remove it; a
network folder (UNC or mapped drive) is looked at only on a click, so the menu never waits for a server, and "folder
not reachable" offers *Retry* and removes nothing by itself. Stored per user in `HKCU\Software\QLUnidrv\LabelMaker`
(value `RecentLabels`), no file; uninstalling removes it with the other settings. No Windows jump list.

## Limits

Black only (no red on DK-22251 two-colour tape yet), barcodes Code 128 / GS1-128 / EAN-13 / QR only, no serial numbers or
printing from a list. See
[differences-from-brother.md](differences-from-brother.md) for what Brother's P-touch Editor offers beyond this.
