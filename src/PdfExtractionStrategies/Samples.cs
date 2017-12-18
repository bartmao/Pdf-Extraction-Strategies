using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public void ExactTables()
        {
            using (var reader = new PdfReader(@"c:\path to pdf with table"))
            {
                var strategy = new TableExtractionStrategy();
                var parser = new PdfReaderContentParser(reader);
                parser.ProcessContent(2, strategy);
                foreach (var table in strategy.GetTables())
                {
                    var tableStr = GetSimpleHTMLTable(table);
                    Debug.WriteLine(tableStr);
                }
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
