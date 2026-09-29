// Git Painel — alterações de todos os repositórios Git de uma pasta pai, com diff, histórico e commit.
// Compilar com compilar.bat (usa o csc.exe que já vem no Windows).

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Git Painel")]
[assembly: System.Reflection.AssemblyProduct("Git Painel")]
// versão do app: suba aqui a cada release (a tag no GitHub deve ser v + este número)
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace GitPainel
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            T.Init();
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }

    static class T
    {
        public static readonly Color Bg = Hex(0x282A36);
        public static readonly Color Panel = Hex(0x21222C);
        public static readonly Color Border = Hex(0x191A21);
        public static readonly Color Line = Hex(0x44475A);
        public static readonly Color Fg = Hex(0xF8F8F2);
        public static readonly Color Comment = Hex(0x6272A4);
        public static readonly Color Cyan = Hex(0x8BE9FD);
        public static readonly Color Green = Hex(0x50FA7B);
        public static readonly Color Orange = Hex(0xFFB86C);
        public static readonly Color Pink = Hex(0xFF79C6);
        public static readonly Color Purple = Hex(0xBD93F9);
        public static readonly Color Red = Hex(0xFF5555);
        public static readonly Color Yellow = Hex(0xF1FA8C);
        public static readonly Color Field = Mix(Panel, Border, 0.7f); // fundo das caixas de texto

        public static float S = 1f;
        public static Font Ui, UiBold, UiStrike, Small, SmallBold, Mono, MonoSmall, Icon, IconSmall, IconBig;

        const TextFormatFlags BaseFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
            TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            Ui = MakeFont(9.75f, FontStyle.Regular, "Segoe UI");
            UiBold = MakeFont(9.75f, FontStyle.Regular, "Segoe UI Semibold");
            if (UiBold.Name != "Segoe UI Semibold") UiBold = new Font(Ui, FontStyle.Bold);
            UiStrike = new Font(Ui, FontStyle.Strikeout);
            Small = MakeFont(8.75f, FontStyle.Regular, "Segoe UI");
            SmallBold = MakeFont(8.25f, FontStyle.Regular, "Segoe UI Semibold");
            if (SmallBold.Name != "Segoe UI Semibold") SmallBold = new Font(Small, FontStyle.Bold);
            Mono = MakeFont(10f, FontStyle.Regular, "Cascadia Mono", "Cascadia Code", "JetBrains Mono", "Consolas");
            MonoSmall = MakeFont(8.5f, FontStyle.Regular, Mono.Name);
            Icon = MakeFont(10.5f, FontStyle.Regular, "Segoe Fluent Icons", "Segoe MDL2 Assets");
            IconSmall = MakeFont(7.5f, FontStyle.Regular, "Segoe Fluent Icons", "Segoe MDL2 Assets");
            IconBig = MakeFont(30f, FontStyle.Regular, "Segoe Fluent Icons", "Segoe MDL2 Assets");
        }

        static Font MakeFont(float size, FontStyle style, params string[] names)
        {
            foreach (var n in names)
            {
                var f = new Font(n, size, style);
                if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            return new Font(names[names.Length - 1], size, style);
        }

        public static int Px(float v) { return (int)Math.Round(v * S); }
        public static Color Hex(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        public static Color A(Color c, int a) { return Color.FromArgb(a, c); }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c))
            using (var p = Round(r, rad)) g.FillPath(b, p);
            g.SmoothingMode = old;
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float rad, float width)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(c, width))
            using (var p = Round(r, rad)) g.DrawPath(pen, p);
            g.SmoothingMode = old;
        }

        public static void Fill(Graphics g, Color c, Rectangle r)
        {
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        public static void Dot(Graphics g, Color c, float x, float y, float d)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y, d, d);
            g.SmoothingMode = old;
        }

        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0) return;
            TextRenderer.DrawText(g, s, f, r, c, BaseFlags | extra);
        }

        public static int Measure(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        }

        // indicador de carregamento: arco girando sobre um anel apagado
        public static void Spinner(Graphics g, RectangleF r, Color c, float width)
        {
            float a = (Environment.TickCount % 800) / 800f * 360f;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var ring = new Pen(A(c, 55), width)) g.DrawEllipse(ring, r);
            using (var arc = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, r, a, 110);
            g.SmoothingMode = old;
        }

        // 0..1 oscilando suavemente (pulso de "trabalhando")
        public static float Pulse { get { return (float)(0.5 + 0.5 * Math.Sin(Environment.TickCount / 260.0)); } }

        // evita que clique duplo dispare a mesma ação duas vezes
        public static bool Debounce(ref DateTime last, int ms)
        {
            var now = DateTime.Now;
            if ((now - last).TotalMilliseconds < ms) return false;
            last = now;
            return true;
        }

        // caixa de marcar: 0 vazia, 1 marcada, 2 parcial
        public static void Check(Graphics g, float x, float cy, int state, bool hot)
        {
            float s = Px(15);
            var r = new RectangleF(x, cy - s / 2f, s, s);
            if (state == 0) { StrokeRound(g, hot ? Comment : Line, r, Px(4), Px(1.3f)); return; }
            FillRound(g, Purple, r, Px(4));
            if (state == 1) Text(g, G.Check, IconSmall, Bg, Rectangle.Round(r), TextFormatFlags.HorizontalCenter);
            else Fill(g, Bg, new Rectangle((int)(r.X + s * 0.25f), (int)(r.Y + s / 2f - Px(1)), (int)(s * 0.5f), Px(2)));
        }

        public static Color StatusColor(char k)
        {
            switch (k)
            {
                case 'A': case 'U': return Green;
                case 'D': case '!': return Red;
                case 'R': return Cyan;
                default: return Orange;
            }
        }

        public static string StatusName(char k)
        {
            switch (k)
            {
                case 'A': return "ADICIONADO";
                case 'U': return "NÃO RASTREADO";
                case 'D': return "EXCLUÍDO";
                case 'R': return "RENOMEADO";
                case '!': return "CONFLITO";
                default: return "MODIFICADO";
            }
        }

        public static void DrawLogo(Graphics g, RectangleF r)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new LinearGradientBrush(r, Purple, Pink, 45f))
            using (var p = Round(r, r.Width * 0.28f)) g.FillPath(br, p);
            float w = r.Width;
            float x0 = r.X + w * 0.36f, y0 = r.Y + w * 0.27f, y1 = r.Y + w * 0.73f;
            float x1 = r.X + w * 0.66f, ym = r.Y + w * 0.40f, rad = w * 0.085f;
            using (var pen = new Pen(Bg, w * 0.08f))
            {
                g.DrawLine(pen, x0, y0, x0, y1);
                g.DrawBezier(pen, x1, ym, x1, r.Y + w * 0.58f, x0, r.Y + w * 0.50f, x0, r.Y + w * 0.64f);
            }
            using (var b = new SolidBrush(Bg))
            {
                g.FillEllipse(b, x0 - rad, y0 - rad, 2 * rad, 2 * rad);
                g.FillEllipse(b, x0 - rad, y1 - rad, 2 * rad, 2 * rad);
                g.FillEllipse(b, x1 - rad, ym - rad, 2 * rad, 2 * rad);
            }
            g.SmoothingMode = sm;
        }
    }

    static class G
    {
        public const string Folder = "", Refresh = "", Down = "", Right = "",
            Doc = "", Check = "", Info = "", Warn = "", Search = "",
            FolderClosed = "", Star = "", StarFill = "", History = "",
            Pull = "", Push = "", Undo = "", Close = "",
            Note = "\uE70B", Code = "\uE943", Copy = "\uE8C8", More = "\uE712", Up = "\uE70E";
    }

    static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct NCCALCSIZE_PARAMS { public RECT r0, r1, r2; public IntPtr pos; }

        // espessura da borda que o Windows põe para fora da tela quando a janela está maximizada
        public static int FrameThickness() { return GetSystemMetrics(33) + GetSystemMetrics(92); } // SM_CYFRAME + SM_CXPADDEDBORDER

        public static void RefreshFrame(IntPtr h) { SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0002 | 0x0001 | 0x0004); } // FRAMECHANGED|NOMOVE|NOSIZE|NOZORDER

        public static void DarkScroll(IntPtr h) { try { SetWindowTheme(h, "DarkMode_Explorer", null); } catch { } }

        public static void EnableDpi()
        {
            try { if (SetProcessDpiAwareness(1) == 0) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static void DarkTitle(IntPtr h, Color caption, Color text)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(h, 20, ref on, 4);
                int c = ColorRef(caption);
                DwmSetWindowAttribute(h, 35, ref c, 4);
                DwmSetWindowAttribute(h, 34, ref c, 4);
                int t = ColorRef(text);
                DwmSetWindowAttribute(h, 36, ref t, 4);
            }
            catch { }
        }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }
    }
}
