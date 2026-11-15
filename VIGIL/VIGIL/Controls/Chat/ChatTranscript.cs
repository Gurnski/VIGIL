using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls.Chat
{
    /// <summary>
    ///  The messages of one conversation, drawn top to bottom. It is as tall as its content:
    ///  <see cref="ChatView"/> puts it in a scrolling panel. With no messages it shows a short
    ///  introduction and example questions to click.
    ///
    ///  A question sits on the right in a raised box. An answer runs the full width with no
    ///  box, followed by what the Detective looked up to reach it (each lookup clickable, to see
    ///  on the globe where it looked), the findings it rests on (each one clickable) and its caveats.
    /// </summary>
    public class ChatTranscript : VigilControl
    {
        private const TextFormatFlags Wrapped =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        private readonly List<Piece> pieces = new();
        private readonly List<(Rectangle Bounds, string Value, bool IsCitation, AskStep? Step)> links = new();
        private IReadOnlyList<ChatMessage> messages = Array.Empty<ChatMessage>();
        private IReadOnlyList<string> suggestions = Array.Empty<string>();
        private bool waiting;
        private int hoveredLink = -1;
        private int laidOutWidth = -1;

        /// <summary>Raised with the question when an example question is clicked.</summary>
        public event EventHandler<string>? SuggestionClicked;

        /// <summary>Raised with the finding id when a cited finding is clicked.</summary>
        public event EventHandler<string>? CitationClicked;

        /// <summary>Raised when one of the Detective's lookups is clicked.</summary>
        public event EventHandler<AskStep>? StepClicked;

        protected override Size DefaultSize => Theme.Scaled(360, 200);

        /// <summary>Example questions shown while the conversation is empty.</summary>
        public void SetSuggestions(IReadOnlyList<string> questions)
        {
            suggestions = questions;
            Relayout();
        }

        /// <summary>Shows a conversation's messages, with a "working" line at the end while waiting.</summary>
        public void SetMessages(IReadOnlyList<ChatMessage> items, bool isWaiting)
        {
            messages = items;
            waiting = isWaiting;
            Relayout();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width != laidOutWidth)
            {
                Relayout();
            }
        }

        /// <summary>One run of text, or a filled box behind text, placed by <see cref="Relayout"/>.</summary>
        private readonly record struct Piece(Rectangle Bounds, string Text, Font? Font, Color Colour, Color? Box, int Link);

        private void Relayout()
        {
            pieces.Clear();
            links.Clear();
            laidOutWidth = Width;
            int width = Math.Max(Px(120), Width);
            int y = Px(Theme.Space2);

            if (messages.Count == 0)
            {
                y = LayoutIntroduction(width, y);
            }

            foreach (var message in messages)
            {
                y = message.Role == ChatRole.User ? LayoutQuestion(message, width, y) : LayoutAnswer(message, width, y);
                y += Px(Theme.Space5);
            }

            if (waiting)
            {
                y = AddText("Looking through the findings…", Theme.Body, Theme.TextMuted, 0, y, width);
                y += Px(Theme.Space5);
            }

            int height = y + Px(Theme.Space1);
            if (Height != height)
            {
                Height = height;
            }
            Invalidate();
        }

        private int LayoutIntroduction(int width, int y)
        {
            y = AddText("Ask about this scan", Theme.Title, Theme.TextPrimary, 0, y, width) + Px(Theme.Space2);
            y = AddText("The Detective answers only from the scan's findings and names the ones it used. "
                + "When the results can't settle a question, it says so.", Theme.Body, Theme.TextSecondary, 0, y, width);
            if (suggestions.Count == 0)
            {
                return y;
            }

            y += Px(Theme.Space5);
            y = AddText("Try one of these", Theme.CaptionStrong, Theme.TextSecondary, 0, y, width) + Px(Theme.Space2);
            foreach (string question in suggestions)
            {
                int pad = Px(Theme.Space3);
                var size = Measure(question, Theme.Body, width - pad * 2);
                var box = new Rectangle(0, y, width, size.Height + pad * 2);
                links.Add((box, question, false, null));
                pieces.Add(new Piece(box, "", null, Color.Empty, Theme.SurfaceRaised, links.Count - 1));
                pieces.Add(new Piece(new Rectangle(pad, y + pad, width - pad * 2, size.Height), question, Theme.Body, Theme.TextPrimary, null, -1));
                y = box.Bottom + Px(Theme.Space2);
            }
            return y;
        }

        private int LayoutQuestion(ChatMessage message, int width, int y)
        {
            int pad = Px(Theme.Space3);
            int room = (int)(width * 0.86f) - pad * 2;
            var textSize = Measure(message.Text, Theme.Body, room);
            var contextSize = message.ContextSummary == null ? Size.Empty : Measure("About: " + message.ContextSummary, Theme.Caption, room);
            int inner = Math.Max(textSize.Width, contextSize.Width);
            int height = textSize.Height + (contextSize.IsEmpty ? 0 : contextSize.Height + Px(Theme.Space1));
            var box = new Rectangle(width - inner - pad * 2, y, inner + pad * 2, height + pad * 2);
            pieces.Add(new Piece(box, "", null, Color.Empty, Theme.SurfaceRaised, -1));

            int textY = box.Y + pad;
            if (!contextSize.IsEmpty)
            {
                pieces.Add(new Piece(new Rectangle(box.X + pad, textY, inner, contextSize.Height),
                    "About: " + message.ContextSummary, Theme.Caption, Theme.TextSecondary, null, -1));
                textY += contextSize.Height + Px(Theme.Space1);
            }
            pieces.Add(new Piece(new Rectangle(box.X + pad, textY, inner, textSize.Height), message.Text, Theme.Body, Theme.TextPrimary, null, -1));
            return box.Bottom;
        }

        private int LayoutAnswer(ChatMessage message, int width, int y)
        {
            // A line above the answer when it is anything other than a plain answer.
            (string Text, Color Colour)? label = message.Source == AnswerSource.NotConnected ? ("No answer", Theme.Negative)
                : message.Status == "inconclusive" ? ("The results can't settle this", Theme.Warning)
                : message.Status == "out_of_scope" ? ("Outside what the scan covers", Theme.TextSecondary)
                : null;
            if (label is var (text, colour))
            {
                y = AddText(text, Theme.CaptionStrong, colour, 0, y, width) + Px(Theme.Space1);
            }

            y = AddText(message.Text, Theme.Body, message.Source == AnswerSource.NotConnected ? Theme.TextSecondary : Theme.TextPrimary, 0, y, width);

            if (message.Steps.Count > 0)
            {
                y += Px(Theme.Space3);
                string heading = message.Steps.Count == 1 ? "It looked one thing up" : "It looked " + message.Steps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " things up";
                y = AddText(heading, Theme.CaptionStrong, Theme.TextSecondary, 0, y, width) + Px(Theme.Space1);
                foreach (var step in message.Steps)
                {
                    string line = step.Summary + StepCount(step);
                    var size = Measure(line, Theme.Caption, width);
                    var bounds = new Rectangle(0, y, width, size.Height);
                    // A lookup that named a place, or returned findings, can be shown on the globe.
                    bool shows = step.Box != null || step.FindingIds.Count > 0;
                    if (shows)
                    {
                        links.Add((new Rectangle(0, y, Math.Min(size.Width + 1, width), size.Height), "", false, step));
                    }
                    pieces.Add(new Piece(bounds, line, Theme.Caption, step.Failed ? Theme.TextMuted : Theme.TextSecondary, null, shows ? links.Count - 1 : -1));
                    y = bounds.Bottom + Px(2);
                }
                // What it read is not what it relied on, and the answer must not be taken to rest on all of it.
                y = AddText("Only the findings under \"Based on\" back the answer.", Theme.Caption, Theme.TextMuted, 0, y + Px(2), width);
            }

            if (message.Citations.Count > 0)
            {
                y += Px(Theme.Space3);
                y = AddText("Based on", Theme.CaptionStrong, Theme.TextSecondary, 0, y, width) + Px(Theme.Space1);
                foreach (var citation in message.Citations)
                {
                    var idSize = Measure(citation.FindingId, Theme.NumericSmall, width);
                    var idBounds = new Rectangle(0, y, Math.Min(idSize.Width + 1, width), idSize.Height);
                    links.Add((idBounds, citation.FindingId, true, null));
                    pieces.Add(new Piece(idBounds, citation.FindingId, Theme.NumericSmall, Theme.Series, null, links.Count - 1));
                    y = idBounds.Bottom;
                    if (!string.IsNullOrEmpty(citation.Supports))
                    {
                        y = AddText(citation.Supports, Theme.Caption, Theme.TextSecondary, 0, y, width);
                    }
                    y += Px(Theme.Space1);
                }
            }

            if (message.Limitations.Count > 0)
            {
                y += Px(Theme.Space2);
                y = AddText("Keep in mind", Theme.CaptionStrong, Theme.TextSecondary, 0, y, width) + Px(Theme.Space1);
                foreach (string limitation in message.Limitations)
                {
                    y = AddText(limitation, Theme.Caption, Theme.TextSecondary, 0, y, width) + Px(2);
                }
            }

            if (message.Source == AnswerSource.Sample)
            {
                y += Px(Theme.Space2);
                y = AddText("Sample answer from made-up results. The Detective service isn't connected.", Theme.Caption, Theme.Warning, 0, y, width);
            }
            return y;
        }

        /// <summary>How much a lookup returned, for the end of its line.</summary>
        private static string StepCount(AskStep step)
        {
            if (step.Failed)
            {
                return ": refused";
            }
            if (step.Tool == "get_run_summary")
            {
                return "";
            }
            int found = step.FindingIds.Count;
            if (step.Matching is int matching && matching > found)
            {
                return ": " + found.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of "
                    + matching.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) + " shown to it";
            }
            return found == 0 ? ": nothing found" : ": " + found.ToString(System.Globalization.CultureInfo.InvariantCulture) + " found";
        }

        private int AddText(string text, Font font, Color colour, int x, int y, int width)
        {
            var size = Measure(text, font, width);
            pieces.Add(new Piece(new Rectangle(x, y, width, size.Height), text, font, colour, null, -1));
            return y + size.Height;
        }

        private static Size Measure(string text, Font font, int width)
        {
            return TextRenderer.MeasureText(text.Length > 0 ? text : " ", font, new Size(Math.Max(1, width), int.MaxValue), Wrapped);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            foreach (var piece in pieces)
            {
                if (!piece.Bounds.IntersectsWith(e.ClipRectangle))
                {
                    continue;
                }

                bool hovered = piece.Link >= 0 && piece.Link == hoveredLink;
                if (piece.Box is Color fill)
                {
                    Draw.RoundedBox(g, piece.Bounds, Px(Theme.Radius), hovered ? Theme.SurfaceHover : fill);
                }
                else if (piece.Font != null)
                {
                    TextRenderer.DrawText(g, piece.Text, piece.Font, piece.Bounds, hovered ? Theme.TextPrimary : piece.Colour, Wrapped);
                    if (piece.Link >= 0)
                    {
                        // A cited finding is underlined, so it reads as something to click.
                        Draw.HorizontalLine(g, piece.Bounds.Left, piece.Bounds.Right - 2, piece.Bounds.Bottom - 1,
                            hovered ? Theme.TextPrimary : Draw.Blend(Theme.Surface, Theme.Series, 0.5f));
                    }
                }
            }
        }

        private int LinkAt(Point location)
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].Bounds.Contains(location))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = LinkAt(e.Location);
            if (hit != hoveredLink)
            {
                hoveredLink = hit;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredLink != -1)
            {
                hoveredLink = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int hit = LinkAt(e.Location);
            if (e.Button != MouseButtons.Left || hit < 0)
            {
                return;
            }

            var link = links[hit];
            if (link.Step != null)
            {
                StepClicked?.Invoke(this, link.Step);
            }
            else if (link.IsCitation)
            {
                CitationClicked?.Invoke(this, link.Value);
            }
            else
            {
                SuggestionClicked?.Invoke(this, link.Value);
            }
        }
    }
}
