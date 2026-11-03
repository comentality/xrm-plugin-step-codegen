using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PluginStepCodegen
{
    /// <summary>One kind of mark a <see cref="MarkedListView"/> paints, and the rows that wear it.</summary>
    public class RowMark
    {
        public Image Image;
        public Func<ListViewItem, bool> Applies;
    }

    /// <summary>
    /// A details-view list that can paint small marks just after a row's text, in the first
    /// column, on the rows a predicate picks out. A ListView's own image goes before the text
    /// and indents every row to make room for it; a mark that follows the name, on the rows
    /// that have earned one and nowhere else, has to be painted by hand - after the control has
    /// painted itself, so selection and hover draw under it rather than over it.
    ///
    /// A row can earn more than one. They follow the name in the order they are listed here,
    /// each after the last, and a row whose name leaves no room for one shows as many as fit.
    /// </summary>
    public class MarkedListView : ListView
    {
        private const int WmPaint = 0x000F;

        /// <summary>The kinds of mark, in the order they follow the name.</summary>
        public List<RowMark> Marks { get; } = new List<RowMark>();

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WmPaint && Marks.Count > 0 && View == View.Details
                && Columns.Count > 0 && Items.Count > 0 && !IsDisposed)
            {
                PaintMarks();
            }
        }

        /// <summary>
        /// How wide the first column has to be for every row's marks to fit after its name, or
        /// 0 if no row wears one. A column sized to its text alone would leave the marks no room,
        /// and <see cref="PaintMarks"/> leaves out a mark that does not fit rather than spill it.
        /// </summary>
        public int MarkedWidth()
        {
            var widest = 0;
            if (Marks.Count == 0 || Columns.Count == 0 || !IsHandleCreated)
            {
                return widest;
            }

            foreach (ListViewItem item in Items)
            {
                var marks = 0;
                var count = 0;
                foreach (var mark in Marks)
                {
                    if (mark.Image == null || mark.Applies == null || !mark.Applies(item))
                    {
                        continue;
                    }

                    marks += mark.Image.Width;
                    count++;
                }

                if (count == 0)
                {
                    continue;
                }

                var indent = item.GetBounds(ItemBoundsPortion.Label).Left - item.GetBounds(ItemBoundsPortion.Entire).Left;
                var text = TextRenderer.MeasureText(item.Text, item.Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

                // The same arithmetic PaintMarks lays them out by: two pixels before the glyphs,
                // one between marks, and two clear of the cell's edge after the last.
                widest = Math.Max(widest, indent + 2 + text + marks + (count - 1) + 2);
            }

            return widest;
        }

        private void PaintMarks()
        {
            var client = ClientRectangle;
            using (var g = Graphics.FromHwnd(Handle))
            {
                foreach (ListViewItem item in Items)
                {
                    // Where the text is drawn: the first column from the checkbox on, which
                    // is where a ListView starts the label.
                    var label = item.GetBounds(ItemBoundsPortion.Label);
                    if (label.Bottom <= client.Top || label.Top >= client.Bottom)
                    {
                        continue;
                    }

                    int? x = null;
                    var cellRight = item.GetBounds(ItemBoundsPortion.Entire).Left + Columns[0].Width;
                    foreach (var mark in Marks)
                    {
                        if (mark.Image == null || mark.Applies == null || !mark.Applies(item))
                        {
                            continue;
                        }

                        if (x == null)
                        {
                            var width = TextRenderer.MeasureText(g, item.Text, item.Font,
                                new Size(int.MaxValue, label.Height),
                                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

                            // The label has a couple of pixels of its own before the glyphs
                            // start; the mark touches the last one.
                            x = label.Left + 2 + width;
                        }

                        // Only inside the column: a name long enough to be cut short with an
                        // ellipsis has no room after it, and the mark must not walk into the
                        // next cell.
                        if (x.Value + mark.Image.Width > cellRight - 2)
                        {
                            break;
                        }

                        // Sits up like a superscript, its top clear of the capitals and its
                        // bottom about level with the lower-case letters' tops. Three pixels
                        // above the row is into the row above, which is blank there; on the
                        // first row it is under the header, and that is accepted.
                        g.DrawImageUnscaled(mark.Image, x.Value, label.Top - 3);
                        x += mark.Image.Width + 1;
                    }
                }
            }
        }
    }
}
