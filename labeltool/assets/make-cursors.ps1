<#
.SYNOPSIS
  Draws QL Label Maker's own mouse pointers with System.Drawing and writes them as .cur files next to this script.
  pan-move.cur: the four-way cross with rounded heads shown while the view is moved with the middle mouse button
  (2026-11-10, proposal 1 in the draw-tools mockup). White with a black outline, no shadow, hotspot in the
  centre; one image per size 32/40/48/64 px (100/125/150/200 %), 32-bit with alpha. EditorCursors.cs picks the size.
  Changes nothing but the .cur files here. The build does not run this; the .cur files are checked in
  (build.cmd: /resource:assets\pan-move.cur,pan-move.cur).
  Usage: powershell -NoProfile -File labeltool\assets\make-cursors.ps1 [-OutDir <folder>]
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
using System.IO;

public static class QlCursors {
    // The cross on the 32 grid round the hotspot: tips 13 out, heads 4.6 wide each side, head bases 7.8 out, shafts
    // 1.5 wide each side. Scaled per size, and the straight edges (shaft sides, head bases) snapped to whole pixels.
    // The hotspot pixel is size / 2; with PixelOffsetMode.Half its centre is c = size / 2 + 0.5.
    public static Bitmap Draw(int size) {
        float s = size / 32f, c = size / 2 + 0.5f;
        float t = 13 * s, h = 4.6f * s;
        float b = (float)Math.Round(7.8 * s - 0.5) + 0.5f, w = (float)Math.Floor(1.5 * s) + 0.5f;
        // one arm (pointing up) from the left shaft side round to the inner corner before the next arm
        float[] arm = { -w, -b, -h, -b, 0, -t, h, -b, w, -b, w, -w };
        var pts = new List<PointF>();
        for (int k = 0; k < 4; k++)              // the four arms clockwise: up, right, down, left
            for (int i = 0; i < arm.Length; i += 2) pts.Add(Turn(arm[i], arm[i + 1], k, c));
        return Render(size, pts.ToArray(), s);
    }

    // A point turned by quarter turns clockwise (screen coordinates) round the centre c.
    static PointF Turn(float x, float y, int quarter, float c) {
        for (int i = 0; i < quarter; i++) { float nx = -y, ny = x; x = nx; y = ny; }
        return new PointF(c + x, c + y);
    }

    // Outline: black, drawn first with round joins; the white fill on top.
    static Bitmap Render(int size, PointF[] pts, float s) {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var path = new GraphicsPath()) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            path.AddPolygon(pts);
            float outline = Math.Max(1, (float)Math.Round(1.1 * s));
            using (var pen = new Pen(Color.Black, 2 * outline) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
            using (var white = new SolidBrush(Color.White)) g.FillPath(white, path);
        }
        return bmp;
    }

    // A .cur file: ICONDIR type 2, the entries carry the hotspot; each image a 32-bit DIB (height doubled) + AND mask.
    public static void WriteCur(string path, int[] sizes) {
        var images = new List<byte[]>();
        foreach (int size in sizes) using (var bmp = Draw(size)) images.Add(Dib(bmp));
        using (var w = new BinaryWriter(File.Create(path))) {
            w.Write((short)0); w.Write((short)2); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)(sizes[i] / 2)); w.Write((short)(sizes[i] / 2));
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    static byte[] Dib(Bitmap bmp) {
        int n = bmp.Width, maskStride = ((n + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms)) {
            w.Write(40); w.Write(n); w.Write(2 * n); w.Write((short)1); w.Write((short)32); w.Write(0);
            w.Write(n * n * 4 + maskStride * n); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = n - 1; y >= 0; y--)
                for (int x = 0; x < n; x++) { var p = bmp.GetPixel(x, y); w.Write(p.B); w.Write(p.G); w.Write(p.R); w.Write(p.A); }
            for (int y = n - 1; y >= 0; y--) {
                var row = new byte[maskStride];
                for (int x = 0; x < n; x++) if (bmp.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                w.Write(row);
            }
            return ms.ToArray();
        }
    }

    // A look at every size, 4x enlarged and real size, on white, grey and dark.
    public static void WritePreview(string path, int[] sizes) {
        using (var sheet = new Bitmap(40 + sizes.Length * 300, 330, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(sheet)) {
            g.Clear(Color.FromArgb(246, 248, 248));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int x = 20;
            foreach (int size in sizes) using (var bmp = Draw(size)) {
                int big = size * 4 > 256 ? 256 : size * 4;
                g.DrawImage(bmp, x, 20, big, big);
                var bgs = new[] { Color.White, Color.FromArgb(160, 160, 160), Color.FromArgb(15, 18, 19) };
                for (int b = 0; b < 3; b++) {
                    using (var br = new SolidBrush(bgs[b])) g.FillRectangle(br, x + b * 90, 290 - size / 2 - 6, 84, size + 12);
                    g.DrawImageUnscaled(bmp, x + b * 90 + 42 - size / 2, 290 - size / 2);
                }
                x += 300;
            }
            sheet.Save(path, ImageFormat.Png);
        }
    }
}
"@
$sizes = [int[]](32, 40, 48, 64)
[QlCursors]::WriteCur((Join-Path $OutDir "pan-move.cur"), $sizes)
if ($env:QL_CURSOR_PREVIEW) { [QlCursors]::WritePreview($env:QL_CURSOR_PREVIEW, $sizes) }
"Cursors written to $OutDir"
