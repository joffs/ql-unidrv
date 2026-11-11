# Printing labels from the command line: QlLabelPrint.exe

`QlLabelPrint.exe` prints a label made in QL Label Maker (`.qllabel`) with data from a CSV or JSON file, from
`--set` values or from a script. It is installed with the programs (`C:\Program Files\QLUnidrv\QlLabelPrint.exe`),
has no window and never asks anything: what the editor would ask about is reported, with an exit code.

Data fields: a text, a barcode's content or a QR field in the label may contain `{Name}` or `{Name|default}`
(QL Label Maker, see [label-editor.md](label-editor.md)). Each record of the data fills them; `{date}` and `{time}`
work as in the editor, and a value from the data is printed as it is (a `{` in the data stays a brace).

## Commands

```
QlLabelPrint --print <label.qllabel> [--data <file.csv|file.json|->] [--set Name=Value ...] [options]
QlLabelPrint --fields <label.qllabel>     the label's data fields (name, default, required)
QlLabelPrint --printers                   the QL printers: model, port, Windows' default (*), loaded roll
QlLabelPrint --help
```

| Option | Meaning |
|---|---|
| `--data <file>` | CSV (first row = column names, matched to the fields by name, upper/lower case ignored; `;` `,` or tab; UTF-8, UTF-16 or Windows-1252) or JSON (`.json`, or content starting with `{` or `[` unless the file is `.csv`: one object `{"Name":"Value"}` or a list of them; values text, number, `true`/`false` or `null`, no nested objects or lists; a name missing in an object is an empty cell). `-` reads stdin (JSON when it starts with `{` or `[`, else CSV). A column `labelname` names each label (see `--name`, the problems, QL Label Maker's preview) |
| (no `--data`) | The label's remembered data file (QL Label Maker's *Print with data* stores it in the `.qllabel`), looked for next to the label first, then at its full path; not there: code 3 with the path. Without one (or with `--set`): one label |
| `--set Name=Value` | A value for every record (repeatable); without `--data` one label is printed, the remembered data file isn't used |
| `--printer <name>` | The printer. Without it: Windows' default printer if it has the label's paper, else the only QL printer that has it, else the only one whose loaded roll fits |
| `--copies N` | Copies of each label (1-999). A column `copies` in the data multiplies (0 = skip that row) |
| `--rows 2-10` | Only these data rows (1 = the first row after the header; `5` or `5-` too) |
| `--name <labelname>` | Only the rows whose `labelname` column has this value (upper/lower case ignored; repeatable). A name no row has stops with code 3 |
| `--missing-empty` | A field without a value prints empty instead of stopping (a field's own default still wins) |
| `--strict` | Warnings (a barcode that may not scan, a text cut off at its frame) stop with code 3 instead of printing |
| `--preview <folder>` | One PNG per row (`row-0001.png`, ...), as shown in the editor; nothing is printed |
| `--dry-run` | Check everything (label, data, printer, roll), print nothing |
| `--no-roll-check` | Print without asking the printer for its roll (never the default) |
| `--json` | One JSON object on stdout instead of text |
| `--lang en\|de` | The language of the messages (default: the user's language, as in the other programs) |

All or nothing: every record is filled and checked before the first label is sent; then all labels go as one print
job, one page per label (at most 10,000).

## Printer and roll

Before printing, the printer is asked for its state and loaded roll, as QL Label Maker does. It stops with code 5
when the queue is set to "Use Printer Offline", the printer reports an error (cover open, no roll), the roll doesn't
fit the label, or the roll can't be read (a shared printer whose PC doesn't run the status service, see
[status-service.md](status-service.md), or a port without status). A roll that doesn't fit would stop the printer
with a red LED, so only `--no-roll-check` prints without the check.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | printed (or preview / dry run OK) |
| 1 | wrong usage (unknown option, missing value) |
| 2 | the label file is missing, too large (over 5 MB) or can't be read |
| 3 | data error: a field without a value, a bad `copies` cell, a bad CSV/JSON file, too many labels; with `--strict` also warnings. The message names the row and field |
| 4 | printer not found, not a printer of this driver, without the label's paper, or several fit and none was chosen |
| 5 | printer not ready: offline, printer error, wrong roll, or roll unknown |
| 6 | Windows refused the print job |

## JSON answer

`--json` writes one object; the keys are the same in every language:

```json
{"ok":true,"code":0,"error":null,"problems":[],"command":"print",
 "template":"D:\\Labels\\Artikel.qllabel","printer":"Brother QL-800 (Unidrv)","label":"62 mm x 50 mm",
 "fields":{"found":["Artikel"],"missing":[],"unused":["Lager"]},
 "records":3,"labels":3,"roll":"62 mm continuous","printed":true}
```

`problems` lists each record's problems: `row`, `name` (the row's `labelname`, else empty), `kind` (`missing`, `copies`, `barcode`, `overflow`), `field`,
`message` and `blocks` (true: the reason nothing was printed). `--printers` answers with `printers` (`name`,
`model`, `port`, `default`, `offline`, `status`, `message`, `roll`), `--fields` with `fields` (`name`, `default`,
`required`).

## Examples

```
QlLabelPrint --print D:\Labels\Artikel.qllabel --data D:\Labels\liste.csv
QlLabelPrint --print D:\Labels\Artikel.qllabel --set Artikel="Schraube M4" --set Preis=0,15 --copies 5
QlLabelPrint --print D:\Labels\Artikel.qllabel --data liste.csv --rows 2-4 --preview D:\Vorschau
QlLabelPrint --print D:\Labels\Artikel.qllabel --data liste.csv --name A1234
type order.json | QlLabelPrint --print D:\Labels\Versand.qllabel --data - --json
```

In PowerShell the exit code is `$LASTEXITCODE`, in cmd `%ERRORLEVEL%`.
