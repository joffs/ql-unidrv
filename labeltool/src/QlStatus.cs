// The QL printer's 32-byte status message: decoding and plain-language explanations.
//
// Byte meanings from Brother's "QL-800/810W/820NWB Raster Command Reference", checked on the
// QL-800 on 2026-10-16: a 29 x 90 mm job on the 62 mm roll gives a phase change (type 6) and then an error
// message (type 2) with byte 9 = 0x01 "replace media"; the printer then ignores ESC i S and ESC @ until the
// roll cover is opened and closed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QlLabels
{
    sealed class QlProblem
    {
        public string Headline, Cause, Title, Fix;  // "Wrong media", "the roll doesn't fit the job", the full sentence, what to do
        public QlProblem(string headline, string cause, string title, string fix) { Headline = headline; Cause = cause; Title = title; Fix = fix; }

        // One line for the status window and notifications: "Wrong media – the roll doesn't fit the job".
        public string Line { get { return Headline + " – " + Cause; } }
    }

    sealed class QlStatus
    {
        public const int Size = 32;
        public const int TypeReply = 0, TypeCompleted = 1, TypeError = 2, TypeTurnedOff = 4, TypeNotification = 5, TypePhase = 6;

        public readonly byte[] Raw;
        public readonly DateTime Time;

        public QlStatus(byte[] raw, DateTime time)
        {
            Raw = (byte[])raw.Clone();
            Time = time;
        }

        public static QlStatus TryParse(byte[] raw, int length, DateTime time)
        {
            if (raw == null || length != Size || raw[0] != 0x80 || raw[1] != 0x20) return null;
            return new QlStatus(raw, time);
        }

        public int Model { get { return Raw[4]; } }
        public int Error1 { get { return Raw[8]; } }
        public int Error2 { get { return Raw[9]; } }
        public int WidthMm { get { return Raw[10]; } }
        public int MediaType { get { return Raw[11]; } }
        public int LengthMm { get { return Raw[17]; } }
        public int Type { get { return Raw[18]; } }
        public int Phase { get { return Raw[19]; } }
        public int Notification { get { return Raw[22]; } }
        public int TextColor { get { return Raw[25]; } }
        // Red/black roll (DK-22251): text colour 0x81 (0x01 = black), measured on the QL-800 2026-10-17.
        public bool BlackRed { get { return TextColor == 0x81; } }

        public bool HasError { get { return Type == TypeError || Error1 != 0 || Error2 != 0; } }
        public bool NoMedia { get { return (MediaType & 0x0F) == 0 || WidthMm == 0; } }
        public bool Continuous { get { return (MediaType & 0x0F) == 0x0A; } }
        public bool DieCut { get { return (MediaType & 0x0F) == 0x0B; } }

        public string ModelName
        {
            get
            {
                return QlModel.NameForStatusCode(Model) ?? Lang.F("model code 0x{0}", Model.ToString("X2"));
            }
        }

        public string TypeText
        {
            get
            {
                switch (Type)
                {
                    case TypeReply: return Lang.T("status");
                    case TypeCompleted: return Lang.T("printing completed");
                    case TypeError: return Lang.T("error");
                    case TypeTurnedOff: return Lang.T("turned off");
                    case TypeNotification: return Notification == 3 ? Lang.T("cooling down") : Notification == 4 ? Lang.T("cooling finished") : Lang.F("notification {0}", Notification);
                    case TypePhase: return Phase == 1 ? Lang.T("printing") : Lang.T("ready to receive");
                    default: return Lang.F("message type {0}", Type);
                }
            }
        }

        // The loaded roll from Brother's media table, or null (no media / unknown size).
        public Media LoadedMedia
        {
            get
            {
                if (NoMedia) return null;
                if (Continuous)
                    return MediaTable.All.FirstOrDefault(m => m.Continuous && Near(m.Width01, WidthMm));
                if (DieCut)
                    return MediaTable.All.FirstOrDefault(m => !m.Continuous && Near(m.Width01, WidthMm) && Near(m.Length01, LengthMm));
                return null;
            }
        }

        public string LoadedText
        {
            get
            {
                if (NoMedia) return Lang.T("no roll");
                var m = LoadedMedia;
                if (m != null) return BlackRed ? Lang.F("{0} black/red", m.Display) : m.Display;
                if (Continuous) return Lang.F("{0} mm continuous", WidthMm);
                if (DieCut) return Lang.F("{0} × {1} mm labels", WidthMm, LengthMm);
                return Lang.F("{0} mm, media type 0x{1}", WidthMm, MediaType.ToString("X2"));
            }
        }

        static bool Near(int value01, int mm) { return Math.Abs(value01 - mm * 10) <= 10; }

        // Does a label format's Brother media fit the loaded roll? null = unknown.
        public bool? Fits(Media m)
        {
            if (m == null) return null;
            if (NoMedia) return false;
            if (!Continuous && !DieCut) return null;
            if (m.Continuous) return Continuous && Near(m.Width01, WidthMm);
            return DieCut && Near(m.Width01, WidthMm) && Near(m.Length01, LengthMm);
        }

        public List<QlProblem> Problems()
        {
            var list = new List<QlProblem>();
            string loaded = NoMedia ? "" : " " + Lang.F("(loaded: {0})", LoadedText);
            if ((Error2 & 0x01) != 0)
                list.Add(new QlProblem(Lang.T("Wrong media"), Lang.T("the roll doesn't fit the job"), Lang.F("Wrong media: the job's label size doesn't match the loaded roll{0}.", loaded),
                    Lang.T("Open and close the roll cover to clear the error. Then print again with a label format for the loaded roll, or load the roll the format needs.")));
            if ((Error1 & 0x01) != 0)
                list.Add(new QlProblem(Lang.T("No roll"), Lang.T("missing or not inserted correctly"), Lang.T("No media: there is no roll, or it isn't inserted correctly."),
                    Lang.T("Insert a DK roll, close the cover and print again.")));
            if ((Error1 & 0x02) != 0)
                list.Add(new QlProblem(Lang.T("End of the roll"), Lang.T("the roll is used up"), Lang.T("End of the label roll."), Lang.T("Insert a new roll and print again.")));
            if ((Error1 & 0x04) != 0)
                list.Add(new QlProblem(Lang.T("Cutter jam"), Lang.T("tape stuck in the cutter"), Lang.T("Cutter jam."), Lang.T("Switch the printer off, open the cover and remove the jammed tape, then switch it on again.")));
            if ((Error2 & 0x06) != 0)
                list.Add(new QlProblem(Lang.T("Data error"), Lang.T("incomplete data received"), Lang.T("Data error: the printer received incomplete or invalid data."),
                    Lang.T("Cancel the print jobs, switch the printer off and on, and print again.")));
            if ((Error2 & 0x10) != 0)
                list.Add(new QlProblem(Lang.T("Cover open"), Lang.T("the roll cover is open"), Lang.T("The roll cover is open."), Lang.T("Close the cover. If a label was being printed, print it again.")));
            if ((Error2 & 0x40) != 0)
                list.Add(new QlProblem(Lang.T("Tape can't be fed"), Lang.T("roll empty or stuck"), Lang.T("The tape can't be fed (or the end of the roll was reached)."),
                    Lang.T("Check the roll: is it empty or stuck? Reinsert it, close the cover and print again.")));
            if ((Error2 & 0x80) != 0)
                list.Add(new QlProblem(Lang.T("System error"), Lang.T("fault in the printer"), Lang.T("System error in the printer."), Lang.T("Switch the printer off and on. If it happens again, contact Brother support.")));
            // Measured 2026-10-17 (QL-800, DK-22205 loaded, black/red job): error message with both
            // error bytes 0 and byte 31 = 0x23 (the red/black roll's code in byte 14); one press of
            // the power button cleared it.
            if (Type == TypeError && Error1 == 0 && Error2 == 0 && Raw[31] == 0x23 && !BlackRed)
                list.Add(new QlProblem(Lang.T("Black/red job, black roll"), Lang.T("needs DK-22251"), Lang.F("The job is black and red, but the loaded roll prints black only{0}.", loaded),
                    Lang.T("Press the printer's power button once to clear the error. Then print again with the colour mode \"Monochrome\", or load the black/red roll DK-22251.")));
            int known1 = 0x07, known2 = 0xD7;
            if ((Error1 & ~known1) != 0 || (Error2 & ~known2) != 0 || (list.Count == 0 && Type == TypeError))
                list.Add(new QlProblem(Lang.T("Printer error"), Lang.F("code {0} {1}", Error1.ToString("X2"), Error2.ToString("X2")), Lang.F("Printer error (code {0} {1}).", Error1.ToString("X2"), Error2.ToString("X2")),
                    Lang.T("Open and close the roll cover. If the error stays, switch the printer off and on.")));
            return list;
        }

        public string Summary
        {
            get
            {
                var p = HasError ? Problems() : null;
                if (p != null && p.Count > 0) return TypeText + ": " + string.Join(" ", p.Select(x => x.Title));
                return TypeText + ", " + LoadedText;
            }
        }

        public string Hex { get { return BitConverter.ToString(Raw).Replace("-", " "); } }

        public static QlStatus FromHex(string hex, DateTime time)
        {
            try
            {
                var bytes = hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(b => byte.Parse(b, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
                return TryParse(bytes, bytes.Length, time);
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }  // a byte like "FFF" (published by another PC)
        }
    }
}
