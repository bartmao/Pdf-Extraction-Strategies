using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PdfExtractionStrategies
{
    public class Samples
    {
        /// <summary>
        /// Exact table from page
        /// </summary>
        public IEnumerable<string> ExactTables(int page, string pathToPdf)
        {
            using (var reader = new PdfReader(pathToPdf))
            {
                var strategy = new TableExtractionStrategy();
                var parser = new PdfReaderContentParser(reader);
                parser.ProcessContent(page, strategy);
                foreach (var table in strategy.GetTables())
                {
                    yield return GetSimpleHTMLTable(table);
                }
            }
        }

        public Bitmap PrintTableSkeletonInPage(int page, string pathToPdf, bool drawAxis = false)
        {
            using (var reader = new PdfReader(pathToPdf))
            {
                var strategy = new TableExtractionStrategy();
                var parser = new PdfReaderContentParser(reader);
                parser.ProcessContent(page, strategy);
                var size = reader.GetPageSize(page);
                var bmp = new Bitmap((int)size.Width, (int)size.Height);
                using (var gp = Graphics.FromImage(bmp))
                {
                    gp.Clear(Color.White);
                    //gp.ScaleTransform(1, -1);
                    //gp.TranslateTransform(0, -bmp.Height);
                    var h = bmp.Height;
                    var ft = new Font("arial", 8);
                    foreach (var line in strategy.Lines)
                    {
                        gp.DrawLine(Pens.Black, line.GetStartPoint()[0], h - line.GetStartPoint()[1], line.GetEndPoint()[0], h - line.GetEndPoint()[1]);
                        if (drawAxis)
                        {
                            gp.DrawString(string.Format("({0},{1})", line.GetStartPoint()[0], h - line.GetStartPoint()[1]), ft, Brushes.Black, new PointF(line.GetStartPoint()[0], h - line.GetStartPoint()[1]));
                            gp.DrawString(string.Format("({0},{1})", line.GetEndPoint()[0], h - line.GetEndPoint()[1]), ft, Brushes.Black, new PointF(line.GetEndPoint()[0], h - line.GetEndPoint()[1]));
                        }
                    }
                    foreach (var rect in strategy.Rects)
                    {
                        gp.DrawRectangle(Pens.Black, rect.Left, h - rect.Top - rect.Height, rect.Width, rect.Height);
                        if (drawAxis)
                        {
                            gp.DrawString(string.Format("({0},{1})", rect.Left, h - rect.Top - rect.Height), ft, Brushes.Red, new PointF(rect.Left, h - rect.Top - rect.Height));
                            gp.DrawString(string.Format("w:{0}", rect.Width), ft, Brushes.Red, new PointF(rect.Left + rect.Width / 2, h - rect.Top - rect.Height));
                            gp.DrawString(string.Format("h:{0}", rect.Height), ft, Brushes.Red, new PointF(rect.Left, h - rect.Top - rect.Height / 2));
                        }

                    }
                }

                return bmp;
            }
        }

        public Bitmap DrawContours(int page, string pathToPdf)
        {
            using (var reader = new PdfReader(pathToPdf))
            {
                var strategy = new TableExtractionStrategy();
                var parser = new PdfReaderContentParser(reader);
                parser.ProcessContent(page, strategy);
                var size = reader.GetPageSize(page);
                var bmp = new Bitmap((int)size.Width, (int)size.Height);
                var h = bmp.Height;
                using (var gp = Graphics.FromImage(bmp))
                {
                    gp.Clear(Color.White);
                    strategy.GetAllInLines().ForEach(l =>
                    {
                        gp.DrawLine(Pens.Black, l.GetStartPoint()[0], h - l.GetStartPoint()[1]
                            , l.GetEndPoint()[0], h - l.GetEndPoint()[1]);
                    });
                    strategy.GetAllInPoints().ForEach(p =>
                    {
                        gp.DrawEllipse(Pens.Black, p.X - 2, h - p.Y - 2, 4, 4);
                    });
                }

                return bmp;
            }
        }

        private string GetSimpleHTMLTable(PdfTableCell table)
        {
            if (table == null) return null;

            var sb = new StringBuilder();

            sb.Append("<table>");
            for (int i = 0; i < table.Rows; i++)
            {
                sb.Append("<tr>");
                for (int j = 0; j < table.Cols; j++)
                {
                    sb.Append("<td>");
                    if (table.Children.Count == 0)
                    {
                        sb.Append(table.Text);
                    }
                    else
                    {
                        var idx = i * table.Cols + j;
                        if (idx < table.Children.Count)
                        {
                            var cell = table.Children[idx];
                            if (cell.Children.Count == 0)
                            {
                                sb.Append(cell.Text);
                            }
                            else
                            {
                                sb.Append(GetSimpleHTMLTable(cell));
                            }
                        }
                        else
                        {
                            throw new Exception("bad format");
                            // the table is in bad format
                            // which the extraction should having a bug
                        }
                    }

                    sb.Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");

            return sb.ToString();
        }

    }
}
