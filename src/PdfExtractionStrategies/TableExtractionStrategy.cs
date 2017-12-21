﻿using iTextSharp.text;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.util;
using PointF = System.Drawing.PointF;
using Rect = System.Drawing.RectangleF;

namespace PdfExtractionStrategies
{
    public class TableExtractionStrategy : LocationTextExtractionStrategy, IExtRenderListener
    {
        private List<IList<float>> lineData = new List<IList<float>>();
        private List<IList<float>> rectangleData = new List<IList<float>>();

        /// <summary>
        /// Exactly from pdf metadata
        /// </summary>
        private Queue<Tuple<int, Vector>> movements = new Queue<Tuple<int, Vector>>();
        private IList<float> rData;

        /// <summary>
        /// Composed from metadata, it's right
        /// </summary>
        public List<LineSegment> Lines { get; set; } = new List<LineSegment>();
        public List<Rect> Rects { get; set; } = new List<Rect>();

        public void ClipPath(int rule)
        {

        }

        public void ModifyPath(PathConstructionRenderInfo renderInfo)
        {
            if (renderInfo.Operation == 1)
            {
                var x = renderInfo.SegmentData[0];
                var y = renderInfo.SegmentData[1];
                var moveTo = new Vector(x, y, 1);
                movements.Enqueue(Tuple.Create(1, moveTo));
            }
            else if (renderInfo.Operation == 2)
            {
                var x = renderInfo.SegmentData[0];
                var y = renderInfo.SegmentData[1];
                var lineTo = new Vector(x, y, 1);
                movements.Enqueue(Tuple.Create(2, lineTo));
            }
            else if (renderInfo.Operation == 7)
            {
                rData = renderInfo.SegmentData;
            }
        }

        public Path RenderPath(PathPaintingRenderInfo renderInfo)
        {
            if (renderInfo.Operation != 0)
            {
                Tuple<int, Vector> cur = null;
                Vector from = null;
                Vector to = null;
                while (movements.Count > 0 && (cur = movements.Dequeue()) != null)
                {
                    if (cur.Item1 == 1)
                    {
                        from = cur.Item2.Cross(renderInfo.Ctm);
                    }
                    else
                    {
                        if (from == null) continue;

                        to = cur.Item2.Cross(renderInfo.Ctm);
                        var extent = to.Subtract(from);
                        if (extent[0] >= 0)
                        {
                            Lines.Add(new LineSegment(new Vector(FixAxis(from[0]), FixAxis(from[1]), 1)
                                , new Vector(FixAxis(to[0]), FixAxis(to[1]), 1)));
                        }
                        else
                        {
                            Lines.Add(new LineSegment(new Vector(FixAxis(to[0]), FixAxis(to[1]), 1)
                                , new Vector(FixAxis(from[0]), FixAxis(from[1]), 1)));
                        }
                        from = null;
                        to = null;
                    }
                }
            }

            if (rData != null)
            {
                var r = rData;
                Vector vxy = new Vector(r[0], r[1], 1).Cross(renderInfo.Ctm);
                Vector vwh = new Vector(r[2], r[3], 1).Cross(renderInfo.Ctm);
                var x = vxy[0];
                var y = vxy[1];
                if (vwh[0] < 0)
                {
                    x = vxy[0] + vwh[0];
                }
                if (vwh[1] < 0)
                {
                    y = vxy[1] - vwh[1];
                }
                vxy = new Vector(x, y, 1);
                vwh = new Vector(Math.Abs(r[2]), Math.Abs(r[3]), 1).Cross(renderInfo.Ctm);
                Rects.Add(new Rect(vxy[0], vxy[1], vwh[0], vwh[1]));
                rData = null;
            }

            return null;
        }

        public virtual IEnumerable<PdfTableCell> GetTables()
        {
            /// by-products for simple table generation, if for complex tables,
            /// refer to <c>Lines</c> & <c>Rects</c> to re-gen
            var points = new List<PointF>();
            foreach (var line in Lines)
            {
                points.Add(new PointF(line.GetStartPoint()[0], line.GetStartPoint()[1]));
                points.Add(new PointF(line.GetEndPoint()[0], line.GetEndPoint()[1]));
            }
            foreach (var rect in Rects)
            {
                points.Add(new PointF(rect.Left, rect.Bottom));
                points.Add(new PointF(rect.Left, rect.Bottom - rect.Height));
                points.Add(new PointF(rect.Left + rect.Width, rect.Bottom));
                points.Add(new PointF(rect.Left + rect.Width, rect.Bottom - rect.Height));
            }
            // Threshold
            points = points.Select(p =>
            {
                return new PointF((float)Math.Round(p.X, 2), (float)Math.Round(p.Y, 2));
            }).ToList();
            // Sort
            points = points.GroupBy(p => p.X.ToString() + p.Y.ToString())
                .Select(p => p.First())
                .ToList();
            points.Sort((p1, p2) => p1.Y == p2.Y ? (int)(p1.X - p2.X) : (int)(p2.Y - p1.Y));

            points.ForEach(p =>
            {
                Debug.WriteLine("x:{0},y:{1}", p.X, p.Y);
            });

            var pGroups = MakePointsGroupInTable(points);
            foreach (var g in pGroups)
            {
                var rects = MakeRectangles(g);
                yield return MakeTable(rects);
            }
        }

        public List<LineSegment> GetAllInLines()
        {
            var lines = new List<LineSegment>();
            lines.AddRange(Lines);
            foreach (var rect in Rects)
            {
                lines.Add(new LineSegment(new Vector(rect.Left, rect.Top, 1), new Vector(rect.Right, rect.Top, 1)));
                lines.Add(new LineSegment(new Vector(rect.Left, rect.Top + rect.Height, 1), new Vector(rect.Right, rect.Top + rect.Height, 1)));
                lines.Add(new LineSegment(new Vector(rect.Left, rect.Top, 1), new Vector(rect.Left, rect.Top + rect.Height, 1)));
                lines.Add(new LineSegment(new Vector(rect.Right, rect.Top, 1), new Vector(rect.Right, rect.Top + rect.Height, 1)));
            }

            return lines;
        }

        public List<PointF> GetAllInPoints()
        {
            var points = new List<PointF>();
            foreach (var line in Lines)
            {
                points.Add(new PointF(line.GetStartPoint()[0], line.GetStartPoint()[1]));
                points.Add(new PointF(line.GetEndPoint()[0], line.GetEndPoint()[1]));
            }
            foreach (var rect in Rects)
            {
                points.Add(new PointF(rect.Left, rect.Bottom));
                points.Add(new PointF(rect.Left, rect.Top));
                points.Add(new PointF(rect.Right, rect.Bottom));
                points.Add(new PointF(rect.Right, rect.Top));
            }

            //var lines = GetAllInLines();
            //for (int i = 0; i < lines.Count - 1; i++)
            //{
            //    for (int j = i + 1; j < lines.Count; j++)
            //    {
            //        // 1:vertical, 0:horiztonal
            //        var lineTypei = lines[i].GetStartPoint()[0] == lines[i].GetEndPoint()[0] ? 1 : 0;
            //        var lineTypej = lines[j].GetStartPoint()[0] == lines[j].GetEndPoint()[0] ? 1 : 0;
            //        if (lineTypei == lineTypej) continue;
            //        else {
            //            var vx = lineTypei == 1 ? lines[i].GetStartPoint()[0] : lines[j].GetStartPoint()[0];
            //            var vy = lineTypei == 0 ? lines[i].GetStartPoint()[1] : lines[j].GetStartPoint()[1];

            //            if (lineTypei == 1)
            //            {
            //                if ((lines[j].GetStartPoint()[0] + lines[j].GetEndPoint()[0]) / 2 > vx
            //                    && (lines[i].GetStartPoint()[1] + lines[i].GetEndPoint()[1]) / 2 > vy)
            //                {
            //                    points.Add(new PointF(vx, vy));
            //                }
            //            }
            //            else
            //            {
            //                if ((lines[i].GetStartPoint()[0] + lines[i].GetEndPoint()[0]) / 2 > vx
            //                    && (lines[j].GetStartPoint()[1] + lines[j].GetEndPoint()[1]) / 2 > vy)
            //                {
            //                    points.Add(new PointF(vx, vy));
            //                }
            //            }
            //        }
            //    }
            //}

            return points;
        }

        // Helper methods
        public string GetSimpleHTMLTable(PdfTableCell table)
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

        private float FixAxis(float o)
        {
            return (float)Math.Round(o, 2);
        }

        private List<List<PointF>> MakePointsGroupInTable(List<PointF> points)
        {
            var pGroups = new List<List<PointF>>();

            var cur = new List<PointF>();
            foreach (var p in points)
            {
                Debug.WriteLine("x:{0},y:{1}", p.X, p.Y);
                int i;
                for (i = 0; i < pGroups.Count; i++)
                {
                    var g = pGroups[i];
                    if (!g.Find(pf => pf.X == p.X || pf.Y == p.Y).IsEmpty)
                    {
                        g.Add(p);
                        break;
                    }
                }
                if (i == pGroups.Count)
                {
                    pGroups.Add(new List<PointF>() { p });
                }
            }

            return pGroups;
        }

        private List<Rectangle> MakeRectangles(List<PointF> points)
        {
            var rects = new List<Rectangle>();
            // a bug here, need dect if two points are lined
            var ulx = points[0].X;
            var uly = points[0].Y;
            for (int i = 0; i < points.Count - 1; i++)
            {
                var urx = points[i + 1].X;
                var ury = points[i + 1].Y;
                if (urx > ulx && ury == uly)
                {

                    float llx;
                    float lly;
                    float lrx;
                    float lry;

                    var llIdx = i;
                    while (llIdx < points.Count - 1)
                    {
                        llIdx = points.FindIndex(llIdx + 1, pf => pf.X == ulx);
                        if (llIdx == -1) break;

                        var lrIdx = llIdx + 1;
                        while (lrIdx < points.Count)
                        {
                            if (points[lrIdx].Y != points[llIdx].Y || points[lrIdx].X != urx)
                                lrIdx++;
                            else break;
                        }
                        if (lrIdx < points.Count)
                        {
                            llx = points[llIdx].X;
                            lly = points[llIdx].Y;
                            lrx = points[llIdx + 1].X;
                            lry = points[llIdx + 1].Y;

                            rects.Add(new Rectangle(llx, lly, urx, ury));
                            break;
                        }

                    }
                }

                ulx = urx;
                uly = ury;
            }

            return rects;
        }

        private PdfTableCell MakeTable(List<Rectangle> rects)
        {
            var cell = new PdfTableCell();
            var x = rects[0].Left;
            var y = rects[0].Top;
            var maxX = rects.Max(r => r.Left);
            var minY = rects.Min(r => r.Top);
            var w = rects.Where(r => r.Top == y).Sum(r => r.Width);
            var h = rects.Where(r => r.Left == x).Sum(r => r.Height);
            cell.Rectangle = new Rectangle(x, y - h, x + w, y);

            //cols
            cell.Xs.Add(x);
            while (x < maxX)
            {
                var rs = rects.Where(r => r.Left == x);
                if (rs.Count() == 0) break;
                x += rs.Max(r => r.Width);
                if (x < cell.Rectangle.Right)
                    cell.Xs.Add(x);
            }

            //rows
            cell.Ys.Add(y);
            while (y > minY)
            {
                var rs = rects.Where(r => r.Top == y);
                if (rs.Count() == 0) break;
                y -= rs.Max(r => r.Height);
                if (y > cell.Rectangle.Bottom)
                    cell.Ys.Add(y);
            }

            cell.Children = new List<PdfTableCell>();
            if (rects.Count > 1)
            {
                for (int j = 0; j < cell.Rows; j++)
                {
                    for (int i = 0; i < cell.Cols; i++)
                    {
                        var ix = cell.Xs[i];
                        var iy = cell.Ys[j];
                        var iw = 0f;
                        var ih = 0f;
                        if (i == cell.Cols - 1)
                        {
                            iw = cell.Rectangle.Right - ix;
                        }
                        else
                        {
                            iw = cell.Xs[i + 1] - ix;
                        }
                        if (j == cell.Rows - 1)
                        {
                            ih = iy - cell.Rectangle.Bottom;
                        }
                        else
                        {
                            ih = iy - cell.Ys[j + 1];
                        }

                        var innerRects = rects.Where(r => r.Left >= ix && r.Right <= ix + iw
                            && r.Top <= iy && r.Bottom >= iy - ih)
                            .ToList();
                        var innerCell = MakeTable(innerRects);
                        cell.Children.Add(innerCell);
                    }
                }
            }
            else
            {
                cell.Text = GetResultantText(new RectangleSection(cell.Rectangle));
            }

            return cell;
        }
    }

    public class RectangleSection : LocationTextExtractionStrategy.ITextChunkFilter
    {
        public Rectangle Rect { get; set; }

        public RectangleSection(Rectangle rect)
        {
            Rect = rect;
        }

        public bool Accept(LocationTextExtractionStrategy.TextChunk textChunk)
        {
            var rectJ = new RectangleJ(Rect);
            return rectJ.Contains(textChunk.StartLocation[0], textChunk.StartLocation[1])
                && rectJ.Contains(textChunk.EndLocation[0], textChunk.EndLocation[1]);
        }
    }

    public class PdfTableCell
    {
        public int Rows { get { return Ys.Count; } }

        public int Cols { get { return Xs.Count; } }

        public List<float> Xs { get; set; } = new List<float>();

        public List<float> Ys { get; set; } = new List<float>();

        public List<PdfTableCell> Children { get; set; } = new List<PdfTableCell>();

        public Rectangle Rectangle { get; set; }

        public string Text { get; set; }
    }
}
