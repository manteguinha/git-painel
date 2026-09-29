using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GitPainel
{
    // atualização automática a partir das releases do GitHub
    static class Updater
    {
        const string Repo = "manteguinha/git-painel";
        const string AssetName = "GitPainel.exe";

        public class Info
        {
            public Version Version;
            public string Notes = "", ExeUrl, Page;
        }

        public static Version Current { get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); } }

        public static string CurrentText { get { var v = Current; return v.Major + "." + v.Minor + "." + v.Build; } }

        // 1.2 == 1.2.0 == 1.2.0.0 (o Version do .NET trata componentes ausentes como menores)
        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        static string Text(Version v) { return v.Major + "." + v.Minor + "." + v.Build; }
        public static string VersionText(Info i) { return Text(i.Version); }

        // devolve a versão nova, ou null se já está na última (ou se não deu para consultar)
        public static Info Check()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                // GITPAINEL_UPDATE_URL: só para testar o atualizador sem publicar uma release
                string url = Environment.GetEnvironmentVariable("GITPAINEL_UPDATE_URL")
                             ?? "https://api.github.com/repos/" + Repo + "/releases/latest";
                string json;
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.UserAgent] = "GitPainel/" + CurrentText;
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = wc.DownloadString(url);
                }
                var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (d == null || !d.ContainsKey("tag_name")) return null;
                if (d.ContainsKey("draft") && true.Equals(d["draft"])) return null;
                if (d.ContainsKey("prerelease") && true.Equals(d["prerelease"])) return null;

                Version v;
                if (!Version.TryParse(Convert.ToString(d["tag_name"]).TrimStart('v', 'V'), out v)) return null;
                v = Normalize(v);
                if (v <= Current) return null;

                var info = new Info { Version = v, Page = d.ContainsKey("html_url") ? Convert.ToString(d["html_url"]) : null };
                if (d.ContainsKey("body") && d["body"] != null) info.Notes = Convert.ToString(d["body"]);
                var assets = d.ContainsKey("assets") ? d["assets"] as object[] : null;
                if (assets != null)
                    foreach (var o in assets)
                    {
                        var a = o as Dictionary<string, object>;
                        if (a != null && string.Equals(Convert.ToString(a["name"]), AssetName, StringComparison.OrdinalIgnoreCase))
                            info.ExeUrl = Convert.ToString(a["browser_download_url"]);
                    }
                return info.ExeUrl == null ? null : info;
            }
            catch { return null; } // sem internet, limite da API etc.: tenta de novo mais tarde
        }

        // baixa o exe novo para a mesma pasta do app (mesmo disco, para a troca ser instantânea)
        public static string Download(Info info, Action<int> progress)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string tmp = Path.Combine(dir, "GitPainel.update.tmp");
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "GitPainel/" + CurrentText;
                var done = new System.Threading.ManualResetEvent(false);
                Exception error = null;
                wc.DownloadProgressChanged += (s, e) => { if (progress != null) progress(e.ProgressPercentage); };
                wc.DownloadFileCompleted += (s, e) => { error = e.Error; done.Set(); };
                wc.DownloadFileAsync(new Uri(info.ExeUrl), tmp);
                done.WaitOne();
                if (error != null) { TryDelete(tmp); throw error; }
            }

            // confere que veio mesmo um executável do Windows, e não uma página de erro
            var fi = new FileInfo(tmp);
            byte[] head = new byte[2];
            using (var fs = File.OpenRead(tmp)) fs.Read(head, 0, 2);
            if (fi.Length < 20 * 1024 || head[0] != 'M' || head[1] != 'Z')
            {
                TryDelete(tmp);
                throw new InvalidDataException("O arquivo baixado não é um executável válido.");
            }
            return tmp;
        }

        // troca o exe: o Windows deixa renomear um exe em uso, então o atual vira ".old"
        public static void Swap(string newFile)
        {
            string exe = Application.ExecutablePath, old = exe + ".old";
            TryDelete(old);
            File.Move(exe, old);
            try { File.Move(newFile, exe); }
            catch { File.Move(old, exe); throw; } // desfaz para o app nunca ficar sem exe
        }

        // na abertura seguinte, apaga o exe antigo que ficou da atualização
        public static void CleanupOld()
        {
            string exe = Application.ExecutablePath;
            TryDelete(exe + ".old");
            TryDelete(Path.Combine(Path.GetDirectoryName(exe), "GitPainel.update.tmp"));
        }

        public static void Restart()
        {
            var args = Environment.GetCommandLineArgs().Skip(1).Select(a => "\"" + a.TrimEnd('\\') + "\"");
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", args)) { UseShellExecute = false });
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    }
}
