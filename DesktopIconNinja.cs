using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DesktopIconNinja
{
    // ============================================================
    //  桌面图标忍者 · 经典模式  (C# WinForms, .NET Framework 4.x)
    //  所有图标均为截图副本/卡片贴图，切割只做特效与计分，
    //  绝不会打开任何程序。
    // ============================================================

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            bool probe = false;
            bool autostart = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--probe", StringComparison.OrdinalIgnoreCase)) probe = true;
                if (string.Equals(args[i], "--autostart", StringComparison.OrdinalIgnoreCase)) autostart = true;
            }

            Sfx.Init();
            Praise.Init();

            if (probe)
            {
                SelfTest.Run();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            float interval = 1.2f;
            int count = 12;
            bool sound = true;

            if (!autostart)
            {
                SettingsForm sf = new SettingsForm();
                if (sf.ShowDialog() != DialogResult.OK) return;
                interval = sf.SpawnInterval;
                count = sf.IconCount;
                sound = sf.SoundOn;
            }


            Rectangle b = Screen.PrimaryScreen.Bounds;

            // 1) 先读桌面图标位置（几毫秒），2) 立刻把"进场动画素材"丢到后台线程预热，
            //    让它和下面的等待/截屏/毛玻璃并行跑，省掉之前那 ~0.5 秒的干等。
            int cellW = 76, cellH = 82;
            List<Point> iconPos = Desk.GetIconPositions(out cellW, out cellH);

            // 桌面图标清单只枚举一次，两条预热线程共用（省一次 shell 遍历）
            List<string> pathList = IconSource.CollectDesktopPaths();

            GameForm.IntroArt introArt = null;
            GameForm.CardBundle cards = null;
            Thread warmIcons = new Thread(delegate ()
            {
                introArt = GameForm.BuildIntroArt(pathList, iconPos, cellW, cellH, b.Width, b.Height);
            });
            warmIcons.IsBackground = true;
            warmIcons.Start();

            Thread warmCards = new Thread(delegate ()
            {
                cards = GameForm.BuildCardBundle(pathList, b.Width, b.Height, count, new Random());
            });
            warmCards.IsBackground = true;
            warmCards.Start();

            Application.DoEvents();
            Thread.Sleep(60);

            // 先"显示桌面"，让快照拿到干净的桌面，退出时再还原之前最小化的窗口
            Desk.ToggleShowDesktop();
            Thread.Sleep(320);

            // 暂时隐藏真实桌面图标 + 把壁纸做成毛玻璃
            IntPtr iconList = Desk.HideDesktopIcons();
            Thread.Sleep(60);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;   // 不含任务栏
            Bitmap rawShot = new Bitmap(wa.Width, wa.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(rawShot))
            {
                g.CopyFromScreen(wa.Location, Point.Empty, wa.Size);
            }
            Bitmap frost = Desk.FrostWallpaper(rawShot);
            rawShot.Dispose();

            // 拉满整屏（本身是模糊的，纵向拉伸完全看不出）
            Bitmap bg = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bg))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(frost, new Rectangle(0, 0, b.Width, b.Height));
            }
            frost.Dispose();

            // 等后台预热收尾（一般早就完成了，这里只是保底）
            try { warmIcons.Join(1200); } catch { }
            try { warmCards.Join(1200); } catch { }

            try
            {
                Application.Run(new GameForm(bg, interval, count, sound, introArt, cards));
            }
            finally
            {
                Desk.ShowDesktopIcons(iconList);   // 还原桌面图标
                Desk.ToggleShowDesktop();          // 还原用户原来的窗口
            }
        }
    }

    // ============================================================
    //  Win32 互操作
    // ============================================================
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
            IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string cls, string win);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string cls, string win);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);

        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;

        public const int SRCCOPY = 0x00CC0020;

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_LARGEICON = 0x000000000;

        // ---- 读桌面图标位置用的跨进程读取 ----
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll")]
        public static extern IntPtr VirtualAllocEx(IntPtr hp, IntPtr addr, uint size, uint allocType, uint protect);

        [DllImport("kernel32.dll")]
        public static extern bool VirtualFreeEx(IntPtr hp, IntPtr addr, uint size, uint freeType);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hp, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT2 pt);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT2 { public int X; public int Y; }
    }

    // ============================================================
    //  桌面图标读取
    // ============================================================
    internal static class IconSource
    {
        public static List<string> CollectDesktopPaths()
        {
            List<string> list = new List<string>();
            try
            {
                string userDesk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string commonDesk = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (Directory.Exists(userDesk)) AddDir(list, userDesk);
                if (Directory.Exists(commonDesk)) AddDir(list, commonDesk);
            }
            catch { }

            string[] arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            Array.Sort(arr, StringComparer.CurrentCultureIgnoreCase);
            list.Clear();
            for (int i = 0; i < arr.Length; i++) list.Add(arr[i]);
            return list;
        }

        private static void AddDir(List<string> list, string dir)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                FileInfo[] files = di.GetFiles();
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo f = files[i];
                    if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
                    if (string.Equals(f.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(f.FullName);
                }
                DirectoryInfo[] subs = di.GetDirectories();
                for (int i = 0; i < subs.Length; i++)
                {
                    DirectoryInfo d = subs[i];
                    if ((d.Attributes & FileAttributes.Hidden) != 0) continue;
                    list.Add(d.FullName);
                }
            }
            catch { }
        }

        /// <summary>把任意桌面项取出方形图标位图（带透明通道，放大过会自动锐化）</summary>
        public static Bitmap LoadIconSquare(string path, int target)
        {
            Bitmap raw = null;
            int native = 0;

            try
            {
                Native.SHFILEINFO sfi = new Native.SHFILEINFO();
                uint cb = (uint)Marshal.SizeOf(typeof(Native.SHFILEINFO));
                IntPtr res = Native.SHGetFileInfo(path, 0, ref sfi, cb,
                    Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
                if (res != IntPtr.Zero && sfi.hIcon != IntPtr.Zero)
                {
                    using (Icon ic = Icon.FromHandle(sfi.hIcon))
                    {
                        raw = ic.ToBitmap();
                        native = raw.Width;
                    }
                    Native.DestroyIcon(sfi.hIcon);
                }
            }
            catch { }

            if (raw == null)
            {
                try
                {
                    using (Icon ic = Icon.ExtractAssociatedIcon(path))
                    {
                        if (ic != null) { raw = ic.ToBitmap(); native = raw.Width; }
                    }
                }
                catch { }
            }

            if (raw == null) raw = LetterTile(path, target);
            if (native == 0) native = raw.Width;

            Bitmap sq = new Bitmap(target, target, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(sq))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                // 桌面图标原生通常只有 32~48px，这里按比例放大铺满画布
                float sc = Math.Min(target / (float)raw.Width, target / (float)raw.Height);
                float dw = raw.Width * sc, dh = raw.Height * sc;
                RectangleF dst = new RectangleF((target - dw) / 2f, (target - dh) / 2f, dw, dh);
                g.DrawImage(raw, dst);
            }
            raw.Dispose();

            // 小图标被大幅放大会变糊，做一个轻量锐化让卡片上的图标更干净
            if (target >= native * 2) sq = ImageFx.Unsharp(sq, 0.55f);
            return sq;
        }

        private static Bitmap LetterTile(string path, int target)
        {
            Bitmap bmp = new Bitmap(target, target, PixelFormat.Format32bppArgb);
            string name = Path.GetFileNameWithoutExtension(path);
            string ch = name.Length > 0 ? name.Substring(0, 1).ToUpper() : "?";
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                RectangleF r = new RectangleF(0, 0, target, target);
                using (GraphicsPath p = CardArt.RoundRect(r, target * 0.16f))
                using (LinearGradientBrush lg = new LinearGradientBrush(r,
                    Color.FromArgb(255, 120, 150, 205), Color.FromArgb(255, 62, 92, 150), 90f))
                {
                    g.FillPath(lg, p);
                }
                using (Font f = new Font("Microsoft YaHei", target * 0.44f, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(ch, f, Brushes.White, r, sf);
                }
            }
            return bmp;
        }
    }

    // ============================================================
    //  图像工具
    // ============================================================
    internal static class ImageFx
    {
        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); p.CloseFigure(); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>轻量 USM 锐化（3x3 卷积）</summary>
        public static Bitmap Unsharp(Bitmap src, float amount)
        {
            int w = src.Width, h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData sd = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dd = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int stride = sd.Stride;
            byte[] buf = new byte[stride * h];
            byte[] outBuf = new byte[stride * h];
            Marshal.Copy(sd.Scan0, buf, 0, buf.Length);

            for (int y = 1; y < h - 1; y++)
            {
                int row = y * stride;
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = row + x * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        int center = buf[idx + c];
                        int sum = 0;
                        sum += buf[idx - stride - 4 + c];
                        sum += buf[idx - stride + c];
                        sum += buf[idx - stride + 4 + c];
                        sum += buf[idx - 4 + c];
                        sum += buf[idx + c];
                        sum += buf[idx + 4 + c];
                        sum += buf[idx + stride - 4 + c];
                        sum += buf[idx + stride + c];
                        sum += buf[idx + stride + 4 + c];
                        float avg = sum / 9f;
                        float v = center + (center - avg) * amount * 2.2f;
                        if (v < 0) v = 0; if (v > 255) v = 255;
                        outBuf[idx + c] = (byte)v;
                    }
                    outBuf[idx + 3] = buf[idx + 3];
                }
            }
            // 复制边缘行
            for (int x = 0; x < stride * h; x++) if (outBuf[x] == 0) outBuf[x] = buf[x];

            Marshal.Copy(outBuf, 0, dd.Scan0, outBuf.Length);
            src.UnlockBits(sd);
            dst.UnlockBits(dd);
            src.Dispose();
            return dst;
        }
    }

    // ============================================================
    //  卡片美术：把图标印在白色纸卡上（切开时撕纸效果）
    // ============================================================
    internal static class CardArt
    {
        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            return ImageFx.RoundRect(r, rad);
        }

        /// <summary>纸卡片：白纸底 + 细边 + 图标</summary>
        public static Bitmap BuildCard(Bitmap src, int size, bool giant)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float pad = size * 0.045f;
                RectangleF r = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);

                using (GraphicsPath ip = RoundRect(r, size * 0.085f))
                {
                    using (LinearGradientBrush lg = new LinearGradientBrush(r,
                        Color.FromArgb(255, 254, 254, 251), Color.FromArgb(255, 227, 228, 224), 90f))
                    {
                        g.FillPath(lg, ip);
                    }
                    // 纸面高光
                    using (LinearGradientBrush hl = new LinearGradientBrush(
                        new RectangleF(r.X, r.Y, r.Width, r.Height * 0.45f),
                        Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    {
                        g.SetClip(ip);
                        g.FillRectangle(hl, r);
                        g.ResetClip();
                    }
                }

                Color borderCol = giant ? Color.FromArgb(255, 196, 62, 48) : Color.FromArgb(255, 168, 170, 166);
                float bw = giant ? size * 0.018f : size * 0.009f;
                using (GraphicsPath bp = RoundRect(r, size * 0.085f))
                using (Pen pen = new Pen(borderCol, bw))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, bp);
                }

                if (giant)
                {
                    // 巨型图标额外一圈内框，视觉上"更重"
                    RectangleF r2 = new RectangleF(r.X + size * 0.035f, r.Y + size * 0.035f,
                        r.Width - size * 0.07f, r.Height - size * 0.07f);
                    using (GraphicsPath bp2 = RoundRect(r2, size * 0.06f))
                    using (Pen pen2 = new Pen(Color.FromArgb(120, 210, 80, 60), size * 0.008f))
                    {
                        g.DrawPath(pen2, bp2);
                    }
                }

                float isz = size * (giant ? 0.62f : 0.60f);
                g.DrawImage(src, new RectangleF((size - isz) / 2f,
                    (size - isz) / 2f + size * 0.012f, isz, isz));
            }
            return bmp;
        }

        /// <summary>炸弹卡片</summary>
        public static Bitmap BuildBomb(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // 无卡片外框：只画炸弹本体
                float cx = size * 0.50f, cy = size * 0.60f, rr = size * 0.295f;

                // 柔和投影
                GraphicsPath shp = new GraphicsPath();
                shp.AddEllipse(cx - rr * 1.15f, cy - rr * 0.75f + rr * 0.75f, rr * 2.3f, rr * 1.7f);
                using (PathGradientBrush sb = new PathGradientBrush(shp))
                {
                    sb.CenterColor = Color.FromArgb(85, 0, 0, 0);
                    sb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                    g.FillPath(sb, shp);
                }
                shp.Dispose();

                // 橙色余晖，让炸弹在桌面上更扎眼
                GraphicsPath glow = new GraphicsPath();
                glow.AddEllipse(cx - rr * 1.60f, cy - rr * 1.60f, rr * 3.2f, rr * 3.2f);
                using (PathGradientBrush gb = new PathGradientBrush(glow))
                {
                    gb.CenterColor = Color.FromArgb(112, 255, 120, 40);
                    gb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 110, 30) };
                    g.FillPath(gb, glow);
                }
                glow.Dispose();

                GraphicsPath ep = new GraphicsPath();
                ep.AddEllipse(cx - rr, cy - rr, rr * 2f, rr * 2f);
                using (PathGradientBrush pb = new PathGradientBrush(ep))
                {
                    pb.CenterPoint = new PointF(cx - rr * 0.34f, cy - rr * 0.38f);
                    pb.CenterColor = Color.FromArgb(255, 186, 188, 196);
                    pb.SurroundColors = new Color[] { Color.FromArgb(255, 8, 8, 11) };
                    g.FillPath(pb, ep);
                }
                ep.Dispose();

                // 高光点
                using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                {
                    g.FillEllipse(b, cx - rr * 0.45f, cy - rr * 0.62f, rr * 0.42f, rr * 0.34f);
                }

                // 橙色轮廓光
                using (Pen rim = new Pen(Color.FromArgb(215, 255, 132, 62), size * 0.013f))
                {
                    g.DrawArc(rim, cx - rr, cy - rr, rr * 2f, rr * 2f, 15, 120);
                }

                // 引线
                using (Pen pen = new Pen(Color.FromArgb(210, 226, 196, 140), size * 0.022f))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawBezier(pen,
                        new PointF(cx + rr * 0.55f, cy - rr * 0.62f),
                        new PointF(cx + rr * 1.05f, cy - rr * 1.20f),
                        new PointF(cx - rr * 0.15f, cy - rr * 1.35f),
                        new PointF(cx + rr * 0.30f, cy - rr * 1.62f));
                }
                // 火花
                PointF[] spark = new PointF[] {
                    new PointF(cx + rr * 0.30f, cy - rr * 1.72f),
                    new PointF(cx + rr * 0.66f, cy - rr * 1.62f),
                    new PointF(cx + rr * 0.32f, cy - rr * 1.46f),
                    new PointF(cx - rr * 0.10f, cy - rr * 1.55f)
                };
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 255, 176, 54)))
                {
                    g.FillPolygon(b, spark);
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 255, 246, 190)))
                {
                    g.FillEllipse(b, cx + rr * 0.20f, cy - rr * 1.62f, rr * 0.26f, rr * 0.26f);
                }
            }
            return bmp;
        }
    }

    // ============================================================
    //  音效（运行时合成 wav，不依赖外部素材）
    // ============================================================
    internal static class Sfx
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        private const uint SND_ASYNC = 0x0001;
        private const uint SND_LOOP = 0x0008;
        private const uint SND_FILENAME = 0x20000;
        private const uint SND_NODEFAULT = 0x0002;

        private static string _sliceA, _sliceB, _bomb;
        private static string[] _tear = new string[3];
        private static string _tearLoop;
        private static bool _tearLoopOn;
        public static bool Enabled = true;

        /// <summary>供自检断言：循环撕纸声是否正在播</summary>
        public static bool TearLooping { get { return _tearLoopOn; } }

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "DesktopIconNinjaSfx");
                Directory.CreateDirectory(dir);
                _sliceA = Path.Combine(dir, "slice_a.wav");
                _sliceB = Path.Combine(dir, "slice_b.wav");
                _bomb = Path.Combine(dir, "bomb.wav");
                for (int i = 0; i < 3; i++) _tear[i] = Path.Combine(dir, "tear_" + i + ".wav");
                _tearLoop = Path.Combine(dir, "sword_swish.wav");
                if (!File.Exists(_sliceA)) WriteWav(_sliceA, MakeSlice(0.17f, 3300, 0), 44100);
                if (!File.Exists(_sliceB)) WriteWav(_sliceB, MakeSlice(0.17f, 3300, 1), 44100);
                if (!File.Exists(_bomb)) WriteWav(_bomb, MakeBomb(), 44100);
                for (int i = 0; i < 3; i++)
                    if (!File.Exists(_tear[i])) WriteWav(_tear[i], MakeTear(0.26f, 700 + i * 131), 44100);
                if (!File.Exists(_tearLoop)) WriteWav(_tearLoop, MakeTearLoop(0.95f), 44100);
            }
            catch { }
        }

        private static int _flip;
        private static int _tearFlip;

        public static void PlaySlice()
        {
            if (!Enabled) return;
            _flip = 1 - _flip;
            TryPlay(_flip == 0 ? _sliceA : _sliceB);
        }

        /// <summary>巨型图标专用的连续"撕啦"：开始切就一直循环放，直到玩家停手</summary>
        public static void StartTearLoop()
        {
            if (!Enabled) return;
            if (_tearLoopOn) return;
            if (_tearLoop == null) return;
            _tearLoopOn = true;
            try
            {
                if (!PlaySound(_tearLoop, IntPtr.Zero, SND_ASYNC | SND_LOOP | SND_FILENAME | SND_NODEFAULT))
                    _tearLoopOn = false;
            }
            catch { _tearLoopOn = false; }
        }

        public static void StopTearLoop()
        {
            if (!_tearLoopOn) return;
            _tearLoopOn = false;
            try { PlaySound(null, IntPtr.Zero, 0); }
            catch { }
        }

        /// <summary>播放任意一个 wav（会打断循环撕纸声）</summary>
        public static void PlayFile(string path)
        {
            if (!Enabled) return;
            TryPlay(path);
        }

        public static void PlayTear()
        {
            if (!Enabled) return;
            _tearFlip++;
            if (_tearFlip >= 3) _tearFlip = 0;
            TryPlay(_tear[_tearFlip]);
        }

        public static void PlayBomb()
        {
            if (!Enabled) return;
            TryPlay(_bomb);
        }

        public static void PlayError()
        {
            if (!Enabled) return;
            TryPlay(_sliceB);
        }

        private static void TryPlay(string path)
        {
            if (path == null) return;
            _tearLoopOn = false;   // 单通道，别的音一响，循环撕纸声就被打断了
            try { PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT); }
            catch { }
        }

        private static short[] MakeSlice(float dur, int seed, int variant)
        {
            int sr = 44100;
            int n = (int)(sr * dur);
            short[] s = new short[n];
            Random r = new Random(seed * 7 + variant * 977);
            float lp = 0f, hp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                float p = t / dur;
                double env = Math.Pow(1.0 - p, 2.4);
                double noise = r.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.62f + (float)noise * 0.38f;
                hp = (float)noise - lp;
                double body = lp * 0.55 + hp * 0.45;
                double tone = Math.Sin(2.0 * Math.PI * (620.0 + 900.0 * (1.0 - p)) * t) * 0.18;
                double v = (body * 0.85 + tone) * env;
                if (variant == 1) v = -v * 0.92;
                int iv = (int)(v * 22000);
                if (iv > 32767) iv = 32767; if (iv < -32768) iv = -32768;
                s[i] = (short)iv;
            }
            return s;
        }

        /// <summary>巨型图标"厚纸被划开"的声音：低频主体 + 噪声撕裂 + 尾部下坠</summary>
        private static short[] MakeTear(float dur, int seed)
        {
            int sr = 44100;
            int n = (int)(sr * dur);
            short[] s = new short[n];
            Random r = new Random(seed);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                float p = t / dur;

                // 前 40% 是划开的撕裂，后半段留一点余韵
                double env = p < 0.35 ? Math.Pow(p / 0.35, 0.55) : Math.Pow(1.0 - (p - 0.35) / 0.65, 1.8);
                double noise = r.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.52f + (float)noise * 0.48f;
                lp2 = lp2 * 0.80f + lp * 0.20f;

                // 低频"咔"的厚实感
                double body = (lp - lp2) * 1.55 + lp2 * 0.30;
                double thud = Math.Sin(2.0 * Math.PI * (150.0 - 55.0 * p) * t) * 0.22;
                double v = (body * 0.95 + thud) * env;

                int iv = (int)(v * 30000);
                if (iv > 32767) iv = 32767; if (iv < -32768) iv = -32768;
                s[i] = (short)iv;
            }
            return s;
        }

        /// <summary>循环用的"挥刀"声音：刀锋划破空气的"唰——唰——"，带一点点金属尾音</summary>
        private static short[] MakeTearLoop(float dur)
        {
            int sr = 44100;
            int n = (int)(sr * dur);
            short[] s = new short[n];
            Random r = new Random(9911);
            float lp = 0f, hp2 = 0f;

            // 一循环挥三刀，每刀又短又快，连起来就是"唰唰唰"
            float[] swingAt = new float[] { 0.05f, 0.36f, 0.66f };
            float swingLen = 0.27f;

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                float p = t / dur;

                float env = 0f;
                float metal = 0f;
                for (int k = 0; k < swingAt.Length; k++)
                {
                    float q = (p - swingAt[k]) / swingLen;      // 本刀进度 0~1
                    if (q > 0f && q < 1f)
                    {
                        // 起手极快、尾巴稍长：刀是"唰"地一下扫过去
                        float e = (float)Math.Sin(Math.PI * Math.Pow(q, 0.38));
                        float ed = e * e;
                        if (ed > env) env = ed;

                        // 刀锋的金属尾音：高频两下，只有前 60 毫秒
                        float st = q * swingLen;               // 本刀内的秒数
                        if (st < 0.07f)
                        {
                            double fade = Math.Exp(-st / 0.028);
                            metal += (float)((Math.Sin(2.0 * Math.PI * 2350.0 * st) * 0.6
                                           + Math.Sin(2.0 * Math.PI * 3720.0 * st) * 0.4) * fade);
                        }
                    }
                }
                if (env > 1f) env = 1f;

                double noise = r.NextDouble() * 2.0 - 1.0;
                lp = lp + ((float)noise - lp) * 0.10f;       // 低频分量
                float hp = (float)noise - lp;                // 高通：刀划空气的"亮"部分
                hp2 = hp2 + (hp - hp2) * 0.70f;              // 去掉最尖的毛刺

                double v = hp2 * (0.05 + 1.15 * env) + metal * 0.10 * (0.4 + 0.6 * env);

                // 首尾各 2% 淡一下，循环接缝不会"咔"一声
                double edge = Math.Min(1.0, Math.Min(p, 1.0 - p) / 0.02);
                v *= edge;
                int iv = (int)(v * 21000);
                if (iv > 32767) iv = 32767; if (iv < -32768) iv = -32768;
                s[i] = (short)iv;
            }
            return s;
        }

        private static short[] MakeBomb()
        {            int sr = 44100;
            float dur = 0.95f;
            int n = (int)(sr * dur);
            short[] s = new short[n];
            Random r = new Random(4242);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                float p = t / dur;
                double env = Math.Pow(1.0 - p, 1.9);
                double noise = r.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.86f + (float)noise * 0.14f;
                double boom = Math.Sin(2.0 * Math.PI * (52.0 - 18.0 * p) * t);
                double v = (boom * 0.75 + lp * 1.5) * env;
                int iv = (int)(v * 26000);
                if (iv > 32767) iv = 32767; if (iv < -32768) iv = -32768;
                s[i] = (short)iv;
            }
            return s;
        }

        private static void WriteWav(string path, short[] samples, int sampleRate)
        {
            byte[] data = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                data[i * 2] = (byte)(samples[i] & 0xFF);
                data[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
            }
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                int riffSize = 36 + data.Length;
                fs.Write(Bytes("RIFF"), 0, 4);
                fs.Write(BitConverter.GetBytes(riffSize), 0, 4);
                fs.Write(Bytes("WAVE"), 0, 4);
                fs.Write(Bytes("fmt "), 0, 4);
                fs.Write(BitConverter.GetBytes(16), 0, 4);
                fs.Write(BitConverter.GetBytes((short)1), 0, 2);
                fs.Write(BitConverter.GetBytes((short)1), 0, 2);
                fs.Write(BitConverter.GetBytes(sampleRate), 0, 4);
                fs.Write(BitConverter.GetBytes(sampleRate * 2), 0, 4);
                fs.Write(BitConverter.GetBytes((short)2), 0, 2);
                fs.Write(BitConverter.GetBytes((short)16), 0, 2);
                fs.Write(Bytes("data"), 0, 4);
                fs.Write(BitConverter.GetBytes(data.Length), 0, 4);
                fs.Write(data, 0, data.Length);
            }
        }

        private static byte[] Bytes(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }
    }

    // ============================================================
    //  巨型图标切开时的女声夸奖：Good / Nice / Perfect（内含 3 段音频，运行时落盘成 wav）
    // ============================================================
    internal static class Praise
    {
        private static string[] _paths = new string[3];
        private static bool _ready;
        private static int _flip;

        private static readonly string[] B64 = new string[] {
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAEAAAAAAAAAAAAAAAAAAAD//wAAAAAAAAAAAAAAAAAAAAAAAAAAAQABAAEAAQAAAAAAAAAAAAEAAQABAAEAAQABAAAAAQABAAAAAAAAAAAAAAAAAAEAAQAAAAAAAQAAAAAAAAD//////////////////wAA//8AAP//AAD+//z//f/9//7//f/+//////////7//v/+/////v////////8AAAAAAQACAAEAAQABAAEAAgADAAMAAwACAAIAAwADAAQABQAIAAYACAAHAAcABgAGAAUAAwAEAAMAAgABAAAA///+/////v/+/wAAAAABAAEAAAABAAEAAAACAAEAAAACAAAAAAABAAIAAQACAAAAAQD9//r//f/8/wAAAwAFAAIAAgAEAAQABAAAAAEAAwADAP//AAAAAAEA/v/8//////8AAAEAAgACAAMAAwAEAAIAAAAAAAIAAwADAAMAAQACAAUAAwABAAAABAAFAAYABgAIAAYABQAGAAUAAQD5//f/9P/2//f/9//1//T/8v/v/+r/7P/s/+r/6f/q/+v/7P/t//L/+f/+////AQAGAAUABwAIAAgACgAPABAAEAARABIAGAAbABoAGgAhACUAJwAnACkALQAuAC0ALgAqACkAKAArAC8AKAAoACgAJQAhACAAIQAgACAAIgAfABgAHAAdABYAEwAVABEADgAKAAEA///5//T/8f/z//H/7v/m/+f/4f/a/9T/zf/G/8X/wv/B/7z/uf+2/7r/t/+7/7n/uP+5/7v/v//A/8T/xf/C/8L/xf/J/8r/0f/X/+H/5//y//v/+/8FAAoAEAASABkAIgAoADIANAA1ADkAOgBCAEYATQBSAFYAVgBZAFgAWQBZAGAAYwBgAGEAaQBmAGYAYgBiAF0AUQBWAFUATQBIAEgASwBIAEUANgAvACgAHwAWABAADQACAPr/6f/d/9L/xv+5/6b/mP+L/33/cP9t/2b/Zf9h/1//VP9V/1H/TP9Q/0v/Tf9R/1T/WP9m/23/dP+A/4n/mf+r/7r/zP/f/+v/+/8BAA8AHgAvAEUAUgBkAHEAfACOAJwApACzALkAwgDNANAA0gDTANIA1wDfAOYA7wD1AAEBAQEDAf4A+gD3AOkA6ADdANQAwwCrAJcAfwBnAEsA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DwAXAAcAxv+s/9f///8TABAACADq//L/9P/m//3/HgAbAB8ADAAIAA8AGwAWAAkA5v/D/63/tv/U/wAAEgAVABcAFQD7/+//+f8PAB4ADwAHAAkACgAIAAgABwABAPn/9P/2//v///8AAAEABQACAP7//f///wAA",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAEAAQABAAEAAQACAAIAAQABAAEAAQACAAEAAgACAAIAAwACAAIAAwACAAIAAQABAAEAAQABAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAP//AAAAAAAA/////////v/+//3//f/8//v//P/8//z//P/8//z//f/9//3//////wAAAAABAAAAAAAAAAAAAAAAAAEAAQAAAAEAAgACAAQABAAEAAIAAgADAAMAAgACAAIAAgADAAIAAgABAAAAAQABAAEAAAAAAAAAAQAAAP//AAABAAMAAgACAAIAAwADAAMAAgABAAIAAgACAAIAAwADAAQABAABAAAAAQACAAIAAgABAAEAAAAAAAIAAQD+//3/+v/6//v/+//6//n/+//8//r/+f/4//n/+f/4//j/9//1//T/9f/4//r/+f/6//v/+//5//j/+v/6//v//f/+//3//P/8//7/AAAAAAAAAAAAAP//AAD///v/+v/5//n/+f/6//f/9f/2//T/9v/z//H/8//z//X/8v/x//X/9//6//3//P/9////AgAEAAIAAQAFAAkACQAIAA4AEwAYABsAGgAXABUAFgAaABoAGQAYABYAFwATAA8AEAAQABAACwANAAkACQAGAAAAAAD8//b/8P/q/+H/3v/Z/9r/0//Q/9H/0v/O/8z/yf/I/8j/zP/L/8j/y//H/8b/x//M/9H/1v/W/9X/2P/f/+P/5//n/+7/9f/7/wUACgANABMAHAAiACMAJAAqAC0AMAAyADQANwA2ADcANgA0AC8AKQAyADIAKQAkACAAJQAlACIAHQAaABYADAAHAAMAAQAAAP///f/4//P/8v/s/+z/6v/o/+b/4v/f/9z/1//Q/8//zf/M/8n/xP/F/8P/xP/H/8j/w//A/8D/xP/A/8D/v//D/8T/xv/H/8v/zf/R/9n/4//q/+n/7P/0//z/AwAMABYAJAAyADwARwBNAFcAYgBrAHgAgACHAJIAmACiAKkAsAC3ALwAuwC7ALYAtQCzAKwApACZAIoAewBsAGMAVAA7ACIAEgADAO//2f/J/7L/kv92/2L/Uv8+/yn/G/8O//7+7/7e/tn+4f7h/uT+6P7w/vv+B/8V/yX/M/9G/1v/b/+H/6P/w//j//7/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MAB//1YAtv8XAE4Aff9QAKr/t/+2AFf/gQCq/0kACgBdALH/4v8rALr/AgDM/xAAz/8kANz/7P83AJT/fgCX/ykAr//M/1cAyf/z/wYAL/9cAKL/8f8XAGP/cADY/w4A2v8aALr///8kAPD/qv83ALT/QwAjAOj/BwAlAP3/HAAtAML/LADv/2AA0v8sAPn/BQAqAPX/HQDc/z4ABAAjAAUALADT/zUA4f8SAAsA1P80AP3/9f8gAOL/7v8MAO3/EADn/w4A+f/z//L/CAD8//f//P/2/+z/FgD8/9X/LgDq/wwABwDr////CgDs/xMA5v8DAAQA/v8JAPj/AgAHAAEACAD5/wAAAgD+/wQA/P8BAAAAAAA=",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA////////////////////////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQABAAEAAQABAAEAAQAAAAAAAAABAAAAAQAAAAAAAAAAAAAAAAAAAAEAAAAAAAEAAQAAAP//AAAAAAAAAAD//wAAAAD//wAA/////wAAAQAAAP//AQABAAAAAAABAAEAAAABAAEAAQAAAAAAAAAAAP////8AAAAAAAD//wAAAAD+//////////////////7//////wAA//8BAAAAAQAAAAIAAgABAAMAAwABAAAAAgACAAEAAAABAAAAAQABAAMAAAACAP//AAAAAAAAAgAAAAIAAQABAAAAAQABAAEAAQABAAAAAAAAAP7//f////////8AAAAA/////wIAAQABAAIAAAABAAEAAQD//wAAAwADAAMAAQAAAP7/AQACAAIAAgD//wIAAAD//wAAAgACAP3//v/+//3/+//9//3//v8AAAEA//8AAP7///8FAAAAAQADAAIAAgADAAQAAQADAAIABQAFAAEAAwAFAAMAAAAAAP///v////3//v/9//3/+//7//3//P/8//v//v/8//z/+v/7//n/+f/6//z//f/4//z//f/7//v/+//+//r//P///wAAAAD+/wIAAAACAAIAAwAAAP7//////wEAAAABAP7//P/+////AQD9////AAABAAAAAgADAAEA//8AAAMAAQAAAAMABAAFAAQAAgADAAgABgAGAAcABgAHAAUABQAGAAUABAACAAAAAAD////////8//3////9//z/+f/6//r/+P/5//r//P/5//r//P/7//7////+//z//P/9//z//f/8//v//v8AAAIAAAAAAAMAAwADAAMABAAFAAYABgAHAAYABgAFAAUABQADAAMABAACAAEAAAABAAEAAAD+////AAADAAAA//8AAP7//f/8//z//v/8//3//P/+//z/+v/9//3/+//7//3/AQD8//v//P/8/////v/9//3//P///wIAAQAEAAYACwADAPz/AgADAAEAAgD//wAA///9//7//v8AAPz/+P/8//n/+//+//r/+f/4//z//P/8////+//6//3/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3P8OAAMACAATAP//AADk/8f/t//L/+b/EQD4/87/qP+a/5T/o/+k/6P/wv+1/6r/kP9+/37/uP/a//P/+v/8/yEAEwACABoADgAYAAkAMAA2ADYANwAnAEcAOAA4AEMAOgArAEQASgBWAGIANQA0AEkASQBfAIEAbQBRAEMAJwAUABsAJAAqAAMADQAVAAMA4f+//8T/2//I/83/y/+w/7b/1//V/8D/0P/R/+X/AgAOABQA6f+7/93/AAAfAB8ADQATABMA6f/l/+v///8RAAIA9P/t/83/yf/l/wwAGAAmACkALAA4AEgANwA/AEgANABAAEsASQBKAFEAOAA1AC0AOwAxAEcAVwBSAE8APAAsABsADADw/+f/AADj/9v/3P/G/7f/qf+s/5z/l/+C/33/e/98/1//Y/9q/23/gv+L/5n/h/+W/6H/rv/A/8//0//H/77/vf/X/97/2P/r//P//v/9//D//P/+/xIAJAAsACIAIAAuACoAJgAuAC0ALAA7AD0AOgBIAFYATwBPAEYAVgBWAFUAVwBUAFsAWgBGADwAMQBEAFEASQBIAD8AMgAaAAQA/P/3//n/9P/x/+j/2P/L/7n/tP+0/7v/tP+5/7L/pv+n/6v/pf+l/7j/v//L/9f/zP/N/9//6//x/+//+v/5//r/7//8/xEAGQAbAAAABAAUABUAGgAeABoADQARAA4ABQAGAAUADgAYABcAEAATABQAEAAWACcAJgAnACgAKgAvADAALgApAC0ALAAyADEAMwAzADIANAAtAC8AKgAiAB8AIwAdABAABwAKAAoACAD9/+7/7v/v/+r/5v/X/87/0//f/+T/5//j/9z/3P/V/9v/6v/y//P/9v/v//D/7v/0/wAABgAIAAkADAD9////7//7//n/+f/6//n/+v/8/wUA/f///wQACAADAPz///8BAP3////7//v/9//7//r//v/8//3/CAABAP7/+v8AAAcAFwAeAA0A/////wAAAQD9/wcACwAPAAYA///8//n//P/+//z//P/6/////f/0//D/6f/g/+n/7//3//f//f/7//r/9//1//r/AgAAAAIAAgAAAPn/+f/1//z///8EAAgACAAJAAsADAANAAoABAAKAAkACwAPABEAEAAFAAEA+P8BAP//9v/3//z////3/+b/6v/x//r/+//2//f/9f/y//P//v8EAAYAAAABAAUAAgAEAAgACAAAAAYAEAAWAA4ABgABAP7//f/8//z///8DAP3/9//v/+z/6v/u//T/8//v//L/8f/s/+r/7P/t/+//7//x//f/+f/7/wAAAwAFAAsADgANABQAHAAaABcAFAATABMAGgAZAB8AHAAbABgAFQAKAAEABQAFAAUA//8AAAAA+f/1//L/7P/w//X/8//z//X/9v/5//j//P8CAAEABAAFAAsAEAAUABMAEgAPAA0ADQAQABAADwASAAoABgABAPn/9f/0//P/7P/r/+f/4//h/9z/3P/a/+D/4//n/+v/6//u/+z/7f/w//f//f8FAAgACAAJAAgADQARABkAHwAoACYAJAAXAA8AEgASABwAIgAhABQADAAMAAgAAQD7////AQD+//X/9P/w/+7/6f/p/+//9P/3//v/+v/5//n//v8DAAIAAQACAAYABQAEAAIA//8AAAUAAgD///b/+f/7//7///////r/+f/6//f/8//v//L/8f/1//j/+P/z//D/8P/y//X//f/+//v/9v/y//T/9f/4//3/BgAMAAsABgADAAMABwAIAAoADgARAA0ADAAKAAkADQATABIADwAOAA8ADwAMAAwAEAANAAUAAQD///7//P/8/wMA///+//r/9v/y//L/9//5//3/AgADAPz//f/+//v/+/8EAAcACQAKAAUAAwD9//z///8IABAADwAJAAwACAABAAAAAQABAAEAAAD+//f/8//u/+//8P/0//H/7f/p/+n/7P/v//X/+P/3//b/+///////AgAJABIAGQAhACMAHwAfAB8AIQAgACEAJAAlACcAHQAVAA8ADAALAAsACwAJAAQA///6//L/6v/n/+z/7f/q/+j/6v/n/+X/4v/i/+b/5v/v//T/8P/u/+//9f/6/wAA/f/9//z//f/+/wIABAACAAMA/v///wAAAAD+//f/9v/z//H/7f/v/+7/8f/z//X/8f/z//f/+P//////AgAFAAcACwAQABUAFAAVABQAGAAeAB4AHwAiACAAHgAXABIAEQASABAADQAMAAYAAAD9//r/9v/0/+//8P/v/+v/6P/n/+X/5f/l/+n/6v/r/+//8v/y//T/9P/3//f/+v/+/wIABgAHAAoACAAHAAUABAAEAAMAAwACAAIAAAD9//n/9P/x//T/9//z//T/+f/6//b/9P/3//j//P8AAAMAAQACAAUABwAIAAkADAAQABYAFwAXABYAFgAVABYAGQAbABoAGQAVAA8ACwAIAAoACgAIAAQAAwAAAPn/9f/x//H/8f/w//H/8//y//P/8v/0//f/+//7//z///8DAAMAAgACAAMABgAHAAkACAAIAAwABwAEAAAAAAD+//7/AgACAP3/+v/0//L/7//u/+7/8P/w//D/7f/s/+v/6//w//L/9v/4//n/+//+/////v///wIACAAMAAwADwAPABEAEAARABUAEgARABEAEQANAAsACQAJAAYAAgAAAPz/+f/6//r//P///////v/4//X/+f/7/wAABQAJAAgACwALAA0ADgAMABAAFQAXABMAEwATABIADgAKAAwADAAKAAUAAAD///v/+P/2//H/6//s/+v/6P/k/+T/5f/n/+n/6f/p/+n/7//v//H/9P/2//b/+/8AAAMAAgD//wAAAAADAAYACQAIAAkACAAFAAMAAgACAAQAAgABAAAA/v/6//n//P/+//7//v8AAP//AAD+//7///8CAAMABgAGAAcACgAKAA0ADQANABAAEQAQABIAFQATAA4ADQAMAAwACgAHAAkABgADAAEA/f/6//n/9//2//L/8//z/+3/7f/t/+r/6P/s/+z/7v/t/+r/6//r/+v/7f/y//b/+P/2//r/+f/4//v//f///wAAAAAAAAAAAQACAAUABQAHAAUABgAEAAcABwAHAAsACgAHAAMABQAGAAQAAwAEAAQABAAIAAoABwAHAAUABwAIAAoADgAOABEADwANAAwADQANAA4AEAAPAAwACQAHAAUAAQD//wEAAAD+//3//f/6//n/+P/4//b/8//z//T/8f/u/+3/8P/y//P/8P/t/+z/8f/z//b/9//1//f/9//5//3/AgADAAAAAQACAAMAAgAFAAcACgAMAA4ACAAJAAgABgAIAAYABwAIAAUABAADAAEAAQABAAAAAgAEAAQABAAEAAIAAgABAAIAAwAEAAUABQAGAAUAAwAEAAUABgAIAAgACwAKAAgABQAFAAIABAAEAAYAAgAAAAAA/f/6//j/9f/2//T/8//y//H/8P/u/+7/7//x//L/8v/x//H/8//z//f/+f/7//7//v///wIABQADAAYACQALAAsADQAPAAwACgAJAAsABwAFAAgABwAGAAMAAAAAAAEAAAD//////f/8//r//P/8//v/+//6//j/9//4//r//P/9//3/AAAAAAAA/////wEAAgAEAAQABgAEAAQABAAEAAYABgAGAAMAAgABAP7//f/7//n/+//7//v/+v/4//v/+f/5//j/+v/5//v//v////7//////wAAAAABAAQABQAGAAYABgAHAAgACAAJAAkACgALAAwACgAJAAkABgAEAAQABAAFAAUAAwADAAEAAAD//wIAAAD//wAAAQD9//3//f/9//////////7//////wAAAgACAAQABQABAAEAAAABAAAA/f/9//z//v/9//z/+f/3//f/9f/2//T/9f/0//X/9P/z//X/9f/2//b/+P/6//z//f/9//3//f/+/wEAAwAEAAUABwAKAAoACAAJAAoADQAMAAwACgAJAAgACAAHAAcABgAEAAIAAQABAAAAAAD//wAAAAABAAIAAAACAAMAAgACAAIAAgAAAAEAAgAEAAUABAADAAIAAQD+/wAAAAD+/wAA/v/9//3//f/8//z//v/9//3////+////AAD+/////v/+/wAAAAAAAP3/////////AAAAAAIAAgAFAAIAAQACAAIAAwADAAUABQAGAAQAAgD//wAAAAAAAP7//P/+////AAD+//7//P/8//j/+P/5//j/+P/1//b/9v/0//b/9v/1//X/9v/6//7//f/+////AAAAAAAAAAD+/wAA/////wAA//8BAAUAAwABAP3//f/7//z////8//3/9//y//f/+//1//j/+//+/wEAAwACAPz//f8BAAIABQAGAAoACwAOABEAEQAOAA0ADQAPABEADgATABYAGAAXABoAIQAnACoAMAA3ADAANQA0ADoAOQA0ACoAIgAeAAEA9P/r/+X/q/91/0X/Sf9y/x//hf6w/t7+RP9C/+f/RABgAIIAPQA9AG0AWgB9AI0AjACXALQAtgCiAIMATgBVAG0ARABGAGMAOwBiAEYALAAzACMAPQAnAB8AGQAjABMA1f/s/8v/w//N/5D/qv/O/8r/6/+4/5H/l/+j/5n/hP+b/6r/vP/2/xcAFAAqAMz/EQDW/8n/AQD0/+b/yf+u/y8AWP+o//r/gf94/+b/v/+0/4f/2//D/4r/tP8zAOr/z/8NAC0A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BADa/+X/igAFAI3/v/8UAA4Af/8gAPX/0f9iAF0A4/8wABMABQDL/5j/ZQCdAEUA+v+i/87/uf+m/xIA5/+k/5//DgCPAOf/+/9QAJ//Qf+Y/9f/PAAwABMAAwCt/7T/pv/T/xkA6/++/wUACQDe/xsAvwDOACgASgBaAO7/BAAGADIATACd/87/KwAWANj/JQAtANb/wP9GADwA8v/z/9n/0v8jAPz//P9GAAoA2v/7/+L/v/8KACMA/v8iAAgAFQARAL//3v8dANX/wv/1/1YAQwBAADoA+//O/5j/0v/+/7z/1v8RAAkA/v8WAC4A+//s/8X/oP/V/y4APQAFADMAGwD6/+n/2v/r//b/EQAvABwADgAYABoALAAfAP3/+P/f/8b/zv/9/wQA+f/j/97/AQADAB8AJAAeABcA9v8HAP//BAAgAPb/+v/r/97/DgAiAB8AAgAQABsA9P/2//f/DAAYAP//6P8FABgAHQAGAOv/AADx/9n/+v8UAAQAEwAPAAAA6P/z//T/9v/9/wkAEAAbAAAA9//x/+b/6f/l//X/+//8/wgADwADABQAGgAVAPv/7//8/wMABQAEAAIAAAD9//f///8JAP3//P/9/wIACQACAA4AEQAEAPf/+//5//D/8P8DAAMAAAAEAP//+//7////BQABAPz/+//3//r/+P/7/wAAAQABAAgABwAEAAQAAwAEAAEAAQADAAMAAwACAAEAAAD///7//v/+//////8AAAAAAAA="
        };

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "DesktopIconNinjaSfx");
                Directory.CreateDirectory(dir);
                string[] names = new string[] { "praise_good.wav", "praise_nice.wav", "praise_perfect.wav" };
                for (int i = 0; i < 3; i++)
                {
                    string p = Path.Combine(dir, names[i]);
                    if (!File.Exists(p)) WriteWav(p, Convert.FromBase64String(B64[i]));
                    _paths[i] = p;
                }
                _ready = true;
            }
            catch { _ready = false; }
        }

        private static void WriteWav(string path, byte[] pcm)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                int sr = 22050, ch = 1, bits = 16;
                int dataLen = pcm.Length;
                int byteRate = sr * ch * bits / 8;
                short blockAlign = (short)(ch * bits / 8);
                WriteStr(fs, "RIFF"); WriteI32(fs, 36 + dataLen);
                WriteStr(fs, "WAVE");
                WriteStr(fs, "fmt "); WriteI32(fs, 16);
                WriteI16(fs, 1); WriteI16(fs, (short)ch);
                WriteI32(fs, sr); WriteI32(fs, byteRate);
                WriteI16(fs, blockAlign); WriteI16(fs, (short)bits);
                WriteStr(fs, "data"); WriteI32(fs, dataLen);
                fs.Write(pcm, 0, dataLen);
            }
        }

        private static void WriteStr(FileStream fs, string s)
        {
            byte[] b = new byte[4];
            for (int i = 0; i < 4; i++) b[i] = (byte)s[i];
            fs.Write(b, 0, 4);
        }

        private static void WriteI32(FileStream fs, int v)
        {
            byte[] b = new byte[4];
            b[0] = (byte)(v & 0xFF); b[1] = (byte)((v >> 8) & 0xFF);
            b[2] = (byte)((v >> 16) & 0xFF); b[3] = (byte)((v >> 24) & 0xFF);
            fs.Write(b, 0, 4);
        }

        private static void WriteI16(FileStream fs, short v)
        {
            byte[] b = new byte[2];
            b[0] = (byte)(v & 0xFF); b[1] = (byte)((v >> 8) & 0xFF);
            fs.Write(b, 0, 2);
        }

        public static bool Ready { get { return _ready; } }

        /// <summary>自检用：三段音频的数据字节数</summary>
        public static int[] DebugSizes()
        {
            return new int[] { B64[0].Length, B64[1].Length, B64[2].Length };
        }

        public static void Play()
        {
            if (!_ready) return;
            _flip++;
            if (_flip >= 3) _flip = 0;
            Sfx.PlayFile(_paths[_flip]);
        }
    }

    // ============================================================
    //  桌面控制：显示桌面 / 还原窗口 / 隐藏桌面图标 / 壁纸模糊
    // ============================================================
    internal static class Desk
    {
        public static void ToggleShowDesktop()
        {
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) return;
                object shell = Activator.CreateInstance(t);
                t.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, null);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
            catch { }
        }

        /// <summary>找到桌面图标所在的 SysListView32 并隐藏（返回句柄用于还原）</summary>
        public static IntPtr HideDesktopIcons()
        {
            try
            {
                IntPtr listView = FindIconListView();
                if (listView != IntPtr.Zero) Native.ShowWindow(listView, Native.SW_HIDE);
                return listView;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>读桌面每个图标在屏幕上的位置（用于进场动画）；读不到就返回空表</summary>
        public static List<Point> GetIconPositions(out int cellW, out int cellH)
        {
            cellW = 76; cellH = 82;
            List<Point> list = new List<Point>();
            IntPtr hp = IntPtr.Zero, mem = IntPtr.Zero;
            try
            {
                IntPtr lv = FindIconListView();
                if (lv == IntPtr.Zero) return list;

                const int LVM_GETITEMCOUNT = 0x1004;
                const int LVM_GETITEMSPACING = 0x1033;
                const int LVM_GETITEMPOSITION = 0x1010;

                int count = (int)Native.SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0 || count > 600) return list;

                uint pid;
                Native.GetWindowThreadProcessId(lv, out pid);
                hp = Native.OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);   // VM_OPERATION|VM_READ|VM_WRITE
                if (hp == IntPtr.Zero) return list;

                mem = Native.VirtualAllocEx(hp, IntPtr.Zero, 16, 0x1000 | 0x2000, 0x04);
                if (mem == IntPtr.Zero) return list;

                byte[] buf = new byte[8];
                IntPtr read = IntPtr.Zero;

                // 单元格尺寸（决定图标摆放的格子大小）
                Native.SendMessage(lv, LVM_GETITEMSPACING, (IntPtr)1, mem);
                Native.ReadProcessMemory(hp, mem, buf, (IntPtr)8, out read);
                int sw = BitConverter.ToInt32(buf, 0);
                int sh = BitConverter.ToInt32(buf, 4);
                if (sw > 20 && sw < 400 && sh > 20 && sh < 400) { cellW = sw; cellH = sh; }

                for (int i = 0; i < count; i++)
                {
                    Native.SendMessage(lv, LVM_GETITEMPOSITION, (IntPtr)i, mem);
                    if (!Native.ReadProcessMemory(hp, mem, buf, (IntPtr)8, out read)) continue;
                    int x = BitConverter.ToInt32(buf, 0);
                    int y = BitConverter.ToInt32(buf, 4);
                    Native.POINT2 p = new Native.POINT2();
                    p.X = x; p.Y = y;
                    Native.ClientToScreen(lv, ref p);
                    list.Add(new Point(p.X, p.Y));
                }
            }
            catch { }
            finally
            {
                try
                {
                    if (mem != IntPtr.Zero) Native.VirtualFreeEx(hp, mem, 0, 0x8000);
                    if (hp != IntPtr.Zero) Native.CloseHandle(hp);
                }
                catch { }
            }
            return list;
        }

        public static void ShowDesktopIcons(IntPtr handle)        {
            try
            {
                if (handle != IntPtr.Zero) Native.ShowWindow(handle, Native.SW_SHOW);
                else
                {
                    IntPtr lv = FindIconListView();
                    if (lv != IntPtr.Zero) Native.ShowWindow(lv, Native.SW_SHOW);
                }
            }
            catch { }
        }

        private static IntPtr FindIconListView()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero)
            {
                // 壁纸幻灯片等情况：DefView 可能挂在某个 WorkerW 下
                IntPtr workerw = IntPtr.Zero;
                for (int i = 0; i < 8; i++)
                {
                    workerw = Native.FindWindowEx(IntPtr.Zero, workerw, "WorkerW", null);
                    if (workerw == IntPtr.Zero) break;
                    defView = Native.FindWindowEx(workerw, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero) break;
                }
            }

            if (defView == IntPtr.Zero) return IntPtr.Zero;
            IntPtr lv = Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            if (lv == IntPtr.Zero) lv = Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            return lv;
        }

        /// <summary>把桌面快照处理成"毛玻璃"壁纸：缩小模糊再放大 + 轻微压暗 + 暗角</summary>
        public static Bitmap FrostWallpaper(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            int sw = Math.Max(1, w / 6), sh = Math.Max(1, h / 6);

            Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, sw, sh));
            }

            small = BoxBlur(small, 2);
            small = BoxBlur(small, 3);

            Bitmap big = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(small, new Rectangle(0, 0, w, h));

                // 轻微压暗 + 冷色，让白色卡片更跳
                using (SolidBrush b = new SolidBrush(Color.FromArgb(42, 12, 18, 28)))
                    g.FillRectangle(b, 0, 0, w, h);

                // 暗角
                GraphicsPath vp = new GraphicsPath();
                float rx = w * 0.72f, ry = h * 0.72f;
                vp.AddEllipse(w / 2f - rx, h / 2f - ry, rx * 2f, ry * 2f);
                using (PathGradientBrush pb = new PathGradientBrush(vp))
                {
                    pb.CenterColor = Color.FromArgb(0, 0, 0, 0);
                    pb.SurroundColors = new Color[] { Color.FromArgb(120, 4, 8, 14) };
                    g.FillPath(pb, vp);
                }
                vp.Dispose();
            }
            small.Dispose();
            return big;
        }

        private static Bitmap BoxBlur(Bitmap src, int radius)
        {
            int w = src.Width, h = src.Height;
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData sd = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = sd.Stride;
            byte[] a = new byte[stride * h];
            byte[] b = new byte[stride * h];
            Marshal.Copy(sd.Scan0, a, 0, a.Length);
            src.UnlockBits(sd);

            BlurPass(a, b, w, h, stride, radius, true);
            BlurPass(b, a, w, h, stride, radius, false);

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData dd = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(a, 0, dd.Scan0, a.Length);
            dst.UnlockBits(dd);
            src.Dispose();
            return dst;
        }

        private static void BlurPass(byte[] src, byte[] dst, int w, int h, int stride, int radius, bool horizontal)
        {
            int win = radius * 2 + 1;
            if (horizontal)
            {
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int r = 0, g = 0, bl = 0, cnt = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int xx = x + k;
                            if (xx < 0) xx = 0;
                            if (xx >= w) xx = w - 1;
                            int idx = row + xx * 4;
                            r += src[idx + 2]; g += src[idx + 1]; bl += src[idx];
                            cnt++;
                        }
                        int o = row + x * 4;
                        dst[o] = (byte)(bl / cnt);
                        dst[o + 1] = (byte)(g / cnt);
                        dst[o + 2] = (byte)(r / cnt);
                        dst[o + 3] = src[o + 3];
                    }
                }
            }
            else
            {
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int r = 0, g = 0, bl = 0, cnt = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int yy = y + k;
                            if (yy < 0) yy = 0;
                            if (yy >= h) yy = h - 1;
                            int idx = yy * stride + x * 4;
                            r += src[idx + 2]; g += src[idx + 1]; bl += src[idx];
                            cnt++;
                        }
                        int o = y * stride + x * 4;
                        dst[o] = (byte)(bl / cnt);
                        dst[o + 1] = (byte)(g / cnt);
                        dst[o + 2] = (byte)(r / cnt);
                        dst[o + 3] = src[o + 3];
                    }
                }
            }
        }
    }

    // ============================================================
    //  启动前设置窗口
    // ============================================================
    internal class SettingsForm : Form
    {
        public float SpawnInterval = 1.2f;
        public int IconCount = 12;
        public bool SoundOn = true;

        private readonly int _available;
        private readonly int _countMin = 6;
        private readonly int _countMax;
        private float _speedVal = 1.2f;
        private int _countVal = 12;
        private bool _soundVal = true;

        // 图标数量可以直接点数字手输
        private bool _countEdit;
        private bool _countSel;
        private string _countBuf = "";

        private const int CW = 480;
        private const int CH = 452;

        private Rectangle _rSlider, _rMinus, _rPlus, _rToggle, _rStart, _rQuit, _rDrag;
        private bool _dragWin, _dragSlider;
        private Point _dragStart;
        private Point _winStart;
        private int _hover;

        private Font _fTitle, _fSub, _fLabel, _fValue, _fBtn, _fSmall;

        public SettingsForm()
        {
            _available = Math.Max(1, IconSource.CollectDesktopPaths().Count);
            _countMax = Math.Max(_countMin, _available);   // 上限 = 桌面实际图标数
            _countVal = Math.Min(12, _countMax);
            if (_countVal < _countMin) _countVal = _countMin;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(CW, CH);
            ShowInTaskbar = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(12, 17, 26);
            KeyPreview = true;
            Text = "桌面图标忍者";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);

            _fTitle = new Font("Microsoft YaHei", 21f, FontStyle.Bold);
            _fSub = new Font("Microsoft YaHei", 9.5f);
            _fLabel = new Font("Microsoft YaHei", 10.5f, FontStyle.Bold);
            _fValue = new Font("Microsoft YaHei", 10.5f, FontStyle.Bold);
            _fBtn = new Font("Microsoft YaHei", 15f, FontStyle.Bold);
            _fSmall = new Font("Microsoft YaHei", 9.5f);

            _rDrag = new Rectangle(0, 0, CW, 104);
            _rSlider = new Rectangle(36, 146, 408, 36);
            _rMinus = new Rectangle(306, 190, 44, 44);
            _rPlus = new Rectangle(404, 190, 44, 44);
            _rToggle = new Rectangle(CW - 36 - 62, 246, 62, 32);
            _rStart = new Rectangle(36, 312, CW - 72, 58);
            _rQuit = new Rectangle(36, 390, CW - 72, 32);

            using (GraphicsPath p = ImageFx.RoundRect(new RectangleF(0, 0, CW, CH), 18f))
            {
                Region = new Region(p);
            }
        }

        // ---------- 绘制 ----------
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            RectangleF full = new RectangleF(0, 0, CW, CH);
            using (LinearGradientBrush bg = new LinearGradientBrush(full,
                Color.FromArgb(255, 24, 34, 49), Color.FromArgb(255, 9, 13, 20), 100f))
            {
                g.FillRectangle(bg, full);
            }
            DrawGlow(g, new PointF(56, 18), 360, Color.FromArgb(66, 72, 142, 240));
            DrawGlow(g, new PointF(CW - 20, CH - 6), 320, Color.FromArgb(44, 240, 118, 66));

            using (GraphicsPath p = ImageFx.RoundRect(new RectangleF(0.75f, 0.75f, CW - 1.5f, CH - 1.5f), 18f))
            using (Pen pen = new Pen(Color.FromArgb(58, 122, 164, 226), 1.5f))
            {
                g.DrawPath(pen, p);
            }

            DrawBadge(g, new RectangleF(CW - 36 - 46, 24, 46, 46));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 236, 243, 251)))
                g.DrawString("桌面图标忍者", _fTitle, b, 34, 20);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 126, 140, 160)))
                g.DrawString("把你桌面上的图标当水果切 · 经典模式", _fSub, b, 36, 66);

            // 分隔线
            using (Pen pen = new Pen(Color.FromArgb(40, 150, 180, 220), 1f))
                g.DrawLine(pen, 36, 100, CW - 36, 100);

            // 行 1：生成速度
            DrawRowLabel(g, "生成速度", 112);
            DrawRightValue(g, _speedVal.ToString("0.0") + " 秒 / 个", 112, CW - 36, Color.FromArgb(255, 138, 186, 255));
            DrawSlider(g);

            // 行 2：图标数量
            DrawRowLabel(g, "参与图标数量", 203);
            DrawRightValue(g, "共 " + _available + " 项 · 可输入 " + _countMin + "~" + _countMax, 205, _rMinus.X - 14, Color.FromArgb(255, 108, 122, 140));
            DrawStepButton(g, _rMinus, false, _hover == 4);
            DrawStepButton(g, _rPlus, true, _hover == 5);

            Rectangle box = CountBox();
            if (_countEdit)
            {
                using (GraphicsPath p = ImageFx.RoundRect(new RectangleF(box.X, box.Y, box.Width, box.Height), 9f))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(66, 96, 158, 255)))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(220, 132, 184, 255), 1.4f))
                        g.DrawPath(pen, p);
                }
            }

            string shown = _countEdit ? _countBuf : _countVal.ToString();
            using (StringFormat sf = CenterFormat())
            using (SolidBrush b = new SolidBrush(_countEdit && _countSel
                ? Color.FromArgb(255, 150, 190, 250) : Color.FromArgb(255, 236, 243, 251)))
            {
                RectangleF vr = new RectangleF(box.X, box.Y, box.Width, box.Height);
                g.DrawString(shown, _fValue, b, vr, sf);
            }
            if (_countEdit && !_countSel)
            {
                SizeF sz = g.MeasureString(shown, _fValue);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 150, 196, 255)))
                    g.FillRectangle(b, box.X + box.Width / 2f + sz.Width / 2f + 2f, box.Y + 11f, 2f, box.Height - 22f);
            }

            // 行 3：音效
            DrawRowLabel(g, "音效", 256);
            DrawToggle(g);

            // 开始按钮
            DrawStartButton(g);
            DrawQuit(g);

            base.OnPaint(pe);
        }

        private static StringFormat CenterFormat()
        {
            StringFormat sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            return sf;
        }

        private void DrawGlow(Graphics g, PointF c, float r, Color col)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddEllipse(c.X - r, c.Y - r, r * 2f, r * 2f);
            using (PathGradientBrush pb = new PathGradientBrush(p))
            {
                pb.CenterColor = col;
                pb.SurroundColors = new Color[] { Color.FromArgb(0, col.R, col.G, col.B) };
                g.FillPath(pb, p);
            }
            p.Dispose();
        }

        private void DrawBadge(Graphics g, RectangleF r)
        {
            using (GraphicsPath p = ImageFx.RoundRect(r, r.Width * 0.22f))
            using (LinearGradientBrush lg = new LinearGradientBrush(r,
                Color.FromArgb(255, 68, 100, 142), Color.FromArgb(255, 24, 36, 54), 115f))
            {
                g.FillPath(lg, p);
            }

            GraphicsState st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            g.RotateTransform(-14f);
            RectangleF cr = new RectangleF(-r.Width * 0.30f, -r.Height * 0.30f, r.Width * 0.60f, r.Height * 0.60f);
            using (GraphicsPath cp = ImageFx.RoundRect(cr, r.Width * 0.05f))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 252, 252, 249)))
                    g.FillPath(b, cp);
                RectangleF ir = new RectangleF(cr.X + cr.Width * 0.2f, cr.Y + cr.Height * 0.2f,
                    cr.Width * 0.6f, cr.Height * 0.6f);
                using (GraphicsPath ip = ImageFx.RoundRect(ir, r.Width * 0.05f))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 70, 110, 168)))
                    g.FillPath(b, ip);
            }
            g.Restore(st);

            st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            g.RotateTransform(28f);
            using (Pen glow = new Pen(Color.FromArgb(140, 165, 215, 255), r.Height * 0.10f))
                g.DrawLine(glow, -r.Width * 0.55f, 0, r.Width * 0.55f, 0);
            using (Pen core = new Pen(Color.FromArgb(255, 255, 255, 255), r.Height * 0.032f))
                g.DrawLine(core, -r.Width * 0.55f, 0, r.Width * 0.55f, 0);
            g.Restore(st);
        }

        private void DrawRowLabel(Graphics g, string text, float y)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 206, 218, 234)))
                g.DrawString(text, _fLabel, b, 36, y);
        }

        private void DrawRightValue(Graphics g, string text, float y, float rightEdge, Color col)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Far;
                sf.LineAlignment = StringAlignment.Near;
                using (SolidBrush b = new SolidBrush(col))
                    g.DrawString(text, _fSmall, b, new RectangleF(36, y, rightEdge - 36, 20), sf);
            }
        }

        private void DrawSlider(Graphics g)
        {
            int ty = _rSlider.Y + 14;
            float cx = KnobX();

            using (GraphicsPath p = ImageFx.RoundRect(new RectangleF(_rSlider.X, ty, _rSlider.Width, 8), 4f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 34, 46, 62)))
                g.FillPath(b, p);

            if (cx - _rSlider.X > 2f)
            {
                using (GraphicsPath p = ImageFx.RoundRect(new RectangleF(_rSlider.X, ty, cx - _rSlider.X, 8), 4f))
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    new RectangleF(_rSlider.X, ty, Math.Max(8f, cx - _rSlider.X), 8f),
                    Color.FromArgb(255, 94, 158, 255), Color.FromArgb(255, 62, 118, 226), 0f))
                    g.FillPath(lg, p);
            }

            float ky = ty + 4;
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(sh, cx - 11f, ky - 10f, 22f, 22f);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 250, 252, 255)))
                g.FillEllipse(b, cx - 9f, ky - 12f, 18f, 18f);
            using (Pen pen = new Pen(Color.FromArgb(255, 90, 152, 250), 1.2f))
                g.DrawEllipse(pen, cx - 9f, ky - 12f, 18f, 18f);
        }

        private float KnobX()
        {
            float t = (_speedVal - 0.3f) / 2.7f;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return _rSlider.X + 9f + t * (_rSlider.Width - 18f);
        }

        private void DrawStepButton(Graphics g, Rectangle r, bool plus, bool hover)
        {
            using (GraphicsPath p = ImageFx.RoundRect(r, 10f))
            {
                Color c1 = hover ? Color.FromArgb(255, 46, 62, 84) : Color.FromArgb(255, 32, 44, 60);
                Color c2 = hover ? Color.FromArgb(255, 36, 50, 70) : Color.FromArgb(255, 24, 34, 48);
                using (LinearGradientBrush lg = new LinearGradientBrush(r, c1, c2, 90f))
                    g.FillPath(lg, p);
                using (Pen pen = new Pen(Color.FromArgb(hover ? 90 : 48, 140, 180, 230), 1f))
                    g.DrawPath(pen, p);
            }
            Color glyph = Color.FromArgb(255, 214, 226, 240);
            float cxp = r.X + r.Width / 2f, cyp = r.Y + r.Height / 2f;
            using (Pen pen = new Pen(glyph, 2.2f))
            {
                g.DrawLine(pen, cxp - 8f, cyp, cxp + 8f, cyp);
                if (plus) g.DrawLine(pen, cxp, cyp - 8f, cxp, cyp + 8f);
            }
        }

        private void DrawToggle(Graphics g)
        {
            Rectangle r = _rToggle;
            bool on = _soundVal;
            using (GraphicsPath p = ImageFx.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), r.Height / 2f))
            {
                Color c1 = on ? Color.FromArgb(255, 82, 148, 255) : Color.FromArgb(255, 40, 52, 70);
                Color c2 = on ? Color.FromArgb(255, 52, 104, 214) : Color.FromArgb(255, 28, 38, 52);
                using (LinearGradientBrush lg = new LinearGradientBrush(r, c1, c2, 90f))
                    g.FillPath(lg, p);
            }
            float kx = on ? r.Right - r.Height / 2f - 3f : r.X + r.Height / 2f + 3f;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 250, 252, 255)))
                g.FillEllipse(b, kx - r.Height / 2f + 3f, r.Y + 3f, r.Height - 6f, r.Height - 6f);
        }

        private void DrawStartButton(Graphics g)
        {
            Rectangle r = _rStart;
            bool hover = _hover == 1;

            using (GraphicsPath shp = ImageFx.RoundRect(
                new RectangleF(r.X, r.Y + 4, r.Width, r.Height), 13f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 20, 70, 170)))
            {
                g.FillPath(b, shp);
            }

            using (GraphicsPath p = ImageFx.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), 13f))
            {
                Color c1 = hover ? Color.FromArgb(255, 104, 168, 255) : Color.FromArgb(255, 82, 148, 255);
                Color c2 = hover ? Color.FromArgb(255, 46, 108, 232) : Color.FromArgb(255, 40, 94, 208);
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    new RectangleF(r.X, r.Y, r.Width, r.Height), c1, c2, 90f))
                    g.FillPath(lg, p);
                using (Pen pen = new Pen(Color.FromArgb(hover ? 220 : 150, 210, 232, 255), 1.2f))
                    g.DrawPath(pen, p);
            }

            using (StringFormat sf = CenterFormat())
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
            {
                g.DrawString("开始游戏", _fBtn, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
            }
        }

        private void DrawQuit(Graphics g)
        {
            Rectangle r = _rQuit;
            Color c = _hover == 2 ? Color.FromArgb(255, 208, 220, 236) : Color.FromArgb(255, 118, 132, 150);
            using (StringFormat sf = CenterFormat())
            using (SolidBrush b = new SolidBrush(c))
            using (Font f = new Font("Microsoft YaHei", 9.5f, FontStyle.Underline))
            {
                g.DrawString("退出", f, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
            }
        }

        // ---------- 交互 ----------
        private Rectangle CountBox()
        {
            return new Rectangle(_rMinus.Right + 2, _rMinus.Y, _rPlus.Left - _rMinus.Right - 4, _rMinus.Height);
        }

        /// <summary>提交手输的数量：低于 6 强制 6，高于桌面图标总数取总数</summary>
        private void CommitCount()
        {
            if (!_countEdit) return;
            int v = _countVal;
            if (!_countSel && _countBuf.Length > 0)
            {
                try { v = int.Parse(_countBuf); }
                catch { v = _countVal; }
            }
            if (v < _countMin) v = _countMin;
            if (v > _countMax) v = _countMax;
            _countVal = v;
            _countEdit = false;
            _countSel = false;
            _countBuf = "";
        }

        private void BeginCountEdit()
        {
            _countEdit = true;
            _countSel = true;          // 先全选，直接敲数字就覆盖
            _countBuf = _countVal.ToString();
            Invalidate();
        }

        private int HitTest(Point p)
        {
            if (_rStart.Contains(p)) return 1;
            if (_rQuit.Contains(p)) return 2;
            if (_rPlus.Contains(p)) return 5;
            if (_rMinus.Contains(p)) return 4;
            if (_rToggle.Contains(p)) return 3;
            if (_rSlider.Contains(p)) return 6;
            if (CountBox().Contains(p)) return 7;
            return 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            int h = HitTest(e.Location);
            if (_countEdit && h != 7) CommitCount();   // 点到别处就先落值
            switch (h)
            {
                case 1:
                    StartGame();
                    return;
                case 2:
                    Close();
                    return;
                case 5:
                    CommitCount();
                    if (_countVal < _countMax) { _countVal++; Invalidate(); }
                    return;
                case 4:
                    CommitCount();
                    if (_countVal > _countMin) { _countVal--; Invalidate(); }
                    return;
                case 7:
                    BeginCountEdit();
                    return;
                case 3:
                    _soundVal = !_soundVal;
                    Invalidate();
                    return;
                case 6:
                    _dragSlider = true;
                    SetSpeedFromX(e.X);
                    return;
            }

            if (_rDrag.Contains(e.Location))
            {
                _dragWin = true;
                _dragStart = Cursor.Position;
                _winStart = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragSlider) { SetSpeedFromX(e.X); return; }
            if (_dragWin)
            {
                Point now = Cursor.Position;
                Location = new Point(_winStart.X + (now.X - _dragStart.X), _winStart.Y + (now.Y - _dragStart.Y));
                return;
            }

            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            if (h >= 1 && h <= 5) Cursor = Cursors.Hand;
            else if (h == 7) Cursor = Cursors.IBeam;
            else if (h == 6) Cursor = Cursors.SizeWE;
            else if (_rDrag.Contains(e.Location)) Cursor = Cursors.SizeAll;
            else Cursor = Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragWin = false;
            _dragSlider = false;
        }

        private void SetSpeedFromX(int x)
        {
            float t = (x - (_rSlider.X + 9f)) / (_rSlider.Width - 18f);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float v = 0.3f + t * 2.7f;
            v = (float)Math.Round(v * 10f) / 10f;
            if (v < 0.3f) v = 0.3f;
            if (v > 3.0f) v = 3.0f;
            if (v != _speedVal) { _speedVal = v; Invalidate(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_countEdit)
            {
                if (e.KeyCode == Keys.Escape) { _countEdit = false; _countSel = false; _countBuf = ""; Invalidate(); return; }
                if (e.KeyCode == Keys.Enter) { CommitCount(); StartGame(); return; }
                if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
                {
                    _countBuf = ""; _countSel = false; Invalidate(); return;
                }
                int d = -1;
                if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) d = e.KeyCode - Keys.D0;
                else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) d = e.KeyCode - Keys.NumPad0;
                if (d >= 0)
                {
                    if (_countSel) { _countBuf = ""; _countSel = false; }
                    if (_countBuf.Length < 3) _countBuf += (char)('0' + d);
                    if (_countBuf.Length > 3) _countBuf = _countBuf.Substring(0, 3);
                    Invalidate();
                }
                return;
            }

            if (e.KeyCode == Keys.Enter) { StartGame(); return; }
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            base.OnKeyDown(e);
        }

        private void StartGame()
        {
            CommitCount();
            SpawnInterval = _speedVal;
            IconCount = _countVal;
            SoundOn = _soundVal;
            Sfx.Enabled = _soundVal;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    //  游戏实体
    // ============================================================
    internal class Item
    {
        public int Kind;          // 0 图标卡 / 1 炸弹 / 2 巨型图标
        public Bitmap Tex;
        public float X, Y, VX, VY;
        public float Ang, AngV;
        public float Size;
        // 巨型图标专用
        public int Phase;         // 0 上升 / 1 悬停 / 2 下落
        public float Timer;
        public float BaseY, WobbleSeed;
        public int HitNeed, HitDone;
        public List<float[]> Scratches;
    }

    internal class Shred
    {
        public float X, Y, VX, VY, Ang, AngV, W, Hh;
        public float Life, MaxLife;
        public Color Col;
        public int Seed;
    }

    internal class Spark
    {
        public float X, Y, VX, VY, Life, MaxLife, R;
        public Color Col;
    }

    internal class Floater
    {
        public float X, Y, Life, MaxLife;
        public string Text;
        public Color Col;
        public float Size;
        public float Scale = 1f;   // 起手放大倍数（爆涨感）
    }

    internal class TrailPt
    {
        public PointF P;
        public float Age;
    }

    // ============================================================
    //  游戏主窗口
    // ============================================================
    internal class GameForm : Form
    {
        private readonly Bitmap _bg;
        private readonly int W, H;
        private readonly Random _rnd = new Random();
        private readonly float _spawnInterval;
        private readonly int _iconCount;

        private readonly List<IconEntry> _icons = new List<IconEntry>();
        private readonly Bitmap[] _bombBucket = new Bitmap[5];
        private readonly int[] _bucket = new int[5];
        private int _giantSize;
        private float _gravity, _launchH;

        private readonly List<Item> _items = new List<Item>();
        private readonly List<Shred> _shreds = new List<Shred>();
        private readonly List<Spark> _sparks = new List<Spark>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly List<TrailPt> _trail = new List<TrailPt>();
        private readonly List<float> _pending = new List<float>();

        private int _score, _lives, _comboBest;
        private bool _gameOver;
        private float _spawnTimer, _giantTimer, _elapsed, _sinceStart;
        private bool _giantActive;

        private bool _mouseDown;
        private PointF _lastPt;

        private float _shake, _shakeX, _shakeY;
        private Floater _giantPop;
        private Floater _comboPop;
        private int _comboCount;
        private float _comboTimer;
        private const float ComboWindow = 0.62f;   // 连击时间窗口（秒）

        private int _best;
        private bool _newRecord;
        private Bitmap _dangerVig;
        private float _tearSoundUntil;
        private bool _everSpawned;

        // 进场动画：桌面图标先聚到屏幕中间，再一起掉下去
        private bool _introActive;
        private float _introT;
        private Bitmap[] _introTex;
        private PointF[] _introFrom;
        private PointF[] _introTo;
        private float[] _introDelay;
        private float[] _introSpin;
        private float[] _introSize;
        private const float IntroGather = 0.58f;
        private const float IntroHold = 0.10f;
        private const float IntroTotal = 1.62f;

        private readonly Stopwatch _clock = new Stopwatch();
        private float _prevTime;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        private IntPtr _bgDc = IntPtr.Zero;
        private IntPtr _bgHbm = IntPtr.Zero;

        private Font _fHud, _fHudBig, _fMid, _fBig, _fSmall, _fGiant;
        private readonly Dictionary<int, GraphicsPath> _shadowCache = new Dictionary<int, GraphicsPath>();

        private const float SpawnIntervalMin = 0.3f;
        private const float UndoLifeLostGrace = 0f;

        internal class IconEntry
        {
            public string Name;
            public Bitmap[] Cards;
            public Bitmap GiantCard;
        }

        public GameForm(Bitmap bg, float spawnInterval, int iconCount, bool soundOn)
            : this(bg, spawnInterval, iconCount, soundOn, (IntroArt)null, (CardBundle)null)
        {
        }

        public GameForm(Bitmap bg, float spawnInterval, int iconCount, bool soundOn,
            IntroArt intro, CardBundle cards)
        {
            _bg = bg;
            W = bg.Width;
            H = bg.Height;
            _spawnInterval = spawnInterval < SpawnIntervalMin ? SpawnIntervalMin : spawnInterval;
            _iconCount = iconCount;
            Sfx.Enabled = soundOn;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Rectangle b = Screen.PrimaryScreen.Bounds;
            Bounds = b;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Default;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _gravity = H * 0.55f;
            _launchH = H * 0.96f;

            _fHud = new Font("Microsoft YaHei", H * 0.019f, FontStyle.Bold);
            _fHudBig = new Font("Microsoft YaHei", H * 0.030f, FontStyle.Bold);
            _fMid = new Font("Microsoft YaHei", H * 0.017f, FontStyle.Bold);
            _fBig = new Font("Microsoft YaHei", H * 0.050f, FontStyle.Bold);
            _fSmall = new Font("Microsoft YaHei", H * 0.013f);
            _fGiant = new Font("Microsoft YaHei", H * 0.040f, FontStyle.Bold);

            // 卡片素材（后台已预热好就直接挂上，否则当场构建）
            ApplyBundle(cards);
            // 进场动画素材（后台已预热好就挂上；没有就跳过动画）
            ApplyIntro(intro);
            try
            {
                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                _bgHbm = _bg.GetHbitmap(Color.FromArgb(0));
                _bgDc = Native.CreateCompatibleDC(screenDc);
                Native.SelectObject(_bgDc, _bgHbm);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
            catch { }

            _score = 0; _lives = 3; _comboBest = 0;
            LoadBest();
            _spawnTimer = 0.6f;
            _giantTimer = NextGiantDelay();
            _clock.Start();
            _prevTime = 0f;

            _timer.Interval = 15;
            _timer.Tick += new EventHandler(OnTick);
            if (!NoAutoStart) _timer.Start();
        }

        /// <summary>自检模式下不启动主循环</summary>
        public static bool NoAutoStart = false;

        // ---------- 资源构建 ----------
        /// <summary>卡片素材包（纯数据，可在后台线程预先做好）</summary>
        internal sealed class CardBundle
        {
            public List<IconEntry> Icons = new List<IconEntry>();
            public int[] Bucket = new int[5];
            public Bitmap[] BombBucket = new Bitmap[5];
            public int GiantSize;
            public Bitmap Danger;
        }

        /// <summary>后台线程也能调：生成全部卡片/炸弹/危险暗角素材</summary>
        internal static CardBundle BuildCardBundle(int w, int h, int iconCount, Random rnd)
        {
            return BuildCardBundle(null, w, h, iconCount, rnd);
        }

        internal static CardBundle BuildCardBundle(List<string> pathList, int w, int h, int iconCount, Random rnd)
        {
            CardBundle cb = new CardBundle();
            int min = (int)(h * 0.060f);
            int max = (int)(h * 0.088f);
            for (int i = 0; i < 5; i++) cb.Bucket[i] = min + (int)((max - min) * (i / 4f));
            cb.GiantSize = (int)(h * 0.155f);

            List<string> paths = pathList != null ? pathList : IconSource.CollectDesktopPaths();
            int n = Math.Min(iconCount, paths.Count);
            if (n < 0) n = 0;

            // 打散顺序：避免每次都是同样几个
            List<int> idx = new List<int>();
            for (int i = 0; i < paths.Count; i++) idx.Add(i);
            for (int i = idx.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int t = idx[i]; idx[i] = idx[j]; idx[j] = t;
            }

            for (int k = 0; k < n; k++)
            {
                string p = paths[idx[k]];
                Bitmap src = IconSource.LoadIconSquare(p, (int)(h * 0.17f));
                IconEntry e = new IconEntry();
                e.Name = Path.GetFileNameWithoutExtension(p);
                e.Cards = new Bitmap[5];
                for (int s = 0; s < 5; s++) e.Cards[s] = CardArt.BuildCard(src, cb.Bucket[s], false);
                e.GiantCard = CardArt.BuildCard(src, cb.GiantSize, true);
                src.Dispose();
                cb.Icons.Add(e);
            }

            if (cb.Icons.Count == 0)
            {
                Bitmap dummy = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(dummy))
                {
                    g.Clear(Color.Transparent);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(255, 120, 150, 205)), 0, 0, 64, 64);
                }
                IconEntry e = new IconEntry();
                e.Name = "图标";
                e.Cards = new Bitmap[5];
                for (int s = 0; s < 5; s++) e.Cards[s] = CardArt.BuildCard(dummy, cb.Bucket[s], false);
                e.GiantCard = CardArt.BuildCard(dummy, cb.GiantSize, true);
                dummy.Dispose();
                cb.Icons.Add(e);
            }

            for (int s = 0; s < 5; s++) cb.BombBucket[s] = CardArt.BuildBomb(cb.Bucket[s]);
            cb.Danger = DangerBmp(w, h);
            return cb;
        }

        /// <summary>把后台做好的素材挂到窗体上；没有就当场做</summary>
        private void ApplyBundle(CardBundle cb)
        {
            if (cb == null || cb.Icons.Count == 0)
            {
                BuildAssets();
                return;
            }
            _icons.Clear();
            for (int i = 0; i < cb.Icons.Count; i++) _icons.Add(cb.Icons[i]);
            for (int s = 0; s < 5; s++)
            {
                _bucket[s] = cb.Bucket[s];
                _bombBucket[s] = cb.BombBucket[s];
            }
            _giantSize = cb.GiantSize;
            _dangerVig = cb.Danger;
        }

        private void BuildAssets()
        {
            int min = (int)(H * 0.060f);
            int max = (int)(H * 0.088f);
            for (int i = 0; i < 5; i++)
            {
                _bucket[i] = min + (int)((max - min) * (i / 4f));
            }
            _giantSize = (int)(H * 0.155f);

            List<string> paths = IconSource.CollectDesktopPaths();
            int n = Math.Min(_iconCount, paths.Count);
            if (n <= 0) n = 0;

            // 打散顺序：避免每次都是同样几个
            List<int> idx = new List<int>();
            for (int i = 0; i < paths.Count; i++) idx.Add(i);
            for (int i = idx.Count - 1; i > 0; i--)
            {
                int j = _rnd.Next(i + 1);
                int t = idx[i]; idx[i] = idx[j]; idx[j] = t;
            }

            for (int k = 0; k < n; k++)
            {
                string p = paths[idx[k]];
                Bitmap src = IconSource.LoadIconSquare(p, (int)(H * 0.17f));
                IconEntry e = new IconEntry();
                e.Name = Path.GetFileNameWithoutExtension(p);
                e.Cards = new Bitmap[5];
                for (int s = 0; s < 5; s++) e.Cards[s] = CardArt.BuildCard(src, _bucket[s], false);
                e.GiantCard = CardArt.BuildCard(src, _giantSize, true);
                src.Dispose();
                _icons.Add(e);
            }

            if (_icons.Count == 0)
            {
                Bitmap dummy = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(dummy))
                {
                    g.Clear(Color.Transparent);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(255, 120, 150, 205)), 0, 0, 64, 64);
                }
                IconEntry e = new IconEntry();
                e.Name = "图标";
                e.Cards = new Bitmap[5];
                for (int s = 0; s < 5; s++) e.Cards[s] = CardArt.BuildCard(dummy, _bucket[s], false);
                e.GiantCard = CardArt.BuildCard(dummy, _giantSize, true);
                dummy.Dispose();
                _icons.Add(e);
            }

            for (int s = 0; s < 5; s++) _bombBucket[s] = CardArt.BuildBomb(_bucket[s]);
            BuildDangerVignette();
        }

        /// <summary>准备进场动画（未预热的降级路径：当场算）</summary>
        private void PrepareIntro(List<Point> pos, int cellW, int cellH)
        {
            ApplyIntro(BuildIntroArt(pos, cellW, cellH, W, H));
        }

        /// <summary>把后台线程预热好的进场素材挂到本窗体上</summary>
        private void ApplyIntro(IntroArt art)
        {
            if (art == null || art.Tex == null || art.Tex.Length == 0) return;
            _introTex = art.Tex;
            _introFrom = art.From;
            _introTo = art.To;
            _introDelay = art.Delay;
            _introSpin = art.Spin;
            _introSize = art.Size;
            _introActive = true;
            _introT = 0f;
        }

        /// <summary>进场动画素材（纯数据，可在后台线程预先算好）</summary>
        internal sealed class IntroArt
        {
            public Bitmap[] Tex;
            public PointF[] From;
            public PointF[] To;
            public float[] Delay;
            public float[] Spin;
            public float[] Size;
        }

        /// <summary>后台线程调用：加载全部桌面图标并算好"聚到中间"的轨迹</summary>
        internal static IntroArt BuildIntroArt(List<Point> pos, int cellW, int cellH, int w, int h)
        {
            return BuildIntroArt(null, pos, cellW, cellH, w, h);
        }

        internal static IntroArt BuildIntroArt(List<string> pathList, List<Point> pos, int cellW, int cellH, int w, int h)
        {
            try
            {
                if (pos == null || pos.Count == 0) return null;
                List<string> paths = pathList != null ? pathList : IconSource.CollectDesktopPaths();
                int n = Math.Min(paths.Count, pos.Count);
                if (n <= 0) return null;

                // 素材按图标实际显示尺寸附近生成（并行取图后这点开销可接受，图标保持清晰）
                int texPx = (int)(Math.Min(cellW, cellH) * 0.86f);
                if (texPx < 34) texPx = 34;
                if (texPx > 64) texPx = 64;

                int drawPx = (int)(Math.Min(cellW, cellH) * 0.86f);
                if (drawPx < 34) drawPx = 34;
                if (drawPx > (int)(h * 0.075f)) drawPx = (int)(h * 0.075f);

                IntroArt art = new IntroArt();
                art.Tex = new Bitmap[n];
                art.From = new PointF[n];
                art.To = new PointF[n];
                art.Delay = new float[n];
                art.Spin = new float[n];
                art.Size = new float[n];

                Random rnd = new Random();
                int cols = (int)Math.Ceiling(Math.Sqrt(n));
                int rows = (int)Math.Ceiling((double)n / cols);
                float gap = Math.Min(w * 0.92f / cols, h * 0.80f / rows);
                float x0 = (w - cols * gap) * 0.5f, y0 = (h - rows * gap) * 0.5f;

                for (int i = 0; i < n; i++)
                {
                    int col = i % cols, row = i / cols;
                    art.To[i] = new PointF(x0 + (col + 0.5f) * gap, y0 + (row + 0.5f) * gap);
                    art.From[i] = new PointF(pos[i].X + cellW * 0.5f, pos[i].Y + drawPx * 0.5f);
                    art.Size[i] = drawPx;
                    art.Delay[i] = (float)(rnd.NextDouble() * 0.16) + (i % 10) * 0.009f;
                    art.Spin[i] = (float)(rnd.NextDouble() * 2.0 - 1.0);
                }

                // 取图标是这里最慢的一步（每个都要问一次 shell），分 4 条线程并行取
                int nt = 4;
                if (n < 12) nt = 1;
                Thread[] workers = new Thread[nt];
                for (int t = 0; t < nt; t++)
                {
                    int step = nt, start = t;
                    Thread w2 = new Thread(delegate ()
                    {
                        for (int i = start; i < n; i += step)
                        {
                            try { art.Tex[i] = IconSource.LoadIconSquare(paths[i], texPx); }
                            catch { }
                        }
                    });
                    workers[t] = w2;
                    w2.IsBackground = true;
                    w2.Start();
                }
                for (int t = 0; t < nt; t++)
                {
                    try { workers[t].Join(); } catch { }
                }
                return art;
            }
            catch { return null; }
        }

        private void DrawIntroIcon(Graphics g, int i, PointF c, float ang, float alpha, float size)
        {
            Bitmap b = _introTex[i];
            if (b == null || alpha <= 0.03f || size <= 1f) return;
            GraphicsState st = g.Save();
            g.TranslateTransform(c.X, c.Y);
            if (ang != 0f) g.RotateTransform(ang);
            if (alpha >= 0.985f)
            {
                g.DrawImage(b, -size / 2f, -size / 2f, size, size);
            }
            else
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = alpha;
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    Rectangle dst = new Rectangle((int)(-size / 2f), (int)(-size / 2f), (int)size, (int)size);
                    g.DrawImage(b, dst, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
                }
            }
            g.Restore(st);
        }

        private void DrawIntro(Graphics g)
        {
            if (!_introActive || _introTex == null) return;
            float fallG = H * 2.6f;

            for (int i = 0; i < _introTex.Length; i++)
            {
                float t = _introT - _introDelay[i];
                float size = _introSize[i];

                if (t <= 0f)
                {
                    DrawIntroIcon(g, i, _introFrom[i], 0f, 1f, size);
                    continue;
                }
                if (t < IntroGather)
                {
                    float k = t / IntroGather;
                    float e = 1f - (float)Math.Pow(1f - k, 3f);     // 先快后慢地飞过去
                    float x = _introFrom[i].X + (_introTo[i].X - _introFrom[i].X) * e;
                    float y = _introFrom[i].Y + (_introTo[i].Y - _introFrom[i].Y) * e;
                    float ang = _introSpin[i] * 26f * (1f - e);
                    float sc = size * (1f + 0.10f * (float)Math.Sin(Math.PI * Math.Min(1f, k)));
                    DrawIntroIcon(g, i, new PointF(x, y), ang, 1f, sc);
                }
                else if (t < IntroGather + IntroHold)
                {
                    float q = (t - IntroGather) / IntroHold;
                    DrawIntroIcon(g, i, _introTo[i], 0f, 1f, size * (1f - 0.10f * q));   // 中间微微一缩
                }
                else
                {
                    float q = t - IntroGather - IntroHold;
                    float y = _introTo[i].Y + 0.5f * fallG * q * q;
                    if (y - size > H + 30f) continue;
                    float ang = _introSpin[i] * 34f * q;
                    float alpha = 1f;
                    if (q > 0.62f) alpha = (float)Math.Max(0f, 1f - (q - 0.62f) / 0.45f);
                    DrawIntroIcon(g, i, new PointF(_introTo[i].X + _introSpin[i] * 40f * q, y), ang, alpha, size);
                }
            }
        }

        /// <summary>全屏红色危险压边，只在巨型图标出现时显示</summary>
        private static Bitmap DangerBmp(int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                GraphicsPath p = new GraphicsPath();
                float rx = w * 0.86f, ry = h * 0.92f;
                p.AddEllipse(w / 2f - rx, h / 2f - ry, rx * 2f, ry * 2f);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = Color.FromArgb(0, 190, 26, 20);
                    b.SurroundColors = new Color[] { Color.FromArgb(205, 190, 26, 20) };
                    g.FillPath(b, p);
                }
                p.Dispose();
            }
            return bmp;
        }

        private void BuildDangerVignette()
        {
            _dangerVig = DangerBmp(W, H);
        }

        private void DrawDangerVignette(Graphics g)
        {
            if (_dangerVig == null) return;

            bool show = false;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Kind == 2) { show = true; break; }
            if (!show) return;

            float pulse = 0.5f + 0.5f * (float)Math.Sin(_elapsed * 3.6f);
            float k = 0.32f + 0.22f * pulse;

            ColorMatrix cm = new ColorMatrix();
            cm.Matrix33 = k;
            ImageAttributes ia = new ImageAttributes();
            ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            g.DrawImage(_dangerVig, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel, ia);
            ia.Dispose();
        }

        // ---------- 主循环 ----------
        private void OnTick(object sender, EventArgs e)
        {
            float now = (float)_clock.Elapsed.TotalSeconds;
            float dt = now - _prevTime;
            _prevTime = now;
            if (dt < 0) dt = 0;
            if (dt > 0.06f) dt = 0.06f;

            // 进场动画期间不出图标、不算分
            if (_introActive)
            {
                _introT += dt;
                _elapsed += dt;
                if (_introT >= IntroTotal)
                {
                    _introActive = false;
                    _spawnTimer = 0.7f;
                    _giantTimer = NextGiantDelay();
                    _sinceStart = 0f;
                    Sfx.PlaySlice();
                }
                Invalidate();
                return;
            }

            if (!_gameOver) Update(dt);
            else UpdateFloaters(dt);

            // 屏幕震动衰减（跟着切刀节奏）
            _shake *= (float)Math.Exp(-9.5f * dt);
            if (_shake < 0.35f) { _shake = 0f; _shakeX = 0f; _shakeY = 0f; }
            else
            {
                _shakeX = (float)(_rnd.NextDouble() * 2 - 1) * _shake;
                _shakeY = (float)(_rnd.NextDouble() * 2 - 1) * _shake * 0.82f;
            }

            _elapsed += dt;
            Invalidate();
        }

        /// <summary>巨型图标下一次出现的间隔：15~30 秒随机</summary>
        private float NextGiantDelay()
        {
            return 15f + (float)_rnd.NextDouble() * 15f;
        }

        /// <summary>给屏幕加一点震动冲击</summary>
        private void Kick(float amount)
        {
            _shake += amount;
            if (_shake > 11f) _shake = 11f;
        }

        private void Update(float dt)
        {
            _sinceStart += dt;
            UpdateTrail(dt);

            // 连击窗口倒计时
            if (_comboTimer > 0f)            {
                _comboTimer -= dt;
                if (_comboTimer <= 0f)
                {
                    _comboTimer = 0f;
                    _comboCount = 0;
                    _comboPop = null;
                }
            }

            // 生成计时
            if (!_giantActive)
            {
                _spawnTimer -= dt;
                if (_spawnTimer <= 0f)
                {
                    _spawnTimer = _spawnInterval;
                    QueueSpawns();
                }
            }

            // 巨型图标计时
            if (!_giantActive)
            {
                _giantTimer -= dt;
                if (_giantTimer <= 0f)
                {
                    SpawnGiant();
                    _giantTimer = NextGiantDelay();
                    _giantActive = true;
                }
            }

            // 延迟生成队列
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                _pending[i] -= dt;
                if (_pending[i] <= 0f)
                {
                    _pending.RemoveAt(i);
                    SpawnNormal();
                }
            }

            // 物体物理
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                Item it = _items[i];
                if (it.Kind == 2) UpdateGiant(it, dt);
                else
                {
                    it.VY += _gravity * dt;
                    it.X += it.VX * dt;
                    it.Y += it.VY * dt;
                    it.Ang += it.AngV * dt;
                    if (it.Y > H + it.Size)
                    {
                        if (it.Kind == 0)
                        {
                            _lives--;
                            if (_lives <= 0) { _lives = 0; TriggerGameOver(); }
                        }
                        _items.RemoveAt(i);
                        continue;
                    }
                    if (it.X < -it.Size * 2 || it.X > W + it.Size * 2) _items.RemoveAt(i);
                }
            }

            // 纸屑
            for (int i = _shreds.Count - 1; i >= 0; i--)
            {
                Shred s = _shreds[i];
                s.VY += _gravity * 0.42f * dt;
                s.VX *= 1f - 1.4f * dt;
                s.X += s.VX * dt;
                s.Y += s.VY * dt;
                s.Ang += s.AngV * dt;
                s.Life -= dt;
                if (s.Life <= 0f || s.Y > H + 120) _shreds.RemoveAt(i);
            }

            // 爆炸粒子
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                Spark s = _sparks[i];
                s.VY += _gravity * 0.5f * dt;
                s.VX *= 1f - 2.2f * dt;
                s.X += s.VX * dt;
                s.Y += s.VY * dt;
                s.Life -= dt;
                if (s.Life <= 0f) _sparks.RemoveAt(i);
            }

            UpdateFloaters(dt);

            // 停手一会儿就把"撕啦"收掉
            if (_tearSoundUntil > 0f && _sinceStart > _tearSoundUntil)
            {
                _tearSoundUntil = 0f;
                Sfx.StopTearLoop();
            }
        }

        private void UpdateFloaters(float dt)
        {
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                Floater f = _floaters[i];
                f.Y -= (H * 0.05f) * dt;
                f.Life -= dt;
                if (f.Life <= 0f) _floaters.RemoveAt(i);
            }
        }

        private void UpdateTrail(float dt)
        {
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                _trail[i].Age += dt;
                if (_trail[i].Age > 0.30f) _trail.RemoveAt(i);
            }
            if (!_mouseDown && _trail.Count > 0 && _trail[_trail.Count - 1].Age > 0.05f)
            {
                // 松手后自然淡出
            }
        }

        // ---------- 生成 ----------
        private void QueueSpawns()
        {
            double r = _rnd.NextDouble();
            int count = 1;
            if (r < 0.26) count = 2;
            else if (r < 0.34) count = 3;
            for (int i = 0; i < count; i++) _pending.Add(i * 0.13f);
        }

        private void SpawnNormal()
        {
            // 第一个出来的必须是普通图标，炸弹不许打头阵
            bool isBomb = _everSpawned && _rnd.NextDouble() < 0.13;
            _everSpawned = true;
            int bi = _rnd.Next(5);
            Bitmap tex;
            int size;
            if (isBomb) { tex = _bombBucket[bi]; size = _bucket[bi]; }
            else
            {
                IconEntry e = _icons[_rnd.Next(_icons.Count)];
                tex = e.Cards[bi];
                size = _bucket[bi];
            }

            float x = (float)(W * (0.10 + _rnd.NextDouble() * 0.80));
            float vy = -(float)(_launchH * (0.94 + _rnd.NextDouble() * 0.12));
            float tUp = Math.Abs(vy) / _gravity;
            float targetX = W * 0.5f + (float)(_rnd.NextDouble() * W * 0.26 - W * 0.13);
            float vx = (targetX - x) / (tUp * 1.35f);

            Item it = new Item();
            it.Kind = isBomb ? 1 : 0;
            it.Tex = tex;
            it.Size = size;
            it.X = x;
            it.Y = H + size * 0.7f;
            it.VX = vx;
            it.VY = vy;
            it.Ang = (float)(_rnd.NextDouble() * 30 - 15);
            it.AngV = (float)(_rnd.NextDouble() * 26 - 13);
            _items.Add(it);
        }

        private void SpawnGiant()
        {
            int need = 50 + _rnd.Next(71);           // 50 ~ 120
            IconEntry e = _icons[_rnd.Next(_icons.Count)];
            Item it = new Item();
            it.Kind = 2;
            it.Tex = e.GiantCard;
            it.Size = _giantSize;
            it.X = W * 0.5f + (float)(_rnd.NextDouble() * W * 0.22 - W * 0.11);
            it.Y = H + _giantSize;
            it.VX = 0f;
            it.VY = -H * 0.95f;
            it.Ang = 0f;
            it.AngV = 0f;
            it.Phase = 0;
            it.Timer = 0f;
            it.BaseY = H * 0.36f;
            it.WobbleSeed = (float)(_rnd.NextDouble() * 6.28);
            it.HitNeed = need;
            it.HitDone = 0;
            it.Scratches = new List<float[]>();
            _items.Add(it);
            _everSpawned = true;
        }

        private void UpdateGiant(Item it, float dt)
        {
            if (it.Phase == 0)
            {
                it.Y += it.VY * dt;
                if (it.Y <= it.BaseY)
                {
                    it.Y = it.BaseY;
                    it.Phase = 1;
                    it.Timer = 5f;
                }
            }
            else if (it.Phase == 1)
            {
                it.Timer -= dt;
                float t = 5f - it.Timer;
                it.Y = it.BaseY + (float)Math.Sin(it.WobbleSeed + t * 1.9f) * H * 0.012f;
                it.X += (float)Math.Sin(it.WobbleSeed + t * 0.7f) * W * 0.0011f;
                if (it.Timer <= 0f)
                {
                    // 时间到还没切够：直接掉下去，不计分、不扣命
                    it.Phase = 2;
                    it.VY = 0f;
                    _tearSoundUntil = 0f;
                    Sfx.StopTearLoop();
                }
            }
            else
            {
                it.VY += _gravity * 1.2f * dt;
                it.Y += it.VY * dt;
                if (it.Y > H + it.Size * 1.2f)
                {
                    _items.Remove(it);
                    _giantActive = false;
                    _giantPop = null;
                    _spawnTimer = 0.5f;
                }
            }
        }

        // ---------- 输入 ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _mouseDown = true;
                _lastPt = new PointF(e.X, e.Y);
                _trail.Clear();
                _trail.Add(NewTrailPt(_lastPt));
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _mouseDown = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            PointF pt = new PointF(e.X, e.Y);
            if (_mouseDown)
            {
                float len = Dist(_lastPt, pt);
                if (len >= 1.5f)
                {
                    _trail.Add(NewTrailPt(pt));
                    if (_trail.Count > 46) _trail.RemoveAt(0);
                    if (!_gameOver) SlashSegment(_lastPt, pt);
                }
                _lastPt = pt;
            }
            base.OnMouseMove(e);
        }

        private TrailPt NewTrailPt(PointF p)
        {
            TrailPt t = new TrailPt();
            t.P = p;
            t.Age = 0f;
            return t;
        }

        private static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool SegCircle(PointF a, PointF b, float cx, float cy, float r)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float fx = a.X - cx, fy = a.Y - cy;
            float a2 = dx * dx + dy * dy;
            float t;
            if (a2 < 0.0001f) return (fx * fx + fy * fy) <= r * r;
            t = -(fx * dx + fy * dy) / a2;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float px = a.X + dx * t, py = a.Y + dy * t;
            float dd = (px - cx) * (px - cx) + (py - cy) * (py - cy);
            return dd <= r * r;
        }

        private void SlashSegment(PointF a, PointF b)
        {
            float len = Dist(a, b);
            if (len < 6f) return;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                Item it = _items[i];
                float r = it.Size * 0.46f;
                if (!SegCircle(a, b, it.X, it.Y, r)) continue;

                if (it.Kind == 0)
                {
                    Sfax1(it);
                    _score += 1;
                    _items.RemoveAt(i);
                    RegisterCut(it.X, it.Y);
                    Kick(1.9f);
                }
                else if (it.Kind == 1)
                {
                    // 切到炸弹：爆炸 + 扣一条命（不再直接结束）
                    Boom(it);
                    _items.RemoveAt(i);
                    _lives--;
                    if (_lives < 0) _lives = 0;
                    if (_lives == 0) { TriggerGameOver(); return; }
                }
                else
                {
                    // 巨型图标：只计算划在它身上的刀
                    if (len >= 10f)
                    {
                        it.HitDone++;
                        AddScratch(it, a, b);
                        // 只要还在切就一直有"撕啦"声：开循环，停手 0.5 秒后才收
                        Sfx.StartTearLoop();
                        _tearSoundUntil = _sinceStart + 0.5f;

                        // 每刀都崩出碎屑（只是视觉冲击，没到目标刀数不会破裂）
                        float hx = (a.X + b.X) * 0.5f, hy = (a.Y + b.Y) * 0.5f;
                        for (int k = 0; k < 3; k++)
                        {
                            AddShredAt(hx, hy, it.Size * 0.45f,
                                (float)(_rnd.NextDouble() * 6.2832), 0.55f, 0.75f);
                        }

                        // 爆涨的计数文字：只有一个，贴卡片上方，每刀跳一次且越切越大
                        float grow = Math.Min(1f, it.HitDone / 20f);
                        float tx = it.X, ty = it.Y - it.Size * 0.62f;
                        float tsz = H * (0.026f + 0.014f * grow);
                        if (_giantPop == null || _floaters.IndexOf(_giantPop) < 0)
                        {
                            _giantPop = AddFloater(tx, ty, "×" + it.HitDone,
                                Color.FromArgb(255, 255, 196, 128), tsz, 0.55f, 0.85f + 0.55f * grow);
                        }
                        else
                        {
                            _giantPop.X = tx; _giantPop.Y = ty;
                            _giantPop.Text = "×" + it.HitDone;
                            _giantPop.Size = tsz;
                            _giantPop.Scale = 0.85f + 0.55f * grow;
                            _giantPop.MaxLife = 0.55f; _giantPop.Life = 0.55f;   // 重置计时 = 重新弹一次
                        }

                        Kick(1.5f);
                        if (it.HitDone >= it.HitNeed) { GiantSuccess(it, i); return; }
                    }
                }
            }
        }

        /// <summary>切中一个普通图标的记连击：连击窗口内切到第 2 个起，每多一个 +5 分</summary>
        private void RegisterCut(float x, float y)
        {
            if (_comboTimer > 0f) _comboCount++;
            else _comboCount = 1;
            _comboTimer = ComboWindow;

            if (_comboCount > _comboBest) _comboBest = _comboCount;
            if (_comboCount < 2) return;

            _score += 5;
            ShowComboPop(x, y, _comboCount);
        }

        private void ShowComboPop(float x, float y, int n)
        {
            float size = H * (0.034f + 0.008f * Math.Min(5, n - 1));
            float ty = y - H * 0.05f;

            if (_comboPop == null || _floaters.IndexOf(_comboPop) < 0)
            {
                _comboPop = AddFloater(x, ty, "连击 ×" + n,
                    Color.FromArgb(255, 255, 220, 120), size, 0.85f, 1.05f);
            }
            else
            {
                _comboPop.X = x; _comboPop.Y = ty;
                _comboPop.Text = "连击 ×" + n;
                _comboPop.Size = size;
                _comboPop.MaxLife = 0.85f; _comboPop.Life = 0.85f;   // 每多切一个就再弹一次
            }
        }

        private void Sfax1(Item it)
        {
            Sfx.PlaySlice();
            MakeShreds(it);
        }

        private void AddScratch(Item it, PointF a, PointF b)
        {
            float half = it.Size * 0.5f;
            float[] seg = new float[4];
            seg[0] = (a.X - it.X) / half; seg[1] = (a.Y - it.Y) / half;
            seg[2] = (b.X - it.X) / half; seg[3] = (b.Y - it.Y) / half;

            // 裁剪到整张卡片（含白纸边）范围：-0.92 ~ 0.92，之前只裁到 ±0.5，
            // 等于只有卡片正中间四分之一能留下刀痕
            if (!ClipSegToBox(seg, -0.92f, 0.92f)) return;
            it.Scratches.Add(seg);
            if (it.Scratches.Count > 140) it.Scratches.RemoveAt(0);
        }

        /// <summary>Liang-Barsky：把线段裁到 ±limit 的方形里，返回是否还有可见部分</summary>
        private static bool ClipSegToBox(float[] seg, float lo, float hi)
        {
            float x0 = seg[0], y0 = seg[1], x1 = seg[2], y1 = seg[3];
            float dx = x1 - x0, dy = y1 - y0;
            float t0 = 0f, t1 = 1f;

            float[] p = new float[] { -dx, dx, -dy, dy };
            float[] q = new float[] { x0 - lo, hi - x0, y0 - lo, hi - y0 };

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-6f)
                {
                    if (q[i] < 0f) return false;
                }
                else
                {
                    float t = q[i] / p[i];
                    if (p[i] < 0f)
                    {
                        if (t > t1) return false;
                        if (t > t0) t0 = t;
                    }
                    else
                    {
                        if (t < t0) return false;
                        if (t < t1) t1 = t;
                    }
                }
            }

            if (t0 >= t1) return false;
            seg[0] = x0 + dx * t0; seg[1] = y0 + dy * t0;
            seg[2] = x0 + dx * t1; seg[3] = y0 + dy * t1;
            return true;
        }

        private void GiantSuccess(Item it, int index)
        {
            int got = it.HitDone * 2;
            _score += got;
            _items.RemoveAt(index);
            _giantActive = false;
            _giantPop = null;
            _spawnTimer = 0.5f;
            _tearSoundUntil = 0f;
            Sfx.StopTearLoop();
            Praise.Play();          // 碎开的瞬间夸一句：Good / Nice / Perfect
            Kick(8.5f);

            // 撕开：大量纸屑
            for (int i = 0; i < 26; i++) AddShredAt(it.X, it.Y, it.Size, (float)(_rnd.NextDouble() * 6.28), 1.5f);
            for (int i = 0; i < 10; i++) AddShredAt(it.X, it.Y, it.Size, (float)(_rnd.NextDouble() * 6.28), 2.2f);
            AddFloater(it.X, it.Y - H * 0.05f, "拆开！ +" + got,
                Color.FromArgb(255, 255, 226, 120), H * 0.048f, 1.5f, 1.2f);
        }

        private void TriggerGameOver()
        {
            if (_gameOver) return;
            _gameOver = true;
            _pending.Clear();
            _tearSoundUntil = 0f;
            Sfx.StopTearLoop();

            // 历史最高分：比纪录高就写盘
            if (_score > _best)
            {
                _best = _score;
                _newRecord = true;
                SaveBest();
            }
        }

        private static string BestFilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopIconNinja");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "best.txt");
        }

        private void LoadBest()
        {
            try
            {
                string p = BestFilePath();
                if (File.Exists(p)) _best = int.Parse(File.ReadAllText(p).Trim());
            }
            catch { _best = 0; }
        }

        private void SaveBest()
        {
            if (NoAutoStart) return;   // 自检模式不碰真实最高分文件
            try { File.WriteAllText(BestFilePath(), _best.ToString()); }
            catch { }
        }

        private void Boom(Item it)
        {
            Sfx.PlayBomb();
            Kick(11f);
            for (int i = 0; i < 90; i++)
            {
                Spark s = new Spark();
                float ang = (float)(_rnd.NextDouble() * 6.2832);
                float spd = (float)(H * (0.30 + _rnd.NextDouble() * 0.85));
                s.X = it.X; s.Y = it.Y;
                s.VX = (float)Math.Cos(ang) * spd;
                s.VY = (float)Math.Sin(ang) * spd - H * 0.1f;
                s.MaxLife = 0.55f + (float)_rnd.NextDouble() * 0.7f;
                s.Life = s.MaxLife;
                s.R = (float)(H * (0.004 + _rnd.NextDouble() * 0.012));
                int pick = _rnd.Next(4);
                if (pick == 0) s.Col = Color.FromArgb(255, 255, 236, 150);
                else if (pick == 1) s.Col = Color.FromArgb(255, 255, 160, 60);
                else if (pick == 2) s.Col = Color.FromArgb(255, 236, 84, 44);
                else s.Col = Color.FromArgb(255, 90, 90, 96);
                _sparks.Add(s);
            }
            // 冲击波纸屑
            for (int i = 0; i < 22; i++)
                AddShredAt(it.X, it.Y, it.Size, (float)(_rnd.NextDouble() * 6.28), 1.1f);
        }

        // ---------- 纸屑特效 ----------
        private void MakeShreds(Item it)
        {
            int count = it.Kind == 2 ? 26 : 12;
            for (int i = 0; i < count; i++)
                AddShredAt(it.X, it.Y, it.Size, (float)(_rnd.NextDouble() * 6.2832), 1.0f);
        }

        private void AddShredAt(float cx, float cy, float size, float dirAng, float speedScale)
        {
            AddShredAt(cx, cy, size, dirAng, speedScale, 2.0f);
        }

        private void AddShredAt(float cx, float cy, float size, float dirAng, float speedScale, float maxLife)
        {
            Shred s = new Shred();
            float rr = size * (float)_rnd.NextDouble() * 0.42f;
            s.X = cx + (float)Math.Cos(dirAng) * rr;
            s.Y = cy + (float)Math.Sin(dirAng) * rr;
            float spd = size * (float)(0.5 + _rnd.NextDouble() * 1.6) * speedScale;
            s.VX = (float)Math.Cos(dirAng) * spd;
            s.VY = (float)Math.Sin(dirAng) * spd - size * 0.35f;
            s.Ang = (float)(_rnd.NextDouble() * 360);
            s.AngV = (float)(_rnd.NextDouble() * 520 - 260);
            s.W = size * (float)(0.10 + _rnd.NextDouble() * 0.18);
            s.Hh = size * (float)(0.05 + _rnd.NextDouble() * 0.11);
            s.MaxLife = maxLife;
            s.Life = maxLife;
            s.Seed = _rnd.Next(4);
            int tone = _rnd.Next(3);
            if (tone == 0) s.Col = Color.FromArgb(255, 253, 253, 249);
            else if (tone == 1) s.Col = Color.FromArgb(255, 238, 238, 230);
            else s.Col = Color.FromArgb(255, 214, 216, 212);
            _shreds.Add(s);
        }

        // ---------- 文字 ----------
        private void AddFloater(float x, float y, string text, Color c, Font f)
        {
            AddFloater(x, y, text, c, f.Size, 1.15f, 1f);
        }

        private Floater AddFloater(float x, float y, string text, Color c, float sizePt, float life, float scale)
        {
            Floater fl = new Floater();
            fl.X = x; fl.Y = y; fl.Text = text; fl.Col = c;
            fl.MaxLife = life; fl.Life = life;
            fl.Size = sizePt;
            fl.Scale = scale;
            _floaters.Add(fl);
            if (_floaters.Count > 18) _floaters.RemoveAt(0);
            return fl;
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            PaintScene(e.Graphics);
            base.OnPaint(e);
        }

        internal void RenderTo(Bitmap target)
        {
            using (Graphics g = Graphics.FromImage(target)) PaintScene(g);
        }

        private void PaintScene(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            // 桌面快照（游戏期间底图，退出后真实桌面原样不动）
            int sdx = (int)_shakeX, sdy = (int)_shakeY;
            if (_bgDc != IntPtr.Zero)
            {
                IntPtr hdc = g.GetHdc();
                Native.BitBlt(hdc, 0, 0, W, H, _bgDc, 0, 0, Native.SRCCOPY);
                if (sdx != 0 || sdy != 0)
                {
                    // 再偏移一次，边缘不会露出黑边
                    Native.BitBlt(hdc, sdx, sdy, W, H, _bgDc, 0, 0, Native.SRCCOPY);
                }
                g.ReleaseHdc(hdc);
            }
            else
            {
                g.DrawImage(_bg, new Rectangle(0, 0, W, H));
            }

            GraphicsState sceneState = g.Save();
            g.TranslateTransform(sdx, sdy);

            // 进场动画：桌面图标聚到中间再掉下去（这期间不画 HUD/卡片）
            if (_introActive)
            {
                DrawIntro(g);
                g.Restore(sceneState);
                return;
            }

            // 巨型图标期间的红色危险压边（全屏，跟着脉搏忽明忽暗）
            DrawDangerVignette(g);

            // 纸屑（在卡片后面）
            DrawShreds(g);

            // 卡片
            for (int i = 0; i < _items.Count; i++) DrawItem(g, _items[i]);

            // 爆炸粒子
            DrawSparks(g);

            // 刀光
            DrawTrail(g);

            // 飘字
            DrawFloaters(g);

            g.Restore(sceneState);

            // HUD（不跟着抖，保证读数清晰）
            DrawHud(g);

            if (_gameOver) DrawGameOver(g);
        }

        private void DrawItem(Graphics g, Item it)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(it.X, it.Y);

            float s = it.Size;

            // 巨型图标：脉动红光，被切得越多越红
            if (it.Kind == 2)
            {
                float pulse = 0.5f + 0.5f * (float)Math.Sin(_elapsed * 5.0f);
                float cut = it.Scratches == null ? 0f : Math.Min(1f, it.Scratches.Count / 22f);
                float gr = s * (1.03f + 0.11f * pulse + 0.09f * cut);
                int alpha = (int)(72 + 72 * pulse + 85 * cut);
                if (alpha > 235) alpha = 235;
                GraphicsPath gp = new GraphicsPath();
                gp.AddEllipse(-gr, -gr, gr * 2f, gr * 2f);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb(alpha, 255, 52, 38);
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, 255, 52, 38) };
                    g.FillPath(pb, gp);
                }
                gp.Dispose();

                // 缓慢旋转的红色危险环，一眼能看出这货不一样
                using (Pen ring = new Pen(Color.FromArgb(140 + (int)(60 * pulse), 255, 74, 58), s * 0.020f))
                {
                    ring.DashStyle = DashStyle.Custom;
                    ring.DashPattern = new float[] { 2.4f, 2.2f };
                    GraphicsState rs = g.Save();
                    g.RotateTransform(_elapsed * 40f);
                    g.DrawEllipse(ring, -s * 0.665f, -s * 0.665f, s * 1.33f, s * 1.33f);
                    g.Restore(rs);
                }
            }

            if (it.Ang != 0f) g.RotateTransform(it.Ang);

            // 炸弹自带柔和投影，不再套卡片阴影
            if (it.Kind != 1)
            {
                using (GraphicsPath p1 = ImageFx.RoundRect(
                    new RectangleF(-s / 2f + s * 0.045f, -s / 2f + s * 0.055f, s * 0.97f, s * 0.97f), s * 0.085f))
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
                {
                    g.FillPath(b1, p1);
                }
                using (GraphicsPath p2 = ImageFx.RoundRect(
                    new RectangleF(-s / 2f + s * 0.022f, -s / 2f + s * 0.028f, s * 0.985f, s * 0.985f), s * 0.085f))
                using (SolidBrush b2 = new SolidBrush(Color.FromArgb(34, 0, 0, 0)))
                {
                    g.FillPath(b2, p2);
                }
            }

            g.DrawImage(it.Tex, -s / 2f, -s / 2f, s, s);

            if (it.Kind == 2 && it.Scratches != null && it.Scratches.Count > 0)
            {
                float half = s / 2f;

                // 深色切痕 + 暖白晕光，白纸上才看得清
                using (Pen halo = new Pen(Color.FromArgb(120, 255, 250, 238), s * 0.034f))
                using (Pen core = new Pen(Color.FromArgb(190, 70, 78, 90), s * 0.0095f))
                using (Pen shine = new Pen(Color.FromArgb(225, 255, 255, 255), s * 0.0032f))
                {
                    halo.LineJoin = LineJoin.Round;
                    core.LineJoin = LineJoin.Round;
                    shine.LineJoin = LineJoin.Round;
                    for (int i = 0; i < it.Scratches.Count; i++)
                    {
                        float[] c = it.Scratches[i];
                        g.DrawLine(halo, c[0] * half, c[1] * half, c[2] * half, c[3] * half);
                        g.DrawLine(core, c[0] * half, c[1] * half, c[2] * half, c[3] * half);
                        g.DrawLine(shine, c[0] * half, c[1] * half, c[2] * half, c[3] * half);
                    }
                }
            }

            g.Restore(st);
        }

        private void DrawShreds(Graphics g)
        {
            for (int i = 0; i < _shreds.Count; i++)
            {
                Shred s = _shreds[i];
                float a = 1f;
                if (s.Life < 0.8f) a = s.Life / 0.8f;
                if (a <= 0f) continue;
                int alpha = (int)(255 * a);
                if (alpha > 255) alpha = 255;

                GraphicsState st = g.Save();
                g.TranslateTransform(s.X, s.Y);
                g.RotateTransform(s.Ang);

                PointF[] quad = PaperQuad(s);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha,
                    s.Col.R, s.Col.G, s.Col.B)))
                {
                    g.FillPolygon(b, quad);
                }
                using (Pen pen = new Pen(Color.FromArgb((int)(alpha * 0.35f), 90, 92, 90), 1f))
                {
                    g.DrawPolygon(pen, quad);
                }
                g.Restore(st);
            }
        }

        private static PointF[] PaperQuad(Shred s)
        {
            float w = s.W, h = s.Hh;
            float jag = s.W * 0.22f;
            switch (s.Seed)
            {
                case 0:
                    return new PointF[] {
                        new PointF(-w / 2f, -h / 2f),
                        new PointF(w / 2f - jag * 0.3f, -h / 2f + jag * 0.5f),
                        new PointF(w / 2f, h / 2f),
                        new PointF(-w / 2f + jag, h / 2f - jag * 0.25f) };
                case 1:
                    return new PointF[] {
                        new PointF(-w / 2f, -h / 2f),
                        new PointF(w / 2f, -h / 2f - jag * 0.3f),
                        new PointF(w / 2f - jag * 0.5f, h / 2f),
                        new PointF(-w / 2f, h / 2f - jag * 0.4f) };
                case 2:
                    return new PointF[] {
                        new PointF(-w / 2f, -h / 2f + jag * 0.6f),
                        new PointF(w / 2f, -h / 2f),
                        new PointF(w / 2f - jag * 0.4f, h / 2f - jag * 0.3f),
                        new PointF(-w / 2f + jag * 0.2f, h / 2f) };
                default:
                    return new PointF[] {
                        new PointF(-w / 2f, -h / 2f),
                        new PointF(w / 2f, -h / 2f),
                        new PointF(w / 2f - jag * 0.6f, h / 2f - jag * 0.2f),
                        new PointF(-w / 2f + jag * 0.5f, h / 2f) };
            }
        }

        private void DrawSparks(Graphics g)
        {
            for (int i = 0; i < _sparks.Count; i++)
            {
                Spark s = _sparks[i];
                float a = s.Life / s.MaxLife;
                if (a <= 0f) continue;
                int alpha = (int)(255 * a);
                if (alpha > 255) alpha = 255;
                float r = s.R * (0.4f + a * 0.9f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, s.Col.R, s.Col.G, s.Col.B)))
                {
                    g.FillEllipse(b, s.X - r, s.Y - r, r * 2f, r * 2f);
                }
            }
        }

        private void DrawTrail(Graphics g)
        {
            if (_trail.Count < 2) return;
            for (int i = 1; i < _trail.Count; i++)
            {
                TrailPt p0 = _trail[i - 1];
                TrailPt p1 = _trail[i];
                float a = 1f - p1.Age / 0.30f;
                if (a <= 0f) continue;
                float wdt = 3f + 15f * a * (i / (float)_trail.Count);
                int alpha = (int)(240 * a);
                if (alpha > 255) alpha = 255;
                using (Pen glow = new Pen(Color.FromArgb((int)(alpha * 0.5f), 150, 210, 255), wdt * 1.9f))
                {
                    glow.LineJoin = LineJoin.Round;
                    glow.StartCap = LineCap.Round;
                    glow.EndCap = LineCap.Round;
                    g.DrawLine(glow, p0.P, p1.P);
                }
                using (Pen core = new Pen(Color.FromArgb(alpha, 255, 255, 255), wdt))
                {
                    core.LineJoin = LineJoin.Round;
                    core.StartCap = LineCap.Round;
                    core.EndCap = LineCap.Round;
                    g.DrawLine(core, p0.P, p1.P);
                }
            }
        }

        private void DrawFloaters(Graphics g)
        {
            for (int i = 0; i < _floaters.Count; i++)
            {
                Floater f = _floaters[i];
                float a = f.Life / f.MaxLife;
                if (a <= 0f) continue;

                float t = 1f - a;                       // 0 -> 1 的进度
                int alpha = (int)(255 * Math.Min(1f, a * 2.6f));
                if (alpha > 255) alpha = 255;

                // 起手瞬间炸大再收回来，形成"爆涨"手感
                float pop = (float)Math.Exp(-5.0f * t);
                float size = f.Size * f.Scale * (1f + 0.55f * pop);
                float y = f.Y - H * 0.038f * t;

                using (Font fn = new Font("Microsoft YaHei", size, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    RectangleF r = new RectangleF(f.X - W * 0.35f, y - H * 0.06f, W * 0.7f, H * 0.12f);
                    DrawTextOutlined(g, f.Text, fn, r, sf, Color.FromArgb(alpha, f.Col.R, f.Col.G, f.Col.B));
                }
            }
        }

        private static void DrawTextOutlined(Graphics g, string text, Font font, RectangleF r,
            StringFormat sf, Color col)
        {
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(col.A / 2, 0, 0, 0)))
            {
                RectangleF rs = new RectangleF(r.X + 2, r.Y + 3, r.Width, r.Height);
                g.DrawString(text, font, sh, rs, sf);
            }
            using (SolidBrush b = new SolidBrush(col))
            {
                g.DrawString(text, font, b, r, sf);
            }
        }

        private void DrawHud(Graphics g)
        {
            // 居中一体的 HUD：分数在左、生命在右，整块压在屏幕正上方中央
            float plateW = H * 0.465f;
            float plateH = H * 0.104f;
            float plateX = (W - plateW) / 2f;
            float plateY = H * 0.016f;

            using (GraphicsPath p = ImageFx.RoundRect(
                new RectangleF(plateX, plateY, plateW, plateH), H * 0.020f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(72, 8, 10, 14)))
            {
                g.FillPath(b, p);
            }

            // 分数
            string sc = "分数  " + _score;
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                DrawTextOutlined(g, sc, _fHudBig,
                    new RectangleF(plateX + H * 0.030f, plateY + plateH * 0.03f, plateW * 0.55f, plateH * 0.56f), sf,
                    Color.FromArgb(250, 255, 255, 255));
            }

            // 历史最高
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                RectangleF rr = new RectangleF(plateX + H * 0.034f, plateY + plateH * 0.545f, plateW * 0.55f, plateH * 0.34f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(228, 202, 218, 244)))
                    g.DrawString("历史最高  " + _best, _fMid, b, rr, sf);
            }

            // 生命（居中偏右，和分数在同一块底板上）
            float hs = H * 0.023f;
            float sp = H * 0.042f;
            float heartsRight = plateX + plateW - H * 0.040f;
            float cx0 = heartsRight - 2f * sp;
            float cy = plateY + plateH * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                Color c = i < _lives ? Color.FromArgb(250, 255, 96, 96) : Color.FromArgb(85, 255, 255, 255);
                DrawHeart(g, cx0 + i * sp, cy, hs, c);
            }

            // 开场提示
            if (_sinceStart < 4.0f && !_gameOver)
            {
                float a = _sinceStart > 3.0f ? (4.0f - _sinceStart) : 1f;
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    DrawTextOutlined(g, "按住鼠标滑动切开图标卡片 · 炸弹会扣一条命 · ESC 退出", _fMid,
                        new RectangleF(0, H * 0.86f, W, H * 0.06f), sf,
                        Color.FromArgb((int)(235 * a), 245, 248, 252));
                }
            }
        }

        private static void DrawHeart(Graphics g, float cx, float cy, float s, Color c)
        {
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillEllipse(b, cx - s * 0.62f, cy - s * 0.5f, s * 0.66f, s * 0.66f);
                g.FillEllipse(b, cx - s * 0.04f, cy - s * 0.5f, s * 0.66f, s * 0.66f);
                PointF[] tri = new PointF[] {
                    new PointF(cx - s * 0.66f, cy + s * 0.02f),
                    new PointF(cx + s * 0.66f, cy + s * 0.02f),
                    new PointF(cx, cy + s * 0.88f) };
                g.FillPolygon(b, tri);
            }
        }

        private void DrawGameOver(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 8, 10, 14)))
            {
                g.FillRectangle(b, 0, 0, W, H);
            }

            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                DrawTextOutlined(g, "游戏结束", _fBig,
                    new RectangleF(0, H * 0.32f, W, H * 0.09f), sf, Color.FromArgb(255, 255, 255, 255));
                DrawTextOutlined(g, "本局得分  " + _score, _fHudBig,
                    new RectangleF(0, H * 0.44f, W, H * 0.07f), sf, Color.FromArgb(255, 255, 214, 102));
                DrawTextOutlined(g, "历史最高  " + _best + (_newRecord ? "   新纪录！" : ""), _fMid,
                    new RectangleF(0, H * 0.515f, W, H * 0.05f), sf,
                    _newRecord ? Color.FromArgb(255, 255, 208, 96) : Color.FromArgb(230, 200, 210, 222));
                DrawTextOutlined(g, "最高一次连击  ×" + _comboBest, _fMid,
                    new RectangleF(0, H * 0.575f, W, H * 0.05f), sf, Color.FromArgb(230, 210, 218, 228));
                DrawTextOutlined(g, "按 空格 重新开始        ESC 退出", _fHud,
                    new RectangleF(0, H * 0.67f, W, H * 0.06f), sf, Color.FromArgb(240, 255, 255, 255));
            }
        }

        // ---------- 键盘 ----------
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                return;
            }
            if (e.KeyCode == Keys.Space && _gameOver)
            {
                RestartGame();
                return;
            }
        }

        private void RestartGame()
        {
            _items.Clear();
            _shreds.Clear();
            _sparks.Clear();
            _floaters.Clear();
            _trail.Clear();
            _pending.Clear();
            _score = 0;
            _lives = 3;
            _comboBest = 0;
            _comboCount = 0;
            _comboTimer = 0f;
            _comboPop = null;
            _newRecord = false;
            _gameOver = false;
            _tearSoundUntil = 0f;
            Sfx.StopTearLoop();
            _giantActive = false;
            _giantPop = null;
            _everSpawned = false;      // 新一局，炸弹还是不许打头阵
            _spawnTimer = 0.8f;
            _giantTimer = NextGiantDelay();
            _sinceStart = 0f;
            _prevTime = (float)_clock.Elapsed.TotalSeconds;
        }

        private Item NewCard(int bucket, float x, float y, float ang)
        {
            Item it = new Item();
            it.Kind = 0;
            it.Tex = _icons[_rnd.Next(_icons.Count)].Cards[bucket];
            it.Size = _bucket[bucket];
            it.X = x; it.Y = y; it.Ang = ang; it.AngV = 0f; it.VX = 0f; it.VY = 0f;
            return it;
        }

        // ---------- 自检：离屏场景 + 规则断言（--probe 使用） ----------
        internal void BuildDemoScene(int variant)
        {
            _items.Clear(); _shreds.Clear(); _sparks.Clear(); _floaters.Clear();
            _trail.Clear(); _pending.Clear();
            _sinceStart = 6f;
            _giantActive = false;
            _gameOver = false;

            if (variant == 0)
            {
                _score = 87; _lives = 2; _comboBest = 2;

                _items.Add(NewCard(2, W * 0.22f, H * 0.30f, -12f));
                _items.Add(NewCard(0, W * 0.33f, H * 0.60f, 9f));
                _items.Add(NewCard(4, W * 0.70f, H * 0.26f, -20f));
                _items.Add(NewCard(1, W * 0.80f, H * 0.62f, 14f));

                Item bomb = new Item();
                bomb.Kind = 1;
                bomb.Tex = _bombBucket[2];
                bomb.Size = _bucket[2];
                bomb.X = W * 0.63f; bomb.Y = H * 0.78f; bomb.Ang = 8f;
                _items.Add(bomb);

                // 刀光
                for (int i = 0; i < 16; i++)
                {
                    TrailPt t = new TrailPt();
                    t.P = new PointF(W * 0.20f + i * W * 0.030f, H * 0.64f - i * 9f);
                    t.Age = 0.28f - i * 0.015f;
                    if (t.Age < 0f) t.Age = 0f;
                    _trail.Add(t);
                }

                // 巨型图标（悬停中，带划痕）
                SpawnGiant();
                Item g = _items[_items.Count - 1];
                g.X = W * 0.50f; g.Y = H * 0.38f; g.Phase = 1; g.Timer = 2.4f; g.HitNeed = 30;
                for (int i = 0; i < 24; i++)
                {
                    float yy = g.Y - g.Size * 0.34f + i * g.Size * 0.028f;
                    SlashSegment(new PointF(g.X - g.Size * 0.52f, yy),
                        new PointF(g.X + g.Size * 0.52f, yy + g.Size * 0.05f));
                }

                for (int i = 0; i < 18; i++)
                    AddShredAt(W * 0.40f, H * 0.44f, _bucket[3], (float)(_rnd.NextDouble() * 6.28), 0.55f);

                // 连击大字（放到中段年龄上，截图能看到"炸开"后的样子）
                AddFloater(W * 0.50f, H * 0.15f, "连击 ×3",
                    Color.FromArgb(255, 255, 220, 120), H * 0.048f, 1.25f, 1.05f);
                _floaters[_floaters.Count - 1].Life = 0.55f;
                AddFloater(W * 0.50f, H * 0.225f, "+10",
                    Color.FromArgb(255, 255, 246, 190), H * 0.020f, 1.05f, 0.85f);
                _floaters[_floaters.Count - 1].Life = 0.55f;

                // 让自检截图带上震动偏移，检查底图边缘是否露黑
                _shakeX = -6f; _shakeY = 4f;
            }
            else
            {
                _gameOver = true;
                _score = 312;
                _lives = 0;
                _comboBest = 3;
                _best = 312; _newRecord = true;
            }
        }

        internal string SelfCheck()
        {
            StringBuilder sb = new StringBuilder();
            _spawnTimer = 999f;
            _giantTimer = 999f;
            _pending.Clear();

            // 1) 一刀切 3 张卡：应 +3 分，连击最多 3，附加 (3-1)*5=10
            _items.Clear(); _floaters.Clear(); _shreds.Clear();
            _score = 0; _lives = 3; _comboBest = 0; _gameOver = false; _giantActive = false;
            float yy0 = H * 0.5f;
            _items.Add(NewCard(2, W * 0.30f, yy0, 0f));
            _items.Add(NewCard(0, W * 0.44f, yy0, 0f));
            _items.Add(NewCard(4, W * 0.58f, yy0, 0f));
            SlashSegment(new PointF(W * 0.20f, yy0), new PointF(W * 0.66f, yy0));
            sb.AppendLine("check1 一刀三切 -> score=" + _score + " comboBest=" + _comboBest
                + " itemsLeft=" + _items.Count + "  (expect 13 / 3 / 0)");

            // 2) 漏掉一个图标：扣 1 条命
            _items.Clear(); _lives = 3;
            Item miss = NewCard(1, W * 0.5f, H + 400f, 0f);
            _items.Add(miss);
            Update(0.05f);
            sb.AppendLine("check2 漏图标 -> lives=" + _lives + " itemsLeft=" + _items.Count + "  (expect 2 / 0)");

            // 3) 巨型图标切满 30 刀 -> 得分 30*2=60，移除，恢复正常刷怪
            _items.Clear(); _score = 0; _giantActive = true; _lives = 3;
            SpawnGiant();
            Item g = _items[_items.Count - 1];
            g.Y = H * 0.40f; g.Phase = 1; g.Timer = 3f;
            g.HitNeed = 30; g.HitDone = 0; g.X = W * 0.5f;
            int guard = 0;
            while (_items.Count > 0 && guard < 60)
            {
                guard++;
                float yy = g.Y - 60f + (guard % 20) * 7f;
                SlashSegment(new PointF(g.X - g.Size * 0.5f, yy),
                    new PointF(g.X + g.Size * 0.5f, yy + 5f));
            }
            sb.AppendLine("check3 巨型切满 -> score=" + _score + " itemsLeft=" + _items.Count
                + " giantActive=" + _giantActive + "  (expect 60 / 0 / False)");

            // 4) 巨型图标 5 秒未达标 -> 掉下去，不计分不扣命
            _items.Clear(); _score = 0; _lives = 3; _giantActive = true; _spawnTimer = 999f;
            SpawnGiant();
            Item g2 = _items[_items.Count - 1];
            g2.Y = H * 0.40f; g2.Phase = 1; g2.Timer = 5f; g2.HitNeed = 30; g2.X = W * 0.5f;
            int steps = 0;
            while (_items.Count > 0 && steps < 200)
            {
                steps++;
                Update(0.05f);
            }
            sb.AppendLine("check4 巨型超时 -> score=" + _score + " lives=" + _lives
                + " itemsLeft=" + _items.Count + " frames=" + steps + "  (expect 0 / 3 / 0)");

            // 5) 切到炸弹 -> 只扣一条命，不结束
            _items.Clear(); _score = 0; _lives = 3; _gameOver = false; _sparks.Clear();
            Item b1 = new Item();
            b1.Kind = 1; b1.Tex = _bombBucket[1]; b1.Size = _bucket[1];
            b1.X = W * 0.5f; b1.Y = H * 0.5f;
            _items.Add(b1);
            SlashSegment(new PointF(W * 0.35f, H * 0.5f), new PointF(W * 0.65f, H * 0.5f));
            sb.AppendLine("check5 切炸弹 -> lives=" + _lives + " gameOver=" + _gameOver
                + " itemsLeft=" + _items.Count + " sparks=" + _sparks.Count
                + "  (expect 2 / False / 0 / >0)");

            // 6) 再被炸两次 -> 三条命耗尽才结束
            for (int k = 0; k < 2 && !_gameOver; k++)
            {
                Item bk = new Item();
                bk.Kind = 1; bk.Tex = _bombBucket[1]; bk.Size = _bucket[1];
                bk.X = W * 0.5f; bk.Y = H * 0.5f;
                _items.Add(bk);
                SlashSegment(new PointF(W * 0.30f, H * 0.5f), new PointF(W * 0.70f, H * 0.5f));
            }
            sb.AppendLine("check6 三命耗尽 -> lives=" + _lives + " gameOver=" + _gameOver
                + "  (expect 0 / True)");

            // 7) 时间窗连击：两刀（不是一刀）在 0.62 秒窗口内切中两张 -> 连击 2，得分 2+5=7
            _items.Clear(); _shreds.Clear(); _sparks.Clear(); _floaters.Clear();
            _score = 0; _comboBest = 0; _comboCount = 0; _comboTimer = 0f; _comboPop = null;
            _gameOver = false; _lives = 3;
            _items.Add(NewCard(1, W * 0.34f, H * 0.40f, 0f));
            SlashSegment(new PointF(W * 0.20f, H * 0.40f), new PointF(W * 0.48f, H * 0.40f));
            Update(0.35f);   // 窗口未过期
            _items.Add(NewCard(1, W * 0.66f, H * 0.40f, 0f));
            SlashSegment(new PointF(W * 0.52f, H * 0.40f), new PointF(W * 0.80f, H * 0.40f));
            sb.AppendLine("check7 时间窗连击 -> score=" + _score + " comboBest=" + _comboBest
                + "  (expect 7 / 2)");

            // 8) 历史最高分：破纪录就更新并置新纪录标记
            _best = 5; _score = 21; _gameOver = false; _newRecord = false;
            TriggerGameOver();
            sb.AppendLine("check8 最高分落盘 -> best=" + _best + " newRecord=" + _newRecord
                + "  (expect 21 / True)");

            // 9) 巨型图标目标刀数区间：50 ~ 150
            int lo = 999, hi = 0;
            for (int i = 0; i < 60; i++)
            {
                SpawnGiant();
                int need = _items[_items.Count - 1].HitNeed;
                if (need < lo) lo = need;
                if (need > hi) hi = need;
                _items.Clear();
            }
            sb.AppendLine("check9 巨型刀数区间 -> min=" + lo + " max=" + hi
                + "  (expect 50 / 120 区间内)");

            // 12) 炸弹不许第一个出现：反复模拟第一刷，都必须是普通图标
            bool firstOk = true;
            for (int i = 0; i < 40; i++)
            {
                _items.Clear();
                _everSpawned = false;
                SpawnNormal();
                if (_items[0].Kind != 0) { firstOk = false; break; }
            }
            _items.Clear(); _everSpawned = true;
            sb.AppendLine("check12 首个图标不是炸弹 -> firstAlwaysNormal=" + firstOk
                + "  (expect True)");

            // 13) 炸弹实际出现比例（按每次刷新统计，理论 12%）
            int bombN = 0, totalN = 0;
            _items.Clear(); _everSpawned = true;
            for (int i = 0; i < 20000; i++)
            {
                _items.Clear();
                SpawnNormal();
                totalN++;
                if (_items[0].Kind == 1) bombN++;
            }
            _items.Clear();
            float rate = totalN > 0 ? (100f * bombN / totalN) : 0f;
            sb.AppendLine("check13 炸弹出现比例 -> " + bombN + "/" + totalN + " = " + rate.ToString("0.0")
                + "%  (expect 约 13%)");

            // 10) 巨型连切音效：开循环 -> 一直在响；停手后能停掉
            bool soundWas = Sfx.Enabled;
            Sfx.Enabled = true;
            Sfx.StartTearLoop();
            System.Threading.Thread.Sleep(180);
            bool on = Sfx.TearLooping;
            Sfx.StartTearLoop();                 // 再切一刀不应该重置播放
            bool stillOn = Sfx.TearLooping;
            Sfx.StopTearLoop();
            bool off = !Sfx.TearLooping;
            Sfx.Enabled = soundWas;
            sb.AppendLine("check10 巨型循环音效 -> started=" + on + " keep=" + stillOn + " stopped=" + off
                + "  (expect True / True / True)");

            // 11) 夸奖语音（Good / Nice / Perfect）已就绪、三段互不相同
            bool pr = Praise.Ready;
            int praiseBytes = 3 > 0 ? Praise.DebugSizes()[0] + Praise.DebugSizes()[1] + Praise.DebugSizes()[2] : 0;
            sb.AppendLine("check11 夸奖语音 -> ready=" + pr + " totalBytes=" + praiseBytes
                + "  (expect True / >0)");

            return sb.ToString();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _timer.Stop(); } catch { }
            try { Sfx.StopTearLoop(); } catch { }
            try
            {
                if (_bgDc != IntPtr.Zero) { Native.DeleteDC(_bgDc); _bgDc = IntPtr.Zero; }
                if (_bgHbm != IntPtr.Zero) { Native.DeleteObject(_bgHbm); _bgHbm = IntPtr.Zero; }
            }
            catch { }
            base.OnFormClosed(e);
        }
    }

    // ============================================================
    //  自检：--probe 模式（生成图标卡片预览 + 性能采样）
    // ============================================================
    internal static class SelfTest
    {
        public static void Run()
        {
            StringBuilder sb = new StringBuilder();
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
            try
            {
                List<string> paths = IconSource.CollectDesktopPaths();
                sb.AppendLine("desktop items found: " + paths.Count);
                int n = Math.Min(paths.Count, 12);
                int size = 108;
                List<Bitmap> cards = new List<Bitmap>();
                for (int i = 0; i < n; i++)
                {
                    string p = paths[i];
                    Bitmap src = IconSource.LoadIconSquare(p, 230);
                    cards.Add(CardArt.BuildCard(src, size, i == 0));
                    sb.AppendLine("  [" + i + "] " + Path.GetFileName(p) + " src=" + src.Width);
                    src.Dispose();
                }
                cards.Add(CardArt.BuildBomb(size));

                int cols = 6;
                int rows = (cards.Count + cols - 1) / cols;
                int gap = 18;
                int cw = cols * size + (cols + 1) * gap;
                int ch = rows * size + (rows + 1) * gap + 46;
                Bitmap canvas = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new Rectangle(0, 0, cw, ch), Color.FromArgb(255, 64, 96, 140), Color.FromArgb(255, 28, 38, 52), 120f))
                    {
                        g.FillRectangle(lg, 0, 0, cw, ch);
                    }
                    using (Font f = new Font("Microsoft YaHei", 20f, FontStyle.Bold))
                    {
                        g.DrawString("桌面图标卡片预览", f, Brushes.White, gap, 10);
                    }
                    for (int i = 0; i < cards.Count; i++)
                    {
                        int cx = gap + (i % cols) * (size + gap);
                        int cy = 46 + gap + (i / cols) * (size + gap);
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                        {
                            g.FillRectangle(sh, cx + 4, cy + 5, size, size);
                        }
                        g.DrawImage(cards[i], cx, cy, size, size);
                    }
                }
                canvas.Save(Path.Combine(dir, "preview_cards.png"), ImageFormat.Png);
                canvas.Dispose();
                for (int i = 0; i < cards.Count; i++) cards[i].Dispose();

                // 性能采样：全屏底图三种方式对比
                Rectangle scr = Screen.PrimaryScreen.Bounds;
                Bitmap srcImg = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(srcImg))
                {
                    g.Clear(Color.FromArgb(255, 40, 44, 52));
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new Rectangle(0, 0, scr.Width, scr.Height), Color.FromArgb(255, 70, 100, 145), Color.FromArgb(255, 25, 32, 44), 90f))
                        g.FillRectangle(lg, 0, 0, scr.Width, scr.Height);
                }
                Bitmap dstImg = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppPArgb);
                Graphics dg = Graphics.FromImage(dstImg);
                Rectangle rct = new Rectangle(0, 0, scr.Width, scr.Height);
                int iter = 60;
                Stopwatch sw;

                sw = Stopwatch.StartNew();
                for (int i = 0; i < iter; i++) dg.DrawImage(srcImg, rct);
                sw.Stop();
                sb.AppendLine("A DrawImage(rect)     ms: " + (sw.Elapsed.TotalMilliseconds / iter).ToString("0.00"));

                sw = Stopwatch.StartNew();
                for (int i = 0; i < iter; i++) dg.DrawImageUnscaled(srcImg, 0, 0);
                sw.Stop();
                sb.AppendLine("B DrawImageUnscaled   ms: " + (sw.Elapsed.TotalMilliseconds / iter).ToString("0.00"));

                IntPtr hbm = srcImg.GetHbitmap(Color.FromArgb(0));
                IntPtr mdc = Native.CreateCompatibleDC(IntPtr.Zero);
                Native.SelectObject(mdc, hbm);
                IntPtr hdcDst = dg.GetHdc();
                sw = Stopwatch.StartNew();
                for (int i = 0; i < iter; i++) Native.BitBlt(hdcDst, 0, 0, scr.Width, scr.Height, mdc, 0, 0, Native.SRCCOPY);
                sw.Stop();
                dg.ReleaseHdc(hdcDst);
                sb.AppendLine("C BitBlt              ms: " + (sw.Elapsed.TotalMilliseconds / iter).ToString("0.00"));
                Native.DeleteDC(mdc);
                Native.DeleteObject(hbm);
                dg.Dispose();
                dstImg.Dispose();
                srcImg.Dispose();

                // ---- 规则自检 + 离屏渲染真实画面 ----
                GameForm.NoAutoStart = true;
                int pcellW = 0, pcellH = 0;
                List<Point> ips = Desk.GetIconPositions(out pcellW, out pcellH);
                sb.AppendLine("桌面图标位置 -> count=" + ips.Count + " cell=" + pcellW + "x" + pcellH);
                Bitmap fakeDesktop = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(fakeDesktop))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new Rectangle(0, 0, scr.Width, scr.Height),
                        Color.FromArgb(255, 118, 156, 196), Color.FromArgb(255, 38, 58, 82), 115f))
                        g.FillRectangle(lg, 0, 0, scr.Width, scr.Height);
                    // 模拟被盖住的真实桌面图标
                    using (Font f = new Font("Microsoft YaHei", 12f))
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                    {
                        for (int i = 0; i < 24; i++)
                        {
                            int cx = 30 + (i % 8) * 120;
                            int cy = 120 + (i / 8) * 140;
                            using (GraphicsPath p = ImageFx.RoundRect(
                                new RectangleF(cx, cy, 56, 56), 8f))
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 92, 128, 176)))
                                g.FillPath(b, p);
                            g.DrawString("桌面", f, tb, cx + 6, cy + 62);
                        }
                    }
                }

                GameForm gf = new GameForm(fakeDesktop, 1.2f, 12, false);
                sb.AppendLine();
                sb.AppendLine("=== 规则自检 ===");
                sb.Append(gf.SelfCheck());

                Bitmap shot = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppPArgb);
                gf.BuildDemoScene(0);
                gf.RenderTo(shot);
                shot.Save(Path.Combine(dir, "shot_play.png"), ImageFormat.Png);
                using (Bitmap half = new Bitmap(shot, new Size(scr.Width / 2, scr.Height / 2)))
                    half.Save(Path.Combine(dir, "shot_play_small.png"), ImageFormat.Png);

                gf.BuildDemoScene(1);
                gf.RenderTo(shot);
                shot.Save(Path.Combine(dir, "shot_over.png"), ImageFormat.Png);
                using (Bitmap half2 = new Bitmap(shot, new Size(scr.Width / 2, scr.Height / 2)))
                    half2.Save(Path.Combine(dir, "shot_over_small.png"), ImageFormat.Png);

                shot.Dispose();
                sb.AppendLine("probe OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine("probe FAILED: " + ex.ToString());
            }
            File.WriteAllText(Path.Combine(dir, "probe_report.txt"), sb.ToString(), new UTF8Encoding(true));
        }
    }
}
