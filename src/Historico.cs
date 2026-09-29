using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitPainel
{
    // painel de histórico: commits do repositório (ou de um módulo) e os arquivos de cada um
    class HistoryPane : Panel // Panel respeita o Padding: a lista começa abaixo do cabeçalho
    {
        readonly HistoryList list = new HistoryList();
        readonly FlatButton close;
        RepoInfo repo;
        string path;
        public event Action<FileChange> FileSelected;
        public event EventHandler Closed, UndoRequested;

        public HistoryPane()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = T.Px(380);
            Padding = new Padding(0, T.Px(58), 1, 0);
            list.Dock = DockStyle.Fill;
            Controls.Add(list);
            close = new FlatButton(null, "Fechar") { Back = T.Panel, Width = T.Px(76), Height = T.Px(30) };
            close.Click += delegate { if (Closed != null) Closed(this, EventArgs.Empty); };
            Controls.Add(close);
            list.FileSelected += f => { if (FileSelected != null) FileSelected(f); };
            list.NeedFiles += LoadFiles;
            list.NeedMore += LoadMore;
            list.UndoRequested += delegate { if (UndoRequested != null) UndoRequested(this, EventArgs.Empty); };
        }

        public RepoInfo Repo { get { return repo; } }

        public void Open(RepoInfo r, string modulePath)
        {
            repo = r;
            path = modulePath ?? "";
            list.Reset(r);
            Invalidate();
            LoadMore();
        }

        // depois de commit/desfazer/pull o histórico precisa ser relido
        public void Reload() { if (repo != null && Visible) Open(repo, path); }

        public void RepoRefreshed(List<RepoInfo> repos)
        {
            if (repo == null) return;
            var n = repos.FirstOrDefault(r => r.FullPath == repo.FullPath);
            if (n != null) repo = n;
        }

        void LoadMore()
        {
            var r = repo;
            string p = path;
            int skip = list.CommitCount;
            list.Loading = true;
            Task.Factory.StartNew(() => Git.Log(r, p, skip, 60)).ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                var res = t.Exception == null ? t.Result : new List<CommitInfo>();
                try { BeginInvoke((Action)(() => { if (repo != null && r.FullPath == repo.FullPath && p == path) list.Append(res, res.Count == 60); })); } catch { }
            });
        }

        void LoadFiles(CommitInfo c)
        {
            var r = repo;
            c.Loading = true;
            Task.Factory.StartNew(() => Git.CommitFiles(r, c)).ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                var res = t.Exception == null ? t.Result : new List<FileChange>();
                try { BeginInvoke((Action)(() => { c.Files = res; c.Loading = false; list.Rebuild(); })); } catch { }
            });
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (close != null) close.Location = new Point(Width - close.Width - T.Px(14), (T.Px(58) - close.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Panel);
            T.Fill(g, T.Border, new Rectangle(Width - 1, 0, 1, Height));
            T.Fill(g, T.Border, new Rectangle(0, T.Px(58) - 1, Width, 1));
            if (repo == null) return;
            int x = T.Px(16), right = close.Left - T.Px(10);
            T.Text(g, G.History, T.Icon, T.Purple, new Rectangle(x, T.Px(8), T.Px(18), T.Px(22)), TextFormatFlags.HorizontalCenter);
            x += T.Px(26);
            T.Text(g, "Histórico", T.UiBold, T.Fg, new Rectangle(x, T.Px(8), right - x, T.Px(22)), 0);
            string sub = repo.Name + (path.Length > 0 ? "  ›  " + path : "") + (repo.Branch.Length > 0 ? "   ·   " + repo.Branch : "");
            T.Text(g, sub, T.Small, T.Comment, new Rectangle(T.Px(16), T.Px(30), right - T.Px(16), T.Px(20)), 0);
        }
    }

    class HistoryList : ScrollView
    {
        enum Kind { Commit, File, More, Info }

        class Row { public Kind K; public CommitInfo C; public FileChange F; public int Y, H; }

        readonly List<CommitInfo> commits = new List<CommitInfo>();
        readonly List<Row> rows = new List<Row>();
        RepoInfo repo;
        bool hasMore, loading;
        int hover = -1;
        bool hoverUndo;
        FileChange selected;
        Rectangle undoRect;
        DateTime lastUndo;

        public event Action<FileChange> FileSelected;
        public event Action<CommitInfo> NeedFiles;
        public event Action NeedMore;
        public event EventHandler UndoRequested;

        public HistoryList() { BackColor = T.Panel; }

        public int CommitCount { get { return commits.Count; } }
        public bool Loading { set { loading = value; Build(); } }

        public void Reset(RepoInfo r)
        {
            repo = r;
            commits.Clear();
            selected = null;
            hasMore = false;
            JumpTo(0, 0);
            Build();
        }

        public void Append(List<CommitInfo> list, bool more)
        {
            commits.AddRange(list);
            hasMore = more;
            loading = false;
            // o commit mais recente já vem aberto
            if (commits.Count == list.Count && list.Count > 0) Toggle(list[0]);
            Build();
        }

        public void Rebuild() { Build(); }

        void Toggle(CommitInfo c)
        {
            c.Expanded = !c.Expanded;
            if (c.Expanded && c.Files == null && !c.Loading && NeedFiles != null) NeedFiles(c);
        }

        void Build()
        {
            rows.Clear();
            int y = T.Px(6);
            foreach (var c in commits)
            {
                rows.Add(new Row { K = Kind.Commit, C = c, Y = y, H = T.Px(58) });
                y += T.Px(58);
                if (!c.Expanded) continue;
                if (c.Files == null)
                {
                    rows.Add(new Row { K = Kind.Info, C = c, Y = y, H = T.Px(28) });
                    y += T.Px(28);
                    continue;
                }
                foreach (var f in c.Files)
                {
                    rows.Add(new Row { K = Kind.File, C = c, F = f, Y = y, H = T.Px(28) });
                    y += T.Px(28);
                }
                y += T.Px(6);
            }
            if (hasMore || loading) { rows.Add(new Row { K = Kind.More, Y = y, H = T.Px(40) }); y += T.Px(40); }
            contentH = y + T.Px(8);
            Reclamp();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Panel);
            undoRect = Rectangle.Empty;
            if (rows.Count == 0)
            {
                T.Text(g, loading ? "Carregando…" : "Nenhum commit encontrado", T.Ui, T.Comment,
                    new Rectangle(0, T.Px(30), ClientSize.Width, T.Px(30)), TextFormatFlags.HorizontalCenter);
                return;
            }
            int oy = (int)Math.Round(offY);
            int right = ClientSize.Width - T.Px(8) - (HasV ? T.Px(6) : 0);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int y = row.Y - oy;
                if (y + row.H < 0) continue;
                if (y > ClientSize.Height) break;
                var rect = new Rectangle(T.Px(8), y, right - T.Px(8), row.H);
                bool hot = i == hover;
                switch (row.K)
                {
                    case Kind.Commit: PaintCommit(g, row.C, rect, hot); break;
                    case Kind.File: PaintFile(g, row.F, rect, hot); break;
                    case Kind.Info:
                        T.Text(g, "carregando arquivos…", T.Small, T.Comment, new Rectangle(rect.X + T.Px(30), rect.Y, rect.Width, rect.Height), 0);
                        break;
                    case Kind.More:
                        if (hot && !loading) T.FillRound(g, T.Mix(T.Panel, T.Line, 0.45f), rect, T.Px(6));
                        T.Text(g, loading ? "Carregando…" : "Carregar mais commits", T.Small, loading ? T.Comment : T.Purple, rect, TextFormatFlags.HorizontalCenter);
                        break;
                }
            }
            PaintBars(g);
        }

        void PaintCommit(Graphics g, CommitInfo c, Rectangle rect, bool hot)
        {
            if (hot) T.FillRound(g, T.Mix(T.Panel, T.Line, 0.45f), rect, T.Px(6));
            int x = rect.X + T.Px(6);
            T.Text(g, c.Expanded ? G.Down : G.Right, T.IconSmall, T.Comment, new Rectangle(x, rect.Y + T.Px(8), T.Px(16), T.Px(20)), TextFormatFlags.HorizontalCenter);
            x += T.Px(22);
            int right = rect.Right - T.Px(8);

            // "local" = ainda não foi enviado; o mais recente local pode ser desfeito
            if (c.Local)
            {
                bool first = commits.Count > 0 && commits[0] == c && repo != null && repo.MaybeLocalHead;
                string pill = "local";
                int pw = T.Measure(g, pill, T.Small) + T.Px(14);
                var pr = new Rectangle(right - pw, rect.Y + T.Px(9), pw, T.Px(18));
                T.FillRound(g, T.Mix(T.Panel, T.Orange, 0.18f), pr, T.Px(9));
                T.Text(g, pill, T.Small, T.Orange, pr, TextFormatFlags.HorizontalCenter);
                right = pr.X - T.Px(6);
                if (first && hot)
                {
                    string ut = "Desfazer";
                    int uw = T.Measure(g, ut, T.SmallBold) + T.Px(28);
                    undoRect = new Rectangle(right - uw, rect.Y + T.Px(7), uw, T.Px(22));
                    T.FillRound(g, hoverUndo ? T.Line : T.Mix(T.Panel, T.Line, 0.8f), undoRect, T.Px(6));
                    T.Text(g, G.Undo, T.IconSmall, T.Orange, new Rectangle(undoRect.X + T.Px(6), undoRect.Y, T.Px(14), undoRect.Height), TextFormatFlags.HorizontalCenter);
                    T.Text(g, ut, T.SmallBold, T.Fg, new Rectangle(undoRect.X + T.Px(22), undoRect.Y, uw - T.Px(24), undoRect.Height), 0);
                    right = undoRect.X - T.Px(6);
                }
            }
            T.Text(g, c.Subject, T.Ui, T.Fg, new Rectangle(x, rect.Y + T.Px(7), right - x, T.Px(22)), 0);
            int hw = T.Measure(g, c.Short, T.MonoSmall) + 1;
            T.Text(g, c.Short, T.MonoSmall, T.Purple, new Rectangle(x, rect.Y + T.Px(31), hw, T.Px(18)), 0);
            T.Text(g, c.Author + "   ·   " + c.When, T.Small, T.Comment, new Rectangle(x + hw + T.Px(10), rect.Y + T.Px(31), rect.Right - x - hw - T.Px(18), T.Px(18)), 0);
        }

        void PaintFile(Graphics g, FileChange f, Rectangle rect, bool hot)
        {
            bool sel = f == selected;
            if (sel)
            {
                T.FillRound(g, T.Line, rect, T.Px(6));
                T.FillRound(g, T.Purple, new RectangleF(rect.X, rect.Y + T.Px(6), T.Px(3), rect.Height - T.Px(12)), T.Px(1.5f));
            }
            else if (hot) T.FillRound(g, T.Mix(T.Panel, T.Line, 0.45f), rect, T.Px(6));
            var sr = new Rectangle(rect.Right - T.Px(26), rect.Y, T.Px(18), rect.Height);
            T.Text(g, f.Kind.ToString(), T.SmallBold, T.StatusColor(f.Kind), sr, TextFormatFlags.HorizontalCenter);
            int x = rect.X + T.Px(30), right = sr.X - T.Px(4);
            Font nf = f.Kind == 'D' ? T.UiStrike : T.Ui;
            int nw = Math.Min(T.Measure(g, f.Name, nf) + 1, right - x);
            T.Text(g, f.Name, nf, f.Kind == 'D' ? T.Comment : T.Fg, new Rectangle(x, rect.Y, nw, rect.Height), 0);
            x += nw + T.Px(8);
            if (f.Folder.Length > 0 && right - x > T.Px(24))
                T.Text(g, f.Folder, T.Small, T.Comment, new Rectangle(x, rect.Y + 1, right - x - T.Px(4), rect.Height), 0);
        }

        int HitTest(int y)
        {
            int yy = y + (int)Math.Round(offY);
            for (int i = 0; i < rows.Count; i++)
                if (yy >= rows[i].Y && yy < rows[i].Y + rows[i].H) return i;
            return -1;
        }

        protected override void OnContentMouseDown(MouseEventArgs e)
        {
            if (undoRect.Contains(e.Location))
            {
                if (e.Clicks == 1 && T.Debounce(ref lastUndo, 800) && UndoRequested != null) UndoRequested(this, EventArgs.Empty);
                return;
            }
            int i = HitTest(e.Y);
            if (i < 0) return;
            var row = rows[i];
            switch (row.K)
            {
                case Kind.Commit: if (e.Clicks == 1) { Toggle(row.C); Build(); } break;
                case Kind.File:
                    selected = row.F;
                    Invalidate();
                    if (FileSelected != null) FileSelected(row.F);
                    break;
                case Kind.More:
                    if (!loading && NeedMore != null) NeedMore();
                    break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Right || repo == null) return;
            int i = HitTest(e.Y);
            if (i < 0 || rows[i].K != Kind.File) return;
            string path = System.IO.Path.Combine(repo.FullPath, rows[i].F.Rel.Replace('/', '\\'));
            OpenMenu.Show(this, e.Location, path, repo.FullPath, false);
        }

        protected override void OnContentMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Y);
            bool u = undoRect.Contains(e.Location);
            if (i != hover || u != hoverUndo) { hover = i; hoverUndo = u; Invalidate(); }
            Cursor = i >= 0 && rows[i].K != Kind.Info ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnContentMouseLeave()
        {
            if (hover != -1) { hover = -1; hoverUndo = false; Invalidate(); }
        }
    }
}
