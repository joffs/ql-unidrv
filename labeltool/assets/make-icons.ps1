<#
.SYNOPSIS
  Draws the QL Unidrv icon family with System.Drawing and writes the ICO files, the README logo
  PNGs and preview.png. The base mark is a petrol label whose bottom-right corner peels off (orange back, light liner).
  Icons below 20 px are their own pixel drawings on a 16 px grid. No dependencies beyond Windows PowerShell.
  The build does not run this; the ICOs are checked in (csc /win32icon:..., /resource:...).
#>
param([string]$OutDir = $PSScriptRoot)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

public static class QlIcons {
    static readonly Color PetrolTop = C(0x23A6A8), PetrolBottom = C(0x0E6F78), PetrolDark = C(0x08474E), PetrolBack = C(0x9AD6D4);
    static readonly Color OrangeTop = C(0xFFA062), OrangeBottom = C(0xEC6A2C), Liner = C(0xE6F1F0), Pencil = C(0xF6FBFB);
    static readonly Color GreyTop = C(0xA9B4B6), GreyBottom = C(0x76878A), GreyFlap = C(0xC9D1D2);
    static readonly Color Amber = C(0xFFB900), Red = C(0xD13438), Ink = C(0x1E1E1E), PageLine = C(0x8A9A9D), PageFold = C(0xE9EEEF);
    public static readonly Color Wordmark = C(0x0E7C84), WordmarkDark = C(0x3CC2C2);
    static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
    static Color A(Color c, double a) { return Color.FromArgb((int)Math.Round(a * 255), c); }

    public static readonly string[] Names = { "qlunidrv", "qllabelformats", "qllabelmaker", "qlstatus", "qlsetup", "qllabel",
                                              "tray-ok", "tray-warning", "tray-error", "tray-offline" };

    // ---- the base mark on the 256 grid ----

    static GraphicsPath Body(float x0, float y0, float x1, float y1, float R, float L) {
        var p = new GraphicsPath();
        p.AddLine(x0 + R, y1, x1 - L, y1);
        p.AddLine(x1 - L, y1, x1, y1 - L);
        p.AddLine(x1, y1 - L, x1, y0 + R);
        if (R > 0) p.AddArc(x1 - 2 * R, y0, 2 * R, 2 * R, 0, -90);
        p.AddLine(x1 - R, y0, x0 + R, y0);
        if (R > 0) p.AddArc(x0, y0, 2 * R, 2 * R, 270, -90);
        p.AddLine(x0, y0 + R, x0, y1 - R);
        if (R > 0) p.AddArc(x0, y1 - 2 * R, 2 * R, 2 * R, 180, -90);
        p.CloseFigure();
        return p;
    }

    static GraphicsPath LinerCorner(float x1, float y1, float R, float L) {
        var p = new GraphicsPath();
        p.AddLine(x1 - L, y1, x1, y1 - L);
        p.AddLine(x1, y1 - L, x1, y1 - R);
        if (R > 0) p.AddArc(x1 - 2 * R, y1 - 2 * R, 2 * R, 2 * R, 0, 90);
        p.CloseFigure();
        return p;
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r) {
        var p = new GraphicsPath();
        if (r <= 0) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
        p.AddArc(x, y, 2 * r, 2 * r, 180, 90);
        p.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
        p.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
        p.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
        p.CloseFigure();
        return p;
    }

    // The peeled-off corner, curl t (0 = flat fold, 1 = strongly curved; the mark uses 0.5).
    static GraphicsPath Flap(float x1, float y1, float L, float t) {
        float cx = x1 - L, cy = y1 - L, d = 0.18f * L * t, r = 14f * (L / 88f) * (1 - t), tx = cx + d, ty = cy + d;
        var p1 = new PointF(x1 - 0.32f * L, cy + 0.02f * L * t);
        var p2 = new PointF(tx + r + 0.14f * L, cy + 0.05f * L * t);
        Func<PointF, PointF> m = q => new PointF(cx + (q.Y - cy), cy + (q.X - cx));
        var a = new PointF(tx + r, ty); var c = new PointF(tx, ty); var b = new PointF(tx, ty + r);
        var p = new GraphicsPath();
        p.AddLine(cx, y1, x1, cy);
        p.AddBezier(new PointF(x1, cy), p1, p2, a);
        p.AddBezier(a, Lerp(a, c, 2f / 3), Lerp(b, c, 2f / 3), b);   // quadratic rounding of the tip
        p.AddBezier(b, m(p2), m(p1), new PointF(cx, y1));
        p.CloseFigure();
        return p;
    }
    static PointF Lerp(PointF a, PointF b, float f) { return new PointF(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f); }

    static void Sticker(Graphics g, float x0, float y0, float x1, float y1, float R, float L, bool grey) {
        float sw = Math.Max(2f, (x1 - x0) / 52f);
        using (var liner = LinerCorner(x1, y1, R, L)) {
            using (var b = new SolidBrush(Liner)) g.FillPath(b, liner);
            using (var pen = new Pen(A(PetrolDark, .35), sw) { LineJoin = LineJoin.Round }) g.DrawPath(pen, liner);
        }
        using (var body = Body(x0, y0, x1, y1, R, L)) {
            using (var b = new LinearGradientBrush(new PointF(0, y0 - 1), new PointF(0, y1 + 1), grey ? GreyTop : PetrolTop, grey ? GreyBottom : PetrolBottom)) g.FillPath(b, body);
            using (var pen = new Pen(A(PetrolDark, .35), sw) { LineJoin = LineJoin.Round }) g.DrawPath(pen, body);
        }
        var state = g.Save();
        using (var clip = RoundRect(x0, y0, x1 - x0, y1 - y0, R)) g.SetClip(clip);
        using (var flap = Flap(x1, y1, L, 0.5f)) {
            float k = (x1 - x0) / 208f;   // shadow offsets relative to the full-size mark
            int[] alpha = { 18, 24, 30 };
            for (int i = 0; i < 3; i++)
                using (var sh = (GraphicsPath)flap.Clone())
                using (var b = new SolidBrush(Color.FromArgb(alpha[i], 0, 0, 0))) {
                    float f = (3 - i) / 3f;
                    using (var mtx = new Matrix()) { mtx.Translate(-3 * k * f, 7 * k * f); sh.Transform(mtx); }
                    g.FillPath(b, sh);
                }
            if (grey) using (var b = new SolidBrush(GreyFlap)) g.FillPath(b, flap);
            else using (var b = new LinearGradientBrush(new PointF(0, y1 - L - 1), new PointF(0, y1 + 1), OrangeTop, OrangeBottom)) g.FillPath(b, flap);
        }
        g.Restore(state);
    }

    static void Bar(Graphics g, float x, float y, float w, float h) {
        using (var p = RoundRect(x, y, w, h, h / 2)) using (var b = new SolidBrush(A(Color.White, .96))) g.FillPath(b, p);
    }
    static void BaseMark(Graphics g, bool grey) { Sticker(g, 24, 24, 232, 232, 36, 88, grey); }
    static void TwoLines(Graphics g) { Bar(g, 60, 64, 136, 24); Bar(g, 60, 108, 88, 24); }
    static Pen RoundPen(Color c, float w) { return new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; }

    static void DrawBig(Graphics g, string name) {
        switch (name) {
            case "qlunidrv": case "tray-ok": BaseMark(g, false); TwoLines(g); break;
            case "tray-offline": BaseMark(g, true); TwoLines(g); break;
            case "qllabelformats":
                using (var back = RoundRect(48, 24, 184, 184, 32)) {
                    using (var b = new SolidBrush(PetrolBack)) g.FillPath(b, back);
                    using (var pen = new Pen(A(PetrolDark, .35), 4)) g.DrawPath(pen, back);
                }
                Sticker(g, 24, 48, 208, 232, 32, 78, false);
                Bar(g, 52, 84, 120, 22); Bar(g, 52, 124, 68, 22);
                break;
            case "qllabelmaker": {
                BaseMark(g, false); Bar(g, 60, 64, 88, 24); Bar(g, 60, 108, 48, 24);
                var st = g.Save();
                g.TranslateTransform(76, 188); g.RotateTransform(-45);
                using (var body = Poly(0, 0, 40, -18, 150, -18, 150, 18, 40, 18)) using (var b = new SolidBrush(Pencil)) g.FillPath(b, body);
                using (var tip = Poly(0, 0, 14, -6.3f, 14, 6.3f)) using (var b = new SolidBrush(PetrolDark)) g.FillPath(b, tip);
                using (var pen = new Pen(A(PetrolDark, .25), 4)) g.DrawLine(pen, 40, -18, 40, 18);
                using (var cap = RoundRect(150, -18, 28, 36, 6)) using (var b = new LinearGradientBrush(new PointF(0, -19), new PointF(0, 19), OrangeTop, OrangeBottom)) g.FillPath(b, cap);
                using (var outline = Poly(0, 0, 40, -18, 178, -18, 178, 18, 40, 18)) using (var pen = new Pen(A(PetrolDark, .45), 4) { LineJoin = LineJoin.Round }) g.DrawPath(pen, outline);
                g.Restore(st);
                break; }
            case "qlstatus":
                BaseMark(g, false);
                using (var pen = RoundPen(Color.White, 22)) g.DrawLines(pen, Pts(52, 110, 86, 110, 108, 62, 134, 158, 156, 110, 204, 110));
                break;
            case "qlsetup":
                BaseMark(g, false);
                using (var pen = RoundPen(Color.White, 26)) { g.DrawLine(pen, 104, 48, 104, 128); g.DrawLines(pen, Pts(60, 102, 104, 150, 148, 102)); }
                Bar(g, 56, 176, 88, 24);
                break;
            case "qllabel":
                using (var page = Poly(44, 10, 170, 10, 218, 58, 218, 246, 44, 246)) {
                    using (var b = new SolidBrush(Color.White)) g.FillPath(b, page);
                    using (var pen = new Pen(PageLine, 5) { LineJoin = LineJoin.Round }) g.DrawPath(pen, page);
                }
                using (var fold = Poly(170, 10, 170, 58, 218, 58)) {
                    using (var b = new SolidBrush(PageFold)) g.FillPath(b, fold);
                    using (var pen = new Pen(PageLine, 5) { LineJoin = LineJoin.Round }) g.DrawPath(pen, fold);
                }
                Sticker(g, 66, 104, 196, 234, 24, 56, false);
                Bar(g, 88, 128, 84, 16); Bar(g, 88, 156, 48, 16);
                break;
            case "tray-warning":
                BaseMark(g, false); TwoLines(g);
                using (var tri = Poly(188, 118, 254, 240, 122, 240)) {
                    using (var b = new SolidBrush(Amber)) g.FillPath(b, tri);
                    using (var pen = new Pen(Color.White, 10) { LineJoin = LineJoin.Round }) g.DrawPath(pen, tri);
                }
                using (var bang = RoundRect(180, 152, 16, 48, 8)) using (var b = new SolidBrush(Ink)) { g.FillPath(b, bang); g.FillEllipse(b, 179, 211, 18, 18); }
                break;
            case "tray-error":
                BaseMark(g, false); TwoLines(g);
                using (var b = new SolidBrush(Red)) g.FillEllipse(b, 126, 126, 124, 124);
                using (var pen = new Pen(Color.White, 10)) g.DrawEllipse(pen, 126, 126, 124, 124);
                using (var pen = RoundPen(Color.White, 18)) { g.DrawLine(pen, 164, 164, 212, 212); g.DrawLine(pen, 212, 164, 164, 212); }
                break;
            default: throw new ArgumentException(name);
        }
    }

    // ---- the same on the 16 px grid (straight fold, whole pixels) ----

    static void Sticker16(Graphics g, float x0, float y0, float x1, float y1, float R, float l, bool grey) {
        using (var liner = LinerCorner(x1, y1, R, l)) using (var b = new SolidBrush(Liner)) g.FillPath(b, liner);
        using (var body = Body(x0, y0, x1, y1, R, l))
        using (var b = new LinearGradientBrush(new PointF(0, y0 - .5f), new PointF(0, y1 + .5f), grey ? GreyTop : PetrolTop, grey ? GreyBottom : PetrolBottom)) g.FillPath(b, body);
        using (var flap = Poly(x1 - l, y1, x1, y1 - l, x1 - l, y1 - l)) using (var b = new SolidBrush(grey ? GreyFlap : OrangeBottom)) g.FillPath(b, flap);
    }
    static void Px(Graphics g, Color c, float x, float y, float w, float h) {
        var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
        using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
        g.SmoothingMode = mode;
    }
    static void Base16(Graphics g, bool grey) { Sticker16(g, 1, 1, 15, 15, 2, 6, grey); }
    static void Lines16(Graphics g) { Px(g, Color.White, 4, 3, 8, 2); Px(g, Color.White, 4, 6, 5, 2); }

    static void DrawSmall(Graphics g, string name) {
        switch (name) {
            case "qlunidrv": case "tray-ok": Base16(g, false); Lines16(g); break;
            case "tray-offline": Base16(g, true); Lines16(g); break;
            case "qllabelformats":
                using (var back = RoundRect(3, 0, 13, 13, 2)) using (var b = new SolidBrush(PetrolBack)) g.FillPath(b, back);
                Sticker16(g, 0, 3, 13, 16, 2, 5, false);
                Px(g, Color.White, 2, 5, 7, 2); Px(g, Color.White, 2, 8, 4, 2);
                break;
            case "qllabelmaker":
                Base16(g, false); Px(g, Color.White, 4, 3, 5, 2);
                using (var p = Poly(3, 13, 4, 10, 10, 4, 12, 6, 6, 12)) using (var b = new SolidBrush(Pencil)) g.FillPath(b, p);
                using (var p = Poly(10, 4, 11, 3, 13, 5, 12, 6)) using (var b = new SolidBrush(OrangeBottom)) g.FillPath(b, p);
                using (var p = Poly(3, 13, 3.6f, 11.4f, 4.6f, 12.4f)) using (var b = new SolidBrush(PetrolDark)) g.FillPath(b, p);
                break;
            case "qlstatus":
                Base16(g, false);
                using (var pen = RoundPen(Color.White, 1.6f)) g.DrawLines(pen, Pts(3, 7.5f, 5, 7.5f, 6.5f, 3.5f, 8.5f, 11, 10, 7.5f, 13, 7.5f));
                break;
            case "qlsetup":
                Base16(g, false); Px(g, Color.White, 6, 2, 2, 6);
                using (var p = Poly(3, 7, 11, 7, 7, 11)) using (var b = new SolidBrush(Color.White)) g.FillPath(b, p);
                Px(g, Color.White, 3, 12, 6, 1);
                break;
            case "qllabel":
                using (var page = Poly(2.5f, .5f, 10, .5f, 13.5f, 4, 13.5f, 15.5f, 2.5f, 15.5f)) {
                    using (var b = new SolidBrush(Color.White)) g.FillPath(b, page);
                    using (var pen = new Pen(C(0x7A8A8D), 1)) g.DrawPath(pen, page);
                }
                using (var fold = Poly(10, .5f, 10, 4, 13.5f, 4)) {
                    using (var b = new SolidBrush(PageFold)) g.FillPath(b, fold);
                    using (var pen = new Pen(C(0x7A8A8D), 1)) g.DrawPath(pen, fold);
                }
                Sticker16(g, 4, 7, 12, 15, 1, 3, false);
                Px(g, Color.White, 5, 9, 5, 1); Px(g, Color.White, 5, 11, 3, 1);
                break;
            case "tray-warning":
                Base16(g, false); Lines16(g);
                using (var p = Poly(11.5f, 6, 16, 15.5f, 7, 15.5f)) using (var b = new SolidBrush(Amber)) g.FillPath(b, p);
                Px(g, Ink, 11, 9, 1, 3.5f); Px(g, Ink, 11, 13.5f, 1, 1);
                break;
            case "tray-error":
                Base16(g, false); Lines16(g);
                using (var b = new SolidBrush(Red)) g.FillEllipse(b, 7, 7, 9, 9);
                using (var pen = new Pen(Color.White, 1.3f)) { g.DrawLine(pen, 9.8f, 9.8f, 13.2f, 13.2f); g.DrawLine(pen, 13.2f, 9.8f, 9.8f, 13.2f); }
                break;
            default: throw new ArgumentException(name);
        }
    }

    static GraphicsPath Poly(params float[] xy) { var p = new GraphicsPath(); p.AddPolygon(Pts(xy)); return p; }
    static PointF[] Pts(params float[] xy) { var r = new PointF[xy.Length / 2]; for (int i = 0; i < r.Length; i++) r[i] = new PointF(xy[2 * i], xy[2 * i + 1]); return r; }

    public static Bitmap Draw(string name, int s) {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            if (s < 20) { g.ScaleTransform(s / 16f, s / 16f); DrawSmall(g, name); }
            else { g.ScaleTransform(s / 256f, s / 256f); DrawBig(g, name); }
        }
        return bmp;
    }

    // ---- ICO / PNG output ----

    static byte[] Dib(Bitmap bmp) {
        int w = bmp.Width, h = bmp.Height, maskStride = ((w + 31) / 32) * 4;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        for (int y = h - 1; y >= 0; y--) bw.Write(px, y * data.Stride, w * 4);  // bottom-up BGRA
        bw.Write(new byte[maskStride * h]);                                       // AND mask (alpha is used)
        return ms.ToArray();
    }

    public static void WriteIco(string name, string path, int[] sizes) {
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var bmp = Draw(name, sizes[i])) {
                if (sizes[i] >= 256) { var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png); images[i] = ms.ToArray(); }
                else images[i] = Dib(bmp);
            }
        using (var bw = new BinaryWriter(File.Create(path))) {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }
    }

    // README lockup: the mark and "QL Unidrv" ("QL" bold petrol, "Unidrv" semibold), transparent background.
    public static void WriteLogo(string path, bool dark, int height) {
        float fs = height * 0.5f;
        using (var bold = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var semi = new Font("Segoe UI Semibold", fs, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var probe = new Bitmap(1, 1))
        using (var pg = Graphics.FromImage(probe)) {
            var fmt = StringFormat.GenericTypographic;
            float wQl = pg.MeasureString("QL", bold, 10000, fmt).Width, wSp = fs * 0.26f;
            float wUn = pg.MeasureString("Unidrv", semi, 10000, fmt).Width;
            int gap = (int)(height * 0.22f), width = height + gap + (int)Math.Ceiling(wQl + wSp + wUn) + height / 12;
            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp)) {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var mark = Draw("qlunidrv", height)) g.DrawImage(mark, 0, 0, height, height);
                float y = (height - semi.GetHeight(g)) / 2f, x = height + gap;
                using (var b = new SolidBrush(dark ? WordmarkDark : Wordmark)) g.DrawString("QL", bold, b, x, y, fmt);
                using (var b = new SolidBrush(dark ? C(0xE6EDF3) : C(0x1E2B2F))) g.DrawString("Unidrv", semi, b, x + wQl + wSp, y, fmt);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    public static void WritePreview(string path, int[] sizes) {
        int pad = 12, cell = 0; foreach (int s in sizes) cell += s + pad;
        int rowH = 128 + 2 * pad, width = 2 * pad + 128 + cell, height = rowH * Names.Length;
        using (var sheet = new Bitmap(width * 2, height))
        using (var g = Graphics.FromImage(sheet)) {
            g.Clear(Color.White);
            using (var dark = new SolidBrush(C(0x202020))) g.FillRectangle(dark, width, 0, width, height);
            for (int r = 0; r < Names.Length; r++)
                for (int half = 0; half < 2; half++) {
                    int x = half * width + pad, y = r * rowH + pad;
                    using (var big = Draw(Names[r], 256)) g.DrawImage(big, x, y, 128, 128);
                    x += 128 + pad;
                    foreach (int s in sizes) using (var icon = Draw(Names[r], s)) { g.DrawImageUnscaled(icon, x, y + 128 - s); x += s + pad; }
                }
            sheet.Save(path, ImageFormat.Png);
        }
    }
}
"@

$app = 16, 20, 24, 32, 40, 48, 64, 256
$tray = 16, 20, 24, 32, 40, 48
foreach ($name in [QlIcons]::Names) {
    $sizes = if ($name -like "tray-*") { $tray } else { $app }
    [QlIcons]::WriteIco($name, (Join-Path $OutDir "$name.ico"), [int[]]$sizes)
}
[QlIcons]::WriteLogo((Join-Path $OutDir "logo-light.png"), $false, 192)
[QlIcons]::WriteLogo((Join-Path $OutDir "logo-dark.png"), $true, 192)
[QlIcons]::WritePreview((Join-Path $OutDir "preview.png"), [int[]](64, 48, 40, 32, 24, 20, 16))
"Icons written to $OutDir"
