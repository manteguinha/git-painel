using System.Collections.Generic;
using System.Linq;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GitPainel
{
    // ------------------------------------------------------------------ rolagem suave

    class ScrollView : Control
    {
        protected float offY, offX;
        float tgtY, tgtX;
        protected int contentH, contentW;
        readonly Timer anim = new Timer();
        int drag, dragStart;
        float dragOrig;
        bool hotV, hotH;

        public ScrollView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            anim.Interval = 10;
            anim.Tick += delegate { Step(); };
        }

        protected virtual int ViewW { get { return ClientSize.Width; } }
        protected virtual int ViewLeft { get { return 0; } }
        protected virtual int WheelStep { get { return T.Px(66); } }
        protected float MaxY { get { return Math.Max(0, contentH - ClientSize.Height); } }
        protected float MaxX { get { return Math.Max(0, contentW - ViewW); } }
        protected bool HasV { get { return contentH > ClientSize.Height; } }
        protected bool HasH { get { return ViewW > 0 && contentW > ViewW; } }

        static float Cl(float v, float a, float b) { return v < a ? a : v > b ? b : v; }

        public void ScrollBy(float dy, float dx)
        {
            tgtY = Cl(tgtY + dy, 0, MaxY);
            tgtX = Cl(tgtX + dx, 0, MaxX);
            anim.Start();
        }

        protected void ScrollToY(float y) { tgtY = Cl(y, 0, MaxY); anim.Start(); }
        protected float TargetY { get { return tgtY; } }

        public void JumpTo(float y, float x)
        {
            anim.Stop();
            tgtY = offY = Cl(y, 0, MaxY);
            tgtX = offX = Cl(x, 0, MaxX);
            Invalidate();
        }

        protected void Reclamp()
        {
            tgtY = Cl(tgtY, 0, MaxY); offY = Cl(offY, 0, MaxY);
            tgtX = Cl(tgtX, 0, MaxX); offX = Cl(offX, 0, MaxX);
        }

        void Step()
        {
            float dy = tgtY - offY, dx = tgtX - offX;
            offY += dy * 0.26f;
            offX += dx * 0.26f;
            if (Math.Abs(dy) < 0.6f) offY = tgtY;
            if (Math.Abs(dx) < 0.6f) offX = tgtX;
            if (offY == tgtY && offX == tgtX) anim.Stop();
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Reclamp(); Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float amt = -e.Delta / 120f * WheelStep;
            if ((ModifierKeys & Keys.Shift) != 0) ScrollBy(0, amt); else ScrollBy(amt, 0);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020E) // WM_MOUSEHWHEEL (touchpad)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                ScrollBy(0, delta / 120f * WheelStep);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        Rectangle VThumb()
        {
            int h = ClientSize.Height;
            int th = Math.Max(T.Px(36), (int)((long)h * h / Math.Max(1, contentH)));
            int y = MaxY <= 0 ? 0 : (int)(offY / MaxY * (h - th));
            return new Rectangle(ClientSize.Width - T.Px(12), y, T.Px(12), th);
        }

        Rectangle HThumb()
        {
            int track = ClientSize.Width - ViewLeft - T.Px(12);
            int tw = Math.Max(T.Px(36), (int)((long)track * ViewW / Math.Max(1, contentW)));
            int x = ViewLeft + (MaxX <= 0 ? 0 : (int)(offX / MaxX * (track - tw)));
            return new Rectangle(x, ClientSize.Height - T.Px(12), tw, T.Px(12));
        }

        protected void PaintBars(Graphics g)
        {
            if (HasV)
            {
                var r = VThumb();
                bool big = hotV || drag == 1;
                int w = big ? T.Px(8) : T.Px(5);
                T.FillRound(g, T.A(T.Comment, big ? 200 : 110),
                    new RectangleF(ClientSize.Width - w - T.Px(3), r.Y + T.Px(3), w, r.Height - T.Px(6)), w / 2f);
            }
            if (HasH)
            {
                var r = HThumb();
                bool big = hotH || drag == 2;
                int h = big ? T.Px(8) : T.Px(5);
                T.FillRound(g, T.A(T.Comment, big ? 200 : 110),
                    new RectangleF(r.X + T.Px(3), ClientSize.Height - h - T.Px(3), r.Width - T.Px(6), h), h / 2f);
            }
        }

        bool OnVBar(Point p) { return HasV && p.X >= ClientSize.Width - T.Px(14); }
        bool OnHBar(Point p) { return HasH && !OnVBar(p) && p.Y >= ClientSize.Height - T.Px(14) && p.X >= ViewLeft; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (OnVBar(e.Location))
            {
                var r = VThumb();
                if (e.Y >= r.Y && e.Y <= r.Bottom) { drag = 1; dragStart = e.Y; dragOrig = offY; }
                else ScrollToY((e.Y - r.Height / 2f) / Math.Max(1, ClientSize.Height - r.Height) * MaxY);
                return;
            }
            if (OnHBar(e.Location))
            {
                var r = HThumb();
                if (e.X >= r.X && e.X <= r.Right) { drag = 2; dragStart = e.X; dragOrig = offX; }
                else ScrollBy(0, e.X < r.X ? -ViewW * 0.8f : ViewW * 0.8f);
                return;
            }
            OnContentMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag == 1)
            {
                float track = ClientSize.Height - VThumb().Height;
                JumpTo(dragOrig + (e.Y - dragStart) * (track <= 0 ? 0 : MaxY / track), offX);
                return;
            }
            if (drag == 2)
            {
                float track = ClientSize.Width - ViewLeft - T.Px(12) - HThumb().Width;
                JumpTo(offY, dragOrig + (e.X - dragStart) * (track <= 0 ? 0 : MaxX / track));
                return;
            }
            bool v = OnVBar(e.Location), h = OnHBar(e.Location);
            if (v != hotV || h != hotH) { hotV = v; hotH = h; Invalidate(); }
            if (v || h) { Cursor = Cursors.Default; OnContentMouseLeave(); return; }
            OnContentMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (drag != 0) { drag = 0; Invalidate(); }
            else if (e.Button == MouseButtons.Left) OnContentMouseUp(e);
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hotV = hotH = false;
            OnContentMouseLeave();
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected virtual void OnContentMouseDown(MouseEventArgs e) { }
        protected virtual void OnContentMouseMove(MouseEventArgs e) { }
        protected virtual void OnContentMouseLeave() { }
        protected virtual void OnContentMouseUp(MouseEventArgs e) { }

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.PageDown: ScrollBy(ClientSize.Height * 0.9f, 0); break;
                case Keys.PageUp: ScrollBy(-ClientSize.Height * 0.9f, 0); break;
                case Keys.Home: ScrollToY(0); break;
                case Keys.End: ScrollToY(MaxY); break;
            }
            base.OnKeyDown(e);
        }
    }

    // ------------------------------------------------------------------ controles pequenos

    class Toggle : Control
    {
        readonly string[] items;
        int sel, hot = -1, segW;
        float pos;
        readonly Timer anim = new Timer();
        public event EventHandler Changed;
        public Color Back = T.Bg;

        public Toggle(string[] it)
        {
            items = it;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            int mw = 0;
            foreach (var s in it) mw = Math.Max(mw, TextRenderer.MeasureText(s, T.Small).Width);
            segW = mw + T.Px(22);
            Size = new Size(segW * it.Length + T.Px(6), T.Px(32));
            Cursor = Cursors.Hand;
            anim.Interval = 10;
            anim.Tick += delegate
            {
                pos += (sel - pos) * 0.3f;
                if (Math.Abs(sel - pos) < 0.01f) { pos = sel; anim.Stop(); }
                Invalidate();
            };
        }

        public int Selected
        {
            get { return sel; }
            set
            {
                if (value == sel) return;
                sel = value;
                if (IsHandleCreated) anim.Start(); else pos = sel;
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            T.FillRound(g, T.Border, new RectangleF(0, 0, Width - 1, Height - 1), T.Px(8));
            T.FillRound(g, T.Line, new RectangleF(T.Px(3) + pos * segW, T.Px(3), segW, Height - T.Px(7)), T.Px(6));
            for (int i = 0; i < items.Length; i++)
            {
                Color c = i == sel ? T.Fg : i == hot ? T.Mix(T.Comment, T.Fg, 0.5f) : T.Comment;
                T.Text(g, items[i], T.Small, c, new Rectangle(T.Px(3) + i * segW, 0, segW, Height - 1), TextFormatFlags.HorizontalCenter);
            }
        }

        int Hit(int x) { int i = (x - T.Px(3)) / Math.Max(1, segW); return i >= 0 && i < items.Length ? i : -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.X); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.X); if (h >= 0) Selected = h; base.OnMouseDown(e); }
    }

    // botão liga/desliga compacto (ex.: quebra de linha)
    class ChipToggle : Control
    {
        bool on, over;
        public event EventHandler Changed;

        public ChipToggle(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(TextRenderer.MeasureText(text, T.Small).Width + T.Px(38), T.Px(32));
            Cursor = Cursors.Hand;
        }

        public bool On
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Bg);
            Color bg = on ? T.Mix(T.Bg, T.Purple, 0.20f) : over ? T.Mix(T.Border, T.Line, 0.6f) : T.Border;
            T.FillRound(g, bg, new RectangleF(0, 0, Width - 1, Height - 1), T.Px(8));
            float d = T.Px(7);
            if (on) T.Dot(g, T.Purple, T.Px(12), (Height - d) / 2f, d);
            else T.StrokeRound(g, T.Comment, new RectangleF(T.Px(12), (Height - d) / 2f, d, d), d / 2f, T.Px(1.2f));
            T.Text(g, Text, T.Small, on ? T.Fg : over ? T.Mix(T.Comment, T.Fg, 0.5f) : T.Comment,
                new Rectangle(T.Px(26), 0, Width - T.Px(30), Height - 1), 0);
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) On = !On; base.OnMouseDown(e); }
    }

    // botão plano: com ícone (barra do topo) ou só texto (diálogos)
    class FlatButton : Control
    {
        readonly string glyph;
        float h;
        bool over, down;
        DateTime lastClick;
        readonly Timer anim = new Timer();
        public Color Back = T.Panel;

        public FlatButton(string glyph, string text)
        {
            this.glyph = glyph;
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false); // o clique é tratado em OnMouseUp
            Size = new Size(TextRenderer.MeasureText(text, T.Ui).Width + (glyph == null ? T.Px(36) : T.Px(46)), T.Px(34));
            Cursor = Cursors.Hand;
            anim.Interval = 10;
            anim.Tick += delegate
            {
                float t = over ? 1 : 0;
                h += (t - h) * 0.25f;
                if (Math.Abs(t - h) < 0.01f) { h = t; anim.Stop(); }
                Invalidate();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            Color bg = T.Mix(T.Mix(Back, T.Line, 0.35f), T.Line, h);
            if (down) bg = T.Mix(bg, T.Purple, 0.22f);
            // pressionado: encolhe 1px, como um botão físico
            var r = down ? new RectangleF(1, 1, Width - 3, Height - 3) : new RectangleF(0, 0, Width - 1, Height - 1);
            T.FillRound(g, bg, r, T.Px(7));
            var tr = Rectangle.Round(r);
            if (glyph == null) { T.Text(g, Text, T.Ui, T.Fg, tr, TextFormatFlags.HorizontalCenter); return; }
            T.Text(g, glyph, T.Icon, T.Purple, new Rectangle(tr.X + T.Px(12), tr.Y, T.Px(18), tr.Height), TextFormatFlags.HorizontalCenter);
            T.Text(g, Text, T.Ui, T.Fg, new Rectangle(tr.X + T.Px(36), tr.Y, tr.Width - T.Px(44), tr.Height), 0);
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; down = false; anim.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fire = down && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            down = false;
            Invalidate();
            base.OnMouseUp(e);
            if (fire && T.Debounce(ref lastClick, 400)) OnClick(EventArgs.Empty);
        }
    }

    // botão principal: hover suave, efeito de pressionar e estado "trabalhando" com spinner
    class PrimaryButton : Control
    {
        float h;
        bool over, down, busy;
        DateTime lastClick;
        readonly Timer anim = new Timer { Interval = 15 };
        public bool Active = true;
        public Color Back = T.Panel;
        public Color Accent = T.Purple;

        public PrimaryButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false); // o clique é tratado em OnMouseUp
            Height = T.Px(38);
            Cursor = Cursors.Hand;
            anim.Tick += delegate
            {
                float t = over && Active ? 1 : 0;
                h += (t - h) * 0.25f;
                if (Math.Abs(t - h) < 0.01f) h = t;
                if (h == t && !busy) anim.Stop();
                Invalidate();
            };
        }

        public bool Busy
        {
            get { return busy; }
            set { if (busy == value) return; busy = value; Cursor = value ? Cursors.WaitCursor : Cursors.Hand; anim.Start(); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            Color bg;
            if (busy) bg = T.Mix(T.Mix(Accent, Back, 0.35f), Accent, T.Pulse * 0.25f);
            else if (!Active) bg = T.Line;
            else bg = T.Mix(Accent, T.Fg, 0.15f * h);
            if (down && Active && !busy) bg = T.Mix(bg, T.Bg, 0.18f);
            var r = down && Active && !busy ? new RectangleF(1, 1, Width - 3, Height - 3) : new RectangleF(0, 0, Width - 1, Height - 1);
            T.FillRound(g, bg, r, T.Px(8));
            Color fg = Active || busy ? T.Bg : T.Comment;
            if (!busy) { T.Text(g, Text, T.UiBold, fg, Rectangle.Round(r), TextFormatFlags.HorizontalCenter); return; }

            // spinner + texto centralizados juntos
            int tw = T.Measure(g, Text, T.UiBold) + 1, s = T.Px(14), gap = T.Px(8);
            int x = (int)(r.X + (r.Width - (s + gap + tw)) / 2);
            T.Spinner(g, new RectangleF(x, r.Y + (r.Height - s) / 2f, s, s), fg, T.Px(2));
            T.Text(g, Text, T.UiBold, fg, new Rectangle(x + s + gap, (int)r.Y, tw, (int)r.Height), 0);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { over = true; anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; down = false; anim.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fire = down && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && !busy;
            down = false;
            Invalidate();
            base.OnMouseUp(e);
            if (fire && T.Debounce(ref lastClick, 500)) OnClick(EventArgs.Empty);
        }
    }
    class DarkTextBox : TextBox
    {
        public string Placeholder = "";

        public DarkTextBox(bool multi)
        {
            BorderStyle = BorderStyle.None;
            Font = T.Ui;
            ForeColor = T.Fg;
            BackColor = T.Field;
            if (multi)
            {
                Multiline = true;
                ScrollBars = ScrollBars.None;
                WordWrap = true;
                AcceptsReturn = true;
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && TextLength == 0 && Placeholder.Length > 0)
                using (var g = CreateGraphics())
                    TextRenderer.DrawText(g, Placeholder, Font, new Point(1, 0), T.Comment,
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (Parent != null) Parent.Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); if (Parent != null) Parent.Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A) { SelectAll(); e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }

        // moldura arredondada desenhada pelo controle pai em volta da caixa
        public static void Frame(Graphics g, Rectangle r, Control focus, Color back)
        {
            T.FillRound(g, T.Field, r, T.Px(7));
            T.StrokeRound(g, focus.Focused ? T.Purple : T.Line,
                new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), T.Px(7), T.Px(1));
        }
    }

    // ------------------------------------------------------------------ busca

    class SearchBar : Control
    {
        public readonly DarkTextBox Box;
        public event EventHandler QueryChanged, ExpandToggle;
        public Func<bool> AnyCollapsed = () => true;
        bool overClear, overExpand, downExpand;
        DateTime lastToggle;
        readonly DarkTip tip = new DarkTip();

        public SearchBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = T.Px(50);
            Box = new DarkTextBox(false) { Placeholder = "Buscar arquivo alterado…   Ctrl+F" };
            Controls.Add(Box);
            Box.TextChanged += delegate { Invalidate(); if (QueryChanged != null) QueryChanged(this, EventArgs.Empty); };
            Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Box.Text = ""; e.SuppressKeyPress = true; } };
        }

        public string Query { get { return Box.Text.Trim(); } }

        Rectangle FrameRect { get { return new Rectangle(T.Px(12), T.Px(10), Width - T.Px(24) - T.Px(40), T.Px(32)); } }
        Rectangle ExpandRect { get { var f = FrameRect; return new Rectangle(f.Right + T.Px(6), f.Y, T.Px(34), f.Height); } }
        Rectangle ClearRect { get { var f = FrameRect; return new Rectangle(f.Right - T.Px(28), f.Y, T.Px(26), f.Height); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box == null) return;
            var f = FrameRect;
            int ih = T.Ui.Height;
            Box.SetBounds(f.X + T.Px(32), f.Y + (f.Height - ih) / 2, f.Width - T.Px(64), ih);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Panel);
            var f = FrameRect;
            DarkTextBox.Frame(g, f, Box, T.Panel);
            T.Text(g, G.Search, T.IconSmall, Box.Focused ? T.Purple : T.Comment, new Rectangle(f.X + T.Px(10), f.Y, T.Px(16), f.Height), TextFormatFlags.HorizontalCenter);
            if (Box.TextLength > 0)
                T.Text(g, G.Close, T.IconSmall, overClear ? T.Fg : T.Comment, ClearRect, TextFormatFlags.HorizontalCenter);

            // expandir/recolher tudo: dois chevrons empilhados (⌄⌄ expande, ⌃⌃ recolhe)
            var er = ExpandRect;
            if (downExpand) er = new Rectangle(er.X + 1, er.Y + 1, er.Width - 2, er.Height - 2);
            T.FillRound(g, overExpand ? T.Line : T.Mix(T.Panel, T.Line, 0.45f), er, T.Px(7));
            string gl = AnyCollapsed() ? G.Down : G.Up;
            Color gc = overExpand ? T.Fg : T.Mix(T.Comment, T.Fg, 0.45f);
            T.Text(g, gl, T.IconSmall, gc, new Rectangle(er.X, er.Y - T.Px(3), er.Width, er.Height), TextFormatFlags.HorizontalCenter);
            T.Text(g, gl, T.IconSmall, gc, new Rectangle(er.X, er.Y + T.Px(3), er.Width, er.Height), TextFormatFlags.HorizontalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool o = Box.TextLength > 0 && ClearRect.Contains(e.Location);
            bool x = ExpandRect.Contains(e.Location);
            if (o != overClear || x != overExpand)
            {
                overClear = o;
                if (x != overExpand)
                {
                    if (x) tip.ShowAt(AnyCollapsed() ? "Expandir tudo   (Ctrl+↓)" : "Recolher tudo   (Ctrl+↑)", this, ExpandRect.X - T.Px(60), ExpandRect.Bottom + T.Px(6));
                    else tip.Hide(this);
                }
                overExpand = x;
                Invalidate();
            }
            Cursor = o || x ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (overExpand) tip.Hide(this);
            overExpand = overClear = downExpand = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fire = downExpand && ExpandRect.Contains(e.Location);
            downExpand = false;
            Invalidate();
            base.OnMouseUp(e);
            if (fire && T.Debounce(ref lastToggle, 350))
            {
                if (ExpandToggle != null) ExpandToggle(this, EventArgs.Empty);
                tip.Hide(this);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (ExpandRect.Contains(e.Location)) { if (e.Button == MouseButtons.Left) { downExpand = true; Invalidate(); } base.OnMouseDown(e); return; }
            if (Box.TextLength > 0 && ClearRect.Contains(e.Location)) Box.Text = "";
            Box.Focus();
            base.OnMouseDown(e);
        }
    }

    // dica flutuante no tema escuro
    class DarkTip : ToolTip
    {
        public DarkTip()
        {
            OwnerDraw = true;
            UseAnimation = true;
            UseFading = true;
            Draw += (s, e) =>
            {
                T.Fill(e.Graphics, T.Line, e.Bounds);
                using (var p = new Pen(T.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, T.Small, e.Bounds, T.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
        }

        public void ShowAt(string text, IWin32Window owner, int x, int y) { Show(text, owner, x, y, 4000); }
    }

    // ------------------------------------------------------------------ barras

    // barra do topo que também é a barra de título da janela (logo, pasta, filtro, atualizar e min/max/fechar)
    class TopBar : Control
    {
        public readonly Toggle Filter;
        public string Folder = "";
        public event EventHandler PickRequested, ReloadRequested;
        string hot, pressed;
        Rectangle folderRect, reloadRect, minRect, maxRect, closeRect;
        static readonly Color CloseRed = T.Hex(0xC42B1C);
        // intensidade do hover de cada botão (0..1), animada
        readonly Dictionary<string, float> lvl = new Dictionary<string, float>();
        readonly Timer fade = new Timer { Interval = 15 };
        bool refreshing;
        DateTime refreshStart, lastAction;

        public TopBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = T.Px(48);
            foreach (var id in new[] { "folder", "reload", "min", "max", "close" }) lvl[id] = 0;
            fade.Tick += delegate
            {
                bool moving = false;
                foreach (var id in lvl.Keys.ToList())
                {
                    float t = hot == id ? 1 : 0, v = lvl[id] + (t - lvl[id]) * 0.3f;
                    if (Math.Abs(t - v) < 0.02f) v = t; else moving = true;
                    lvl[id] = v;
                }
                // o giro do "atualizar" dura pelo menos meio segundo, para não piscar
                bool spin = refreshing || (DateTime.Now - refreshStart).TotalMilliseconds < 500;
                if (!moving && !spin) fade.Stop();
                Invalidate();
            };
            Filter = new Toggle(new[] { "Todos", "Meus módulos" });
            Filter.Back = T.Panel;
            Controls.Add(Filter);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Filter == null) return;
            int cw = T.Px(46), h = Height - 1;
            closeRect = new Rectangle(Width - cw, 0, cw, h);
            maxRect = new Rectangle(closeRect.X - cw, 0, cw, h);
            minRect = new Rectangle(maxRect.X - cw, 0, cw, h);
            reloadRect = new Rectangle(minRect.X - T.Px(14) - T.Px(34), (h - T.Px(32)) / 2, T.Px(34), T.Px(32));
            Filter.Location = new Point(reloadRect.X - T.Px(8) - Filter.Width, (h - Filter.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Panel);
            T.Fill(g, T.Border, new Rectangle(0, Height - 1, Width, 1));
            int h = Height - 1;

            float ls = T.Px(22);
            T.DrawLogo(g, new RectangleF(T.Px(14), (h - ls) / 2f, ls, ls));
            int x = T.Px(46);
            string title = "Git Painel";
            int tw = T.Measure(g, title, T.UiBold) + 1;
            T.Text(g, title, T.UiBold, T.Fg, new Rectangle(x, 0, tw, h), 0);
            x += tw + T.Px(14);

            // pasta atual: só o nome; clicar troca de pasta
            string name = Folder.Length == 0 ? "Escolher pasta" : System.IO.Path.GetFileName(Folder.TrimEnd('\\'));
            int maxW = Filter.Left - T.Px(16) - x;
            int nw = Math.Min(T.Measure(g, name, T.Small) + 1, Math.Max(0, maxW - T.Px(52)));
            folderRect = new Rectangle(x, (h - T.Px(28)) / 2, nw + T.Px(52), T.Px(28));
            if (maxW > T.Px(70))
            {
                bool fh = hot == "folder";
                var fr = pressed == "folder" ? new Rectangle(folderRect.X + 1, folderRect.Y + 1, folderRect.Width - 2, folderRect.Height - 2) : folderRect;
                T.FillRound(g, T.Mix(T.Mix(T.Panel, T.Line, 0.45f), T.Line, lvl["folder"]), fr, T.Px(7));
                T.Text(g, G.FolderClosed, T.IconSmall, T.Purple, new Rectangle(folderRect.X + T.Px(10), folderRect.Y, T.Px(14), folderRect.Height), TextFormatFlags.HorizontalCenter);
                T.Text(g, name, T.Small, fh ? T.Fg : T.Mix(T.Comment, T.Fg, 0.55f), new Rectangle(folderRect.X + T.Px(30), folderRect.Y, nw, folderRect.Height), 0);
                T.Text(g, G.Down, T.IconSmall, T.Comment, new Rectangle(folderRect.Right - T.Px(18), folderRect.Y, T.Px(10), folderRect.Height), TextFormatFlags.HorizontalCenter);
            }
            else folderRect = Rectangle.Empty;

            // atualizar (gira enquanto a atualização pedida pelo usuário acontece)
            if (lvl["reload"] > 0) T.FillRound(g, T.Mix(T.Panel, T.Line, lvl["reload"]), reloadRect, T.Px(7));
            if (refreshing || (DateTime.Now - refreshStart).TotalMilliseconds < 500)
            {
                float s = T.Px(15);
                T.Spinner(g, new RectangleF(reloadRect.X + (reloadRect.Width - s) / 2f, reloadRect.Y + (reloadRect.Height - s) / 2f, s, s), T.Purple, T.Px(2));
            }
            else T.Text(g, G.Refresh, T.Icon, T.Mix(T.Purple, T.Fg, lvl["reload"]), reloadRect, TextFormatFlags.HorizontalCenter);

            // botões da janela, no estilo do Windows 11
            var form = FindForm();
            bool maxed = form != null && form.WindowState == FormWindowState.Maximized;
            PaintCaption(g, minRect, "\uE921", "min");
            PaintCaption(g, maxRect, maxed ? "\uE923" : "\uE922", "max");
            PaintCaption(g, closeRect, "\uE8BB", "close");
        }

        void PaintCaption(Graphics g, Rectangle r, string glyph, string id)
        {
            float v = lvl[id];
            bool p = pressed == id;
            Color target = id == "close" ? CloseRed : T.Line;
            if (p) target = T.Mix(target, T.Bg, 0.25f);
            if (v > 0) T.Fill(g, T.Mix(T.Panel, target, v), r);
            T.Text(g, glyph, T.IconSmall, T.Mix(T.Mix(T.Comment, T.Fg, 0.6f), T.Fg, v), r, TextFormatFlags.HorizontalCenter);
        }

        public bool Refreshing
        {
            set
            {
                if (value && !refreshing) refreshStart = DateTime.Now;
                refreshing = value;
                fade.Start();
                Invalidate();
            }
        }

        string HitTest(Point p)
        {
            if (closeRect.Contains(p)) return "close";
            if (maxRect.Contains(p)) return "max";
            if (minRect.Contains(p)) return "min";
            if (reloadRect.Contains(p)) return "reload";
            if (folderRect.Contains(p)) return "folder";
            return null;
        }

        // áreas vazias da barra deixam o Windows tratar como barra de título (arrastar, duplo clique, encaixar)
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) // WM_NCHITTEST
            {
                long lp = m.LParam.ToInt64();
                var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (HitTest(pt) == null) { m.Result = (IntPtr)(-1); return; } // HTTRANSPARENT
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            string h = HitTest(e.Location);
            if (h != hot) { hot = h; fade.Start(); }
            Cursor = h == "folder" || h == "reload" ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = null; pressed = null; fade.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = HitTest(e.Location); Invalidate(); } base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            string h = HitTest(e.Location), p = pressed;
            pressed = null;
            Invalidate();
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || h == null || h != p) return;
            // clique duplo não repete a ação (ex.: abrir o seletor de pasta duas vezes)
            if (h != "max" && !T.Debounce(ref lastAction, 450)) return;
            var form = FindForm();
            switch (h)
            {
                case "close": if (form != null) form.Close(); break;
                case "min": if (form != null) form.WindowState = FormWindowState.Minimized; break;
                case "max":
                    if (form != null) form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                    break;
                case "reload": if (ReloadRequested != null) ReloadRequested(this, EventArgs.Empty); break;
                case "folder": if (PickRequested != null) PickRequested(this, EventArgs.Empty); break;
            }
        }
    }
    class DiffHeader : Control
    {
        public readonly Toggle Mode;
        public readonly ChipToggle Wrap;
        FileChange file;
        DiffData data;
        string note;

        public DiffHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = T.Px(62);
            Mode = new Toggle(new[] { "Unificado", "Lado a lado" });
            Wrap = new ChipToggle("Quebrar linhas");
            Controls.Add(Mode);
            Controls.Add(Wrap);
        }

        public void Set(FileChange f, DiffData d) { file = f; data = d; Invalidate(); }
        public void SetNote(string n) { if (n != note) { note = n; Invalidate(); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Mode == null || Wrap == null) return;
            // em larguras pequenas (histórico aberto) os botões saem para o nome do arquivo caber
            Mode.Visible = Width >= T.Px(560);
            Wrap.Visible = Width >= T.Px(780);
            Mode.Location = new Point(Width - Mode.Width - T.Px(16), (Height - 1 - Mode.Height) / 2);
            Wrap.Location = new Point(Mode.Left - T.Px(8) - Wrap.Width, (Height - 1 - Wrap.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Bg);
            T.Fill(g, T.Border, new Rectangle(0, Height - 1, Width, 1));
            int right = (Wrap.Visible ? Wrap.Left : Mode.Visible ? Mode.Left : Width) - T.Px(16);
            int x = T.Px(20);
            if (file == null)
            {
                T.Text(g, "Nenhum arquivo selecionado", T.Ui, T.Comment, new Rectangle(x, 0, right - x, Height - 1), 0);
                return;
            }

            // estatística +N −M
            if (data != null && data.Title == null)
            {
                string dl = "−" + data.Dels, ad = "+" + data.Adds;
                int dw = T.Measure(g, dl, T.UiBold) + 1, aw = T.Measure(g, ad, T.UiBold) + 1;
                T.Text(g, dl, T.UiBold, T.Red, new Rectangle(right - dw, 0, dw, Height - 1), 0);
                right -= dw + T.Px(10);
                T.Text(g, ad, T.UiBold, T.Green, new Rectangle(right - aw, 0, aw, Height - 1), 0);
                right -= aw + T.Px(20);
            }

            int y1 = T.Px(10), h1 = T.Px(24);
            Color sc = T.StatusColor(file.Kind);
            string chip = T.StatusName(file.Kind);
            int cw = T.Measure(g, chip, T.SmallBold) + T.Px(16), ch = T.Px(20);
            var cr = new Rectangle(x, y1 + (h1 - ch) / 2, cw, ch);
            T.FillRound(g, T.Mix(T.Bg, sc, 0.16f), cr, ch / 2f);
            T.Text(g, chip, T.SmallBold, sc, cr, TextFormatFlags.HorizontalCenter);
            int nx = cr.Right + T.Px(10);
            int nw = Math.Min(T.Measure(g, file.Name, T.UiBold) + 1, right - nx);
            T.Text(g, file.Name, T.UiBold, T.Fg, new Rectangle(nx, y1, nw, h1), 0);
            if (note != null && right - (nx + nw + T.Px(14)) > T.Px(60))
                T.Text(g, note, T.Small, T.Purple, new Rectangle(nx + nw + T.Px(14), y1, right - (nx + nw + T.Px(14)), h1), 0);

            string sub = file.Repo.Name + (file.Folder.Length > 0 ? "  ›  " + file.Folder : "");
            if (file.Orig != null) sub += "      renomeado de " + file.Orig;
            if (file.FromHistory) sub += "      ·   commit " + file.Short + "  " + file.Subject;
            else if (file.Staged) sub += "      • em stage";
            T.Text(g, sub, T.Small, T.Comment, new Rectangle(x, y1 + h1 + T.Px(2), right - x, T.Px(20)), 0);
        }
    }

    class StatusBar : Control
    {
        public string LeftText = "", RightText = "atualização automática     Espaço marca para commit     Ctrl+F buscar     Alt+Z quebrar linhas";
        public bool Busy;
        public string Working; // operação em andamento (push, pull…), mostrada no lugar do resumo
        string flash, flashLink;
        Color flashColor;
        Action flashAction;
        DateTime flashUntil;
        Rectangle linkRect;
        bool overLink;
        readonly Timer flashTimer = new Timer { Interval = 500 };
        readonly Timer pulse = new Timer { Interval = 40 };

        public StatusBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = T.Px(28);
            pulse.Tick += delegate { if (!Busy && Working == null) pulse.Stop(); Invalidate(); };
            flashTimer.Tick += delegate
            {
                if (DateTime.Now > flashUntil) { flashTimer.Stop(); flash = null; flashAction = null; Invalidate(); }
            };
        }

        // mensagem destacada por alguns segundos, com um link de ação opcional (ex.: Desfazer)
        public void Flash(string text, bool ok, string link, Action action)
        {
            flash = text;
            flashColor = ok ? T.Green : T.Orange;
            flashLink = link;
            flashAction = action;
            flashUntil = DateTime.Now.AddSeconds(action != null ? 15 : 8);
            flashTimer.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Border);
            int x = T.Px(14);
            if (Busy || Working != null)
            {
                if (!pulse.Enabled) pulse.Start();
                float d = T.Px(7);
                T.Dot(g, T.Mix(T.Border, T.Purple, 0.45f + 0.55f * T.Pulse), x, (Height - d) / 2f, d);
                x += T.Px(14);
            }
            int rw = T.Measure(g, RightText, T.Small) + 1;
            T.Text(g, RightText, T.Small, T.Mix(T.Comment, T.Border, 0.3f), new Rectangle(Width - rw - T.Px(14), 0, rw, Height), 0);
            int avail = Width - rw - T.Px(40) - x;
            linkRect = Rectangle.Empty;
            if (Working != null)
                T.Text(g, Working, T.Small, T.Purple, new Rectangle(x, 0, avail, Height), 0);
            else if (flash != null)
            {
                T.Text(g, flashColor == T.Green ? G.Check : G.Info, T.IconSmall, flashColor, new Rectangle(x, 0, T.Px(14), Height), TextFormatFlags.HorizontalCenter);
                x += T.Px(20);
                int lw = flashLink != null ? T.Measure(g, flashLink, T.SmallBold) + T.Px(20) : 0;
                int fw = Math.Min(T.Measure(g, flash, T.Small) + 1, avail - lw - T.Px(20));
                T.Text(g, flash, T.Small, flashColor, new Rectangle(x, 0, fw, Height), 0);
                if (flashLink != null)
                {
                    linkRect = new Rectangle(x + fw + T.Px(12), T.Px(4), lw, Height - T.Px(8));
                    T.FillRound(g, overLink ? T.Line : T.Mix(T.Border, T.Line, 0.6f), linkRect, T.Px(5));
                    T.Text(g, flashLink, T.SmallBold, T.Fg, linkRect, TextFormatFlags.HorizontalCenter);
                }
            }
            else T.Text(g, LeftText, T.Small, T.Comment, new Rectangle(x, 0, avail, Height), 0);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool o = linkRect.Contains(e.Location);
            if (o != overLink) { overLink = o; Invalidate(); }
            Cursor = o ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (linkRect.Contains(e.Location) && flashAction != null)
            {
                var a = flashAction;
                flash = null; flashAction = null; flashTimer.Stop(); Invalidate();
                a();
            }
            base.OnMouseDown(e);
        }
    }

    class Divider : Control
    {
        public Control Target;
        bool drag, over;
        int startX, startW;

        public Divider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = T.Px(5);
            Cursor = Cursors.VSplit;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(T.Panel);
            if (over || drag) T.Fill(g, T.A(T.Purple, 160), new Rectangle(Width - T.Px(3), 0, T.Px(2), Height));
            else T.Fill(g, T.Border, new Rectangle(Width - 1, 0, 1, Height));
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            drag = true; startX = Cursor.Position.X; startW = Target.Width;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag && Parent != null)
            {
                int w = startW + Cursor.Position.X - startX;
                w = Math.Max(T.Px(260), Math.Min(Parent.ClientSize.Width - T.Px(360), w));
                if (w != Target.Width) { Target.Width = w; Parent.Update(); }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Invalidate(); base.OnMouseUp(e); }
    }

    // ------------------------------------------------------------------ diálogo no tema

    class MessageDialog : Form
    {
        // primary null = só informativo (um botão "Fechar")
        public MessageDialog(string title, string message, string detail, string primary, Color accent)
        {
            Text = title;
            BackColor = T.Bg;
            ForeColor = T.Fg;
            Font = T.Ui;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            int w = T.Px(620), pad = T.Px(20);

            var head = new Label
            {
                Text = title, Font = T.UiBold, ForeColor = accent, BackColor = T.Bg, AutoSize = false,
                Location = new Point(pad, T.Px(18)), Size = new Size(w - 2 * pad, T.Px(24))
            };
            Controls.Add(head);
            int y = head.Bottom + T.Px(8);

            if (!string.IsNullOrEmpty(message))
            {
                var msg = new Label
                {
                    Text = message, ForeColor = T.Mix(T.Fg, T.Comment, 0.25f), BackColor = T.Bg, AutoSize = true,
                    MaximumSize = new Size(w - 2 * pad, 0), Location = new Point(pad, y)
                };
                Controls.Add(msg);
                y = msg.Bottom + T.Px(14);
            }

            if (!string.IsNullOrEmpty(detail))
            {
                // altura acompanha o conteúdo; rolagem (escura) só quando passa do limite
                string text = detail.Replace("\r\n", "\n").Trim('\n');
                // conta também as linhas que vão quebrar por serem mais largas que a caixa
                int perLine = Math.Max(20, (w - 2 * pad - T.Px(40)) / Math.Max(1, TextRenderer.MeasureText("M", T.MonoSmall, Size.Empty, TextFormatFlags.NoPadding).Width));
                int lines = text.Split('\n').Sum(l => Math.Max(1, (l.Length + perLine - 1) / perLine));
                int want = lines * (T.MonoSmall.Height + 1) + T.Px(26);
                bool scroll = want > T.Px(220);
                var frame = new Panel { BackColor = T.Panel, Location = new Point(pad, y), Size = new Size(w - 2 * pad, Math.Min(want, T.Px(220))), Padding = new Padding(T.Px(12)) };
                var box = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = scroll ? ScrollBars.Vertical : ScrollBars.None, BorderStyle = BorderStyle.None,
                    BackColor = T.Panel, ForeColor = T.Fg, Font = T.MonoSmall, Dock = DockStyle.Fill, TabStop = false,
                    Text = text.Replace("\n", "\r\n")
                };
                box.HandleCreated += delegate { Native.DarkScroll(box.Handle); };
                frame.Controls.Add(box);
                Controls.Add(frame);
                y = frame.Bottom + T.Px(16);
            }

            var ok = new PrimaryButton { Text = primary ?? "Fechar", Back = T.Bg, Accent = primary == null ? T.Line : accent, Width = T.Px(150) };
            if (primary == null) ok.Accent = T.Purple;
            ok.Location = new Point(w - pad - ok.Width, y);
            ok.Click += delegate { DialogResult = primary == null ? DialogResult.Cancel : DialogResult.OK; Close(); };
            Controls.Add(ok);
            if (primary != null)
            {
                var cancel = new FlatButton(null, "Cancelar") { Back = T.Bg, Height = T.Px(38), Width = T.Px(120) };
                cancel.Location = new Point(ok.Left - T.Px(10) - cancel.Width, y);
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cancel);
            }
            ClientSize = new Size(w, y + ok.Height + pad);
            ActiveControl = ok; // nada de texto selecionado ao abrir
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                else if (e.KeyCode == Keys.Enter) { DialogResult = primary == null ? DialogResult.Cancel : DialogResult.OK; Close(); }
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(Handle, T.Border, T.Fg);
        }

        public static bool Confirm(IWin32Window owner, string title, string message, string detail, string primary, Color accent)
        {
            using (var d = new MessageDialog(title, message, detail, primary, accent)) return d.ShowDialog(owner) == DialogResult.OK;
        }

        // erro com explicação em português quando o git devolve uma falha conhecida
        public static void Error(IWin32Window owner, string title, string detail)
        {
            using (var d = new MessageDialog(title, Git.Explain(detail), detail, null, T.Red)) d.ShowDialog(owner);
        }
    }

    // ------------------------------------------------------------------ seletor de pasta moderno

    static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        public static string Pick(IWin32Window owner, string initial)
        {
            try
            {
                var dlg = (IFileDialog)new FileOpenDialogRCW();
                uint opts;
                dlg.GetOptions(out opts);
                dlg.SetOptions(opts | 0x20 | 0x40); // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
                dlg.SetTitle("Escolha a pasta com os projetos");
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
                {
                    Guid iid = typeof(IShellItem).GUID;
                    IShellItem it;
                    if (SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out it) == 0) dlg.SetFolder(it);
                }
                if (dlg.Show(owner == null ? IntPtr.Zero : owner.Handle) != 0) return null;
                IShellItem res;
                dlg.GetResult(out res);
                string path;
                res.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
                return path;
            }
            catch
            {
                using (var f = new FolderBrowserDialog())
                {
                    if (!string.IsNullOrEmpty(initial)) f.SelectedPath = initial;
                    return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
                }
            }
        }
    }
}
