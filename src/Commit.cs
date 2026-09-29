using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GitPainel
{
    // painel de commit compacto: cabeçalho, [tipo(escopo) ▾ | resumo], descrição opcional e botão
    class CommitPanel : Control
    {
        static readonly string[] Types = { "feat", "fix", "refactor", "style", "docs", "test", "chore" };
        static readonly Dictionary<string, string> Emojis = new Dictionary<string, string>
        {
            { "feat", "✨" }, { "fix", "🐛" }, { "refactor", "♻️" }, { "style", "💄" },
            { "docs", "📚" }, { "test", "✅" }, { "chore", "🔧" }
        };

        public readonly DarkTextBox Summary, Desc;
        public readonly PrimaryButton Button;
        public event EventHandler CommitRequested, ClearRequested;

        RepoInfo repo;
        int count, partial;
        Git.RepoStyle style = new Git.RepoStyle();
        string type, scope = "", autoScope = "";
        bool emoji, busy, showDesc;
        Rectangle clearRect, chipRect, descLink;
        string hot;

        readonly int pad = T.Px(12);
        int yField { get { return pad + T.Px(26); } }
        int yDesc { get { return yField + T.Px(34) + T.Px(8); } }
        int yBottom { get { return (showDesc ? yDesc + T.Px(58) + T.Px(8) : yField + T.Px(34) + T.Px(8)); } }
        public int PreferredHeight { get { return yBottom + T.Px(32) + pad; } }

        public CommitPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Summary = new DarkTextBox(false) { Placeholder = "Resumo do commit" };
            Desc = new DarkTextBox(true) { Placeholder = "Descrição (opcional)", Visible = false };
            Button = new PrimaryButton { Height = T.Px(32) };
            Controls.Add(Summary);
            Controls.Add(Desc);
            Controls.Add(Button);

            EventHandler changed = delegate { Refresh2(); };
            Summary.TextChanged += changed;
            Desc.TextChanged += changed;
            KeyEventHandler keys = (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Fire(); }
                else if (s == Summary && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SetDesc(true); Desc.Focus(); }
            };
            Summary.KeyDown += keys;
            Desc.KeyDown += keys;
            Button.Click += delegate { Fire(); };
            Refresh2();
        }

        void Fire()
        {
            if (!CanCommit) return;
            if (CommitRequested != null) CommitRequested(this, EventArgs.Empty);
        }

        public bool CanCommit { get { return !busy && count > 0 && Summary.Text.Trim().Length > 0; } }

        public bool Busy
        {
            get { return busy; }
            set { busy = value; Summary.ReadOnly = Desc.ReadOnly = value; Button.Busy = value; Refresh2(); }
        }

        void SetDesc(bool on)
        {
            if (showDesc == on) return;
            showDesc = on;
            Desc.Visible = on;
            Height = PreferredHeight;
            PerformLayout();
            Invalidate();
        }

        // chamado quando a seleção muda
        public void SetSelection(RepoInfo r, List<FileChange> files, int partialCount, Git.RepoStyle st)
        {
            bool repoChanged = repo == null || r == null || repo.FullPath != r.FullPath;
            repo = r;
            count = files.Count;
            partial = partialCount;
            if (repoChanged)
            {
                // a mensagem pertence ao repositório: trocar de repositório nunca aproveita o texto digitado
                Summary.Text = "";
                Desc.Text = "";
                type = null;
                SetDesc(false);
                style = st ?? new Git.RepoStyle(); // enquanto o estilo carrega, nada do repositório anterior
                emoji = style.Emoji;
            }

            // escopo sugerido: o módulo comum a todos os arquivos (ex.: apps/cadastro -> cadastro)
            var mods = files.Select(f => f.Module == null ? "" : f.Module.Path).Distinct().ToList();
            string suggestion = mods.Count == 1 && mods[0].Length > 0 ? mods[0].Substring(mods[0].LastIndexOf('/') + 1) : "";
            if (repoChanged || scope == autoScope) scope = suggestion;
            autoScope = suggestion;
            Refresh2();
            PerformLayout();
        }

        // estilo do repositório lido em segundo plano (não trava a tela)
        public void SetStyle(RepoInfo r, Git.RepoStyle st)
        {
            if (repo == null || r == null || repo.FullPath != r.FullPath || st == null) return;
            style = st;
            if (type == null && Summary.TextLength == 0) emoji = st.Emoji;
            Invalidate();
        }

        public void ResetMessage()
        {
            Summary.Text = "";
            Desc.Text = "";
            scope = autoScope;
            type = null;
            SetDesc(false);
            Refresh2();
            PerformLayout();
        }

        string Prefix
        {
            get
            {
                if (type == null) return "";
                string pre = type + (scope.Length > 0 ? "(" + scope + ")" : "") + ": ";
                return emoji ? Emojis[type] + " " + pre : pre;
            }
        }

        public string Message
        {
            get
            {
                string head = Prefix + Summary.Text.Trim();
                string body = Desc.Text.Trim();
                return body.Length > 0 ? head + "\n\n" + body : head;
            }
        }

        string ChipText { get { return type == null ? "tipo" : (emoji ? Emojis[type] + " " : "") + type + (scope.Length > 0 ? "(" + scope + ")" : ""); } }

        void Refresh2()
        {
            Button.Active = CanCommit;
            Button.Text = busy ? "Commitando…" : "Commit" + (count > 1 ? " (" + count + ")" : "");
            Button.Invalidate();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Button == null) return;
            int w = Width - 2 * pad;
            int ih = T.Ui.Height;
            int chipW;
            using (var g = CreateGraphics()) chipW = T.Measure(g, ChipText, T.Small) + T.Px(28);
            chipRect = new Rectangle(pad + T.Px(4), yField + T.Px(4), chipW, T.Px(26));
            int sx = chipRect.Right + T.Px(10);
            Summary.SetBounds(sx, yField + (T.Px(34) - ih) / 2, pad + w - T.Px(52) - sx, ih);
            Desc.SetBounds(pad + T.Px(10), yDesc + T.Px(8), w - T.Px(20), T.Px(58) - T.Px(14));
            int bw = T.Px(150);
            Button.SetBounds(Width - pad - bw, yBottom, bw, T.Px(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Panel);
            T.Fill(g, T.Border, new Rectangle(0, 0, Width, 1));
            if (repo == null) return;
            int w = Width - 2 * pad;

            // Commit em <repo>  <branch>  ·  3 arquivos (1 com trechos)            limpar
            int x = pad, y = pad, h = T.Px(20);
            string clear = "limpar";
            int cw = T.Measure(g, clear, T.Small) + 1;
            clearRect = new Rectangle(Width - pad - cw, y, cw, h);
            T.Text(g, clear, T.Small, hot == "clear" ? T.Fg : T.Comment, clearRect, 0);
            int lim = clearRect.X - T.Px(12);
            int nw = Math.Min(T.Measure(g, repo.Name, T.UiBold) + 1, lim - x);
            T.Text(g, repo.Name, T.UiBold, T.Pink, new Rectangle(x, y, nw, h), 0);
            x += nw + T.Px(8);
            string info = (repo.Branch.Length > 0 ? repo.Branch + "   ·   " : "") +
                          (count == 1 ? "1 arquivo" : count + " arquivos") + (partial > 0 ? " (" + partial + " com trechos)" : "");
            if (lim - x > T.Px(30)) T.Text(g, info, T.Small, T.Comment, new Rectangle(x, y, lim - x, h), 0);

            // campo: [tipo(escopo) ▾] resumo ....... 42/72
            var fr = new Rectangle(pad, yField, w, T.Px(34));
            DarkTextBox.Frame(g, fr, Summary, T.Panel);
            bool chipHot = hot == "chip";
            T.FillRound(g, type != null ? T.Mix(T.Field, T.Purple, chipHot ? 0.32f : 0.22f) : (chipHot ? T.Line : T.Mix(T.Field, T.Line, 0.6f)), chipRect, T.Px(6));
            T.Text(g, ChipText, T.Small, type != null ? T.Purple : T.Mix(T.Comment, T.Fg, chipHot ? 0.6f : 0.2f),
                new Rectangle(chipRect.X + T.Px(9), chipRect.Y, chipRect.Width - T.Px(24), chipRect.Height), 0);
            T.Text(g, G.Down, T.IconSmall, T.Comment, new Rectangle(chipRect.Right - T.Px(16), chipRect.Y, T.Px(10), chipRect.Height), TextFormatFlags.HorizontalCenter);
            int len = Message.Split('\n')[0].Length;
            T.Text(g, len + "/72", T.Small, len > 72 ? T.Orange : T.Comment, new Rectangle(fr.Right - T.Px(48), fr.Y, T.Px(40), fr.Height), TextFormatFlags.Right);

            if (showDesc) DarkTextBox.Frame(g, new Rectangle(pad, yDesc, w, T.Px(58)), Desc, T.Panel);

            // "+ descrição" e dica do atalho, à esquerda do botão
            string dl = showDesc ? "− descrição" : "+ descrição";
            int dw = T.Measure(g, dl, T.Small) + 1;
            descLink = new Rectangle(pad, yBottom, dw, T.Px(32));
            T.Text(g, dl, T.Small, hot == "desc" ? T.Fg : T.Purple, descLink, 0);
            int hx = descLink.Right + T.Px(14);
            if (Button.Left - hx > T.Px(90))
                T.Text(g, "Ctrl+Enter", T.Small, T.Mix(T.Comment, T.Panel, 0.3f), new Rectangle(hx, yBottom, Button.Left - hx - T.Px(8), T.Px(32)), 0);
        }

        // menu do seletor: tipo, escopo e emoji
        void ShowChipMenu()
        {
            var items = new List<ToolStripItem>();
            items.Add(Header("TIPO" + (style.Conventional ? "   ·   este repositório usa Conventional Commits" : "")));
            items.Add(Choice("sem tipo", type == null, () => type = null));
            foreach (var t in Types) { string tt = t; items.Add(Choice(t, type == t, () => type = tt)); }

            var scopes = new List<string>();
            if (autoScope.Length > 0) scopes.Add(autoScope);
            foreach (var s in style.Scopes) if (!scopes.Contains(s, StringComparer.OrdinalIgnoreCase)) scopes.Add(s);
            if (scope.Length > 0 && !scopes.Contains(scope, StringComparer.OrdinalIgnoreCase)) scopes.Insert(0, scope);
            items.Add(new ToolStripSeparator());
            items.Add(Header("ESCOPO"));
            items.Add(Choice("sem escopo", scope.Length == 0, () => scope = ""));
            foreach (var s in scopes.Take(8)) { string ss = s; items.Add(Choice(s + (s == autoScope ? "   (módulo selecionado)" : ""), scope == s, () => scope = ss)); }

            items.Add(new ToolStripSeparator());
            items.Add(Choice("usar emoji  ✨", emoji, () => emoji = !emoji));
            OpenMenu.ShowItems(this, new Point(chipRect.X, chipRect.Bottom + T.Px(2)), items.ToArray());
        }

        static ToolStripItem Header(string text)
        {
            // item desativado (não um rótulo) para o menu se ajustar à largura do texto
            return new ToolStripMenuItem(text) { Enabled = false, Font = T.SmallBold, Padding = new Padding(0, T.Px(3), 0, T.Px(1)) };
        }

        ToolStripMenuItem Choice(string text, bool on, Action set)
        {
            var it = new ToolStripMenuItem(text) { Checked = on, Padding = new Padding(0, T.Px(1), 0, T.Px(1)) };
            it.Click += delegate { set(); Refresh2(); PerformLayout(); Summary.Focus(); };
            return it;
        }

        string HitTest(Point p)
        {
            if (clearRect.Contains(p)) return "clear";
            if (chipRect.Contains(p)) return "chip";
            if (descLink.Contains(p)) return "desc";
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            string h = HitTest(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = null; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            string h = HitTest(e.Location);
            if (h != null && !busy && e.Clicks == 1) // clique duplo não abre/fecha duas vezes
            {
                if (h == "clear") { if (ClearRequested != null) ClearRequested(this, EventArgs.Empty); }
                else if (h == "chip") ShowChipMenu();
                else if (h == "desc") { SetDesc(!showDesc); if (showDesc) Desc.Focus(); }
            }
            base.OnMouseDown(e);
        }
    }
}
