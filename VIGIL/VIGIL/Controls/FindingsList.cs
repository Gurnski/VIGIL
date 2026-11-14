using System.ComponentModel;
using System.Globalization;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  The ranked list of findings: rank, the service's one-line summary, then the kind, place
    ///  and missions underneath, with the ranking score on the right. Rows stay in the order they
    ///  are given, which is rank order. Each row is as tall as its text needs, so a summary is
    ///  not cut short at the width of a side panel. Only the rows in view are drawn, so the list
    ///  copes with the thousands of findings a real scan gives. Scroll with the wheel, the keys
    ///  or the bar.
    /// </summary>
    [DefaultEvent(nameof(SelectedChanged))]
    public class FindingsList : VigilControl
    {
        private const TextFormatFlags WrappedText =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private const int SummaryLines = 4;
        private const int DetailLines = 2;

        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

        private IReadOnlyList<Finding> findings = Array.Empty<Finding>();
        private int selected = -1;
        private int hovered = -1;

        // rowTops[i] is where row i starts, in pixels from the top of the list, and the last entry
        // is the height of all the rows. Worked out again when the rows or the width change.
        private int[] rowTops = { 0 };
        private int measuredWidth = -1;
        private int scrollOffset;
        private bool draggingThumb;
        private int dragGrip;

        /// <summary>
        ///  Raised when the selection changes, and again when the selected row is clicked, so a
        ///  page can go back to a finding the user has since moved away from.
        /// </summary>
        public event EventHandler? SelectedChanged;

        public FindingsList()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override Size DefaultSize => new(360, 400);

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<Finding> Findings => findings;

        /// <summary>Replaces the rows and clears the selection.</summary>
        public void SetFindings(IReadOnlyList<Finding> items)
        {
            findings = items ?? Array.Empty<Finding>();
            selected = -1;
            hovered = -1;
            scrollOffset = 0;
            measuredWidth = -1;
            Invalidate();
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => selected;
            set
            {
                if (value == selected || value < -1 || value >= findings.Count)
                {
                    return;
                }

                selected = value;
                if (value >= 0)
                {
                    ScrollIntoView(value);
                }
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Browsable(false)]
        public Finding? SelectedFinding => selected >= 0 ? findings[selected] : null;

        /// <summary>Selects a finding by its id and scrolls to it. Null, or one not in the list, clears the selection.</summary>
        public void SelectFinding(Finding? finding)
        {
            int index = -1;
            for (int i = 0; finding != null && i < findings.Count; i++)
            {
                if (findings[i].Id == finding.Id)
                {
                    index = i;
                    break;
                }
            }
            SelectedIndex = index;
        }

        /// <summary>A contract id as a label: "Change point" for <c>change_point</c>, "Soil moisture" for <c>soil_moisture</c>.</summary>
        public static string LabelText(string id)
        {
            string words = id.Replace('_', ' ');
            return words.Length == 0 ? "" : char.ToUpper(words[0], British) + words[1..];
        }

        /// <summary>The centre of a cell as "8.8°S 51.3°W".</summary>
        public static string PlaceText(Cell cell)
        {
            double latitude = cell.CentreLatitude;
            double longitude = cell.CentreLongitude;
            return string.Format(British, "{0:0.0}°{1} {2:0.0}°{3}",
                Math.Abs(latitude), latitude < 0 ? "S" : "N", Math.Abs(longitude), longitude < 0 ? "W" : "E");
        }

        private int RankWidth => Px(28);
        private int ScoreWidth => Px(36);
        private int RowPadding => Px(Theme.Space3);
        private int LineGap => Px(2);
        private int WheelStep => Px(72);

        /// <summary>Room kept clear on the right for the scroll bar, whether or not it is showing, so text never reflows when it appears.</summary>
        private int BarSpace => Px(Theme.Space3);

        private int TextLeft => RowPadding + RankWidth;
        private int TextWidth => Width - BarSpace - RowPadding - ScoreWidth - Px(Theme.Space2) - TextLeft;

        private int ContentHeight
        {
            get
            {
                MeasureRows();
                return rowTops[^1];
            }
        }

        private int MaxScroll => Math.Max(0, ContentHeight - Height);

        private Rectangle RowBounds(int index)
        {
            MeasureRows();
            return new Rectangle(0, rowTops[index] - scrollOffset, Width - BarSpace, rowTops[index + 1] - rowTops[index]);
        }

        /// <summary>Height of a block of text wrapped to the row's text width, cut off after a number of lines.</summary>
        private int TextHeight(string text, Font font, int maxLines)
        {
            int height = TextRenderer.MeasureText(text, font, new Size(Math.Max(1, TextWidth), int.MaxValue), WrappedText).Height;
            return Math.Clamp(height, font.Height, font.Height * maxLines);
        }

        private int SummaryHeight(Finding finding) => TextHeight(finding.Summary, Theme.Body, SummaryLines);
        private int DetailHeight(Finding finding) => TextHeight(DetailText(finding), Theme.Caption, DetailLines);

        private void MeasureRows()
        {
            if (measuredWidth == Width && rowTops.Length == findings.Count + 1)
            {
                return;
            }
            measuredWidth = Width;
            rowTops = new int[findings.Count + 1];
            for (int i = 0; i < findings.Count; i++)
            {
                rowTops[i + 1] = rowTops[i] + RowPadding * 2 + SummaryHeight(findings[i]) + LineGap + DetailHeight(findings[i]);
            }
        }

        /// <summary>The row covering a distance from the top of the list, or -1 past the last row.</summary>
        private int RowAt(int y)
        {
            MeasureRows();
            if (y < 0 || y >= rowTops[^1])
            {
                return -1;
            }
            int index = Array.BinarySearch(rowTops, y);
            return index >= 0 ? index : ~index - 1;
        }

        private Rectangle ThumbBounds
        {
            get
            {
                if (MaxScroll == 0)
                {
                    return Rectangle.Empty;
                }
                int length = Math.Max(Px(24), (int)((long)Height * Height / ContentHeight));
                int top = (int)((long)(Height - length) * scrollOffset / MaxScroll);
                return new Rectangle(Width - Px(4), top, Px(4), length);
            }
        }

        private int HitTest(Point location)
        {
            if (location.X < 0 || location.X >= Width - BarSpace || location.Y < 0 || location.Y >= Height)
            {
                return -1;
            }
            return RowAt(location.Y + scrollOffset);
        }

        private void ScrollTo(int offset)
        {
            offset = Math.Clamp(offset, 0, MaxScroll);
            if (offset != scrollOffset)
            {
                scrollOffset = offset;
                Invalidate();
            }
        }

        private void ScrollIntoView(int index)
        {
            MeasureRows();
            if (rowTops[index] < scrollOffset)
            {
                ScrollTo(rowTops[index]);
            }
            else if (rowTops[index + 1] > scrollOffset + Height)
            {
                ScrollTo(rowTops[index + 1] - Height);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            scrollOffset = Math.Clamp(scrollOffset, 0, MaxScroll);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            if (findings.Count == 0)
            {
                Draw.Text(g, "No findings", Theme.Body, new Rectangle(0, 0, Width, Px(Theme.ControlHeight)), Theme.TextMuted);
                return;
            }

            for (int i = Math.Max(0, RowAt(scrollOffset)); i < findings.Count; i++)
            {
                var row = RowBounds(i);
                if (row.Top >= Height)
                {
                    break;
                }
                DrawRow(g, findings[i], row, i);
            }

            var thumb = ThumbBounds;
            if (!thumb.IsEmpty)
            {
                Draw.RoundedBox(g, thumb, Px(2f), draggingThumb ? Theme.TextMuted : Theme.Border);
            }
        }

        private void DrawRow(Graphics g, Finding finding, Rectangle row, int index)
        {
            bool isSelected = index == selected;
            if (isSelected)
            {
                Draw.RoundedBox(g, row, Px(Theme.RadiusSmall), Theme.SurfaceHover);
                using var bar = new SolidBrush(Theme.Accent);
                g.FillRectangle(bar, row.X, row.Y + RowPadding, Px(2), row.Height - RowPadding * 2);
            }
            else if (index == hovered)
            {
                Draw.RoundedBox(g, row, Px(Theme.RadiusSmall), Theme.SurfaceRaised);
            }
            else if (index < findings.Count - 1 && index + 1 != selected && index + 1 != hovered)
            {
                Draw.HorizontalLine(g, row.X, row.Right - 1, row.Bottom - 1, Theme.Divider);
            }

            if (isSelected && Focused && ShowFocusCues)
            {
                using var path = Draw.RoundedRect(new RectangleF(row.X + 0.5f, row.Y + 0.5f, row.Width - 1f, row.Height - 1f), Px(Theme.RadiusSmall));
                using var ring = new Pen(Theme.TextSecondary);
                g.DrawPath(ring, path);
            }

            int textWidth = TextWidth;
            if (textWidth <= 0)
            {
                return;
            }

            int lineHeight = Theme.Body.Height;
            int summaryHeight = SummaryHeight(finding);
            int top = row.Y + RowPadding;

            // Not significant is said in the quieter text colour, as everywhere else in the app.
            var summaryColour = finding.Significant ? Theme.TextPrimary : Theme.TextSecondary;
            Draw.Text(g, finding.Summary, Theme.Body, new Rectangle(TextLeft, top, textWidth, summaryHeight), summaryColour, WrappedText);
            Draw.Text(g, DetailText(finding), Theme.Caption,
                new Rectangle(TextLeft, top + summaryHeight + LineGap, textWidth, DetailHeight(finding)), Theme.TextMuted, WrappedText);

            var firstLine = new Rectangle(row.X + RowPadding, top, RankWidth, lineHeight);
            Draw.Text(g, finding.Rank.ToString(British), Theme.NumericSmall, firstLine, Theme.TextMuted);
            firstLine = new Rectangle(row.Right - RowPadding - ScoreWidth, top, ScoreWidth, lineHeight);
            Draw.Text(g, finding.Score.Total.ToString("0.00", British), Theme.NumericSmall, firstLine, Theme.TextSecondary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static string DetailText(Finding finding)
        {
            string text = LabelText(finding.Kind) + ", " + PlaceText(finding.Cell);
            string missions = string.Join(" and ", finding.Variables.Select(variable => variable.Mission).Where(mission => mission.Length > 0).Distinct());
            if (missions.Length > 0)
            {
                text += ", " + missions;
            }
            return finding.Significant ? text : text + ", not significant";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (draggingThumb)
            {
                int travel = Height - ThumbBounds.Height;
                if (travel > 0)
                {
                    ScrollTo((int)((long)(e.Y - dragGrip) * MaxScroll / travel));
                }
                return;
            }

            int hit = HitTest(e.Location);
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            if (hit != hovered)
            {
                hovered = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered != -1)
            {
                hovered = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            Focus();

            if (MaxScroll > 0 && e.X >= Width - BarSpace)
            {
                // A press on the bar away from the thumb brings the thumb's middle to the pointer.
                var thumb = ThumbBounds;
                dragGrip = thumb.Contains(thumb.X, e.Y) ? e.Y - thumb.Y : thumb.Height / 2;
                draggingThumb = true;
                OnMouseMove(e);
                Invalidate();
                return;
            }

            int hit = HitTest(e.Location);
            if (hit < 0)
            {
                return;
            }
            if (hit == selected)
            {
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SelectedIndex = hit;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggingThumb)
            {
                draggingThumb = false;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(scrollOffset - e.Delta * WheelStep / 120);
            hovered = HitTest(PointToClient(MousePosition));
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (findings.Count == 0)
            {
                return;
            }

            int page = Math.Max(1, Height / WheelStep);
            int target = e.KeyCode switch
            {
                Keys.Up => selected - 1,
                Keys.Down => selected + 1,
                Keys.PageUp => selected - page,
                Keys.PageDown => selected + page,
                Keys.Home => 0,
                Keys.End => findings.Count - 1,
                _ => int.MinValue
            };
            if (target != int.MinValue)
            {
                SelectedIndex = Math.Clamp(target, 0, findings.Count - 1);
                e.Handled = true;
            }
        }
    }
}
