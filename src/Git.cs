using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GitPainel
{
    // ------------------------------------------------------------------ modelo

    class RepoInfo
    {
        public string FullPath, Name, Branch = "", Error, Upstream;
        public int Ahead, Behind;
        public bool HasHead, Expanded, HasRemote;
        public List<FileChange> Files = new List<FileChange>();
        public List<ModuleGroup> Modules = new List<ModuleGroup>();

        // repositório sem subprojetos: os arquivos aparecem direto, sem nível de módulo
        public bool Flat { get { return Modules.Count == 0 || (Modules.Count == 1 && Modules[0].Path == ""); } }
        // último commit ainda não enviado ao remoto (ou branch sem upstream)
        public bool MaybeLocalHead { get { return HasHead && (Ahead > 0 || Upstream == null); } }
    }

    // subprojeto dentro do repositório (pasta com package.json, serverless.yml, requirements.txt…)
    class ModuleGroup
    {
        public RepoInfo Repo;
        public string Path;
        public List<FileChange> Files = new List<FileChange>();
        public bool Expanded, Starred;

        public string Key { get { return Repo.Name + ":" + Path; } }
    }

    class FileChange
    {
        public RepoInfo Repo;
        public ModuleGroup Module;
        public string Rel, Orig;
        public char Kind;
        public bool Staged, Untracked;
        // preenchidos só para arquivos de um commit do histórico
        public string Hash, Parent, Short, Subject;

        public bool FromHistory { get { return Hash != null; } }
        public string Key { get { return Repo.FullPath + "|" + Rel; } }
        string Clean { get { return Rel.TrimEnd('/'); } }
        public string Name { get { int i = Clean.LastIndexOf('/'); return i < 0 ? Clean : Clean.Substring(i + 1); } }
        public string Folder { get { int i = Clean.LastIndexOf('/'); return i < 0 ? "" : Clean.Substring(0, i); } }

        // pasta relativa ao módulo, para não repetir "apps/cadastro/" em todas as linhas
        public string LocalFolder
        {
            get
            {
                string f = Folder;
                if (Module == null || Module.Path.Length == 0) return f;
                return f.Length > Module.Path.Length ? f.Substring(Module.Path.Length + 1) : "";
            }
        }

        // trechos só fazem sentido para arquivo modificado (sem renomear) com diff de texto
        public bool HunkSelectable { get { return !FromHistory && Kind == 'M' && Orig == null && !Untracked && Repo.HasHead; } }
    }

    class DiffLine
    {
        public const byte Ctx = 0, Add = 1, Del = 2, Hunk = 3, Meta = 4;
        public byte K;
        public int Old, New, HunkIdx = -1;
        public string Text, Extra;
        public int[] Wrap;               // inícios de cada pedaço quando a quebra de linha está ligada
        public int ChgS = -1, ChgE = -1; // trecho que mudou dentro da linha (realce de palavras)
        public List<int> Tok;            // cores de sintaxe: trincas (início, tamanho, tipo)
    }

    class DiffData
    {
        public List<DiffLine> Lines = new List<DiffLine>();
        public int Adds, Dels, MaxLen, MaxNo;
        public string Glyph, Title, Sub, Lang;
        // texto bruto do patch, para montar commits parciais
        public string Header = "";
        public List<string> HunkRaw = new List<string>();
        public int HunkCount { get { return HunkRaw.Count; } }
        public string Signature { get { return string.Join("\n", Lines.Where(l => l.K == DiffLine.Hunk).Select(l => l.Text)); } }

        public static DiffData Msg(string glyph, string title, string sub)
        {
            var d = new DiffData();
            d.Glyph = glyph; d.Title = title; d.Sub = sub;
            return d;
        }
    }

    class CommitInfo
    {
        public string Hash, Short, Author, When, Subject, Parent;
        public bool Local, Expanded, Loading;
        public List<FileChange> Files;
    }

    class GitResult
    {
        public bool Ok;
        public string Output = "", Hash = "";
        public static GitResult Fail(string msg) { return new GitResult { Output = msg }; }
    }

    // ------------------------------------------------------------------ git

    static class Git
    {
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "node_modules", "bin", "obj", "dist", "__pycache__", "venv", "packages" };
        static readonly Regex HunkRx = new Regex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@ ?(.*)$", RegexOptions.Compiled);

        public static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        public static string Run(string cwd, string args, out int code, out string err)
        {
            return Run(cwd, args, out code, out err, null, null);
        }

        public static string Run(string cwd, string args, out int code, out string err, Dictionary<string, string> env, string stdin)
        {
            var psi = new ProcessStartInfo("git", "-c core.quotepath=off -c color.ui=never " + args);
            psi.WorkingDirectory = cwd;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true; // hooks que tentarem ler do teclado recebem EOF em vez de travar
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            // não disputa o index.lock com outros processos git nas leituras
            psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            using (var p = Process.Start(psi))
            {
                if (stdin != null)
                {
                    var bytes = new UTF8Encoding(false).GetBytes(stdin);
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                }
                p.StandardInput.Close();
                var errTask = p.StandardError.ReadToEndAsync();
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                code = p.ExitCode;
                err = errTask.Result;
                return o;
            }
        }

        static string Out(string cwd, string args)
        {
            int code; string err;
            try { string o = Run(cwd, args, out code, out err); return code == 0 ? o : null; }
            catch { return null; }
        }

        static string FirstLine(string s)
        {
            s = (s ?? "").Trim();
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i).Trim();
        }

        static void WriteList(string file, IEnumerable<string> paths)
        {
            File.WriteAllText(file, string.Join("\0", paths) + "\0", new UTF8Encoding(false));
        }

        // ---------------------------------------------------------- descoberta e status

        static bool IsRepo(string d)
        {
            string g = Path.Combine(d, ".git");
            return Directory.Exists(g) || File.Exists(g);
        }

        public static List<string> FindRepos(string root)
        {
            var list = new List<string>();
            if (IsRepo(root)) list.Add(root);
            Walk(root, 1, list);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        static void Walk(string dir, int depth, List<string> list)
        {
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var s in subs)
            {
                string n = Path.GetFileName(s);
                if (n.StartsWith(".") || Skip.Contains(n)) continue;
                if (IsRepo(s)) { list.Add(s); continue; }
                if (depth < 3) Walk(s, depth + 1, list);
            }
        }

        public static RepoInfo LoadRepo(string root, string dir)
        {
            var r = new RepoInfo();
            r.FullPath = dir;
            string rt = root.TrimEnd('\\');
            r.Name = string.Equals(dir.TrimEnd('\\'), rt, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(rt)
                : dir.Substring(rt.Length + 1).Replace('\\', '/');

            int code; string err, o;
            try { o = Run(dir, "status --porcelain=v2 --branch -z --untracked-files=all", out code, out err); }
            catch (Exception ex) { r.Error = "git não encontrado: " + ex.Message; return r; }
            if (code != 0) { r.Error = FirstLine(err); return r; }

            string head = "", oid = "";
            string[] recs = o.Split('\0');
            for (int i = 0; i < recs.Length; i++)
            {
                string s = recs[i];
                if (s.Length < 2) continue;
                if (s.StartsWith("# "))
                {
                    if (s.StartsWith("# branch.oid ")) { oid = s.Substring(13); r.HasHead = oid != "(initial)"; }
                    else if (s.StartsWith("# branch.head ")) head = s.Substring(14);
                    else if (s.StartsWith("# branch.upstream ")) r.Upstream = s.Substring(18);
                    else if (s.StartsWith("# branch.ab "))
                    {
                        var p = s.Substring(12).Split(' ');
                        if (p.Length == 2)
                        {
                            int.TryParse(p[0].TrimStart('+'), out r.Ahead);
                            int.TryParse(p[1].TrimStart('-'), out r.Behind);
                        }
                    }
                    continue;
                }
                var f = new FileChange();
                f.Repo = r;
                char t = s[0];
                if (t == '?') { f.Rel = s.Substring(2); f.Untracked = true; f.Kind = 'U'; }
                else if (t == '1') { var p = s.Split(new[] { ' ' }, 9); if (p.Length < 9) continue; f.Rel = p[8]; SetKind(f, p[1]); }
                else if (t == '2')
                {
                    var p = s.Split(new[] { ' ' }, 10); if (p.Length < 10) continue;
                    f.Rel = p[9]; SetKind(f, p[1]);
                    if (i + 1 < recs.Length) f.Orig = recs[++i];
                }
                else if (t == 'u') { var p = s.Split(new[] { ' ' }, 11); if (p.Length < 11) continue; f.Rel = p[10]; f.Kind = '!'; }
                else continue;
                r.Files.Add(f);
            }

            r.HasRemote = HasRemoteConfig(dir);
            if (head == "(detached)") r.Branch = "HEAD " + (oid.Length >= 7 ? oid.Substring(0, 7) : oid);
            else r.Branch = head;
            r.Files.Sort((a, b) => string.Compare(a.Rel, b.Rel, StringComparison.OrdinalIgnoreCase));
            GroupModules(r);
            return r;
        }

        // lê o .git/config direto (sem processo extra); em worktree/submódulo assume que tem remoto
        static bool HasRemoteConfig(string dir)
        {
            try
            {
                string cfg = Path.Combine(dir, ".git", "config");
                if (!File.Exists(cfg)) return true;
                return File.ReadAllText(cfg).Contains("[remote \"");
            }
            catch { return true; }
        }

        static void SetKind(FileChange f, string xy)
        {
            char x = xy[0], y = xy[1];
            f.Staged = x != '.';
            if (x == 'A') { f.Kind = 'A'; return; }
            if (x == 'R' || x == 'C') { f.Kind = 'R'; return; }
            char c = y != '.' ? y : x;
            f.Kind = c == 'D' ? 'D' : c == 'A' ? 'A' : (c == 'R' || c == 'C') ? 'R' : 'M';
        }

        static readonly string[] Markers =
        {
            "package.json", "serverless.yml", "serverless.yaml", "template.yaml", "template.yml", "pyproject.toml",
            "requirements.txt", "go.mod", "Cargo.toml", "pom.xml", "build.gradle", "composer.json"
        };

        // agrupa cada arquivo no subprojeto mais externo abaixo da raiz (ex.: apps/cadastro)
        static void GroupModules(RepoInfo r)
        {
            var cache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, ModuleGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in r.Files)
            {
                string m = ModuleOf(r.FullPath, f.Rel.TrimEnd('/'), cache);
                ModuleGroup g;
                if (!map.TryGetValue(m, out g)) { g = new ModuleGroup { Repo = r, Path = m }; map[m] = g; }
                g.Files.Add(f);
                f.Module = g;
            }
            r.Modules = map.Values
                .OrderBy(g => g.Path.Length == 0 ? 1 : 0)
                .ThenBy(g => g.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string ModuleOf(string root, string rel, Dictionary<string, bool> cache)
        {
            string[] parts = rel.Split('/');
            string acc = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                acc = acc.Length == 0 ? parts[i] : acc + "/" + parts[i];
                if (IsModuleDir(root, acc, cache)) return acc;
            }
            return "";
        }

        static bool IsModuleDir(string root, string dir, Dictionary<string, bool> cache)
        {
            bool v;
            if (cache.TryGetValue(dir, out v)) return v;
            v = false;
            try
            {
                string full = Path.Combine(root, dir.Replace('/', '\\'));
                if (Directory.Exists(full))
                {
                    foreach (var m in Markers)
                        if (File.Exists(Path.Combine(full, m))) { v = true; break; }
                    if (!v) v = Directory.EnumerateFiles(full, "*.csproj").Any();
                }
            }
            catch { }
            cache[dir] = v;
            return v;
        }

        // ---------------------------------------------------------- diff

        public static DiffData LoadDiff(FileChange f)
        {
            if (f.FromHistory) return CommitDiff(f);
            var repo = f.Repo;
            string full = Path.Combine(repo.FullPath, f.Rel.TrimEnd('/').Replace('/', '\\'));
            string args;
            if (f.Untracked)
            {
                if (f.Rel.EndsWith("/") || Directory.Exists(full))
                    return DiffData.Msg(G.Folder, "Pasta não rastreada", "Provavelmente é outro repositório dentro deste.");
                try
                {
                    if (new FileInfo(full).Length > 2 * 1024 * 1024)
                        return DiffData.Msg(G.Info, "Arquivo grande demais", "O arquivo tem mais de 2 MB, então o diff não é exibido.");
                }
                catch { }
                args = "diff --no-index --no-color --no-ext-diff -- /dev/null " + Q(f.Rel);
            }
            else if (repo.HasHead)
                // --literal-pathspecs: caminhos como app/[id]/page.tsx não viram curinga
                args = "--literal-pathspecs diff --no-color --no-ext-diff -M HEAD -- " + Q(f.Rel) + (f.Orig != null ? " " + Q(f.Orig) : "");
            else
                args = "--literal-pathspecs diff --no-color --no-ext-diff --cached -- " + Q(f.Rel);
            return RunDiff(repo.FullPath, args, f.Rel);
        }

        static DiffData CommitDiff(FileChange f)
        {
            string paths = Q(f.Rel) + (f.Orig != null ? " " + Q(f.Orig) : "");
            string args = f.Parent != null
                ? "--literal-pathspecs diff --no-color --no-ext-diff -M " + f.Parent + " " + f.Hash + " -- " + paths
                : "--literal-pathspecs show --format= --no-color --no-ext-diff -M " + f.Hash + " -- " + paths;
            return RunDiff(f.Repo.FullPath, args, f.Rel);
        }

        static DiffData RunDiff(string cwd, string args, string rel)
        {
            int code; string err, o;
            try { o = Run(cwd, args, out code, out err); }
            catch (Exception ex) { return DiffData.Msg(G.Warn, "Não foi possível executar o git", ex.Message); }
            if (code > 1) return DiffData.Msg(G.Warn, "Não foi possível gerar o diff", FirstLine(err));
            if (o.Length > 8000000) return DiffData.Msg(G.Info, "Diff grande demais", "A diferença é muito extensa para exibir aqui.");
            if (o.StartsWith("Binary files ") || o.Contains("\nBinary files ") || o.Contains("GIT binary patch"))
                return DiffData.Msg(G.Doc, "Arquivo binário", "Não há diff de texto para mostrar.");
            var d = Parse(o);
            if (d.Lines.Count == 0)
                return DiffData.Msg(G.Check, "Sem diferenças de conteúdo", "A mudança é só de permissão, fim de linha ou o arquivo está vazio.");
            d.Lang = Syntax.LangOf(rel);
            return d;
        }

        public static DiffData Parse(string o)
        {
            var d = new DiffData();
            bool inHunk = false, headerDone = false;
            int oldNo = 0, newNo = 0, hunk = -1;
            var header = new StringBuilder();
            StringBuilder raw = null;
            foreach (var line in o.Split('\n'))
            {
                string s = line.EndsWith("\r") ? line.Substring(0, line.Length - 1) : line;
                if (s.StartsWith("diff --git "))
                {
                    inHunk = false;
                    if (raw != null) { d.HunkRaw.Add(raw.ToString()); raw = null; }
                    if (hunk >= 0) headerDone = true; // só o cabeçalho do primeiro arquivo interessa
                    if (!headerDone) header.Append(line).Append('\n');
                    continue;
                }
                if (s.StartsWith("@@"))
                {
                    var m = HunkRx.Match(s);
                    if (m.Success)
                    {
                        if (raw != null) d.HunkRaw.Add(raw.ToString());
                        raw = new StringBuilder();
                        raw.Append(line).Append('\n');
                        headerDone = true;
                        hunk++;
                        oldNo = int.Parse(m.Groups[1].Value);
                        newNo = int.Parse(m.Groups[2].Value);
                        int close = s.IndexOf("@@", 2);
                        d.Lines.Add(new DiffLine { K = DiffLine.Hunk, HunkIdx = hunk, Text = s.Substring(0, close + 2), Extra = Tabs(m.Groups[3].Value) });
                        inHunk = true;
                        continue;
                    }
                }
                if (!inHunk)
                {
                    if (!headerDone && s.Length > 0) header.Append(line).Append('\n');
                    continue;
                }
                if (s.Length == 0) continue;

                char c = s[0];
                var l = new DiffLine { HunkIdx = hunk, Text = Tabs(s.Substring(1)) };
                if (c == '+') { l.K = DiffLine.Add; l.New = newNo++; d.Adds++; }
                else if (c == '-') { l.K = DiffLine.Del; l.Old = oldNo++; d.Dels++; }
                else if (c == ' ') { l.K = DiffLine.Ctx; l.Old = oldNo++; l.New = newNo++; }
                else if (c == '\\') { l.K = DiffLine.Meta; l.Text = "Sem quebra de linha no fim do arquivo"; }
                else { inHunk = false; continue; }
                raw.Append(line).Append('\n');
                d.Lines.Add(l);
                if (l.Text.Length > d.MaxLen) d.MaxLen = l.Text.Length;
            }
            if (raw != null) d.HunkRaw.Add(raw.ToString());
            d.Header = header.ToString();
            d.MaxNo = Math.Max(oldNo, newNo);
            return d;
        }

        static string Tabs(string s)
        {
            if (s.IndexOf('\t') < 0) return s;
            var sb = new StringBuilder(s.Length + 16);
            foreach (char ch in s)
            {
                if (ch == '\t') sb.Append(' ', 4 - sb.Length % 4);
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------- commit

        // Commit usando um índice temporário: parte do HEAD, recebe só os arquivos (ou trechos)
        // escolhidos e vira o commit. O stage do usuário não é usado; no fim, só as entradas dos
        // caminhos commitados são alinhadas ao novo HEAD.
        public static GitResult Commit(RepoInfo repo, List<FileChange> files, Dictionary<FileChange, HashSet<int>> partial,
                                       Dictionary<FileChange, string> signatures, string message)
        {
            string dir = repo.FullPath;
            string index = Path.Combine(Path.GetTempPath(), "gitpainel-" + Guid.NewGuid().ToString("N") + ".index");
            string listFile = Path.GetTempFileName(), msgFile = Path.GetTempFileName();
            var env = new Dictionary<string, string> { { "GIT_INDEX_FILE", index } };
            int code; string err, o;
            try
            {
                File.WriteAllText(msgFile, message.Replace("\r\n", "\n").Trim() + "\n", new UTF8Encoding(false));
                if (repo.HasHead)
                {
                    o = Run(dir, "read-tree HEAD", out code, out err, env, null);
                    if (code != 0) return GitResult.Fail((o + "\n" + err).Trim());
                }

                var whole = files.Where(f => !partial.ContainsKey(f)).ToList();
                if (whole.Count > 0)
                {
                    var paths = new List<string>();
                    foreach (var f in whole) { paths.Add(f.Rel); if (f.Orig != null) paths.Add(f.Orig); }
                    WriteList(listFile, paths);
                    o = Run(dir, "--literal-pathspecs add -A --pathspec-from-file=" + Q(listFile) + " --pathspec-file-nul", out code, out err, env, null);
                    if (code != 0) return GitResult.Fail((o + "\n" + err).Trim());
                }

                foreach (var kv in partial)
                {
                    // diff recalculado agora: se o arquivo mudou desde que os trechos foram marcados, aborta
                    o = Run(dir, "--literal-pathspecs diff --no-color --no-ext-diff HEAD -- " + Q(kv.Key.Rel), out code, out err);
                    if (code != 0) return GitResult.Fail((o + "\n" + err).Trim());
                    var d = Parse(o);
                    string sig;
                    if (signatures.TryGetValue(kv.Key, out sig) && sig != d.Signature)
                        return GitResult.Fail("O arquivo " + kv.Key.Rel + " mudou depois que os trechos foram marcados.\nAbra o arquivo de novo e revise os trechos antes de commitar.");
                    var patch = new StringBuilder(d.Header);
                    foreach (int i in kv.Value.OrderBy(i => i))
                        if (i < d.HunkRaw.Count) patch.Append(d.HunkRaw[i]);
                    o = Run(dir, "apply --cached --whitespace=nowarn -", out code, out err, env, patch.ToString());
                    if (code != 0) return GitResult.Fail("Não foi possível aplicar os trechos de " + kv.Key.Rel + ":\n" + (o + "\n" + err).Trim());
                }

                o = Run(dir, "commit --cleanup=whitespace -F " + Q(msgFile), out code, out err, env, null);
                string output = (o + "\n" + err).Trim();
                if (code != 0) return GitResult.Fail(output);

                // alinha o stage real dos caminhos commitados ao novo HEAD
                var all = new List<string>();
                foreach (var f in files) { all.Add(f.Rel); if (f.Orig != null) all.Add(f.Orig); }
                WriteList(listFile, all);
                Run(dir, "--literal-pathspecs reset -q --pathspec-from-file=" + Q(listFile) + " --pathspec-file-nul", out code, out err);

                return new GitResult { Ok = true, Output = output, Hash = (Out(dir, "rev-parse --short HEAD") ?? "").Trim() };
            }
            catch (Exception ex) { return GitResult.Fail(ex.Message); }
            finally
            {
                foreach (var p in new[] { listFile, msgFile, index, index + ".lock" })
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
        }

        public class RepoStyle { public bool Conventional, Emoji; public List<string> Scopes = new List<string>(); }

        static readonly Regex ConvRx = new Regex(
            @"^(?:(\S+)\s+)?(feat|fix|refactor|style|docs|test|chore|perf|build|ci|revert)(?:\(([^)]+)\))?!?:\s",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // lê os últimos commits para descobrir o padrão de mensagem que o repositório usa
        public static RepoStyle DetectStyle(string dir)
        {
            var s = new RepoStyle();
            string o = Out(dir, "log -40 --no-merges --format=%s");
            if (o == null) return s;
            var lines = o.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            int conv = 0, emoji = 0;
            foreach (var l in lines)
            {
                var m = ConvRx.Match(l);
                if (!m.Success) continue;
                conv++;
                if (m.Groups[1].Success) emoji++;
                if (m.Groups[3].Success)
                {
                    string sc = m.Groups[3].Value.Trim();
                    if (sc.Length > 0 && !s.Scopes.Contains(sc, StringComparer.OrdinalIgnoreCase)) s.Scopes.Add(sc);
                }
            }
            s.Conventional = lines.Count > 0 && conv * 100 / lines.Count >= 30;
            s.Emoji = conv > 0 && emoji * 2 > conv;
            return s;
        }

        // ---------------------------------------------------------- histórico

        public static List<CommitInfo> Log(RepoInfo repo, string path, int skip, int count)
        {
            var list = new List<CommitInfo>();
            if (!repo.HasHead) return list;
            string args = "--literal-pathspecs log --format=%H%x1f%h%x1f%an%x1f%ar%x1f%P%x1f%s%x1e -n " + count + " --skip=" + skip;
            if (!string.IsNullOrEmpty(path)) args += " -- " + Q(path);
            string o = Out(repo.FullPath, args);
            if (o == null) return list;

            // commits que ainda não estão em nenhum remoto
            var local = new HashSet<string>();
            string lo = Out(repo.FullPath, "rev-list HEAD --not --remotes --max-count=500");
            if (lo != null) foreach (var h in lo.Split('\n')) if (h.Trim().Length > 0) local.Add(h.Trim());

            foreach (var rec in o.Split('\x1e'))
            {
                var p = rec.Trim('\n', '\r').Split('\x1f');
                if (p.Length < 6) continue;
                var parents = p[4].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                list.Add(new CommitInfo
                {
                    Hash = p[0], Short = p[1], Author = p[2], When = p[3], Subject = p[5],
                    Parent = parents.Length > 0 ? parents[0] : null, Local = local.Contains(p[0])
                });
            }
            return list;
        }

        public static List<FileChange> CommitFiles(RepoInfo repo, CommitInfo c)
        {
            var list = new List<FileChange>();
            string args = c.Parent != null
                ? "diff-tree -r -M --name-status -z " + c.Parent + " " + c.Hash
                : "diff-tree -r -M --name-status -z --root " + c.Hash;
            string o = Out(repo.FullPath, args);
            if (o == null) return list;
            var p = o.Split('\0');
            int i = 0;
            // com --root o primeiro campo pode ser o próprio hash
            if (p.Length > 0 && p[0].Length == 40 && !p[0].Contains(" ")) i = 1;
            while (i < p.Length)
            {
                string st = p[i];
                if (st.Length == 0) { i++; continue; }
                var f = new FileChange { Repo = repo, Hash = c.Hash, Parent = c.Parent, Short = c.Short, Subject = c.Subject };
                char k = st[0];
                if ((k == 'R' || k == 'C') && i + 2 < p.Length) { f.Orig = p[i + 1]; f.Rel = p[i + 2]; f.Kind = k == 'R' ? 'R' : 'A'; if (k == 'C') f.Orig = null; i += 3; }
                else if (i + 1 < p.Length) { f.Rel = p[i + 1]; f.Kind = k == 'A' ? 'A' : k == 'D' ? 'D' : 'M'; i += 2; }
                else break;
                list.Add(f);
            }
            list.Sort((a, b) => string.Compare(a.Rel, b.Rel, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // traduz as falhas mais comuns do git para uma explicação em português (null = sem tradução)
        public static string Explain(string output)
        {
            string o = (output ?? "").ToLowerInvariant();
            if (o.Contains("non-fast-forward") || o.Contains("fetch first") || (o.Contains("[rejected]") && o.Contains("push")))
                return "O remoto tem commits que você ainda não tem. Use \"Atualizar\" (pull) primeiro e depois envie de novo.";
            if (o.Contains("not possible to fast-forward") || o.Contains("diverging") || o.Contains("have diverged"))
                return "Seu branch e o remoto divergiram: os dois têm commits novos. Junte as versões pelo VS Code ou terminal (merge ou rebase) e depois envie.";
            if (o.Contains("would be overwritten"))
                return "Há alterações locais em arquivos que o pull precisa atualizar. Faça commit delas (ou guarde com stash) e tente de novo.";
            if (o.Contains("authentication failed") || o.Contains("could not read username") || o.Contains("permission denied (publickey") || o.Contains("error: 403") || o.Contains("403 forbidden"))
                return "O remoto recusou o acesso. Verifique se suas credenciais do Git estão válidas (Gerenciador de Credenciais do Windows).";
            if (o.Contains("could not resolve host") || o.Contains("unable to access") || o.Contains("timed out") || o.Contains("connection"))
                return "Não foi possível conectar ao remoto. Verifique a internet ou a VPN e tente de novo.";
            if (o.Contains("please tell me who you are") || o.Contains("user.email"))
                return "O Git não sabe quem é o autor. Configure com: git config --global user.name \"Seu Nome\" e git config --global user.email \"voce@empresa.com\".";
            if (o.Contains("index.lock"))
                return "Outro programa (VS Code, terminal…) está usando o Git neste repositório agora. Espere ele terminar e tente de novo.";
            if (o.Contains("hook") || o.Contains("husky") || o.Contains("lint-staged"))
                return "Um hook do repositório (lint, testes…) bloqueou a operação. Veja a saída abaixo, corrija e tente de novo.";
            if (o.Contains("no upstream") || o.Contains("has no upstream"))
                return "Este branch ainda não existe no remoto. Use \"Publicar\" para enviá-lo pela primeira vez.";
            return null;
        }

        // ---------------------------------------------------------- desfazer, push, pull

        // devolve null se o último commit pode ser desfeito; senão, o motivo
        public static string CheckUndo(RepoInfo repo, out string summary)
        {
            summary = "";
            if (!repo.HasHead) return "O repositório ainda não tem commits.";
            string par = Out(repo.FullPath, "rev-list --parents -n 1 HEAD");
            if (par == null) return "Não foi possível ler o último commit.";
            int n = par.Trim().Split(' ').Length;
            if (n == 1) return "O último commit é o primeiro do repositório; desfaça pelo terminal se precisar.";
            if (n > 2) return "O último commit é um merge; desfaça pelo terminal se precisar.";
            string remote = Out(repo.FullPath, "branch -r --contains HEAD");
            if (remote == null) return "Não foi possível verificar o remoto.";
            if (remote.Trim().Length > 0)
                return "O último commit já foi enviado ao remoto (push). Desfazer agora reescreveria o histórico compartilhado.";
            summary = (Out(repo.FullPath, "log -1 " + Q("--format=%h  %s")) ?? "").Trim();
            return null;
        }

        // volta o HEAD um commit; as alterações ficam em stage, nada se perde
        public static GitResult UndoLastCommit(RepoInfo repo)
        {
            int code; string err;
            string o = Run(repo.FullPath, "reset --soft HEAD~1", out code, out err);
            return new GitResult { Ok = code == 0, Output = (o + "\n" + err).Trim() };
        }

        public static string OutgoingSummary(RepoInfo repo)
        {
            string range = repo.Upstream != null ? "@{u}..HEAD" : "HEAD --not --remotes";
            return (Out(repo.FullPath, "log " + Q("--format=%h  %s") + " -n 30 " + range) ?? "").Trim();
        }

        public static string FirstRemote(RepoInfo repo)
        {
            string o = Out(repo.FullPath, "remote");
            if (o == null) return null;
            var names = o.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (names.Contains("origin")) return "origin";
            return names.FirstOrDefault();
        }

        public static GitResult Push(RepoInfo repo)
        {
            int code; string err;
            string args;
            if (repo.Upstream != null) args = "push";
            else
            {
                string remote = FirstRemote(repo);
                if (remote == null) return GitResult.Fail("Este repositório não tem remoto configurado.");
                args = "push -u " + remote + " HEAD";
            }
            string o = Run(repo.FullPath, args, out code, out err);
            return new GitResult { Ok = code == 0, Output = (o + "\n" + err).Trim() };
        }

        public static GitResult Fetch(RepoInfo repo, bool quiet)
        {
            int code; string err;
            var env = quiet ? new Dictionary<string, string> { { "GCM_INTERACTIVE", "never" } } : null;
            string o = Run(repo.FullPath, "fetch --quiet", out code, out err, env, null);
            return new GitResult { Ok = code == 0, Output = (o + "\n" + err).Trim() };
        }

        public static string IncomingSummary(RepoInfo repo)
        {
            return (Out(repo.FullPath, "log " + Q("--format=%h  %s  (%an)") + " -n 30 HEAD..@{u}") ?? "").Trim();
        }

        // só avança o branch (fast-forward): nunca cria merge nem mexe em conflito
        public static GitResult FastForward(RepoInfo repo)
        {
            int code; string err;
            string o = Run(repo.FullPath, "merge --ff-only @{u}", out code, out err);
            return new GitResult { Ok = code == 0, Output = (o + "\n" + err).Trim() };
        }
    }
}
