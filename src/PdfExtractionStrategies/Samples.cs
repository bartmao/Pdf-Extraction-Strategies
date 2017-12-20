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

        public Bitmap PrintTableSkeletonInPage(int page, string pathToPdf)
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
                    gp.ScaleTransform(1, -1);
                    gp.TranslateTransform(0, -bmp.Height);
                    foreach (var line in strategy.Lines)
                    {
                        gp.DrawLine(Pens.Black, line.GetStartPoint()[0], line.GetStartPoint()[1], line.GetEndPoint()[0], line.GetEndPoint()[1]);
                    }
                    gp.DrawRectangles(Pens.Black, strategy.Rects.ToArray());
                }

                return bmp;
            }
        }

        private string GetSimpleHTMLTable(PdfTableCell table)
        {
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
                        var cell = table.Children[i * table.Cols + j];
                        if (cell.Children.Count == 0)
                        {
                            sb.Append(cell.Text);
                        }
                        else
                        {
                            sb.Append(GetSimpleHTMLTable(cell));
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
