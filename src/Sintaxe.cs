using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace GitPainel
{
    // realce de sintaxe leve, linha a linha, com a paleta Dracula
    static class Syntax
    {
        public const int Plain = 0, Kw = 1, Str = 2, Com = 3, Num = 4, Fn = 5, Type = 6, Key = 7;

        public static Color ColorOf(int k)
        {
            switch (k)
            {
                case Kw: return T.Pink;
                case Str: return T.Yellow;
                case Com: return T.Comment;
                case Num: return T.Purple;
                case Fn: return T.Green;
                case Type: return T.Cyan;
                case Key: return T.Cyan;
                default: return T.Fg;
            }
        }

        static HashSet<string> Set(string words, bool ignoreCase)
        {
            return new HashSet<string>(words.Split(' '), ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        static readonly HashSet<string> JsKw = Set("abstract as async await break case catch class const continue debugger declare default delete do else enum export extends false finally for from function get if implements import in instanceof interface keyof let new null of private protected public readonly return satisfies set static super switch this throw true try type typeof undefined var void while with yield", false);
        static readonly HashSet<string> PyKw = Set("and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return self True try while with yield", false);
        static readonly HashSet<string> SqlKw = Set("add all alter and as asc begin between by case cast check column commit constraint create cross database declare default delete desc distinct drop else end exec exists foreign from full function go group having if in index inner insert into is join key left like limit merge not null on or order outer over partition primary procedure references return right rollback select set table then top transaction trigger union unique update values view when where while with", true);
        static readonly HashSet<string> CsKw = Set("abstract as async await base bool break byte case catch char class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach get if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sealed set short sizeof static string struct switch this throw true try typeof uint ulong using var virtual void volatile while", false);

        public static string LangOf(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".ts": case ".tsx": case ".js": case ".jsx": case ".mjs": case ".cjs": return "js";
                case ".json": return "json";
                case ".py": return "py";
                case ".sql": return "sql";
                case ".cs": return "cs";
                case ".css": case ".scss": return "css";
                case ".yml": case ".yaml": return "yaml";
                default: return null;
            }
        }

        static bool IdStart(char c) { return char.IsLetter(c) || c == '_' || c == '$'; }
        static bool IdPart(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '$'; }

        // devolve trincas (início, tamanho, tipo) só para os trechos coloridos
        public static List<int> Tokenize(string s, string lang)
        {
            var t = new List<int>();
            if (string.IsNullOrEmpty(s) || lang == null) return t;
            int n = s.Length;

            string trimmed = s.TrimStart();
            bool cstyle = lang == "js" || lang == "cs" || lang == "css";
            if (cstyle && (trimmed.StartsWith("*") || trimmed.StartsWith("/*") || trimmed.StartsWith("*/")) && !trimmed.StartsWith("*=") )
            {
                int st = n - trimmed.Length;
                t.Add(st); t.Add(n - st); t.Add(Com);
                return t;
            }

            string line = lang == "py" || lang == "yaml" ? "#" : lang == "sql" ? "--" : lang == "json" ? null : "//";
            HashSet<string> kw = lang == "js" ? JsKw : lang == "py" ? PyKw : lang == "sql" ? SqlKw : lang == "cs" ? CsKw : null;

            if (lang == "yaml")
            {
                // chave: valor
                int colon = s.IndexOf(':');
                int st = n - trimmed.Length;
                if (trimmed.StartsWith("#")) { t.Add(st); t.Add(n - st); t.Add(Com); return t; }
                if (trimmed.StartsWith("- ")) st += 2;
                if (colon > st && s.Substring(st, colon - st).IndexOf(' ') < 0) { t.Add(st); t.Add(colon - st); t.Add(Key); }
                int hash = s.IndexOf(" #", StringComparison.Ordinal);
                if (hash >= 0) { t.Add(hash + 1); t.Add(n - hash - 1); t.Add(Com); }
                return t;
            }

            int i = 0;
            while (i < n)
            {
                char c = s[i];
                if (line != null && string.CompareOrdinal(s, i, line, 0, line.Length) == 0)
                {
                    t.Add(i); t.Add(n - i); t.Add(Com);
                    break;
                }
                if (cstyle && c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int e = end < 0 ? n : end + 2;
                    t.Add(i); t.Add(e - i); t.Add(Com);
                    i = e;
                    continue;
                }
                if (c == '"' || c == '\'' || (c == '`' && lang == "js"))
                {
                    int j = i + 1;
                    while (j < n && s[j] != c) { if (s[j] == '\\') j++; j++; }
                    int e = Math.Min(n, j + 1);
                    int kind = Str;
                    if (lang == "json")
                    {
                        int k = e;
                        while (k < n && s[k] == ' ') k++;
                        if (k < n && s[k] == ':') kind = Key;
                    }
                    t.Add(i); t.Add(e - i); t.Add(kind);
                    i = e;
                    continue;
                }
                if (char.IsDigit(c) && (i == 0 || !IdPart(s[i - 1])))
                {
                    int j = i + 1;
                    while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '.' || s[j] == '_')) j++;
                    t.Add(i); t.Add(j - i); t.Add(Num);
                    i = j;
                    continue;
                }
                if (IdStart(c))
                {
                    int j = i + 1;
                    while (j < n && IdPart(s[j])) j++;
                    string w = s.Substring(i, j - i);
                    int k = j;
                    while (k < n && s[k] == ' ') k++;
                    if (kw != null && kw.Contains(w)) { t.Add(i); t.Add(j - i); t.Add(Kw); }
                    else if (lang == "json" && (w == "true" || w == "false" || w == "null")) { t.Add(i); t.Add(j - i); t.Add(Kw); }
                    else if (lang == "css" && k < n && s[k] == ':') { t.Add(i); t.Add(j - i); t.Add(Key); }
                    else if (lang != "sql" && lang != "css" && k < n && s[k] == '(') { t.Add(i); t.Add(j - i); t.Add(Fn); }
                    else if ((lang == "js" || lang == "cs") && char.IsUpper(w[0])) { t.Add(i); t.Add(j - i); t.Add(Type); }
                    i = j;
                    continue;
                }
                i++;
            }
            return t;
        }
    }
}
