using System;
using System.Drawing;
using System.Windows.Forms;

namespace PluginStepCodegen
{
    /// <summary>
    /// A details-view list that can paint a small mark just after a row's text, in the first
    /// column, on the rows a predicate picks out. A ListView's own image goes before the text
    /// and indents every row to make room for it; a mark that follows the name, on the rows
    /// that have earned one and nowhere else, has to be painted by hand - after the control has
    /// painted itself, so selection and hover draw under it rather than over it.
    /// </summary>
    public class MarkedListView : ListView
    {
        private const int WmPaint = 0x000F;

        /// <summary>The mark, or null for none.</summary>
        public Image Mark { get; set; }

        /// <summary>Which rows wear it. Null marks nothing.</summary>
        public Func<ListViewItem, bool> IsMarked { get; set; }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WmPaint && Mark != null && IsMarked != null && View == View.Details
                && Columns.Count > 0 && Items.Count > 0 && !IsDisposed)
            {
                PaintMarks();
            }
        }

        private void PaintMarks()
        {
            var client = ClientRectangle;
            using (var g = Graphics.FromHwnd(Handle))
            {
                foreach (ListViewItem item in Items)
                {
                    if (!IsMarked(item))
                    {
                        continue;
                    }

                    // Where the text is drawn: the first column from the checkbox on, which
                    // is where a ListView starts the label.
                    var label = item.GetBounds(ItemBoundsPortion.Label);
                    if (label.Bottom <= client.Top || label.Top >= client.Bottom)
                    {
                        continue;
                    }

                    var width = TextRenderer.MeasureText(g, item.Text, item.Font,
                        new Size(int.MaxValue, label.Height),
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

                    // The label has a couple of pixels of its own before the glyphs start; the
                    // mark touches the last one and sits up like a superscript, its top clear
                    // of the capitals and its bottom about level with the lower-case letters'
                    // tops. Three pixels above the row is into the row above, which is blank
                    // there; on the first row it is under the header, and that is accepted.
                    var x = label.Left + 2 + width;
                    var y = label.Top - 3;

                    // Only inside the column: a name long enough to be cut short with an
                    // ellipsis has no room after it, and the mark must not walk into the next
                    // cell.
                    var cellRight = item.GetBounds(ItemBoundsPortion.Entire).Left + Columns[0].Width;
                    if (x + Mark.Width > cellRight - 2)
                    {
                        continue;
                    }

                    g.DrawImageUnscaled(Mark, x, y);
                }
            }
        }
    }
}
