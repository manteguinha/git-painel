using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GitPainel
{
    // menu do botão direito: abrir no Explorador, Bloco de Notas, VS Code e copiar caminhos
    static class OpenMenu
    {
        static string vsCode;
        static bool vsCodeSearched;

        // procura o VS Code nos lugares de instalação padrão (usuário e máquina)
        static string VsCode
        {
            get
            {
                if (vsCodeSearched) return vsCode;
                vsCodeSearched = true;
                string[] candidates =
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft VS Code\Code.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft VS Code\Code.exe")
                };
                foreach (var c in candidates) if (File.Exists(c)) { vsCode = c; break; }
                if (vsCode == null)
                {
                    // último recurso: "code" no PATH (code.cmd fica em ...\bin, o Code.exe uma pasta acima)
                    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                    {
                        try
                        {
                            if (dir.Trim().Length == 0 || !File.Exists(Path.Combine(dir, "code.cmd"))) continue;
                            string exe = Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\')), "Code.exe");
                            if (File.Exists(exe)) { vsCode = exe; break; }
                        }
                        catch { }
                    }
                }
                return vsCode;
            }
        }

        // abre direto no VS Code (duplo clique na lista); devolve false se não houver VS Code ou arquivo
        public static bool OpenInVsCode(string path)
        {
            if (VsCode == null || !(File.Exists(path) || Directory.Exists(path))) return false;
            Start(VsCode, "\"" + path + "\"");
            return true;
        }

        // path: arquivo ou pasta no disco; root: raiz do repositório (para o caminho relativo)
        public static void Show(Control owner, Point at, string path, string root, bool isFolder)
        {
            ShowItems(owner, at, PathItems(path, root, isFolder));
        }

        // itens "abrir com…" e "copiar caminho", reaproveitados em outros menus
        public static ToolStripItem[] PathItems(string path, string root, bool isFolder)
        {
            bool exists = isFolder ? Directory.Exists(path) : File.Exists(path);
            var menu = new ContextMenuStrip();

            string folder = isFolder ? path : Path.GetDirectoryName(path);
            menu.Items.Add(Item(G.Folder, isFolder ? "Abrir no Explorador de Arquivos" : "Mostrar no Explorador de Arquivos",
                exists || Directory.Exists(folder), () =>
                {
                    if (!isFolder && exists) Start("explorer.exe", "/select,\"" + path + "\"");
                    else Start("explorer.exe", "\"" + (Directory.Exists(path) ? path : folder) + "\"");
                }));

            if (!isFolder)
                menu.Items.Add(Item(G.Note, "Abrir no Bloco de Notas", exists, () => Start("notepad.exe", "\"" + path + "\"")));

            string code = VsCode;
            var vs = Item(G.Code, isFolder ? "Abrir pasta no VS Code" : "Abrir no VS Code", exists && code != null, () => Start(code, "\"" + path + "\""));
            if (code == null) vs.Text += "  (não encontrado)";
            menu.Items.Add(vs);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item(G.Copy, "Copiar caminho completo", true, () => Clipboard.SetText(path)));
            if (root != null && path.Length > root.Length && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string rel = path.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');
                menu.Items.Add(Item(G.Copy, "Copiar caminho relativo", true, () => Clipboard.SetText(rel)));
            }

            var items = new ToolStripItem[menu.Items.Count];
            menu.Items.CopyTo(items, 0);
            menu.Items.Clear();
            menu.Dispose();
            return items;
        }

        public static ToolStripItem Separator() { return new ToolStripSeparator(); }

        public static void ShowItems(Control owner, Point at, params ToolStripItem[] items)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), Font = T.Ui, ShowImageMargin = true, ImageScalingSize = new Size(T.Px(16), T.Px(16)) };
            menu.Items.AddRange(items);
            menu.Closed += delegate { owner.BeginInvoke((Action)(() => menu.Dispose())); };
            menu.Show(owner, at);
        }

        public static ToolStripMenuItem Item(string glyph, string text, bool enabled, Action click)
        {
            var it = new ToolStripMenuItem(text, Glyph(glyph, enabled ? T.Purple : T.Line)) { Enabled = enabled, Padding = new Padding(0, T.Px(3), 0, T.Px(3)) };
            it.Click += delegate
            {
                try { click(); }
                catch (Exception ex) { MessageDialog.Error(null, "Não foi possível abrir", ex.Message); }
            };
            return it;
        }

        static void Start(string exe, string args)
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        }

        static Image Glyph(string glyph, Color c)
        {
            int s = T.Px(16);
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var b = new SolidBrush(c))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, T.Icon, b, new RectangleF(0, 0, s, s), sf);
            }
            return bmp;
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return T.Panel; } }
            public override Color MenuBorder { get { return T.Line; } }
            public override Color MenuItemBorder { get { return T.Line; } }
            public override Color MenuItemSelected { get { return T.Line; } }
            public override Color MenuItemSelectedGradientBegin { get { return T.Line; } }
            public override Color MenuItemSelectedGradientEnd { get { return T.Line; } }
            public override Color MenuItemPressedGradientBegin { get { return T.Line; } }
            public override Color MenuItemPressedGradientEnd { get { return T.Line; } }
            public override Color ImageMarginGradientBegin { get { return T.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return T.Panel; } }
            public override Color ImageMarginGradientEnd { get { return T.Panel; } }
            public override Color SeparatorDark { get { return T.Line; } }
            public override Color SeparatorLight { get { return T.Panel; } }
        }

        class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                // rótulos de seção mantêm a própria cor
                if (!(e.Item is ToolStripLabel)) e.TextColor = e.Item.Enabled ? T.Fg : T.Comment;
                base.OnRenderItemText(e);
            }

            // marca de opção escolhida: bolinha roxa com ✓
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var r = e.ImageRectangle;
                float s = T.Px(15);
                var box = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
                T.FillRound(e.Graphics, T.Purple, box, s / 2f);
                T.Text(e.Graphics, G.Check, T.IconSmall, T.Bg, Rectangle.Round(box), TextFormatFlags.HorizontalCenter);
            }
        }
    }
}
