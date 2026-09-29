using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GitPainel
{
    class DiffView : ScrollView
    {
        class SideRow { public DiffLine L, R, Hunk; }

        DiffData data;
        List<SideRow> side = new List<SideRow>();
        bool sideBySide, wrap;
        bool layoutSide; // lado a lado de fato (desliga sozinho quando a área fica estreita)
        readonly int lineH, charW, signW, topPad;
        int numW, cols, hoverHunk = -1;
        // linhas visuais: índice da linha (ou da linha lado a lado) + qual pedaço dela
        readonly List<int> vSrc = new List<int>(), vPart = new List<int>();

        // seleção de trechos para commit
        bool hunksOn;
        int[] hunkStates;
        public event Action<int> HunkToggled;

        public DiffView()
        {
            BackColor = T.Bg;
            lineH = Math.Max(T.Px(20), T.Mono.Height + T.Px(6));
            charW = Math.Max(1, (int)Math.Round(TextRenderer.MeasureText(new string('M', 100), T.Mono, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width / 100.0));
            signW = T.Px(22);
            topPad = T.Px(8);
            data = DiffData.Msg(G.Doc, "Selecione um arquivo", "Escolha um arquivo alterado na lista ao lado para ver o diff.");
        }

        public DiffData Data { get { return data; } }

        public bool SideBySide
        {
            get { return sideBySide; }
            set { if (sideBySide == value) return; KeepAnchor(() => sideBySide = value); }
        }

        public bool WordWrap
        {
            get { return wrap; }
            set { if (wrap == value) return; KeepAnchor(() => wrap = value); }
        }

        // states: 0/1 por trecho; enabled: mostra as caixas nos cabeçalhos dos trechos
        public void SetHunks(bool enabled, int[] states)
        {
            hunksOn = enabled && data.Title == null && data.HunkCount > 0;
            hunkStates = states;
            Invalidate();
        }

        bool Partial { get { return hunkStates != null && hunkStates.Any(s => s == 1) && hunkStates.Any(s => s == 0); } }
        bool Dim(DiffLine l) { return hunksOn && Partial && l.HunkIdx >= 0 && l.HunkIdx < hunkStates.Length && hunkStates[l.HunkIdx] == 0; }

        protected override int WheelStep { get { return lineH * 3; } }
        int Gutter { get { return 2 * numW + signW; } }
        int Half { get { return ClientSize.Width / 2; } }
        protected override int ViewLeft { get { return layoutSide ? 0 : Gutter; } }
        protected override int ViewW
        {
            get { return layoutSide ? Half - numW - signW - T.Px(8) : ClientSize.Width - Gutter - T.Px(16); }
        }

        public void SetData(DiffData d, bool keepScroll)
        {
            float oy = offY, ox = offX;
            data = d;
            if (!keepScroll) ClearSel();
            hunksOn = false;
            hunkStates = null;
            hoverHunk = -1;
            BuildSide();
            Relayout();
            if (keepScroll) JumpTo(oy, ox); else JumpTo(0, 0);
        }

        // mantém no topo a mesma linha do arquivo quando o layout muda (quebra, modo, largura)
        DiffLine TopLine()
        {
            int idx = Math.Max(0, (int)((offY - topPad) / lineH));
            if (idx >= vSrc.Count) return null;
            int src = vSrc[idx];
            if (!layoutSide) return src < data.Lines.Count ? data.Lines[src] : null;
            if (src >= side.Count) return null;
            return side[src].L ?? side[src].R ?? side[src].Hunk;
        }

        void JumpToAnchor(DiffLine anchor)
        {
            if (anchor == null) { JumpTo(offY, 0); return; }
            for (int i = 0; i < vSrc.Count; i++)
            {
                if (vPart[i] != 0) continue;
                bool hit;
                if (!layoutSide) hit = data.Lines[vSrc[i]] == anchor;
                else { var r = side[vSrc[i]]; hit = r.L == anchor || r.R == anchor || r.Hunk == anchor; }
                if (hit) { JumpTo(topPad + i * lineH, 0); return; }
            }
            JumpTo(offY, 0);
        }

        void KeepAnchor(Action change)
        {
            ClearSel();
            var a = TopLine();
            change();
            Relayout();
            JumpToAnchor(a);
        }

        // pareia remoções e adições consecutivas (lado a lado) e marca o trecho que mudou em cada par
        void BuildSide()
        {
            side = new List<SideRow>();
            var dels = new List<DiffLine>();
            var adds = new List<DiffLine>();
            Action flush = () =>
            {
                int n = Math.Max(dels.Count, adds.Count);
                for (int i = 0; i < n; i++)
                {
                    var l = i < dels.Count ? dels[i] : null;
                    var r = i < adds.Count ? adds[i] : null;
                    if (l != null && r != null) MarkChange(l, r);
                    side.Add(new SideRow { L = l, R = r });
                }
                dels.Clear();
                adds.Clear();
            };
            foreach (var l in data.Lines)
            {
                if (l.K == DiffLine.Del) { if (adds.Count > 0) flush(); dels.Add(l); }
                else if (l.K == DiffLine.Add) adds.Add(l);
                else
                {
                    flush();
                    if (l.K == DiffLine.Hunk) side.Add(new SideRow { Hunk = l });
                    else side.Add(new SideRow { L = l, R = l });
                }
            }
            flush();
        }

        static void MarkChange(DiffLine a, DiffLine b)
        {
            string x = a.Text, y = b.Text;
            int max = Math.Min(x.Length, y.Length), p = 0, s = 0;
            while (p < max && x[p] == y[p]) p++;
            while (s < max - p && x[x.Length - 1 - s] == y[y.Length - 1 - s]) s++;
            int longer = Math.Max(x.Length, y.Length);
            // linhas muito diferentes: realçar tudo não ajuda
            if (longer == 0 || (p + s) * 10 < longer * 3) return;
            if (x.Length - s > p) { a.ChgS = p; a.ChgE = x.Length - s; }
            if (y.Length - s > p) { b.ChgS = p; b.ChgE = y.Length - s; }
        }

        // quebra em pedaços de até "cols" caracteres, preferindo cortar depois de um espaço
        static int[] Split(string s, int cols)
        {
            if (s == null || s.Length <= cols) return new[] { 0 };
            var starts = new List<int> { 0 };
            int pos = 0;
            while (s.Length - pos > cols)
            {
                int cut = pos + cols;
                int sp = s.LastIndexOf(' ', cut - 1, cols);
                if (sp > pos + cols / 2) cut = sp + 1;
                starts.Add(cut);
                pos = cut;
            }
            return starts.ToArray();
        }

        int Parts(DiffLine l)
        {
            if (l == null || l.K == DiffLine.Hunk || !wrap) return 1;
            l.Wrap = Split(l.Text, cols);
            return l.Wrap.Length;
        }

        void Relayout()
        {
            layoutSide = sideBySide && ClientSize.Width >= T.Px(760);
            int digits = Math.Max(3, data.MaxNo.ToString().Length);
            numW = digits * charW + T.Px(20);
            int textW = layoutSide ? Half - numW - signW - T.Px(8) : ClientSize.Width - Gutter - T.Px(20);
            cols = Math.Max(10, textW / charW);

            vSrc.Clear();
            vPart.Clear();
            int count = layoutSide ? side.Count : data.Lines.Count;
            for (int i = 0; i < count; i++)
            {
                int n = layoutSide
                    ? (side[i].Hunk != null ? 1 : Math.Max(Parts(side[i].L), side[i].L == side[i].R ? 1 : Parts(side[i].R)))
                    : Parts(data.Lines[i]);
                for (int p = 0; p < n; p++) { vSrc.Add(i); vPart.Add(p); }
            }
            contentH = vSrc.Count == 0 ? 0 : vSrc.Count * lineH + topPad * 2;
            contentW = wrap ? 0 : data.MaxLen * charW + T.Px(40);
            Reclamp();
            Invalidate();
        }

        int lastW;
        protected override void OnResize(EventArgs e)
        {
            bool sideNow = sideBySide && ClientSize.Width >= T.Px(760);
            if (data != null && ((wrap && ClientSize.Width != lastW) || sideNow != layoutSide)) KeepAnchor(() => { });
            lastW = ClientSize.Width;
            base.OnResize(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C) { Copy(); e.Handled = e.SuppressKeyPress = true; return; }
            if (e.Control && e.KeyCode == Keys.A) { SelectAll(); e.Handled = e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Escape && selSide >= 0) { ClearSel(); Invalidate(); return; }
            switch (e.KeyCode)
            {
                case Keys.Down: ScrollBy(lineH, 0); break;
                case Keys.Up: ScrollBy(-lineH, 0); break;
                case Keys.Right: ScrollBy(0, charW * 8); break;
                case Keys.Left: ScrollBy(0, -charW * 8); break;
            }
            base.OnKeyDown(e);
        }

        // ---------------------------------------------------------- trechos clicáveis

        DiffLine LineAt(int y)
        {
            int i = (int)((y + offY - topPad) / lineH);
            if (y + offY < topPad || i < 0 || i >= vSrc.Count) return null;
            if (!layoutSide) return data.Lines[vSrc[i]];
            var r = side[vSrc[i]];
            return r.Hunk ?? r.L ?? r.R;
        }

        // ---------------------------------------------------------- seleção de texto

        int selSide = -1;          // -1 nenhuma; 0 unificado; 1 lado esquerdo; 2 lado direito
        int aSrc, aCh, bSrc, bCh;  // âncora e ponta da seleção: (linha lógica, caractere)
        bool selecting;
        Point lastMouse;
        Timer dragScroll;

        bool HasSel { get { return selSide >= 0 && (aSrc != bSrc || aCh != bCh); } }
        void ClearSel() { selSide = -1; selecting = false; }

        DiffLine SrcLine(int src, int sd)
        {
            if (sd == 0) return src >= 0 && src < data.Lines.Count ? data.Lines[src] : null;
            if (src < 0 || src >= side.Count || side[src].Hunk != null) return null;
            return sd == 1 ? side[src].L : side[src].R;
        }

        int TextX(int sd) { return sd == 0 ? Gutter : sd == 1 ? numW + signW : Half + 1 + numW + signW; }
        int SideAt(int x) { return !layoutSide ? 0 : x < Half ? 1 : 2; }

        // ponto da tela -> (linha lógica, caractere) do lado sd
        void PosAt(int x, int y, int sd, out int src, out int ch)
        {
            src = 0; ch = 0;
            if (vSrc.Count == 0) return;
            int i = (int)Math.Floor((y + offY - topPad) / lineH);
            if (i < 0) { src = vSrc[0]; return; }
            if (i >= vSrc.Count)
            {
                src = vSrc[vSrc.Count - 1];
                var last = SrcLine(src, sd);
                ch = last == null || last.Text == null ? 0 : last.Text.Length;
                return;
            }
            src = vSrc[i];
            int part = vPart[i];
            var l = SrcLine(src, sd);
            if (l == null || l.K == DiffLine.Hunk || l.Text == null) return;
            int start = 0, end = l.Text.Length;
            if (wrap && l.Wrap != null)
            {
                if (part < l.Wrap.Length) { start = l.Wrap[part]; end = part + 1 < l.Wrap.Length ? l.Wrap[part + 1] : l.Text.Length; }
                else start = end;
            }
            int col = (int)Math.Round((x - TextX(sd) - T.Px(2) + (wrap ? 0 : offX)) / charW);
            ch = Math.Max(start, Math.Min(end, start + Math.Max(0, col)));
        }

        static bool Before(int s1, int c1, int s2, int c2) { return s1 < s2 || (s1 == s2 && c1 < c2); }

        void Ordered(out int s1, out int c1, out int s2, out int c2)
        {
            s1 = aSrc; c1 = aCh; s2 = bSrc; c2 = bCh;
            if (Before(bSrc, bCh, aSrc, aCh)) { s1 = bSrc; c1 = bCh; s2 = aSrc; c2 = aCh; }
        }

        // parte selecionada de uma linha lógica
        bool SelRange(int src, int sd, int len, out int a, out int b)
        {
            a = b = 0;
            if (!HasSel || sd != selSide) return false;
            int s1, c1, s2, c2;
            Ordered(out s1, out c1, out s2, out c2);
            if (src < s1 || src > s2) return false;
            a = src == s1 ? c1 : 0;
            b = src == s2 ? c2 : len;
            return b > a;
        }

        public string SelectedText()
        {
            if (!HasSel) return "";
            int s1, c1, s2, c2;
            Ordered(out s1, out c1, out s2, out c2);
            var sb = new System.Text.StringBuilder();
            bool any = false;
            for (int src = s1; src <= s2; src++)
            {
                var l = SrcLine(src, selSide);
                if (l == null || l.K == DiffLine.Hunk || l.K == DiffLine.Meta || l.Text == null) continue;
                int a = Math.Min(l.Text.Length, src == s1 ? c1 : 0);
                int b = Math.Min(l.Text.Length, src == s2 ? c2 : l.Text.Length);
                if (any) sb.Append("\r\n");
                if (b > a) sb.Append(l.Text, a, b - a);
                any = true;
            }
            return sb.ToString();
        }

        public void Copy()
        {
            string t = SelectedText();
            if (t.Length > 0) try { Clipboard.SetText(t); } catch { }
        }

        public void SelectAll()
        {
            if (data.Title != null || vSrc.Count == 0) return;
            selSide = !layoutSide ? 0 : selSide > 0 ? selSide : 2;
            aSrc = 0; aCh = 0;
            bSrc = layoutSide ? side.Count - 1 : data.Lines.Count - 1;
            var last = SrcLine(bSrc, selSide);
            bCh = last == null || last.Text == null ? 0 : last.Text.Length;
            Invalidate();
        }

        void SelectWord(int src, int ch, int sd)
        {
            var l = SrcLine(src, sd);
            if (l == null || l.Text == null) return;
            string s = l.Text;
            Func<char, bool> word = c => char.IsLetterOrDigit(c) || c == '_' || c == '$';
            int a = Math.Min(ch, s.Length), b = a;
            while (a > 0 && word(s[a - 1])) a--;
            while (b < s.Length && word(s[b])) b++;
            aSrc = bSrc = src; aCh = a; bCh = b;
        }

        protected override void OnContentMouseMove(MouseEventArgs e)
        {
            if (selecting)
            {
                lastMouse = e.Location;
                PosAt(e.X, e.Y, selSide, out bSrc, out bCh);
                Invalidate();
                return;
            }
            int h = -1;
            var l = LineAt(e.Y);
            if (hunksOn && l != null && l.K == DiffLine.Hunk) h = l.HunkIdx;
            if (h != hoverHunk) { hoverHunk = h; Invalidate(); }
            bool text = data.Title == null && l != null && l.K != DiffLine.Hunk && e.X >= TextX(SideAt(e.X));
            Cursor = h >= 0 ? Cursors.Hand : text ? Cursors.IBeam : Cursors.Default;
        }

        protected override void OnContentMouseLeave()
        {
            if (hoverHunk != -1) { hoverHunk = -1; Invalidate(); }
        }

        protected override void OnContentMouseDown(MouseEventArgs e)
        {
            if (data.Title != null) return;
            var l = LineAt(e.Y);
            if (hunksOn && l != null && l.K == DiffLine.Hunk) { if (HunkToggled != null) HunkToggled(l.HunkIdx); return; }
            int sd = SideAt(e.X), s, c;
            PosAt(e.X, e.Y, sd, out s, out c);
            if ((ModifierKeys & Keys.Shift) != 0 && selSide == sd) { bSrc = s; bCh = c; }
            else { selSide = sd; aSrc = bSrc = s; aCh = bCh = c; }
            if (e.Clicks == 2) SelectWord(s, c, sd);
            selecting = e.Clicks < 2;
            lastMouse = e.Location;
            if (dragScroll == null)
            {
                // arrastar além da borda rola o diff
                dragScroll = new Timer { Interval = 40 };
                dragScroll.Tick += delegate
                {
                    if (!selecting) { dragScroll.Stop(); return; }
                    if (lastMouse.Y < T.Px(12)) ScrollBy(-lineH, 0);
                    else if (lastMouse.Y > ClientSize.Height - T.Px(12)) ScrollBy(lineH, 0);
                    if (!wrap && lastMouse.X > ClientSize.Width - T.Px(12)) ScrollBy(0, charW * 2);
                    else if (!wrap && lastMouse.X < TextX(selSide)) ScrollBy(0, -charW * 2);
                    PosAt(lastMouse.X, lastMouse.Y, selSide, out bSrc, out bCh);
                    Invalidate();
                };
            }
            if (selecting) dragScroll.Start();
            Invalidate();
        }

        protected override void OnContentMouseUp(MouseEventArgs e)
        {
            selecting = false;
            if (dragScroll != null) dragScroll.Stop();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Right || data.Title != null) return;
            OpenMenu.ShowItems(this, e.Location,
                OpenMenu.Item(G.Copy, "Copiar   (Ctrl+C)", HasSel, Copy),
                OpenMenu.Item(G.Check, "Selecionar tudo   (Ctrl+A)", true, SelectAll));
        }
        // ---------------------------------------------------------- pintura

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Bg);
            if (data.Title != null) { PaintEmpty(g); return; }

            int first = Math.Max(0, (int)((offY - topPad) / lineH));
            int oy = (int)Math.Round(offY);
            for (int i = first; i < vSrc.Count; i++)
            {
                int y = topPad + i * lineH - oy;
                if (y > ClientSize.Height) break;
                if (layoutSide) PaintSideRow(g, side[vSrc[i]], vSrc[i], vPart[i], y);
                else PaintUnified(g, data.Lines[vSrc[i]], vSrc[i], vPart[i], y);
            }
            if (layoutSide)
                T.Fill(g, T.Border, new Rectangle(Half, 0, 1, ClientSize.Height));
            PaintBars(g);
        }

        void PaintEmpty(Graphics g)
        {
            int cy = ClientSize.Height / 2 - T.Px(40);
            var w = ClientSize.Width;
            T.Text(g, data.Glyph, T.IconBig, T.Line, new Rectangle(0, cy - T.Px(30), w, T.Px(60)), TextFormatFlags.HorizontalCenter);
            T.Text(g, data.Title, T.UiBold, T.Mix(T.Comment, T.Fg, 0.55f), new Rectangle(T.Px(20), cy + T.Px(40), w - T.Px(40), T.Px(24)), TextFormatFlags.HorizontalCenter);
            T.Text(g, data.Sub, T.Small, T.Comment, new Rectangle(T.Px(20), cy + T.Px(64), w - T.Px(40), T.Px(22)), TextFormatFlags.HorizontalCenter);
        }

        void LineColors(DiffLine l, out Color row, out Color gut, out Color sign, out string sc)
        {
            row = T.Bg;
            gut = T.Mix(T.Bg, T.Border, 0.45f);
            sign = T.Comment;
            sc = "";
            if (l.K == DiffLine.Add) { row = T.Mix(T.Bg, T.Green, 0.09f); gut = T.Mix(T.Bg, T.Green, 0.17f); sign = T.Green; sc = "+"; }
            else if (l.K == DiffLine.Del) { row = T.Mix(T.Bg, T.Red, 0.09f); gut = T.Mix(T.Bg, T.Red, 0.17f); sign = T.Red; sc = "−"; }
            if (Dim(l)) { row = T.Mix(row, T.Bg, 0.7f); gut = T.Mix(gut, T.Bg, 0.6f); sign = T.Mix(sign, T.Bg, 0.5f); }
        }

        Color NumColor(DiffLine l)
        {
            Color c = l.K == DiffLine.Add ? T.Mix(T.Comment, T.Green, 0.45f) : l.K == DiffLine.Del ? T.Mix(T.Comment, T.Red, 0.45f) : T.Comment;
            return Dim(l) ? T.Mix(c, T.Bg, 0.4f) : c;
        }

        void PaintUnified(Graphics g, DiffLine l, int src, int part, int y)
        {
            if (l.K == DiffLine.Hunk) { PaintHunkRow(g, l, y, Gutter); return; }
            Color row, gut, sign; string sc;
            LineColors(l, out row, out gut, out sign, out sc);
            int gw = Gutter;
            T.Fill(g, gut, new Rectangle(0, y, 2 * numW, lineH));
            T.Fill(g, row, new Rectangle(2 * numW, y, ClientSize.Width - 2 * numW, lineH));
            if (part == 0)
            {
                Color nc = NumColor(l);
                if (l.K == DiffLine.Ctx || l.K == DiffLine.Del)
                    T.Text(g, l.Old.ToString(), T.Mono, nc, new Rectangle(0, y, numW - T.Px(10), lineH), TextFormatFlags.Right);
                if (l.K == DiffLine.Ctx || l.K == DiffLine.Add)
                    T.Text(g, l.New.ToString(), T.Mono, nc, new Rectangle(numW, y, numW - T.Px(10), lineH), TextFormatFlags.Right);
                T.Text(g, sc, T.Mono, sign, new Rectangle(2 * numW, y, signW, lineH), TextFormatFlags.HorizontalCenter);
            }
            else PaintContinuation(g, 2 * numW, y);
            DrawCode(g, l, part, gw, y, ClientSize.Width - gw, src, 0);
        }

        // marquinha discreta indicando que a linha continua da anterior
        void PaintContinuation(Graphics g, int x, int y)
        {
            T.Text(g, "↪", T.Small, T.Line, new Rectangle(x, y, signW, lineH), TextFormatFlags.HorizontalCenter);
        }

        void PaintHunkRow(Graphics g, DiffLine l, int y, int textX)
        {
            bool hot = hunksOn && l.HunkIdx == hoverHunk;
            T.Fill(g, T.Mix(T.Bg, T.Purple, hot ? 0.16f : 0.08f), new Rectangle(0, y, ClientSize.Width, lineH));
            int right = ClientSize.Width - T.Px(16);
            if (hunksOn)
            {
                int st = hunkStates != null && l.HunkIdx < hunkStates.Length ? hunkStates[l.HunkIdx] : 0;
                T.Check(g, T.Px(12), y + lineH / 2f, st, hot);
                if (hot)
                {
                    string hint = st == 1 ? "tirar este trecho do commit" : "incluir só este trecho no commit";
                    int hw2 = T.Measure(g, hint, T.Small) + 1;
                    T.Text(g, hint, T.Small, T.Purple, new Rectangle(right - hw2, y, hw2, lineH), 0);
                    right -= hw2 + T.Px(16);
                }
                if (layoutSide) textX = T.Px(34);
            }
            else if (layoutSide) textX = T.Px(12);
            int w = right - textX;
            int hw = T.Measure(g, l.Text, T.Mono);
            T.Text(g, l.Text, T.Mono, T.Purple, new Rectangle(textX + T.Px(4), y, Math.Min(hw + 1, w), lineH), 0);
            if (!string.IsNullOrEmpty(l.Extra))
                T.Text(g, l.Extra, T.Mono, T.Comment, new Rectangle(textX + T.Px(4) + hw + charW, y, w - hw - charW - T.Px(8), lineH), 0);
        }

        void PaintSideRow(Graphics g, SideRow r, int src, int part, int y)
        {
            if (r.Hunk != null) { PaintHunkRow(g, r.Hunk, y, 0); return; }
            int h = Half;
            PaintCell(g, r.L, src, part, true, 0, h, y);
            PaintCell(g, r.R, src, part, false, h + 1, ClientSize.Width - h - 1, y);
        }

        void PaintCell(Graphics g, DiffLine l, int src, int part, bool left, int x0, int w, int y)
        {
            if (l == null) { T.Fill(g, T.Mix(T.Bg, T.Border, 0.55f), new Rectangle(x0, y, w, lineH)); return; }
            Color row, gut, sign; string sc;
            LineColors(l, out row, out gut, out sign, out sc);
            T.Fill(g, gut, new Rectangle(x0, y, numW, lineH));
            T.Fill(g, row, new Rectangle(x0 + numW, y, w - numW, lineH));
            int nParts = wrap && l.Wrap != null ? l.Wrap.Length : 1;
            if (part >= nParts) return; // o outro lado tem mais pedaços: aqui só o fundo
            if (part == 0)
            {
                int no = left ? l.Old : l.New;
                if (no > 0 && l.K != DiffLine.Meta)
                    T.Text(g, no.ToString(), T.Mono, NumColor(l), new Rectangle(x0, y, numW - T.Px(10), lineH), TextFormatFlags.Right);
                T.Text(g, sc, T.Mono, sign, new Rectangle(x0 + numW, y, signW, lineH), TextFormatFlags.HorizontalCenter);
            }
            else PaintContinuation(g, x0 + numW, y);
            DrawCode(g, l, part, x0 + numW + signW, y, w - numW - signW, src, left ? 1 : 2);
        }

        void DrawCode(Graphics g, DiffLine l, int part, int x, int y, int w, int src, int sd)
        {
            string s = l.Text;
            if (string.IsNullOrEmpty(s) || w <= 0) return;
            bool dim = Dim(l);

            // trecho visível da linha: [ps, pe) desenhado a partir de ox
            int ps, pe, ox;
            if (wrap && l.Wrap != null)
            {
                if (part >= l.Wrap.Length) return;
                ps = l.Wrap[part];
                pe = part + 1 < l.Wrap.Length ? l.Wrap[part + 1] : s.Length;
                ox = x + T.Px(2);
            }
            else
            {
                ps = (int)(offX / charW);
                if (ps >= s.Length) return;
                pe = Math.Min(s.Length, ps + w / charW + 3);
                ox = x + T.Px(2) - (int)Math.Round(offX - ps * charW);
            }

            var clip = g.Clip;
            g.SetClip(new Rectangle(x, y, w, lineH));

            // realce do que mudou dentro da linha
            if (l.ChgS >= 0 && !dim)
            {
                int a = Math.Max(ps, l.ChgS), b = Math.Min(pe, l.ChgE);
                if (b > a)
                {
                    Color hc = l.K == DiffLine.Add ? T.Mix(T.Bg, T.Green, 0.30f) : T.Mix(T.Bg, T.Red, 0.32f);
                    T.FillRound(g, hc, new RectangleF(ox + (a - ps) * charW, y + T.Px(2), (b - a) * charW, lineH - T.Px(4)), T.Px(3));
                }
            }

            // texto selecionado com o mouse
            int sa, sb;
            if (SelRange(src, sd, s.Length, out sa, out sb))
            {
                int a = Math.Max(ps, sa), b = Math.Min(pe, sb);
                if (b > a) T.Fill(g, T.A(T.Purple, 90), new Rectangle(ox + (a - ps) * charW, y, (b - a) * charW, lineH));
            }

            if (l.Tok == null)
                l.Tok = l.K == DiffLine.Meta || data.Lang == null ? new List<int>() : Syntax.Tokenize(s, data.Lang);
            Color baseC = l.K == DiffLine.Meta ? T.Comment : l.K == DiffLine.Ctx ? T.Mix(T.Fg, T.Bg, 0.12f) : T.Fg;
            var tok = l.Tok;
            int pos = ps, ti = 0, ty = y + (lineH - T.Mono.Height) / 2;
            while (pos < pe)
            {
                while (ti < tok.Count && tok[ti] + tok[ti + 1] <= pos) ti += 3;
                int end;
                Color c;
                if (ti < tok.Count && tok[ti] <= pos) { end = Math.Min(pe, tok[ti] + tok[ti + 1]); c = Syntax.ColorOf(tok[ti + 2]); }
                else { end = ti < tok.Count ? Math.Min(pe, tok[ti]) : pe; c = baseC; }
                if (end <= pos) end = pos + 1;
                if (dim) c = T.Mix(c, T.Bg, 0.55f);
                TextRenderer.DrawText(g, s.Substring(pos, end - pos), T.Mono, new Point(ox + (pos - ps) * charW, ty), c,
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                pos = end;
            }
            g.Clip = clip;
        }
    }
}
