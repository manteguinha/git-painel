using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GitPainel
{
    // visão geral quando nenhum arquivo está selecionado: um cartão por repositório
    // (branch, alterações, a enviar/receber, merge e último commit); clicar leva ao repositório na lista
    class OverviewPanel : ScrollView
    {
        List<RepoInfo> repos = new List<RepoInfo>();
        readonly List<KeyValuePair<Rectangle, RepoInfo>> cards = new List<KeyValuePair<Rectangle, RepoInfo>>();
        RepoInfo hot;
        public event Action<RepoInfo> RepoClicked;
        public string Empty = "Carregando…";

        public OverviewPanel() { BackColor = T.Bg; }

        public void SetRepos(List<RepoInfo> list)
        {
            repos = list;
            if (hot != null) hot = repos.FirstOrDefault(r => r.FullPath == hot.FullPath);
            Layout2();
        }

        int Cols { get { return Math.Max(1, Math.Min(3, (ClientSize.Width - T.Px(48)) / T.Px(320))); } }
        int CardH { get { return T.Px(118); } }

        void Layout2()
        {
            int rowsN = (repos.Count + Cols - 1) / Cols;
            contentH = T.Px(96) + rowsN * (CardH + T.Px(14)) + T.Px(20);
            Reclamp();
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Bg);
            cards.Clear();
            int oy = (int)Math.Round(offY);
            int pad = T.Px(24), w = ClientSize.Width - 2 * pad - (HasV ? T.Px(8) : 0);
            if (w < T.Px(200)) return;

            int y = T.Px(22) - oy;
            T.Text(g, "Visão geral", T.UiBold, T.Fg, new Rectangle(pad, y, w, T.Px(24)), 0);
            int changes = repos.Sum(r => r.Files.Count), outgoing = repos.Sum(r => r.Outgoing.Count), incoming = repos.Sum(r => r.Behind);
            string sub = repos.Count == 0 ? Empty :
                Plural(repos.Count, "repositório", "repositórios") + "  ·  " + Plural(changes, "arquivo alterado", "arquivos alterados") +
                (outgoing > 0 ? "  ·  " + Plural(outgoing, "commit a enviar", "commits a enviar") : "") +
                (incoming > 0 ? "  ·  " + Plural(incoming, "commit a receber", "commits a receber") : "") +
                "   —   escolha um arquivo na lista para ver o diff";
            T.Text(g, sub, T.Small, T.Comment, new Rectangle(pad, y + T.Px(26), w, T.Px(20)), 0);

            int cols = Cols, gap = T.Px(14);
            int cw = (w - (cols - 1) * gap) / cols;
            y += T.Px(70);
            for (int i = 0; i < repos.Count; i++)
            {
                var r = new Rectangle(pad + (i % cols) * (cw + gap), y + (i / cols) * (CardH + gap), cw, CardH);
                if (r.Bottom < 0 || r.Y > ClientSize.Height) { cards.Add(new KeyValuePair<Rectangle, RepoInfo>(r, repos[i])); continue; }
                PaintCard(g, repos[i], r);
                cards.Add(new KeyValuePair<Rectangle, RepoInfo>(r, repos[i]));
            }
            PaintBars(g);
        }

        static string Plural(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }

        void PaintCard(Graphics g, RepoInfo repo, Rectangle r)
        {
            bool h = hot == repo;
            T.FillRound(g, h ? T.Mix(T.Panel, T.Line, 0.35f) : T.Panel, r, T.Px(10));
            T.StrokeRound(g, h ? T.Mix(T.Line, T.Purple, 0.35f) : T.Mix(T.Panel, T.Line, 0.6f),
                new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), T.Px(10), T.Px(1));
            int x = r.X + T.Px(16), right = r.Right - T.Px(16);
            int y = r.Y + T.Px(12);

            // nome + branch
            bool busyRepo = repo.Files.Count > 0 || repo.Outgoing.Count > 0 || repo.Behind > 0 || repo.Error != null;
            int nw = Math.Min(T.Measure(g, repo.Name, T.UiBold) + 1, right - x);
            T.Text(g, repo.Name, T.UiBold, busyRepo ? T.Pink : T.TextSoft, new Rectangle(x, y, nw, T.Px(24)), 0);
            if (repo.Branch.Length > 0 && right - (x + nw + T.Px(10)) > T.Px(40))
            {
                int bx = x + nw + T.Px(10);
                int bw = Math.Min(T.Measure(g, repo.Branch, T.Small) + T.Px(30), right - bx);
                var br = new Rectangle(bx, y + T.Px(2), bw, T.Px(20));
                T.FillRound(g, T.Mix(T.Panel, T.Purple, 0.14f), br, br.Height / 2f);
                T.Text(g, G.Branch, T.IconSmall, T.Purple, new Rectangle(br.X + T.Px(7), br.Y, T.Px(12), br.Height), TextFormatFlags.HorizontalCenter);
                T.Text(g, repo.Branch, T.Small, T.Purple, new Rectangle(br.X + T.Px(22), br.Y, br.Width - T.Px(28), br.Height), 0);
            }
            y += T.Px(34);

            // situação: chips
            int cx = x;
            if (repo.Error != null) cx = Chip(g, cx, y, right, G.Warn, "erro ao ler o repositório", T.Red);
            else
            {
                if (repo.Merging) cx = Chip(g, cx, y, right, G.Warn, repo.Conflicts > 0 ? Plural(repo.Conflicts, "conflito", "conflitos") : "merge a concluir", T.Orange);
                if (repo.Files.Count > 0) cx = Chip(g, cx, y, right, G.Doc, Plural(repo.Files.Count, "alteração", "alterações"), T.Orange);
                if (repo.Outgoing.Count > 0) cx = Chip(g, cx, y, right, G.Push, (repo.OutgoingMore ? repo.Outgoing.Count + "+" : repo.Outgoing.Count.ToString()) + " a enviar", T.Green);
                else if (repo.HasHead && repo.Upstream == null && repo.HasRemote) cx = Chip(g, cx, y, right, G.Push, "não publicado", T.Green);
                if (repo.Behind > 0) cx = Chip(g, cx, y, right, G.Pull, repo.Behind + " a receber", T.Cyan);
                if (!busyRepo && !repo.Merging) cx = Chip(g, cx, y, right, G.Check, "em dia, sem alterações", T.Green);
            }
            y += T.Px(32);

            // último commit
            var c = repo.Last;
            if (c != null)
            {
                int hw = T.Measure(g, c.Short, T.MonoSmall) + 1;
                int ww = T.Measure(g, c.When, T.Small) + 1;
                T.Text(g, c.Short, T.MonoSmall, T.Mix(T.Purple, T.Panel, 0.2f), new Rectangle(x, y, hw, T.Px(20)), 0);
                int sx = x + hw + T.Px(8), sr = right - ww - T.Px(10);
                T.Text(g, c.Subject, T.Small, T.TextSoft, new Rectangle(sx, y, Math.Max(0, sr - sx), T.Px(20)), 0);
                T.Text(g, c.When, T.Small, T.Comment, new Rectangle(right - ww, y, ww, T.Px(20)), 0);
            }
            else T.Text(g, "ainda sem commits", T.Small, T.Comment, new Rectangle(x, y, right - x, T.Px(20)), 0);
        }

        // pílula de situação; devolve o x seguinte (não desenha se não couber)
        int Chip(Graphics g, int x, int y, int right, string glyph, string text, Color c)
        {
            int tw = T.Measure(g, text, T.Small) + 1;
            int w = tw + T.Px(34);
            if (x + w > right) return x;
            var r = new Rectangle(x, y, w, T.Px(24));
            T.FillRound(g, T.Mix(T.Panel, c, 0.13f), r, r.Height / 2f);
            T.Text(g, glyph, T.IconSmall, c, new Rectangle(r.X + T.Px(9), r.Y, T.Px(14), r.Height), TextFormatFlags.HorizontalCenter);
            T.Text(g, text, T.Small, T.Mix(c, T.Fg, 0.25f), new Rectangle(r.X + T.Px(26), r.Y, tw, r.Height), 0);
            return r.Right + T.Px(6);
        }

        RepoInfo CardAt(Point p)
        {
            foreach (var kv in cards) if (kv.Key.Contains(p)) return kv.Value;
            return null;
        }

        protected override void OnContentMouseMove(MouseEventArgs e)
        {
            var c = CardAt(e.Location);
            if (c != hot) { hot = c; Invalidate(); }
            Cursor = c != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnContentMouseLeave() { if (hot != null) { hot = null; Invalidate(); } }

        protected override void OnContentMouseDown(MouseEventArgs e)
        {
            var c = CardAt(e.Location);
            if (c != null && e.Clicks == 1 && RepoClicked != null) RepoClicked(c);
        }
    }
}
