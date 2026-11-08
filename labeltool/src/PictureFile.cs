using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace QlLabels
{
    // Picture files beyond GDI+'s own formats, read with Windows' own decoders only (no DLL of ours): WebP (first frame) and
    // HEIC/HEIF through WIC (WebP: the "Webp Image Extensions", part of Windows 11; HEIC: the "HEIF Image Extensions" and
    // "HEVC Video Extensions" from the Microsoft Store), a PDF's first page through Windows.Data.Pdf (WinRT), rendered at
    // the printer's 300 dpi. Both are reached by reflection (WPF's PresentationCore wraps WIC; the WinRT types load from
    // Windows' metadata), so every build of src\*.cs compiles without extra references.
    static class PictureFile
    {
        public static readonly string[] HeifExtensions = { ".heic", ".heif", ".hif" };
        const int PdfDpi = 300;
        const int PdfMaxPixels = 4000;   // a page's longest side at most (ImageObject.FromFile scales to MaxPixels anyway)

        public static Bitmap Load(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (HeifExtensions.Contains(ext)) return Heif(path);
            if (ext == ".webp") return WebP(path);
            if (ext == ".pdf") return PdfFirstPage(path);
            return new Bitmap(path);
        }

        // ---------- HEIC / HEIF through WIC ----------

        static Bitmap Heif(string path)
        {
            try { return Wic(path); }
            catch (Exception ex)
            {
                if (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is UnauthorizedAccessException) throw;
                // no WIC decoder for the format at all: the HEIF extension is missing; else damaged, or HEVC missing
                throw new InvalidDataException(ex is NotSupportedException
                    ? Lang.T("Windows can't read HEIC pictures yet: install the free \"HEIF Image Extensions\" and \"HEVC Video Extensions\" from the Microsoft Store.")
                    : Lang.T("Windows can't read this HEIC picture: the file may be damaged, or the free \"HEVC Video Extensions\" from the Microsoft Store are missing."), ex);
            }
        }

        // ---------- WebP through WIC ----------

        static Bitmap WebP(string path)
        {
            try { return Wic(path); }
            catch (Exception ex)
            {
                if (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is UnauthorizedAccessException) throw;
                throw new InvalidDataException(ex is NotSupportedException
                    ? Lang.T("Windows can't read WebP pictures yet: install the free \"Webp Image Extensions\" from the Microsoft Store.")
                    : Lang.T("Windows can't read this WebP picture: the file may be damaged."), ex);
            }
        }

        // the first frame (an animated WebP: its first picture)
        static Bitmap Wic(string path)
        {
            var core = Assembly.Load("PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            var wbase = Assembly.Load("WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            Type decoderType = core.GetType("System.Windows.Media.Imaging.BitmapDecoder", true);
            Type createOptions = core.GetType("System.Windows.Media.Imaging.BitmapCreateOptions", true);
            Type cacheOption = core.GetType("System.Windows.Media.Imaging.BitmapCacheOption", true);
            Type source = core.GetType("System.Windows.Media.Imaging.BitmapSource", true);
            Type converted = core.GetType("System.Windows.Media.Imaging.FormatConvertedBitmap", true);
            Type pixelFormat = core.GetType("System.Windows.Media.PixelFormat", true);
            Type palette = core.GetType("System.Windows.Media.Imaging.BitmapPalette", true);
            Type rect = wbase.GetType("System.Windows.Int32Rect", true);
            using (var file = File.OpenRead(path))
            {
                object decoder = Call(decoderType.GetMethod("Create", new[] { typeof(Stream), createOptions, cacheOption }), null,
                                      file, Enum.Parse(createOptions, "IgnoreColorProfile"), Enum.Parse(cacheOption, "OnLoad"));
                object frame = ((IList)decoderType.GetProperty("Frames").GetValue(decoder))[0];
                object bgra = core.GetType("System.Windows.Media.PixelFormats", true).GetProperty("Bgra32").GetValue(null);
                object bitmap = Activator.CreateInstance(converted, frame, bgra, null, 0.0);
                int w = (int)source.GetProperty("PixelWidth").GetValue(bitmap), h = (int)source.GetProperty("PixelHeight").GetValue(bitmap);
                var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                var data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Call(source.GetMethod("CopyPixels", new[] { rect, typeof(IntPtr), typeof(int), typeof(int) }), bitmap,
                         rect.GetProperty("Empty").GetValue(null), data.Scan0, data.Stride * h, data.Stride);
                }
                catch { result.UnlockBits(data); result.Dispose(); throw; }
                result.UnlockBits(data);
                return result;
            }
        }

        // ---------- PDF: the first page through Windows.Data.Pdf ----------

        static Bitmap PdfFirstPage(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            // on a thread pool (MTA) thread: WinRT's asynchronous calls are waited for there, never on the window's thread
            var dpi = new double[1];
            var task = Task.Run(() => RenderPdf(bytes, dpi));
            try { task.Wait(); }
            catch (AggregateException ex)
            {
                var inner = ex.InnerException is TargetInvocationException ? ex.InnerException.InnerException : ex.InnerException;
                throw new InvalidDataException(Lang.T("Windows can't read this PDF (damaged, protected by a password, or not a PDF)."), inner);
            }
            using (var ms = new MemoryStream(task.Result))
            using (var decoded = new Bitmap(ms))
            {
                var page = new Bitmap(decoded);   // a copy: a Bitmap read from a stream needs the stream open
                if (dpi[0] > 0) page.SetResolution((float)dpi[0], (float)dpi[0]);   // the page's size (dropped 1:1)
                return page;
            }
        }

        // dpi[0]: the resolution the page was drawn at.
        static byte[] RenderPdf(byte[] pdf, double[] dpi)
        {
            var rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            Type streams = rt.GetType("System.IO.WindowsRuntimeStreamExtensions", true);
            Type tasks = rt.GetType("System.WindowsRuntimeSystemExtensions", true);
            Type document = Type.GetType("Windows.Data.Pdf.PdfDocument, Windows.Data, ContentType=WindowsRuntime", true);
            Type options = Type.GetType("Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data, ContentType=WindowsRuntime", true);
            MethodInfo asRandomAccess = streams.GetMethod("AsRandomAccessStream", new[] { typeof(Stream) });

            using (var input = new MemoryStream(pdf))
            using (var output = new MemoryStream())
            {
                object inStream = asRandomAccess.Invoke(null, new object[] { input });
                MethodInfo loadMethod = document.GetMethod("LoadFromStreamAsync", new[] { asRandomAccess.ReturnType });
                object doc = Await(tasks, loadMethod.Invoke(null, new[] { inStream }), loadMethod.ReturnType);
                if ((uint)document.GetProperty("PageCount").GetValue(doc) == 0) throw new InvalidDataException("no pages");
                object page = document.GetMethod("GetPage").Invoke(doc, new object[] { 0u });
                Type pageType = document.Assembly.GetType("Windows.Data.Pdf.PdfPage", true);   // page itself is a bare COM object
                object size = pageType.GetProperty("Size").GetValue(page);   // in 1/96 inch
                double pw = (double)size.GetType().GetProperty("Width").GetValue(size), ph = (double)size.GetType().GetProperty("Height").GetValue(size);
                double k = Math.Min((double)PdfDpi / 96, PdfMaxPixels / Math.Max(1, Math.Max(pw, ph)));
                dpi[0] = 96 * k;
                object opts = Activator.CreateInstance(options);
                options.GetProperty("DestinationWidth").SetValue(opts, (uint)Math.Max(1, Math.Round(pw * k)));
                options.GetProperty("DestinationHeight").SetValue(opts, (uint)Math.Max(1, Math.Round(ph * k)));
                object outStream = asRandomAccess.Invoke(null, new object[] { output });
                MethodInfo renderMethod = pageType.GetMethods()
                    .First(m => m.Name == "RenderToStreamAsync" && m.GetParameters().Length == 2);
                Await(tasks, renderMethod.Invoke(page, new[] { outStream, opts }), renderMethod.ReturnType);   // a PNG by default, on white
                ((IDisposable)outStream).Dispose();
                ((IDisposable)page).Dispose();
                return output.ToArray();
            }
        }

        // Waits for a WinRT IAsyncOperation<T> or IAsyncAction (WindowsRuntimeSystemExtensions.AsTask) and returns its result.
        // declared: the method's return type (the object itself is a bare COM object that names no interface).
        static object Await(Type tasks, object operation, Type declared)
        {
            Type op = declared.IsGenericType && declared.GetGenericTypeDefinition().FullName == "Windows.Foundation.IAsyncOperation`1" ? declared : null;
            Task t;
            if (op != null)
            {
                var asTask = tasks.GetMethods().First(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition().FullName == "Windows.Foundation.IAsyncOperation`1");
                t = (Task)asTask.MakeGenericMethod(op.GetGenericArguments()[0]).Invoke(null, new[] { operation });
            }
            else
            {
                var asTask = tasks.GetMethods().First(m => m.Name == "AsTask" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.FullName == "Windows.Foundation.IAsyncAction");
                t = (Task)asTask.Invoke(null, new[] { operation });
            }
            t.Wait();
            var result = t.GetType().GetProperty("Result");
            return result != null ? result.GetValue(t) : null;
        }

        static object Call(MethodInfo method, object target, params object[] args)
        {
            try { return method.Invoke(target, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
    }
}
