// 본문 화면: 글을 줄로 나눠 직접 그린다(글자 선택·밑줄 없음). 페이지 방식과 스크롤 방식.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovelViewer
{
    public class ReaderView : Control
    {
        public float UiScale = 1f;
        int textW;
        public float LineHeightMul = 1.9f;
        public int SideMargin = 48;     // 양옆 여백(px)
        public int TopMargin = 32;      // 위아래 여백(px)
        public bool PageMode = true;
        public Color Fg = Color.Black, Muted = Color.Gray;
        public string Placeholder = "";

        public event Action Changed;          // 페이지·스크롤 위치가 바뀜
        public event Action<int> Edge;        // 끝(1)·처음(-1)에서 더 넘김
        public event Action Center;           // 가운데를 누름

        Font font;
        List<string> paras = new List<string>();
        readonly List<int> lineP = new List<int>();     // 각 줄이 속한 문단
        readonly List<string> lineT = new List<string>();
        int lineH = 30, fontH = 18, top = 24, perPage = 1, first, scrollY, x0;   // first: 페이지 방식에서 화면 맨 윗줄 번호
        readonly VScrollBar bar = new VScrollBar();

        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        public ReaderView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            bar.Dock = DockStyle.Right;
            bar.Visible = false;
            bar.Scroll += (s, e) => { scrollY = bar.Value; Invalidate(); Fire(); };
            Controls.Add(bar);
        }

        public bool HasText { get { return paras.Count > 0; } }
        public int ParagraphCount { get { return paras.Count; } }
        public string ParagraphText(int i) { return i >= 0 && i < paras.Count ? paras[i] : ""; }

        public Font TextFont
        {
            get { return font; }
            set { font = value; }
        }

        public void SetText(string text)
        {
            paras = text == null ? new List<string>() : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
            first = 0; scrollY = 0;
            layoutKey = null;
            Reflow();
        }

        void Fire() { if (Changed != null) Changed(); }

        // ---- 줄 나누기 ----
        int LineW(string s, int from, int to)
        {
            string t = s.Substring(from, to - from).Replace("\t", "    ");
            return TextRenderer.MeasureText(t, font, new Size(int.MaxValue, int.MaxValue), Flags).Width;
        }

        int ViewW { get { return ClientSize.Width - (bar.Visible ? bar.Width : 0); } }

        // 크기·글꼴·방식이 바뀌면 줄을 다시 나눈다. 읽던 문단은 그대로 보여 준다.
        // 높이만 바뀐 경우(메뉴 숨기기·보이기 등)는 줄을 다시 나누지 않고, 맨 윗줄을 그대로 둔 채 아래로 더 보여 준다.
        public void Relayout()
        {
            string key = ViewW + "|" + (font == null ? 0 : font.Size) + "|" + LineHeightMul + "|" + SideMargin + "|" + TopMargin + "|" + PageMode;
            if (key == layoutKey && lineT.Count > 0)
            {
                perPage = Math.Max(1, (ClientSize.Height - top * 2) / lineH);
                first = Math.Max(0, Math.Min(first, lineT.Count - 1));
                UpdateBar();
                Invalidate(); Fire();
                return;
            }
            int keep = TopParagraph();
            Reflow();
            layoutKey = key;
            ShowParagraph(keep);
        }

        string layoutKey;

        void Reflow()
        {
            lineP.Clear(); lineT.Clear();
            if (font == null) return;
            bar.Visible = !PageMode && HasText;
            fontH = TextRenderer.MeasureText("가", font, new Size(int.MaxValue, int.MaxValue), Flags).Height;
            lineH = Math.Max(fontH + 1, (int)Math.Round(font.Size * LineHeightMul));
            // 글 폭은 창 폭에서 양옆 여백을 뺀 만큼. 창 크기가 바뀌면 그에 맞춰 줄을 다시 나눈다.
            top = (int)(TopMargin * UiScale);
            int side = (int)(SideMargin * UiScale);
            int w = Math.Max(100, ViewW - side * 2);
            x0 = Math.Max(0, (ViewW - w) / 2);
            textW = w;
            perPage = Math.Max(1, (ClientSize.Height - top * 2) / lineH);

            for (int p = 0; p < paras.Count; p++)
            {
                string s = paras[p];
                if (s.Length == 0) { lineP.Add(p); lineT.Add(""); continue; }
                int i = 0;
                while (i < s.Length)
                {
                    // 글자 하나씩 잰 폭을 더하면 실제보다 넓게 나와 줄이 일찍 끊긴다.
                    // 그래서 줄 후보 전체를 실제로 재서, 들어가는 가장 긴 길이를 이분 탐색으로 찾는다.
                    int lo = i + 1, hi = s.Length;
                    if (LineW(s, i, hi) <= w) lo = hi;
                    else
                    {
                        hi--;
                        while (lo < hi)
                        {
                            int mid = (lo + hi + 1) / 2;
                            if (LineW(s, i, mid) <= w) lo = mid; else hi = mid - 1;
                        }
                    }
                    int j = lo;
                    // 낱말 중간에서 자르지 않는다(띄어쓰기에서 줄을 바꿈).
                    if (j < s.Length && s[j] != ' ')
                    {
                        int lastSpace = s.LastIndexOf(' ', j - 1, j - i);
                        if (lastSpace > i) j = lastSpace + 1;
                    }
                    lineP.Add(p);
                    lineT.Add(s.Substring(i, j - i).TrimEnd());
                    i = j;
                    // 줄 첫머리의 띄어쓰기는 버린다(문단 첫 줄 들여쓰기는 그대로 둔다).
                    while (i < s.Length && s[i] == ' ') i++;
                }
            }
            UpdateBar();
            Invalidate();
        }

        int MaxScroll { get { return Math.Max(0, top + lineT.Count * lineH + top - ClientSize.Height); } }

        void UpdateBar()
        {
            if (!bar.Visible) return;
            int view = Math.Max(1, ClientSize.Height);
            bar.Minimum = 0;
            bar.LargeChange = view;
            bar.SmallChange = lineH;
            bar.Maximum = MaxScroll + view - 1;
            bar.Value = Math.Max(0, Math.Min(scrollY, MaxScroll));
        }

        // ---- 위치 ----
        // 맨 윗줄이 페이지 경계와 어긋날 수 있으므로(메뉴를 숨겨 화면이 길어졌을 때 등) 앞뒤 쪽수를 따로 센다.
        int PagesBefore { get { return (first + perPage - 1) / perPage; } }
        public int PageCount { get { return Math.Max(1, PagesBefore + (Math.Max(0, lineT.Count - first) + perPage - 1) / perPage); } }
        public int PageIndex { get { return Math.Min(PagesBefore, PageCount - 1); } }
        public int ScrollPermille { get { return MaxScroll > 0 ? (int)((long)scrollY * 1000 / MaxScroll) : 1000; } }

        public void GoPage(int i)
        {
            i = Math.Max(0, Math.Min(PageCount - 1, i));
            if (i == PageIndex) return;
            SetFirst(i * perPage);
        }

        void SetFirst(int line)
        {
            first = Math.Max(0, Math.Min(Math.Max(0, lineT.Count - 1), line));
            Invalidate(); Fire();
        }

        public void SetScroll(int y)
        {
            scrollY = Math.Max(0, Math.Min(MaxScroll, y));
            if (bar.Visible) bar.Value = scrollY;
            Invalidate(); Fire();
        }

        public void SetScrollPermille(int v) { SetScroll((int)((long)MaxScroll * v / 1000)); }

        public void GoStart() { if (PageMode) GoPage(0); else SetScroll(0); }
        public void GoEnd() { if (PageMode) SetFirst(Math.Max(0, (lineT.Count - 1) / perPage * perPage)); else SetScroll(MaxScroll); }

        // 화면 맨 위에 보이는 문단 번호(읽던 곳 기억·책갈피에 쓴다)
        public int TopParagraph()
        {
            if (lineT.Count == 0) return 0;
            int idx = PageMode ? first :(scrollY + lineH / 2) / lineH;
            idx = Math.Max(0, Math.Min(lineT.Count - 1, idx));
            int p = lineP[idx];
            while (p < paras.Count - 1 && paras[p].Trim().Length == 0) p++;
            return p;
        }

        public void ShowParagraph(int p)
        {
            int idx = lineP.FindIndex(x => x >= p);
            if (idx < 0) idx = Math.Max(0, lineT.Count - 1);
            if (PageMode) SetFirst(idx);
            else SetScroll(idx * lineH);
        }

        // 한 페이지 넘기기. 끝에서 더 넘기면 Edge 를 알린다.
        public void Turn(int dir)
        {
            if (!HasText) return;
            if (PageMode)
            {
                if (dir > 0 && first + perPage >= lineT.Count) { if (Edge != null) Edge(1); return; }
                if (dir < 0 && first <= 0) { if (Edge != null) Edge(-1); return; }
                SetFirst(first + dir * perPage);
            }
            else
            {
                if (dir > 0 && scrollY >= MaxScroll) { if (Edge != null) Edge(1); return; }
                if (dir < 0 && scrollY <= 0) { if (Edge != null) Edge(-1); return; }
                SetScroll(scrollY + dir * (ClientSize.Height - lineH * 2));
            }
        }

        // ---- 그리기 ----
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (!HasText || font == null)
            {
                TextRenderer.DrawText(g, Placeholder, Font, new Rectangle(0, 0, ViewW, ClientSize.Height), Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            int dy = (lineH - fontH) / 2;
            if (PageMode)
            {
                for (int k = 0; k < perPage && first + k < lineT.Count; k++)
                    DrawLine(g, first + k, top + k * lineH + dy);
            }
            else
            {
                int first = Math.Max(0, (scrollY - top) / lineH);
                for (int idx = first; idx < lineT.Count; idx++)
                {
                    int y = top + idx * lineH - scrollY;
                    if (y > ClientSize.Height) break;
                    DrawLine(g, idx, y + dy);
                }
            }
        }

        // 문단 중간에서 끊긴 줄은 낱말 사이를 고르게 벌려 오른쪽 끝도 여백에 맞춘다(양쪽 맞춤).
        // 문단의 마지막 줄은 그대로 왼쪽에 붙인다.
        void DrawLine(Graphics g, int idx, int y)
        {
            string s = lineT[idx];
            bool wrapped = idx + 1 < lineT.Count && lineP[idx + 1] == lineP[idx];
            string[] words = wrapped ? s.Split(' ') : null;
            if (words == null || words.Length < 2)
            {
                TextRenderer.DrawText(g, s, font, new Point(x0, y), Fg, Flags);
                return;
            }
            var big = new Size(int.MaxValue, int.MaxValue);
            int[] ww = new int[words.Length];
            int sum = 0;
            for (int i = 0; i < words.Length; i++) { ww[i] = TextRenderer.MeasureText(words[i], font, big, Flags).Width; sum += ww[i]; }
            float gap = (float)(textW - sum) / (words.Length - 1);
            // 너무 벌어지면(낱말이 아주 긴 경우) 보기 흉하니 그냥 왼쪽 정렬
            if (gap > font.Size * 2) { TextRenderer.DrawText(g, s, font, new Point(x0, y), Fg, Flags); return; }
            float x = x0;
            for (int i = 0; i < words.Length; i++)
            {
                TextRenderer.DrawText(g, words[i], font, new Point((int)Math.Round(x), y), Fg, Flags);
                x += ww[i] + gap;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (font != null) Relayout();
        }

        // 위쪽 1/3 = 이전 페이지, 가운데 = 메뉴, 아래쪽 1/3 = 다음 페이지 (스크롤 방식에서는 가운데만 동작)
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            int h = ClientSize.Height;
            int zone = e.Y < h / 3 ? -1 : e.Y > h * 2 / 3 ? 1 : 0;
            if (zone == 0 || !HasText) { if (Center != null) Center(); }
            else if (PageMode) Turn(zone);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!HasText) return;
            if (PageMode) Turn(e.Delta > 0 ? -1 : 1);
            else SetScroll(scrollY - e.Delta / 120 * lineH * 3);
        }
    }

    // 페이지 이동 바: 끌어서 원하는 페이지로 바로 간다.
    public class SeekBar : Control
    {
        public int Max = 0, Value = 0;
        public Color Track = Color.LightGray, Accent = Color.RoyalBlue;
        public event Action<int> Scrub;
        bool dragging;

        public SeekBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        int Pad { get { return Height / 2; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int y = Height / 2, w = Width - Pad * 2;
            float t = Max > 0 ? (float)Value / Max : 0;
            int th = Math.Max(4, Height / 8);
            using (var b = new SolidBrush(Track)) g.FillRectangle(b, Pad, y - th / 2, w, th);
            using (var b = new SolidBrush(Accent))
            {
                g.FillRectangle(b, Pad, y - th / 2, w * t, th);
                float r = Height * 0.28f;
                g.FillEllipse(b, Pad + w * t - r, y - r, r * 2, r * 2);
            }
        }

        void SetFromMouse(int x)
        {
            int w = Math.Max(1, Width - Pad * 2);
            int v = (int)Math.Round((double)(x - Pad) / w * Max);
            v = Math.Max(0, Math.Min(Max, v));
            if (v != Value) { Value = v; Invalidate(); if (Scrub != null) Scrub(v); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { dragging = true; SetFromMouse(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }
    }
}
