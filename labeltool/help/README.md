# QL Label Maker help

The user manual of QL Label Maker as plain HTML pages that work offline (no web fonts, no scripts, no external files).
The programs open them in the default browser: *Help* menu and F1, each window its own page (`src/HelpPages.cs`). The
build copies this folder without `img/shots/`, `formats/img/shots/` and this
README next to the programs (`out\tools\help`), and the install puts it into `C:\Program Files\QLUnidrv\help`.

- **Fixed names:** the programs open pages by their file names, so a renamed or removed page needs the same change in
  `HelpPages.cs` and its callers. The sections' `h2` carry `id`s, English and the same in both languages
  (`codes.html#qr-code`), for links into a page; keep them when a heading changes.
- **Printing:** `help.css` has a print style (light colours, no navigation and pager), so the browser's *Print* /
  *Save as PDF* gives a clean page.

- `de/` and `en/`: one page per topic, the **same file names in both languages**; each page links to its twin
  (`../en/text.html` ↔ `../de/text.html`). `index.html` is the overview and contents, `appendix.html` the keys and the
  command line.
- `help.css`: the look of all pages (light and dark, phone width). `img/mark.svg`: the editor's icon in the header.
- `img/de/`, `img/en/`: the pictures, same names in both languages, named `<page>-<what>.png`.
  `img/shots/make-shots.ps1` cuts them from window screenshots it takes itself (its list says which screenshot and
  which part): it runs the built QL Label Maker with the labels in `img/shots/labels/` through
  `out\tools\uitest\deskrun.exe` (built by `tools\uitest\build.cmd`; a desktop of its own, the user's mouse and focus stay untouched), at 100 % and 150 %,
  German and English.

  To replace a picture, write a new file with the same name; the pages need no change.
- `formats/`: the user manual of **QL Label Formats** in the same style and layout (`formats/de/`, `formats/en/`,
  `formats/img/`; the pages use `../../help.css`). Its pictures: `formats/img/shots/make-shots.ps1` (the program with
  the sample formats of `labels-de.json` / `labels-en.json` and one printer, the QL-800 on USB as test printer;
  nothing is saved or printed).

Texts: UI names exactly as in the program (German from `src/Strings.de.cs`, English from the program's own texts),
facts from `docs/label-editor.md`. German: „funktioniert“, not „geht“. Topics not written yet are listed in the
contents as „folgt“ / "to follow".
