using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GitPainel
{
    class RepoList : ScrollView
    {
        enum Kind { Repo, Module, File, Error, Note }

        class Row
        {
            public Kind K;
            public RepoInfo Repo;
            public ModuleGroup Module;
            public FileChange File;
            public List<FileChange> Files; // arquivos visíveis do módulo (respeita a busca)
            public int Y, H, Indent, StarX = -1, StarW;
            public string Note;
            public List<KeyValuePair<Rectangle, string>> Actions = new List<KeyValuePair<Rectangle, string>>();
        }

        List<RepoInfo> repos = new List<RepoInfo>();
        readonly List<Row> rows = new List<Row>();
        int hover = -1;
        string hoverAction;
        string[] terms = new string[0];

        public FileChange Selected { get; private set; }
        public event EventHandler SelectionChanged, StarsChanged, ShowAllRequested, CheckChanged;
        public event Action<RepoInfo, ModuleGroup, string> RepoAction;
        public event EventHandler OpenRequested; // sender = FileChange
        public string EmptyText = "Carregando…";
        public readonly HashSet<string> Stars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool OnlyMine;
        public int VisibleFiles { get; private set; }

        // seleção para commit: arquivo inteiro (Checked) ou só alguns trechos (Partial)
        public readonly HashSet<string> Checked = new HashSet<string>();
        public readonly Dictionary<string, HashSet<int>> Partial = new Dictionary<string, HashSet<int>>();
        public readonly Dictionary<string, string> PartialSig = new Dictionary<string, string>();
        public RepoInfo CheckedRepo { get; private set; }

        // operação em andamento (push/pull…): a pílula dela mostra spinner e as outras ficam bloqueadas
        string busyRepo, busyAction, busyLabel;
        readonly Timer spin = new Timer { Interval = 30 };
        // pílula pressionada (a ação dispara ao soltar o botão, como um botão de verdade)
        int pressedRow = -1;
        string pressedAction;
        DateTime lastAction;

        public RepoList()
        {
            BackColor = T.Panel;
            spin.Tick += delegate { Invalidate(); };
        }

        public void SetBusy(string repoPath, string action, string label)
        {
            busyRepo = repoPath;
            busyAction = action;
            busyLabel = label;
            if (repoPath != null) spin.Start(); else spin.Stop();
            Invalidate();
        }

        public bool IsBusy { get { return busyRepo != null; } }


        // ---------------------------------------------------------- dados

        public void SetRepos(List<RepoInfo> list)
        {
            repos = list;
            // mantém a seleção de commit só para arquivos que ainda têm alteração
            var alive = new HashSet<string>(repos.SelectMany(r => r.Files).Select(f => f.Key));
            Checked.RemoveWhere(k => !alive.Contains(k));
            foreach (var k in Partial.Keys.Where(k => !alive.Contains(k)).ToList()) { Partial.Remove(k); PartialSig.Remove(k); }
            CheckedRepo = CheckedRepo == null ? null : repos.FirstOrDefault(r => r.FullPath == CheckedRepo.FullPath);
            if (SelectedCount == 0) CheckedRepo = null;
            Build();
            if (CheckChanged != null) CheckChanged(this, EventArgs.Empty);
        }

        public void Rebuild() { Build(); }

        public string Query
        {
            set
            {
                terms = (value ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                Build();
                JumpTo(0, 0);
            }
        }

        bool Searching { get { return terms.Length > 0; } }
        bool Match(FileChange f) { string p = f.Rel.ToLowerInvariant(); return terms.All(t => p.Contains(t)); }

        public void Select(FileChange f, bool raise)
        {
            Selected = f;
            var row = rows.FirstOrDefault(r => r.File != null && r.File == f);
            if (row != null) EnsureVisible(row);
            Invalidate();
            if (raise && SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        // expandir/recolher: todos os repositórios ou só um (e os módulos deles)
        public bool AnyCollapsed
        {
            get { return repos.Any(r => r.Files.Count > 0 && (!r.Expanded || (!r.Flat && r.Modules.Any(m => !m.Expanded)))); }
        }

        public void SetAllExpanded(bool on, RepoInfo only)
        {
            foreach (var r in repos)
            {
                if (only != null && r.FullPath != only.FullPath) continue;
                r.Expanded = on;
                foreach (var m in r.Modules) m.Expanded = on;
            }
            Build();
        }

        public event EventHandler Rebuilt; // a barra de busca atualiza o ícone de expandir/recolher

        public void FocusFirstFile()
        {
            var first = rows.FirstOrDefault(r => r.File != null);
            Focus();
            if (first != null && first.File != Selected) Select(first.File, true);
        }

        bool RepoHasStars(RepoInfo r)
        {
            string p = r.Name + ":";
            return Stars.Any(s => s.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        void Build()
        {
            rows.Clear();
            VisibleFiles = 0;
            int y = T.Px(8);
            foreach (var r in repos)
            {
                foreach (var m in r.Modules) m.Starred = Stars.Contains(m.Key);

                // "Meus": se o repositório tem módulos marcados, mostra só eles; se não tem, mostra tudo
                bool filter = OnlyMine && !r.Flat && RepoHasStars(r);
                var mods = filter ? r.Modules.Where(m => m.Starred).ToList() : r.Modules;
                int count = mods.Sum(m => m.Files.Count);
                int hidden = r.Files.Count - count;

                var modFiles = mods.Select(m => new KeyValuePair<ModuleGroup, List<FileChange>>(
                    m, Searching ? m.Files.Where(Match).ToList() : m.Files)).ToList();
                int shown = modFiles.Sum(kv => kv.Value.Count);
                if (Searching && shown == 0) continue;
                VisibleFiles += Searching ? shown : count;

                rows.Add(new Row { K = Kind.Repo, Repo = r, Y = y, H = T.Px(44) });
                y += T.Px(44);
                if (r.Expanded || Searching)
                {
                    if (r.Error != null) { rows.Add(new Row { K = Kind.Error, Repo = r, Y = y, H = T.Px(30) }); y += T.Px(30); }
                    if (r.Flat)
                    {
                        foreach (var kv in modFiles)
                            foreach (var f in kv.Value)
                            {
                                rows.Add(new Row { K = Kind.File, Repo = r, File = f, Y = y, H = T.Px(30), Indent = T.Px(42) });
                                y += T.Px(30);
                            }
                    }
                    else
                    {
                        foreach (var kv in modFiles)
                        {
                            if (Searching && kv.Value.Count == 0) continue;
                            var m = kv.Key;
                            rows.Add(new Row { K = Kind.Module, Repo = r, Module = m, Files = kv.Value, Y = y, H = T.Px(34), Indent = T.Px(20) });
                            y += T.Px(34);
                            if (!m.Expanded && !Searching) continue;
                            foreach (var f in kv.Value)
                            {
                                rows.Add(new Row { K = Kind.File, Repo = r, Module = m, File = f, Y = y, H = T.Px(30), Indent = T.Px(64) });
                                y += T.Px(30);
                            }
                        }
                        if (filter && hidden > 0 && !Searching)
                        {
                            rows.Add(new Row
                            {
                                K = Kind.Note, Repo = r, Y = y, H = T.Px(30), Indent = T.Px(20),
                                Note = "+ " + hidden + (hidden == 1 ? " arquivo" : " arquivos") + " em módulos da equipe"
                            });
                            y += T.Px(30);
                        }
                    }
                }
                y += T.Px(4);
            }
            contentH = y + T.Px(8);
            Reclamp();
            Invalidate();
            if (Rebuilt != null) Rebuilt(this, EventArgs.Empty);
        }

        void EnsureVisible(Row r)
        {
            int pad = T.Px(8);
            if (r.Y - pad < TargetY) ScrollToY(r.Y - pad);
            else if (r.Y + r.H + pad > TargetY + ClientSize.Height) ScrollToY(r.Y + r.H + pad - ClientSize.Height);
        }

        int RepoCount(RepoInfo r)
        {
            bool filter = OnlyMine && !r.Flat && RepoHasStars(r);
            return filter ? r.Modules.Where(m => m.Starred).Sum(m => m.Files.Count) : r.Files.Count;
        }

        // ---------------------------------------------------------- seleção para commit

        public int SelectedCount { get { return Checked.Count + Partial.Count; } }

        public List<FileChange> CheckedFiles
        {
            get
            {
                if (CheckedRepo == null) return new List<FileChange>();
                return CheckedRepo.Files.Where(f => Checked.Contains(f.Key) || Partial.ContainsKey(f.Key)).ToList();
            }
        }

        public event EventHandler PartialDropped;

        static bool Checkable(FileChange f) { return !f.Rel.EndsWith("/"); }

        int FileState(FileChange f) { return Checked.Contains(f.Key) ? 1 : Partial.ContainsKey(f.Key) ? 2 : 0; }

        int CheckState(List<FileChange> files)
        {
            int on = 0, off = 0;
            foreach (var f in files)
            {
                if (!Checkable(f)) continue;
                int s = FileState(f);
                if (s == 1) on++; else if (s == 0) off++; else { on++; off++; }
            }
            if (on + off == 0) return -1;
            return on == 0 ? 0 : off == 0 ? 1 : 2;
        }

        void SwitchRepo(RepoInfo r)
        {
            // commit é por repositório: marcar em outro repositório troca a seleção
            if (CheckedRepo != null && CheckedRepo != r) { Checked.Clear(); Partial.Clear(); PartialSig.Clear(); }
            CheckedRepo = r;
        }

        void Changed()
        {
            if (SelectedCount == 0) CheckedRepo = null;
            Invalidate();
            if (CheckChanged != null) CheckChanged(this, EventArgs.Empty);
        }

        void SetChecked(IEnumerable<FileChange> files, bool on)
        {
            foreach (var f in files)
            {
                if (!Checkable(f)) continue;
                if (on) { SwitchRepo(f.Repo); Checked.Add(f.Key); }
                else Checked.Remove(f.Key);
                Partial.Remove(f.Key);
                PartialSig.Remove(f.Key);
            }
            Changed();
        }

        public void ToggleFile(FileChange f) { SetChecked(new[] { f }, FileState(f) != 1); }

        public void ClearChecks()
        {
            Checked.Clear();
            Partial.Clear();
            PartialSig.Clear();
            Changed();
        }

        public int[] HunkStates(FileChange f, DiffData d)
        {
            var st = new int[d.HunkCount];
            HashSet<int> set;
            if (Checked.Contains(f.Key)) for (int i = 0; i < st.Length; i++) st[i] = 1;
            else if (Partial.TryGetValue(f.Key, out set)) foreach (int i in set) if (i < st.Length) st[i] = 1;
            return st;
        }

        public void ToggleHunk(FileChange f, DiffData d, int idx)
        {
            var set = new HashSet<int>();
            HashSet<int> cur;
            if (Checked.Contains(f.Key)) for (int i = 0; i < d.HunkCount; i++) set.Add(i);
            else if (Partial.TryGetValue(f.Key, out cur)) set.UnionWith(cur);
            if (!set.Remove(idx)) set.Add(idx);

            Checked.Remove(f.Key);
            Partial.Remove(f.Key);
            PartialSig.Remove(f.Key);
            if (set.Count > 0) SwitchRepo(f.Repo);
            if (set.Count == d.HunkCount) Checked.Add(f.Key);
            else if (set.Count > 0) { Partial[f.Key] = set; PartialSig[f.Key] = d.Signature; }
            Changed();
        }

        // o arquivo mudou: trechos marcados podem não bater mais com o diff atual
        public void ValidatePartial(FileChange f, DiffData d)
        {
            string sig;
            if (!PartialSig.TryGetValue(f.Key, out sig) || sig == d.Signature) return;
            Partial.Remove(f.Key);
            PartialSig.Remove(f.Key);
            Changed();
            if (PartialDropped != null) PartialDropped(f, EventArgs.Empty);
        }

        // ---------------------------------------------------------- pintura

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Panel);
            if (rows.Count == 0)
            {
                string t = Searching ? "Nenhum arquivo alterado corresponde à busca" : EmptyText;
                T.Text(g, t, T.Ui, T.Comment, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height / 2), TextFormatFlags.HorizontalCenter);
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
                switch (row.K)
                {
                    case Kind.Repo: PaintRepo(g, row, rect, i == hover); break;
                    case Kind.Module: PaintModule(g, row, rect, i == hover); break;
                    case Kind.File: PaintFile(g, row, rect, i == hover, row.File == Selected); break;
                    case Kind.Error:
                        T.Text(g, row.Repo.Error, T.Small, T.Red, new Rectangle(rect.X + T.Px(26), rect.Y, rect.Width - T.Px(32), rect.Height), 0);
                        break;
                    case Kind.Note:
                        if (i == hover) T.FillRound(g, T.Mix(T.Panel, T.Line, 0.35f), rect, T.Px(6));
                        T.Text(g, row.Note, T.Small, i == hover ? T.Mix(T.Comment, T.Fg, 0.4f) : T.Comment,
                            new Rectangle(rect.X + row.Indent + T.Px(4), rect.Y, rect.Width - row.Indent - T.Px(8), rect.Height), 0);
                        break;
                }
            }
            PaintBars(g);
        }

        // botão-pílula de sincronização (Enviar / Atualizar / Publicar); devolve o x livre à esquerda
        int SyncPill(Graphics g, Row row, Rectangle rect, int right, string action, string glyph, string count, string label, Color accent)
        {
            int idx = rows.IndexOf(row);
            bool mine = busyRepo == row.Repo.FullPath && busyAction == action;
            bool blocked = busyRepo != null && !mine;
            bool hot = !blocked && !mine && idx == hover && hoverAction == action;
            bool down = !blocked && !mine && idx == pressedRow && pressedAction == action;
            if (mine) { count = ""; label = busyLabel ?? label; }
            int h = T.Px(24);
            int cw = count.Length > 0 ? T.Measure(g, count, T.SmallBold) + 1 : 0;
            int lw = label.Length > 0 ? T.Measure(g, label, T.Small) + 1 : 0;
            int w = T.Px(10) + T.Px(14) + (cw > 0 ? T.Px(4) + cw : 0) + (lw > 0 ? T.Px(6) + lw : 0) + T.Px(11);
            var r = new Rectangle(right - w, rect.Y + (rect.Height - h) / 2, w, h);
            var full = r;
            float tint = mine ? 0.20f + 0.14f * T.Pulse : blocked ? 0.07f : down ? 0.40f : hot ? 0.30f : 0.15f;
            if (down) r = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            T.FillRound(g, T.Mix(T.Panel, accent, tint), r, r.Height / 2f);
            if (hot || mine) T.StrokeRound(g, T.Mix(T.Panel, accent, 0.55f), new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f, T.Px(1));
            if (blocked) accent = T.Mix(accent, T.Panel, 0.55f);
            int x = r.X + T.Px(10);
            if (mine)
            {
                float s = T.Px(12);
                T.Spinner(g, new RectangleF(x + 1, r.Y + (h - s) / 2f, s, s), accent, T.Px(1.6f));
            }
            else T.Text(g, glyph, T.IconSmall, accent, new Rectangle(x, r.Y, T.Px(14), h), TextFormatFlags.HorizontalCenter);
            x += T.Px(14);
            if (cw > 0) { x += T.Px(4); T.Text(g, count, T.SmallBold, accent, new Rectangle(x, r.Y, cw, h), 0); x += cw; }
            if (lw > 0) { x += T.Px(6); T.Text(g, label, T.Small, hot ? T.Fg : T.Mix(accent, T.Fg, 0.4f), new Rectangle(x, r.Y, lw, h), 0); }
            if (!blocked && !mine) row.Actions.Add(new KeyValuePair<Rectangle, string>(full, action));
            return full.X - T.Px(6);
        }

        // ícone discreto que só aparece no hover (⋯ do repositório, histórico do módulo)
        int HoverIcon(Graphics g, Row row, Rectangle rect, int right, string action, string glyph)
        {
            var r = new Rectangle(right - T.Px(26), rect.Y + (rect.Height - T.Px(26)) / 2, T.Px(26), T.Px(26));
            bool hot = hoverAction == action && rows.IndexOf(row) == hover;
            if (hot) T.FillRound(g, T.Line, r, T.Px(6));
            T.Text(g, glyph, T.Icon, hot ? T.Fg : T.Mix(T.Comment, T.Fg, 0.3f), r, TextFormatFlags.HorizontalCenter);
            row.Actions.Add(new KeyValuePair<Rectangle, string>(r, action));
            return r.X - T.Px(4);
        }

        void PaintRepo(Graphics g, Row row, Rectangle rect, bool hot)
        {
            var r = row.Repo;
            row.Actions.Clear();
            if (hot) T.FillRound(g, T.Mix(T.Panel, T.Line, 0.45f), rect, T.Px(6));
            int count = RepoCount(r);
            bool has = count > 0 || r.Error != null;
            int x = rect.X + T.Px(4);
            T.Text(g, r.Expanded || Searching ? G.Down : G.Right, T.IconSmall, T.Comment,
                new Rectangle(x, rect.Y, T.Px(18), rect.Height), TextFormatFlags.HorizontalCenter);
            x += T.Px(22);

            // contador à direita
            bool clean = r.Error == null && count == 0;
            string badge = r.Error != null ? "erro" : clean ? G.Check : count.ToString();
            Font bf = clean ? T.IconSmall : T.SmallBold;
            Color bc = r.Error != null ? T.Red : clean ? T.Mix(T.Panel, T.Green, 0.7f) : T.Fg;
            Color bb = r.Error != null ? T.Mix(T.Panel, T.Red, 0.16f) : clean ? T.Mix(T.Panel, T.Green, 0.10f) : T.Mix(T.Panel, T.Line, 0.9f);
            int bh = T.Px(20);
            int bw = Math.Max(T.Px(26), T.Measure(g, badge, bf) + T.Px(14));
            var brc = new Rectangle(rect.Right - bw - T.Px(6), rect.Y + (rect.Height - bh) / 2, bw, bh);
            T.FillRound(g, bb, brc, bh / 2f);
            T.Text(g, badge, bf, bc, brc, TextFormatFlags.HorizontalCenter);
            int limit = brc.X - T.Px(6);

            if (r.Error == null)
            {
                if (hot) limit = HoverIcon(g, row, rect, limit, "more", G.More);
                // sincronização sempre visível quando há algo a fazer
                if (r.Ahead > 0) limit = SyncPill(g, row, rect, limit, "push", G.Push, r.Ahead.ToString(), "Enviar", T.Green);
                else if (r.HasHead && r.Upstream == null && r.HasRemote) limit = SyncPill(g, row, rect, limit, "push", G.Push, "", "Publicar", T.Green);
                if (r.Behind > 0) limit = SyncPill(g, row, rect, limit, "pull", G.Pull, r.Behind.ToString(), "Atualizar", T.Cyan);
            }

            // nome
            int nw = Math.Min(T.Measure(g, r.Name, T.UiBold) + 1, limit - T.Px(4) - x);
            T.Text(g, r.Name, T.UiBold, has ? T.Pink : T.Comment, new Rectangle(x, rect.Y, nw, rect.Height), 0);
            x += nw + T.Px(8);

            // branch
            string bt = r.Branch;
            if (bt.Length == 0) return;
            int pw = Math.Min(T.Measure(g, bt, T.Small) + T.Px(16), limit - x);
            if (pw < T.Px(34)) return;
            int ph = T.Px(20);
            var pr = new Rectangle(x, rect.Y + (rect.Height - ph) / 2, pw, ph);
            Color pc = has ? T.Purple : T.Comment;
            T.FillRound(g, T.Mix(T.Panel, pc, 0.14f), pr, ph / 2f);
            T.Text(g, bt, T.Small, pc, new Rectangle(pr.X + T.Px(8), pr.Y, pr.Width - T.Px(16), pr.Height), 0);
        }

        // menu do ⋯ e do botão direito em repositório/módulo
        void ShowRepoMenu(RepoInfo r, ModuleGroup m, Point at)
        {
            var items = new List<ToolStripItem>();
            Func<string, string, bool, string, ToolStripItem> act = (glyph, text, enabled, action) =>
                OpenMenu.Item(glyph, text, enabled, () => { if (RepoAction != null) RepoAction(r, m, action); });
            string root = r.FullPath, path = root;
            if (m == null)
            {
                items.Add(act(G.History, "Ver histórico", r.HasHead, "history"));
                bool open = r.Expanded && (r.Flat || r.Modules.All(x => x.Expanded));
                items.Add(OpenMenu.Item(open ? G.Up : G.Down, open ? "Recolher tudo neste repositório" : "Expandir tudo neste repositório", r.Files.Count > 0,
                    () => SetAllExpanded(!open, r)));
                items.Add(act(G.Pull, r.Behind > 0 ? "Atualizar do remoto  (↓" + r.Behind + ")" : "Buscar e atualizar do remoto (pull)", r.Upstream != null, "pull"));
                string pushText = r.Upstream == null ? "Publicar branch no remoto (push)"
                    : r.Ahead > 0 ? "Enviar " + r.Ahead + (r.Ahead == 1 ? " commit" : " commits") + " (push)" : "Enviar (push)";
                items.Add(act(G.Push, pushText, r.HasHead && (r.Ahead > 0 || (r.Upstream == null && r.HasRemote)), "push"));
                items.Add(act(G.Undo, "Desfazer último commit", r.MaybeLocalHead, "undo"));
            }
            else
            {
                items.Add(act(G.History, "Ver histórico do módulo", r.HasHead && m.Path.Length > 0, "history"));
                if (m.Path.Length > 0) path = Path.Combine(root, m.Path.Replace('/', '\\'));
            }
            items.Add(OpenMenu.Separator());
            items.AddRange(OpenMenu.PathItems(path, root, true));
            OpenMenu.ShowItems(this, at, items.ToArray());
        }
        void PaintModule(Graphics g, Row row, Rectangle rect, bool hot)
        {
            var m = row.Module;
            var files = row.Files ?? m.Files;
            if (hot) T.FillRound(g, T.Mix(T.Panel, T.Line, 0.35f), rect, T.Px(6));
            int x = rect.X + T.Px(6);
            T.Text(g, m.Expanded || Searching ? G.Down : G.Right, T.IconSmall, T.Comment,
                new Rectangle(x, rect.Y, T.Px(16), rect.Height), TextFormatFlags.HorizontalCenter);
            x = rect.X + T.Px(26);
            int st = CheckState(files);
            if (st >= 0) T.Check(g, x, rect.Y + rect.Height / 2f, st, hot);
            x += T.Px(20);
            T.Text(g, G.FolderClosed, T.Icon, m.Starred ? T.Yellow : T.Cyan, new Rectangle(x, rect.Y, T.Px(18), rect.Height), TextFormatFlags.HorizontalCenter);
            x += T.Px(24);

            // contador
            string badge = files.Count.ToString();
            int bh = T.Px(18);
            int bw = Math.Max(T.Px(24), T.Measure(g, badge, T.SmallBold) + T.Px(12));
            var brc = new Rectangle(rect.Right - bw - T.Px(8), rect.Y + (rect.Height - bh) / 2, bw, bh);
            T.FillRound(g, T.Mix(T.Panel, T.Line, 0.6f), brc, bh / 2f);
            T.Text(g, badge, T.SmallBold, T.Mix(T.Comment, T.Fg, 0.5f), brc, TextFormatFlags.HorizontalCenter);

            // estrela: marcada sempre visível; desmarcada só no hover
            row.StarW = T.Px(24);
            row.StarX = brc.X - T.Px(4) - row.StarW;
            if (m.Starred || hot)
                T.Text(g, m.Starred ? G.StarFill : G.Star, T.Icon, m.Starred ? T.Yellow : T.Comment,
                    new Rectangle(row.StarX, rect.Y, row.StarW, rect.Height), TextFormatFlags.HorizontalCenter);
            int limit = row.StarX - T.Px(4);
            row.Actions.Clear();
            if (hot && m.Repo.HasHead && m.Path.Length > 0) limit = HoverIcon(g, row, rect, limit, "history", G.History) - T.Px(2);

            // rótulo: pasta pai esmaecida + último nome em destaque
            int maxW = limit - x;
            string label = m.Path.Length == 0 ? "raiz do repositório" : m.Path;
            int cut = label.LastIndexOf('/');
            string prefix = cut < 0 || m.Path.Length == 0 ? "" : label.Substring(0, cut + 1);
            string last = cut < 0 || m.Path.Length == 0 ? label : label.Substring(cut + 1);
            int lw = Math.Min(T.Measure(g, last, T.UiBold) + 1, maxW);
            int pw = Math.Min(T.Measure(g, prefix, T.Ui) + 1, Math.Max(0, maxW - lw));
            if (pw > 0) T.Text(g, prefix, T.Ui, T.Comment, new Rectangle(x, rect.Y, pw, rect.Height), 0);
            T.Text(g, last, T.UiBold, m.Path.Length == 0 ? T.Comment : T.Fg, new Rectangle(x + pw, rect.Y, lw, rect.Height), 0);
        }

        void PaintFile(Graphics g, Row row, Rectangle rect, bool hot, bool sel)
        {
            var f = row.File;
            if (sel)
            {
                T.FillRound(g, T.Line, rect, T.Px(6));
                T.FillRound(g, T.Purple, new RectangleF(rect.X, rect.Y + T.Px(7), T.Px(3), rect.Height - T.Px(14)), T.Px(1.5f));
            }
            else if (hot) T.FillRound(g, T.Mix(T.Panel, T.Line, 0.45f), rect, T.Px(6));

            // guia vertical discreta, como numa árvore: liga o arquivo ao repositório/módulo acima
            int guide = rect.X + (row.K == Kind.File && row.Module != null && !row.Repo.Flat ? T.Px(33) : T.Px(13));
            T.Fill(g, sel ? T.Mix(T.Line, T.Comment, 0.35f) : T.Mix(T.Panel, T.Line, 0.7f), new Rectangle(guide, rect.Y, Math.Max(1, T.Px(1)), rect.Height));

            Color sc = T.StatusColor(f.Kind);
            var sr = new Rectangle(rect.Right - T.Px(26), rect.Y, T.Px(18), rect.Height);
            T.Text(g, f.Kind.ToString(), T.SmallBold, sc, sr, TextFormatFlags.HorizontalCenter);
            int right = sr.X - T.Px(4);
            if (f.Staged)
            {
                float d = T.Px(6);
                T.Dot(g, T.Purple, right - d, rect.Y + (rect.Height - d) / 2f, d);
                right -= T.Px(14);
            }
            HashSet<int> part;
            if (Partial.TryGetValue(f.Key, out part))
            {
                string pt = part.Count + (part.Count == 1 ? " trecho" : " trechos");
                int pw = T.Measure(g, pt, T.Small) + T.Px(14);
                var pr = new Rectangle(right - pw, rect.Y + (rect.Height - T.Px(18)) / 2, pw, T.Px(18));
                T.FillRound(g, T.Mix(T.Panel, T.Purple, 0.2f), pr, T.Px(9));
                T.Text(g, pt, T.Small, T.Purple, pr, TextFormatFlags.HorizontalCenter);
                right = pr.X - T.Px(6);
            }

            if (Checkable(f)) T.Check(g, rect.X + row.Indent - T.Px(22), rect.Y + rect.Height / 2f, FileState(f), hot || sel);
            int x = rect.X + row.Indent;
            Font nf = f.Kind == 'D' ? T.UiStrike : T.Ui;
            Color nc = f.Kind == 'D' ? T.Comment : T.Fg;
            string name = f.Name;
            int nw = Math.Min(T.Measure(g, name, nf) + 1, right - x);

            // realce do termo buscado no nome
            if (Searching)
            {
                string low = name.ToLowerInvariant();
                foreach (var t in terms)
                {
                    int at = low.IndexOf(t, StringComparison.Ordinal);
                    if (at < 0) continue;
                    int hx = x + T.Measure(g, name.Substring(0, at), nf);
                    int hw = T.Measure(g, name.Substring(at, t.Length), nf);
                    if (hx < x + nw) T.FillRound(g, T.Mix(T.Panel, T.Yellow, 0.25f), new RectangleF(hx - 1, rect.Y + T.Px(6), Math.Min(hw + 2, x + nw - hx + 1), rect.Height - T.Px(12)), T.Px(3));
                }
            }
            T.Text(g, name, nf, nc, new Rectangle(x, rect.Y, nw, rect.Height), 0);
            x += nw + T.Px(8);
            string folder = f.LocalFolder;
            if (folder.Length > 0 && right - x > T.Px(24))
                T.Text(g, folder, T.Small, sel ? T.Mix(T.Comment, T.Fg, 0.25f) : T.Comment,
                    new Rectangle(x, rect.Y + 1, right - x - T.Px(4), rect.Height), 0);
        }

        // ---------------------------------------------------------- mouse e teclado

        int HitTest(int y)
        {
            int yy = y + (int)Math.Round(offY);
            for (int i = 0; i < rows.Count; i++)
                if (yy >= rows[i].Y && yy < rows[i].Y + rows[i].H) return i;
            return -1;
        }

        string ActionAt(Row row, int x)
        {
            foreach (var a in row.Actions)
                if (x >= a.Key.X && x < a.Key.Right) return a.Value;
            return null;
        }

        protected override void OnContentMouseDown(MouseEventArgs e)
        {
            int i = HitTest(e.Y);
            if (i < 0) return;
            var row = rows[i];
            string action = ActionAt(row, e.X);
            if (action == "more")
            {
                var r = row.Actions.First(a => a.Value == "more").Key;
                ShowRepoMenu(row.Repo, null, new Point(r.X, r.Bottom + T.Px(2)));
                return;
            }
            if (action != null)
            {
                pressedRow = i;
                pressedAction = action;
                Invalidate();
                return;
            }
            switch (row.K)
            {
                case Kind.Repo:
                    if (Searching || e.Clicks > 1) break;
                    row.Repo.Expanded = !row.Repo.Expanded;
                    Build();
                    break;
                case Kind.Module:
                    int cbx = T.Px(8) + T.Px(26);
                    var files = row.Files ?? row.Module.Files;
                    if (e.Clicks > 1) break;
                    if (e.X >= cbx - T.Px(4) && e.X < cbx + T.Px(19) && CheckState(files) >= 0)
                        SetChecked(files, CheckState(files) != 1);
                    else if (row.StarX >= 0 && e.X >= row.StarX && e.X < row.StarX + row.StarW)
                    {
                        if (!Stars.Remove(row.Module.Key)) { Stars.Add(row.Module.Key); row.Module.Expanded = true; }
                        Build();
                        if (StarsChanged != null) StarsChanged(this, EventArgs.Empty);
                    }
                    else if (!Searching)
                    {
                        row.Module.Expanded = !row.Module.Expanded;
                        Build();
                    }
                    break;
                case Kind.File:
                    int fcx = T.Px(8) + row.Indent - T.Px(22);
                    bool onBox = Checkable(row.File) && e.X >= fcx - T.Px(4) && e.X < fcx + T.Px(19);
                    if (onBox) { if (e.Clicks == 1) ToggleFile(row.File); }
                    else if (e.Clicks > 1) { if (OpenRequested != null) OpenRequested(row.File, EventArgs.Empty); } // duplo clique abre no VS Code
                    else if (row.File != Selected) Select(row.File, true);
                    break;
                case Kind.Note:
                    if (ShowAllRequested != null) ShowAllRequested(this, EventArgs.Empty);
                    break;
            }
            hover = HitTest(e.Y);
        }

        protected override void OnContentMouseUp(MouseEventArgs e)
        {
            if (pressedAction == null) return;
            int i = HitTest(e.Y);
            string a = pressedAction;
            bool same = i == pressedRow && i >= 0 && ActionAt(rows[i], e.X) == a;
            var repo = pressedRow >= 0 && pressedRow < rows.Count ? rows[pressedRow] : null;
            pressedRow = -1;
            pressedAction = null;
            Invalidate();
            if (!same || repo == null || busyRepo != null || !T.Debounce(ref lastAction, 600)) return;
            if (RepoAction != null) RepoAction(repo.Repo, repo.K == Kind.Module ? repo.Module : null, a);
        }

        // botão direito: abrir no Explorador / Bloco de Notas / VS Code
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Right) return;
            int i = HitTest(e.Y);
            if (i < 0) return;
            var row = rows[i];
            string root = row.Repo.FullPath, path;
            bool folder;
            switch (row.K)
            {
                case Kind.File:
                    path = Path.Combine(root, row.File.Rel.TrimEnd('/').Replace('/', '\\'));
                    folder = row.File.Rel.EndsWith("/");
                    break;
                case Kind.Module:
                    ShowRepoMenu(row.Repo, row.Module, e.Location);
                    return;
                case Kind.Repo:
                    ShowRepoMenu(row.Repo, null, e.Location);
                    return;
                default:
                    return;
            }
            OpenMenu.Show(this, e.Location, path, root, folder);
        }

        protected override void OnContentMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.Y);
            if (i >= 0 && rows[i].K == Kind.Error) i = -1;
            string a = i >= 0 ? ActionAt(rows[i], e.X) : null;
            if (i != hover || a != hoverAction) { hover = i; hoverAction = a; Invalidate(); }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnContentMouseLeave()
        {
            if (hover != -1 || hoverAction != null) { hover = -1; hoverAction = null; Invalidate(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up))
            {
                SetAllExpanded(e.KeyCode == Keys.Down, null);
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.C && Selected != null)
            {
                try { Clipboard.SetText(Selected.Rel); } catch { }
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Space && Selected != null)
            {
                ToggleFile(Selected);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                var files = rows.Where(r => r.File != null).ToList();
                if (files.Count > 0)
                {
                    int idx = files.FindIndex(r => r.File == Selected);
                    int next = idx < 0 ? 0 : Math.Max(0, Math.Min(files.Count - 1, idx + (e.KeyCode == Keys.Down ? 1 : -1)));
                    if (files[next].File != Selected) Select(files[next].File, true);
                }
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }
    }
}
