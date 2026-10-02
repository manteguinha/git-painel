using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GitPainel
{
    class MainForm : Form, IMessageFilter
    {
        string root;
        List<RepoInfo> repos = new List<RepoInfo>();
        readonly TopBar top = new TopBar();
        readonly StatusBar status = new StatusBar();
        readonly SearchBar search = new SearchBar();
        readonly RepoList list = new RepoList();
        readonly Panel leftPane = new Panel();
        readonly CommitPanel commit = new CommitPanel();
        readonly Divider divider = new Divider();
        readonly HistoryPane history = new HistoryPane();
        readonly DiffHeader header = new DiffHeader();
        readonly DiffView diff = new DiffView();
        readonly Dictionary<string, Git.RepoStyle> styles = new Dictionary<string, Git.RepoStyle>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> ini = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly string iniPath;
        readonly bool testMode; // aberto com uma pasta na linha de comando: não grava preferências
        bool busy, pending, opBusy, manualRefresh;
        DateTime lastRefresh = DateTime.MinValue, lastDone, lastFetch = DateTime.Now.AddMinutes(-9.5);
        int diffReq;
        FileChange current; // arquivo cujo diff está na tela (alteração ou histórico)
        // atualização automática: primeira consulta ~8 s depois de abrir, depois a cada 6 horas
        Updater.Info update;
        DateTime lastUpdateCheck = DateTime.Now.AddHours(-6).AddSeconds(8);
        bool updating, restartAfterClose;

        public MainForm(string arg)
        {
            testMode = arg != null;
            Text = testMode ? "Git Painel — " + Path.GetFileName(arg.TrimEnd('\\')) : "Git Painel";
            BackColor = T.Bg;
            ForeColor = T.Fg;
            Font = T.Ui;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(T.Px(1320), T.Px(820));
            MinimumSize = new Size(T.Px(900), T.Px(520));
            Icon = MakeIcon();

            iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GitPainel.ini");
            LoadIni();
            // sem preferência salva (primeira vez, ou app copiado para outra pessoa): pergunta a pasta ao abrir
            root = arg ?? Ini("pasta");

            // direita: histórico (opcional) + cabeçalho + diff
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg };
            diff.Dock = DockStyle.Fill;
            header.Dock = DockStyle.Top;
            inner.Controls.Add(diff);
            inner.Controls.Add(header);
            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg };
            history.Dock = DockStyle.Left;
            history.Visible = false;
            rightPanel.Controls.Add(inner);
            rightPanel.Controls.Add(history);

            // esquerda: busca + lista + painel de commit
            leftPane.Dock = DockStyle.Left;
            leftPane.BackColor = T.Panel;
            leftPane.Width = T.Px(IniInt("lateral", 420));
            list.Dock = DockStyle.Fill;
            search.Dock = DockStyle.Top;
            commit.Dock = DockStyle.Bottom;
            commit.Height = commit.PreferredHeight;
            commit.Visible = false;
            leftPane.Controls.Add(list);
            leftPane.Controls.Add(search);
            leftPane.Controls.Add(commit);
            divider.Dock = DockStyle.Left;
            divider.Target = leftPane;
            top.Dock = DockStyle.Top;
            status.Dock = DockStyle.Bottom;

            Controls.Add(rightPanel);
            Controls.Add(divider);
            Controls.Add(leftPane);
            Controls.Add(top);
            Controls.Add(status);

            top.Folder = root ?? "";
            top.PickRequested += delegate { PickFolder(); };
            top.ReloadRequested += delegate { manualRefresh = true; top.Refreshing = true; DoRefresh(); };
            top.UpdateRequested += delegate { DoUpdate(); };
            Updater.CleanupOld();
            list.SelectionChanged += delegate { if (list.Selected != null) ShowFile(list.Selected, false); };

            header.Mode.Selected = Ini("modo") == "lado" ? 1 : 0;
            diff.SideBySide = header.Mode.Selected == 1;
            header.Mode.Changed += delegate { diff.SideBySide = header.Mode.Selected == 1; };
            header.Wrap.On = Ini("quebra") == "1";
            diff.WordWrap = header.Wrap.On;
            header.Wrap.Changed += delegate { diff.WordWrap = header.Wrap.On; };

            foreach (var s in (Ini("meus") ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) list.Stars.Add(s);
            top.Filter.Selected = Ini("filtro") == "meus" ? 1 : 0;
            list.OnlyMine = top.Filter.Selected == 1;
            top.Filter.Changed += delegate { list.OnlyMine = top.Filter.Selected == 1; list.Rebuild(); UpdateStatus(); };
            list.StarsChanged += delegate { UpdateStatus(); SaveIni(); };
            list.ShowAllRequested += delegate { top.Filter.Selected = 0; };
            list.CheckChanged += delegate { UpdateCommitPanel(); UpdateHunkUi(); };
            list.PartialDropped += (s, e) =>
                status.Flash("Os trechos marcados em " + ((FileChange)s).Name + " mudaram no arquivo e foram desmarcados. Revise antes de commitar.", false, null, null);
            list.RepoAction += OnRepoAction;
            search.QueryChanged += delegate { list.Query = search.Query; };
            search.AnyCollapsed = () => list.AnyCollapsed;
            search.ExpandToggle += delegate { list.SetAllExpanded(list.AnyCollapsed, null); };
            list.Rebuilt += delegate { search.Invalidate(); };
            // da busca, ↓ ou Enter levam para o primeiro arquivo encontrado
            search.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.FocusFirstFile(); e.SuppressKeyPress = true; }
            };
            list.OpenRequested += (s, e) =>
            {
                var f = (FileChange)s;
                string path = Path.Combine(f.Repo.FullPath, f.Rel.TrimEnd('/').Replace('/', '\\'));
                if (!OpenMenu.OpenInVsCode(path)) status.Flash("Não foi possível abrir no VS Code (arquivo excluído ou VS Code não encontrado).", false, null, null);
            };
            commit.ClearRequested += delegate { list.ClearChecks(); };
            commit.CommitRequested += delegate { DoCommit(); };
            diff.HunkToggled += idx =>
            {
                if (current != null && current.HunkSelectable) list.ToggleHunk(current, diff.Data, idx);
            };
            history.FileSelected += f => { list.Select(null, false); ShowFile(f, false); };
            history.Closed += delegate { CloseHistory(); };
            history.UndoRequested += delegate { if (history.Repo != null) DoUndo(history.Repo); };

            var poll = new Timer { Interval = 400 };
            poll.Tick += delegate { PollChanges(); };
            poll.Start();
            SetupWatcher();

            RestoreWindow();
            Application.AddMessageFilter(this);
        }

        static Icon MakeIcon()
        {
            var bmp = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                T.DrawLogo(g, new RectangleF(2, 2, 60, 60));
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(Handle, T.Border, T.Fg);
            Native.RefreshFrame(Handle); // aplica a remoção da barra de título nativa
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            top.Invalidate(); // ícone de maximizar/restaurar
        }

        // A barra do app vira a barra de título: some a faixa nativa do topo, mas ficam as bordas
        // de redimensionar, a sombra, os cantos arredondados e o encaixe nas bordas da tela.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0083 && m.WParam != IntPtr.Zero) // WM_NCCALCSIZE
            {
                var before = (Native.NCCALCSIZE_PARAMS)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                int topEdge = before.r0.Top;
                base.WndProc(ref m);
                var p = (Native.NCCALCSIZE_PARAMS)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                // maximizada, a janela passa um pouco da tela: desce o conteúdo para não cortar
                p.r0.Top = topEdge + (Native.IsZoomed(Handle) ? Native.FrameThickness() : 0);
                System.Runtime.InteropServices.Marshal.StructureToPtr(p, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0084) // WM_NCHITTEST
            {
                base.WndProc(ref m);
                if (m.Result.ToInt32() == 1) // HTCLIENT
                {
                    long lp = m.LParam.ToInt64();
                    var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    int edge = T.Px(6);
                    if (WindowState == FormWindowState.Normal && pt.Y < edge)
                        m.Result = (IntPtr)(pt.X < edge * 2 ? 13 : pt.X > ClientSize.Width - edge * 2 ? 14 : 12); // HTTOPLEFT / HTTOPRIGHT / HTTOP
                    else if (pt.Y < top.Bottom)
                        m.Result = (IntPtr)2; // HTCAPTION
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (root == null || !Directory.Exists(root)) PickFolder();
            else DoRefresh();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if ((DateTime.Now - lastRefresh).TotalSeconds > 2) DoRefresh();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { DoRefresh(); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Z) { header.Wrap.On = !header.Wrap.On; e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.F) { search.Box.Focus(); search.Box.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.O) { PickFolder(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public bool PreFilterMessage(ref Message m)
        {
            // a roda do mouse rola o painel que está sob o cursor, não o que tem foco
            if (m.Msg != 0x020A && m.Msg != 0x020E) return false;
            IntPtr h = Native.WindowFromPoint(Cursor.Position);
            if (h == IntPtr.Zero || h == m.HWnd) return false;
            var c = Control.FromHandle(h);
            if (c == null || c.FindForm() != this) return false;
            Native.SendMessage(h, m.Msg, m.WParam, m.LParam);
            return true;
        }

        // executa em segundo plano e volta para a thread da tela
        void RunAsync<TR>(Func<TR> work, Action<TR> done) { RunAsync(work, done, null); }

        // fail: desfaz estados de "trabalhando" se algo inesperado acontecer (o app nunca fica travado)
        void RunAsync<TR>(Func<TR> work, Action<TR> done, Action fail)
        {
            Task.Factory.StartNew(work).ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            if (t.Exception == null) { done(t.Result); return; }
                            if (fail != null) fail();
                            MessageDialog.Error(this, "Erro inesperado", t.Exception.GetBaseException().Message);
                        }
                        catch (Exception ex)
                        {
                            if (fail != null) try { fail(); } catch { }
                            MessageDialog.Error(this, "Erro inesperado", ex.Message);
                        }
                    }));
                }
                catch { }
            });
        }

        RepoInfo Fresh(RepoInfo r) { return repos.FirstOrDefault(x => x.FullPath == r.FullPath) ?? r; }

        // ---------------------------------------------------------- pasta e atualização

        void PickFolder()
        {
            string p = FolderPicker.Pick(this, root);
            if (p == null) return;
            root = p;
            top.Folder = root;
            top.Invalidate();
            list.Select(null, false);
            list.ClearChecks();
            CloseHistory();
            ShowNothing();
            SetupWatcher();
            DoRefresh();
        }

        void ShowNothing()
        {
            current = null;
            header.Set(null, null);
            header.SetNote(null);
            diff.SetData(DiffData.Msg(G.Doc, "Selecione um arquivo", "Escolha um arquivo alterado na lista ao lado para ver o diff."), false);
        }

        void DoRefresh()
        {
            if (root == null || !Directory.Exists(root)) return;
            if (busy) { pending = true; return; }
            busy = true;
            lastRefresh = DateTime.Now;
            status.Busy = true;
            if (status.LeftText.Length == 0) status.LeftText = "Atualizando…";
            status.Invalidate();
            string r = root;
            RunAsync(() => Git.FindRepos(r).AsParallel().AsOrdered().WithDegreeOfParallelism(4).Select(p => Git.LoadRepo(r, p)).ToList(),
                     res => Apply(r, res),
                     () => { busy = false; status.Busy = false; if (manualRefresh) { manualRefresh = false; top.Refreshing = false; } });
        }

        void Apply(string r, List<RepoInfo> result)
        {
            busy = false;
            status.Busy = false;
            if (manualRefresh && !pending) { manualRefresh = false; top.Refreshing = false; }
            if (r != root) { DoRefresh(); return; }

            var old = new Dictionary<string, RepoInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in repos) old[o.FullPath] = o;
            foreach (var n in result)
            {
                RepoInfo o;
                if (old.TryGetValue(n.FullPath, out o)) n.Expanded = o.Expanded || (o.Files.Count == 0 && n.Files.Count > 0);
                else n.Expanded = n.Files.Count > 0 || n.Error != null;
            }

            // módulos: mantém o que o usuário abriu/fechou; novos abrem se forem "meus" (ou se forem poucos)
            var oldMods = new Dictionary<string, ModuleGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in repos) foreach (var m in o.Modules) oldMods[m.Key] = m;
            foreach (var n in result)
            {
                string prefix = n.Name + ":";
                bool repoStars = list.Stars.Any(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                foreach (var m in n.Modules)
                {
                    ModuleGroup om;
                    if (oldMods.TryGetValue(m.Key, out om)) m.Expanded = om.Expanded;
                    else m.Expanded = repoStars ? list.Stars.Contains(m.Key) : n.Modules.Count <= 3;
                }
            }
            repos = result;
            history.RepoRefreshed(repos);

            string key = list.Selected != null ? list.Selected.Key : null;
            list.EmptyText = "Nenhum repositório Git encontrado nesta pasta";
            list.SetRepos(repos);
            FileChange keep = key == null ? null : repos.SelectMany(x => x.Files).FirstOrDefault(f => f.Key == key);
            list.Select(keep, false);
            if (keep != null) ShowFile(keep, true);
            else if (key != null && current != null && !current.FromHistory)
            {
                current = null;
                header.Set(null, null);
                header.SetNote(null);
                diff.SetData(DiffData.Msg(G.Check, "O arquivo não tem mais alterações", "Ele foi commitado ou revertido desde a última atualização."), false);
            }
            lastDone = DateTime.Now;
            UpdateStatus();
            if (pending) { pending = false; DoRefresh(); }
        }

        void UpdateStatus()
        {
            int total = repos.Sum(x => x.Files.Count);
            string s = Plural(repos.Count, "repositório", "repositórios") + "   ·   " +
                       Plural(total, "arquivo alterado", "arquivos alterados");
            if (list.OnlyMine && list.VisibleFiles != total) s += " (" + list.VisibleFiles + " nos seus módulos)";
            s += "   ·   atualizado às " + lastDone.ToString("HH:mm:ss");
            status.LeftText = s;
            status.Invalidate();
        }

        static string Plural(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }

        // ---------------------------------------------------------- diff

        void ShowFile(FileChange f, bool keepScroll)
        {
            int req = ++diffReq;
            RunAsync(() => Git.LoadDiff(f), d =>
            {
                if (req != diffReq) return;
                current = f;
                header.Set(f, d);
                diff.SetData(d, keepScroll);
                if (!f.FromHistory) list.ValidatePartial(f, d);
                UpdateHunkUi();
            });
        }

        // caixas nos trechos do diff + aviso no cabeçalho
        void UpdateHunkUi()
        {
            var d = diff.Data;
            if (current == null || !current.HunkSelectable || d.Title != null || d.HunkCount == 0)
            {
                diff.SetHunks(false, null);
                header.SetNote(null);
                return;
            }
            var st = list.HunkStates(current, d);
            diff.SetHunks(true, st);
            int on = st.Count(x => x == 1);
            if (d.HunkCount < 2) header.SetNote(null);
            else if (on == 0) header.SetNote("clique no @@ de um trecho para commitar só ele");
            else if (on == d.HunkCount) header.SetNote("arquivo inteiro no commit");
            else header.SetNote(on + " de " + d.HunkCount + " trechos no commit");
        }

        // ---------------------------------------------------------- commit

        void UpdateCommitPanel()
        {
            var repo = list.CheckedRepo;
            if (repo == null)
            {
                if (commit.Visible && !commit.Busy) commit.Visible = false;
                return;
            }
            Git.RepoStyle st;
            bool known = styles.TryGetValue(repo.FullPath, out st);
            var files = list.CheckedFiles;
            commit.SetSelection(repo, files, files.Count(f => list.Partial.ContainsKey(f.Key)), st);
            commit.Height = commit.PreferredHeight;
            // não rouba o foco: quem marca arquivos pelo teclado continua navegando na lista
            if (!commit.Visible) commit.Visible = true;
            if (!known)
            {
                styles[repo.FullPath] = null;
                string path = repo.FullPath;
                RunAsync(() => Git.DetectStyle(path), s => { styles[path] = s; commit.SetStyle(Fresh(repo), s); });
            }
        }

        void DoCommit()
        {
            var repo = list.CheckedRepo;
            var files = list.CheckedFiles;
            if (repo == null || files.Count == 0 || opBusy) return;
            if (Fresh(repo).Merging)
            {
                MessageDialog.Confirm(this, "Há um merge em andamento",
                    "Enquanto o merge não for concluído ou cancelado, commits avulsos ficam bloqueados neste repositório para não estragar o merge. Use \"Concluir merge\" na linha do repositório.",
                    null, null, T.Orange);
                return;
            }
            var partial = new Dictionary<FileChange, HashSet<int>>();
            var sigs = new Dictionary<FileChange, string>();
            foreach (var f in files)
            {
                HashSet<int> set;
                if (list.Partial.TryGetValue(f.Key, out set)) { partial[f] = new HashSet<int>(set); sigs[f] = list.PartialSig[f.Key]; }
            }
            string msg = commit.Message;
            if (!StartOp("Fazendo commit em " + repo.Name + "…", repo, "commit", null)) return;
            commit.Busy = true;
            RunAsync(() => Git.Commit(repo, files, partial, sigs, msg), res =>
            {
                EndOp();
                commit.Busy = false;
                if (res.Ok)
                {
                    status.Flash("Commit " + res.Hash + " em " + repo.Name + ":  " + msg.Split('\n')[0], true, "Desfazer", () => DoUndo(Fresh(repo)));
                    commit.ResetMessage();
                    styles.Remove(repo.FullPath);
                    list.ClearChecks();
                    if (history.Visible && history.Repo != null && history.Repo.FullPath == repo.FullPath) history.Reload();
                }
                else
                {
                    status.Invalidate();
                    MessageDialog.Error(this, "Não foi possível fazer o commit", res.Output);
                }
                DoRefresh();
            }, () => { EndOp(); commit.Busy = false; });
        }

        // ---------------------------------------------------------- ações do repositório

        void OnRepoAction(RepoInfo repo, ModuleGroup module, string action)
        {
            switch (action)
            {
                case "history": OpenHistory(repo, module != null ? module.Path : null); break;
                case "pull": DoPull(repo, false); break;
                case "push": DoPush(repo, false); break;
                case "undo": DoUndo(repo); break;
                case "mergedone": DoFinishMerge(repo); break;
                case "abortmerge": DoAbortMerge(repo); break;
                case "vscode":
                    if (!OpenMenu.OpenInVsCode(repo.FullPath)) status.Flash("VS Code não encontrado. Abra a pasta " + repo.FullPath + " no seu editor.", false, null, null);
                    break;
            }
        }

        void OpenHistory(RepoInfo repo, string path)
        {
            history.Visible = true;
            history.Open(repo, path);
        }

        void CloseHistory()
        {
            if (!history.Visible) return;
            history.Visible = false;
            if (current != null && current.FromHistory)
            {
                if (list.Selected != null) ShowFile(list.Selected, false);
                else ShowNothing();
            }
        }

        // uma operação de escrita por vez; a pílula da ação mostra o spinner com "label"
        bool StartOp(string text, RepoInfo repo, string action, string label)
        {
            if (opBusy) { status.Flash("Espere a operação em andamento terminar.", false, null, null); return false; }
            opBusy = true;
            status.Working = text;
            status.Invalidate();
            list.SetBusy(repo.FullPath, action, label);
            return true;
        }

        void EndOp()
        {
            opBusy = false;
            status.Working = null;
            status.Invalidate();
            list.SetBusy(null, null, null);
        }

        void DoUndo(RepoInfo repo)
        {
            if (!StartOp("Verificando o último commit de " + repo.Name + "…", repo, "undo", null)) return;
            string summary = null;
            RunAsync(() => { string s; string why = Git.CheckUndo(repo, out s); summary = s; return why; }, why =>
            {
                EndOp();
                if (why != null) { MessageDialog.Confirm(this, "Não dá para desfazer este commit", why, null, null, T.Orange); return; }
                bool ok = MessageDialog.Confirm(this, "Desfazer o último commit?",
                    "O commit sai do histórico de " + repo.Name + " e as alterações dele voltam para a lista, prontas para um novo commit. Nada é perdido.",
                    summary, "Desfazer commit", T.Orange);
                if (!ok || !StartOp("Desfazendo o último commit…", repo, "undo", null)) return;
                RunAsync(() => Git.UndoLastCommit(repo), res =>
                {
                    EndOp();
                    if (res.Ok) status.Flash("Último commit de " + repo.Name + " desfeito. As alterações voltaram para a lista.", true, null, null);
                    else MessageDialog.Error(this, "Não foi possível desfazer o commit", res.Output);
                    if (history.Visible) history.Reload();
                    DoRefresh();
                }, EndOp);
            }, EndOp);
        }

        void DoPush(RepoInfo repo, bool afterPull)
        {
            if (repo.Merging) { status.Flash("Conclua ou cancele o merge de " + repo.Name + " antes de enviar.", false, null, null); return; }
            if (!afterPull && repo.Behind > 0 && repo.Upstream != null)
            {
                bool go = MessageDialog.Confirm(this, "Traga as novidades antes de enviar",
                    "O remoto tem " + repo.Behind + (repo.Behind == 1 ? " commit" : " commits") + " que você ainda não tem, então o push seria recusado. " +
                    "O app traz essas alterações primeiro (juntando com um merge, se precisar) e depois envia os seus commits.",
                    null, "Atualizar e enviar", T.Cyan);
                if (go) DoPull(repo, true);
                return;
            }
            string sending = repo.Upstream == null ? "Publicando…" : "Enviando…";
            if (!StartOp("Preparando o envio de " + repo.Name + "…", repo, "push", "Preparando…")) return;
            RunAsync(() => Git.OutgoingSummary(repo), outgoing =>
            {
                EndOp();
                if (outgoing.Length == 0 && repo.Upstream != null) { status.Flash("Nada para enviar em " + repo.Name + ".", true, null, null); return; }
                string msg = repo.Upstream != null
                    ? "Os commits abaixo vão de " + repo.Branch + " para " + repo.Upstream + "."
                    : "O branch " + repo.Branch + " ainda não existe no remoto e será publicado.";
                if (!MessageDialog.Confirm(this, "Enviar para o remoto (push)?", msg, outgoing, "Enviar (push)", T.Green)) return;
                if (!StartOp("Enviando " + repo.Name + " para o remoto…", repo, "push", sending)) return;
                RunAsync(() => Git.Push(repo), res =>
                {
                    EndOp();
                    if (res.Ok) status.Flash("Push de " + repo.Name + " concluído.", true, null, null);
                    else MessageDialog.Error(this, "O push não foi concluído", res.Output);
                    if (history.Visible) history.Reload();
                    DoRefresh();
                }, EndOp);
            }, EndOp);
        }

        void DoPull(RepoInfo repo, bool thenPush)
        {
            if (repo.Upstream == null) { status.Flash(repo.Name + " não tem branch remoto configurado.", false, null, null); return; }
            if (repo.Merging) { status.Flash("Conclua ou cancele o merge de " + repo.Name + " antes de atualizar.", false, null, null); return; }
            if (!StartOp("Buscando novidades de " + repo.Name + " no remoto…", repo, "pull", "Buscando…")) return;
            string fetchError = null;
            Git.MergeInfo preview = null;
            RunAsync(() =>
            {
                var f = Git.Fetch(repo, false);
                if (!f.Ok) { fetchError = f.Output.Length > 0 ? f.Output : "git fetch falhou sem mensagem."; return null; }
                string incoming = Git.IncomingSummary(repo);
                if (incoming.Length > 0) preview = Git.PreviewMerge(repo);
                return incoming;
            }, incoming =>
            {
                EndOp();
                DoRefresh();
                if (incoming == null) { MessageDialog.Error(this, "Não foi possível buscar do remoto", fetchError); return; }
                if (incoming.Length == 0)
                {
                    if (thenPush) DoPush(repo, true);
                    else status.Flash(repo.Name + " já está atualizado com o remoto.", true, null, null);
                    return;
                }
                int n = incoming.Split('\n').Length;
                if (preview != null && preview.Ahead > 0) { ConfirmMerge(repo, incoming, n, preview, thenPush); return; }

                bool ok = MessageDialog.Confirm(this, "Trazer " + n + (n == 1 ? " commit" : " commits") + " do remoto (pull)?",
                    "O branch " + repo.Branch + " é avançado até " + repo.Upstream + ". Se algum arquivo alterado aqui entrar em conflito, nada é mudado e o app avisa.",
                    incoming, "Atualizar (pull)", T.Cyan);
                if (!ok || !StartOp("Atualizando " + repo.Name + "…", repo, "pull", "Atualizando…")) return;
                RunAsync(() => Git.FastForward(repo), res =>
                {
                    EndOp();
                    if (history.Visible) history.Reload();
                    DoRefresh();
                    if (!res.Ok) { MessageDialog.Error(this, "O pull não foi concluído", res.Output); return; }
                    if (thenPush) DoPush(repo, true);
                    else status.Flash(repo.Name + " atualizado com o remoto.", true, null, null);
                }, EndOp);
            }, EndOp);
        }

        // os dois lados têm commits novos: junta com merge, avisando antes se vai haver conflito
        void ConfirmMerge(RepoInfo repo, string incoming, int n, Git.MergeInfo preview, bool thenPush)
        {
            string msg = "Seu branch tem " + preview.Ahead + (preview.Ahead == 1 ? " commit que ainda não foi enviado" : " commits que ainda não foram enviados") +
                         " e o remoto tem " + n + (n == 1 ? " novo" : " novos") + ". Para seguir, o app junta as duas versões com um merge, como o \"Sync\" do VS Code.";
            if (preview.Conflicts.Count == 0)
                msg += "\n\nNenhum conflito previsto: os dois lados mexeram em arquivos ou trechos diferentes.";
            else
                msg += "\n\nAtenção: " + preview.Conflicts.Count + (preview.Conflicts.Count == 1 ? " arquivo foi alterado" : " arquivos foram alterados") +
                       " dos dois lados e vai ter conflito. Você resolve no VS Code e conclui pelo app:\n" +
                       string.Join("\n", preview.Conflicts.Take(8).Select(x => "   • " + x)) + (preview.Conflicts.Count > 8 ? "\n   …" : "");
            if (!MessageDialog.Confirm(this, "Juntar com o remoto (merge)?", msg, incoming, "Juntar (merge)", T.Cyan)) return;
            if (!StartOp("Juntando " + repo.Name + " com o remoto…", repo, "pull", "Juntando…")) return;
            RunAsync(() => Git.Merge(repo), res =>
            {
                EndOp();
                if (history.Visible) history.Reload();
                DoRefresh();
                if (res.Ok)
                {
                    if (thenPush) DoPush(repo, true);
                    else status.Flash("Merge de " + repo.Name + " concluído. Agora é só enviar.", true, "Enviar", () => DoPush(Fresh(repo), true));
                    return;
                }
                if (res.HasConflicts)
                {
                    bool open = MessageDialog.Confirm(this, "O merge tem conflitos",
                        "Os arquivos marcados com ! na lista foram alterados dos dois lados. Resolva no VS Code (ele mostra as duas versões lado a lado), salve, " +
                        "e depois clique em \"Concluir merge\" na linha do repositório. Se preferir desistir, use \"Cancelar o merge\" no menu ⋯.",
                        res.Output, "Abrir no VS Code", T.Orange, "Agora não");
                    if (open && !OpenMenu.OpenInVsCode(repo.FullPath)) status.Flash("VS Code não encontrado.", false, null, null);
                    return;
                }
                MessageDialog.Error(this, "O merge não foi feito", res.Output);
            }, EndOp);
        }

        void DoFinishMerge(RepoInfo repo)
        {
            var r = Fresh(repo);
            if (!r.Merging) return;
            if (r.Conflicts > 0) { status.Flash("Ainda há " + r.Conflicts + " arquivo(s) em conflito em " + r.Name + ".", false, null, null); return; }
            if (!MessageDialog.Confirm(this, "Concluir o merge?",
                "O commit de merge de " + r.Name + " será criado com a mensagem padrão do Git. Depois é só enviar.", null, "Concluir merge", T.Green)) return;
            if (!StartOp("Concluindo o merge de " + r.Name + "…", r, "mergedone", "Concluindo…")) return;
            RunAsync(() => Git.FinishMerge(r), res =>
            {
                EndOp();
                if (history.Visible) history.Reload();
                DoRefresh();
                if (res.Ok) status.Flash("Merge de " + r.Name + " concluído (" + res.Hash + "). Agora é só enviar.", true, "Enviar", () => DoPush(Fresh(r), true));
                else MessageDialog.Error(this, "Não foi possível concluir o merge", res.Output);
            }, EndOp);
        }

        void DoAbortMerge(RepoInfo repo)
        {
            if (!MessageDialog.Confirm(this, "Cancelar o merge?",
                "O repositório " + repo.Name + " volta ao estado de antes do merge. Seus commits continuam como estavam; só a junção com o remoto é desfeita.",
                null, "Cancelar o merge", T.Orange, "Voltar")) return;
            if (!StartOp("Cancelando o merge de " + repo.Name + "…", repo, "abortmerge", null)) return;
            RunAsync(() => Git.AbortMerge(repo), res =>
            {
                EndOp();
                DoRefresh();
                if (res.Ok) status.Flash("Merge de " + repo.Name + " cancelado. Tudo voltou ao estado anterior.", true, null, null);
                else MessageDialog.Error(this, "Não foi possível cancelar o merge", res.Output);
            }, EndOp);
        }
        // busca silenciosa no remoto de tempos em tempos, para os indicadores ↑/↓ ficarem corretos
        void BackgroundFetch()
        {
            lastFetch = DateTime.Now;
            var targets = repos.Where(r => r.Upstream != null).ToList();
            if (targets.Count == 0) return;
            Task.Factory.StartNew(() => { foreach (var r in targets) try { Git.Fetch(r, true); } catch { } });
        }

        // ---------------------------------------------------------- atualização do app

        void CheckUpdate()
        {
            lastUpdateCheck = DateTime.Now;
            RunAsync(() => Updater.Check(), info =>
            {
                if (info == null || updating) return;
                update = info;
                top.SetUpdate("Nova versão " + Updater.VersionText(info), false);
            });
        }

        void DoUpdate()
        {
            if (update == null || updating) return;
            if (opBusy) { status.Flash("Espere a operação em andamento terminar para atualizar o app.", false, null, null); return; }
            string v = Updater.VersionText(update);
            string msg = "Você está na versão " + Updater.CurrentText + ". O app baixa a versão " + v +
                         " do GitHub, reinicia sozinho e mantém suas preferências.";
            if (commit.Visible && commit.Summary.TextLength > 0)
                msg += "\n\nA mensagem de commit que você começou a digitar será perdida.";
            string notes = update.Notes.Trim();
            if (!MessageDialog.Confirm(this, "Atualizar para a versão " + v + "?", msg, notes.Length > 0 ? notes : null, "Atualizar agora", T.Green)) return;

            updating = true;
            top.SetUpdate("Baixando…", true);
            var info = update;
            string error = null;
            RunAsync(() =>
            {
                try
                {
                    return Updater.Download(info, p =>
                    {
                        try { BeginInvoke((Action)(() => top.SetUpdate("Baixando… " + p + "%", true))); } catch { }
                    });
                }
                catch (Exception ex) { error = ex.Message; return null; }
            }, file =>
            {
                if (file == null) { UpdateFailed("Não foi possível baixar a atualização", error); return; }
                top.SetUpdate("Reiniciando…", true);
                try { Updater.Swap(file); }
                catch (UnauthorizedAccessException)
                {
                    UpdateFailed("Sem permissão para atualizar nesta pasta",
                        "O Windows não deixou substituir o GitPainel.exe aqui. Baixe a versão nova pela página da release: " + (update.Page ?? ""));
                    return;
                }
                catch (Exception ex) { UpdateFailed("Não foi possível instalar a atualização", ex.Message); return; }
                restartAfterClose = true;
                Close();
            }, () => UpdateFailed("Não foi possível atualizar", error));
        }

        void UpdateFailed(string title, string detail)
        {
            updating = false;
            top.SetUpdate("Nova versão " + Updater.VersionText(update), false);
            MessageDialog.Error(this, title, detail ?? "Erro desconhecido.");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // abre a versão nova só depois que esta salvou as preferências
            if (restartAfterClose) try { Updater.Restart(); } catch { }
        }

        // ---------------------------------------------------------- ini

        void LoadIni()
        {
            try
            {
                if (!File.Exists(iniPath)) return;
                foreach (var line in File.ReadAllLines(iniPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) ini[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
        }

        string Ini(string k) { string v; return ini.TryGetValue(k, out v) && v.Length > 0 ? v : null; }
        int IniInt(string k, int def) { int v; return int.TryParse(Ini(k), out v) ? v : def; }

        void RestoreWindow()
        {
            var p = (Ini("janela") ?? "").Split(',');
            if (p.Length < 5) return;
            int x, y, w, h;
            if (!int.TryParse(p[0], out x) || !int.TryParse(p[1], out y) || !int.TryParse(p[2], out w) || !int.TryParse(p[3], out h)) return;
            var rect = new Rectangle(x, y, w, h);
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = rect;
            if (p[4] == "1") WindowState = FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (watcher != null) watcher.EnableRaisingEvents = false;
            SaveIni();
        }

        void SaveIni()
        {
            if (testMode) return;
            try
            {
                var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                ini["pasta"] = root ?? "";
                ini["lateral"] = ((int)Math.Round(leftPane.Width / T.S)).ToString();
                ini["modo"] = header.Mode.Selected == 1 ? "lado" : "unificado";
                ini["quebra"] = header.Wrap.On ? "1" : "0";
                ini["filtro"] = top.Filter.Selected == 1 ? "meus" : "todos";
                ini["meus"] = string.Join("|", list.Stars.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
                ini["janela"] = b.X + "," + b.Y + "," + b.Width + "," + b.Height + "," + (WindowState == FormWindowState.Maximized ? "1" : "0");
                File.WriteAllLines(iniPath, ini.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------------------------------------------------- atualização automática

        FileSystemWatcher watcher;
        long fsEvent, fsHandled;

        void SetupWatcher()
        {
            if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
            if (root == null || !Directory.Exists(root)) return;
            try
            {
                watcher = new FileSystemWatcher(root);
                watcher.IncludeSubdirectories = true;
                watcher.InternalBufferSize = 64 * 1024;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                FileSystemEventHandler h = (s, e) => OnFsEvent(e.FullPath);
                watcher.Changed += h;
                watcher.Created += h;
                watcher.Deleted += h;
                watcher.Renamed += (s, e) => { OnFsEvent(e.FullPath); OnFsEvent(e.OldFullPath); };
                watcher.Error += (s, e) => OnFsEvent(null);
                watcher.EnableRaisingEvents = true;
            }
            catch { watcher = null; }
        }

        // roda na thread do watcher: só marca o horário; o timer da UI decide quando atualizar
        void OnFsEvent(string path)
        {
            if (path != null && !Relevant(path)) return;
            System.Threading.Interlocked.Exchange(ref fsEvent, DateTime.UtcNow.Ticks);
        }

        static bool Relevant(string path)
        {
            string s = path.ToLowerInvariant();
            string[] noise = { "\\node_modules\\", "\\.next\\", "\\.turbo\\", "\\__pycache__\\", "\\.serverless\\", "\\.expo\\", "\\.venv\\", "\\.pytest_cache\\" };
            foreach (var n in noise) if (s.Contains(n)) return false;
            if (s.EndsWith("gitpainel.ini")) return false;
            int gi = s.IndexOf("\\.git\\");
            if (gi >= 0)
            {
                // dentro do .git só interessa o que muda o status: index, HEAD e refs (commit, checkout, stage, fetch)
                string rest = s.Substring(gi + 6);
                return rest == "index" || rest == "head" || rest.StartsWith("refs\\");
            }
            return !s.EndsWith("\\.git");
        }

        void PollChanges()
        {
            long ev = System.Threading.Interlocked.Read(ref fsEvent);
            if (ev > fsHandled && DateTime.UtcNow.Ticks - ev > TimeSpan.FromMilliseconds(700).Ticks)
            {
                fsHandled = ev;
                DoRefresh();
            }
            // rede de segurança caso algum evento se perca
            else if (WindowState != FormWindowState.Minimized && (DateTime.Now - lastRefresh).TotalSeconds > 30)
                DoRefresh();
            if ((DateTime.Now - lastFetch).TotalMinutes >= 10 && repos.Count > 0) BackgroundFetch();
            if ((DateTime.Now - lastUpdateCheck).TotalHours >= 6 && !updating) CheckUpdate();
        }
    }
}
