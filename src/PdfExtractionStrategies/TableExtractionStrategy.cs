using iTextSharp.text;
using iTextSharp.text.pdf.parser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

        public int MaxHierarchy { get; set; } = 10;
        public int Variance { get; set; } = 2;
        public int Rotation { get; set; } = 0;
        public bool TreatSmallRectAsLine { get; set; }
        public bool DetectStrikeThroughs { get; set; }

        public TableExtractionStrategy(int rotation = 0, bool treatSmallRectAsLine = true, bool detectStrikeThroughs = true)
        {
            Rotation = rotation;
            TreatSmallRectAsLine = treatSmallRectAsLine;
            DetectStrikeThroughs = detectStrikeThroughs;
        }

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
            //var p = typeof(PathPaintingRenderInfo).GetField("gs", BindingFlags.NonPublic | BindingFlags.Instance);
            //var gs = (GraphicsState)(p.GetValue(renderInfo));
            //Debug.WriteLine(gs.StrokeColor?.RGB.ToString()??"empty");
            // detect gs.StrokeColor != null

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

                        if (from[0] == to[0] && from[1] < to[1]
                            || from[1] == to[1] && from[0] < to[0])
                        {
                            Lines.Add(new LineSegment(new Vector(FixAxis(from[0]), FixAxis(from[1]), 1)
                                , new Vector(FixAxis(to[0]), FixAxis(to[1]), 1)));
                        }
                        else if (from[0] == to[0] && from[1] > to[1]
                            || from[1] == to[1] && from[0] > to[0])
                        {
                            Lines.Add(new LineSegment(new Vector(FixAxis(to[0]), FixAxis(to[1]), 1)
                                , new Vector(FixAxis(from[0]), FixAxis(from[1]), 1)));
                        }
                        else
                        {
                            //throw new Exception("oblique line");
                        }

                        //Debug.WriteLine("x:{0},y:{1}", Lines[Lines.Count - 1].GetStartPoint()[0], Lines[Lines.Count - 1].GetStartPoint()[1]);
                        from = null;
                        to = null;
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
                    if (TreatSmallRectAsLine && vwh[0] < Variance)
                    {
                        //Lines.Add(new LineSegment(new Vector(FixAxis(x), FixAxis(y - vwh[1]), 1)
                        //       , new Vector(FixAxis(x), FixAxis(y), 1)));
                        Lines.Add(new LineSegment(new Vector(FixAxis(x), FixAxis(y), 1)
                               , new Vector(FixAxis(x), FixAxis(y + vwh[1]), 1)));
                    }
                    else if (TreatSmallRectAsLine && vwh[1] < Variance)
                    {
                        Lines.Add(new LineSegment(new Vector(FixAxis(x), FixAxis(y), 1)
                               , new Vector(FixAxis(x + vwh[0]), FixAxis(y), 1)));
                    }
                    else
                    {
                        Rects.Add(new Rect(vxy[0], vxy[1], vwh[0], vwh[1]));
                    }
                    rData = null;
                }
            }

            return null;
        }

        public virtual IEnumerable<PdfTableCell> GetTables()
        {
            // remove strikethroughs
            RemoveStrikethroughs();
            var points = GetAllInPoints();
            var pGroups = MakePointsGroupInTable(points);
            foreach (var g in pGroups)
            {
                var rects = MakeRectangles(g);
                yield return MakeTable(rects);
            }
        }

        public void RemoveStrikethroughs()
        {
            var mayIndex = Rotation == 0 ? 1 : 0;
            var hlines = Lines.Where(l => NearlyEqual(l.GetStartPoint()[mayIndex], l.GetEndPoint()[mayIndex])).ToList();
            var hs = new List<float>();
            foreach (var hl in hlines)
            {
                int i = 0;
                for (i = 0; i < hs.Count; i++)
                {
                    if (NearlyEqual(hs[i], hl.GetStartPoint()[mayIndex]))
                        break;
                }
                if (i == hs.Count)
                {
                    hs.Add(hl.GetStartPoint()[mayIndex]);
                }
            }
            hs.Sort();

            var deltas = new List<float>();
            var deltaGroup = new List<Tuple<float, int>>();
            for (int i = 0; i < hs.Count - 1; i++)
            {
                deltas.Add(hs[i + 1] - hs[i]);
            }
            foreach (var d in deltas)
            {
                int j = 0;
                for (; j < deltaGroup.Count; j++)
                {
                    var avg = deltaGroup[j].Item1;
                    var count = deltaGroup[j].Item2;
                    if (NearlyEqual(d, avg, variance: 0.5f))
                    {
                        avg = (avg + d) / 2;
                        deltaGroup[j] = Tuple.Create(avg, ++count);
                    }
                }
                if (j == deltaGroup.Count)
                {
                    deltaGroup.Add(Tuple.Create(d, 1));
                }
            }
            var theRightDeltaItem = deltaGroup.OrderByDescending(d => d.Item2).FirstOrDefault();
            // over 4 strikethroughs and interleave is less than 12px
            if (theRightDeltaItem != null && theRightDeltaItem.Item1 < 12 && theRightDeltaItem.Item2 > 4)
            {
                var theRightDelta = theRightDeltaItem.Item1;
                var deleteYs = new List<float>();
                for (int i = 1; i < hs.Count - 2; i++)
                {
                    if (NearlyEqual(hs[i + 1], hs[i], theRightDelta, 0.5f))
                    {
                        deleteYs.Add(hs[i]);
                        deleteYs.Add(hs[i + 1]);
                    }
                }
                deleteYs.Distinct();
                Lines = Lines.Where(l => !deleteYs.Contains(l.GetStartPoint()[mayIndex]) || !hlines.Contains(l)).ToList();
            }
        }

        private bool NearlyEqual(float x1, float x2, float diff = 0, float variance = 0)
        {
            if (variance == 0)
                return x1 == x2 || Math.Abs(Math.Abs(x1 - x2) - diff) < Variance;
            else
                return x1 == x2 || Math.Abs(Math.Abs(x1 - x2) - diff) < variance;
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

        private void AddToPoints(List<PointF> points, PointF p)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Math.Abs(points[i].X - p.X) < Variance && Math.Abs(points[i].Y - p.Y) < Variance)
                    return;
            }
            points.Add(p);
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

            var lines = GetAllInLines();
            for (int i = 0; i < lines.Count - 1; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    // 1:vertical, 0:horiztonal
                    var lineTypei = lines[i].GetStartPoint()[0] == lines[i].GetEndPoint()[0] ? 1 : 0;
                    var lineTypej = lines[j].GetStartPoint()[0] == lines[j].GetEndPoint()[0] ? 1 : 0;
                    if (lineTypei == lineTypej) continue;
                    else {
                        var vx = lineTypei == 1 ? lines[i].GetStartPoint()[0] : lines[j].GetStartPoint()[0];
                        var vy = lineTypei == 0 ? lines[i].GetStartPoint()[1] : lines[j].GetStartPoint()[1];

                        if (lineTypei == 1)
                        {
                            if (vx > lines[j].GetStartPoint()[0] && vx < lines[j].GetEndPoint()[0]
                                && vy > lines[i].GetStartPoint()[1] && vy < lines[i].GetEndPoint()[1])
                            {
                                points.Add(new PointF(vx, vy));
                            }
                        }
                        else
                        {
                            if (vx > lines[i].GetStartPoint()[0] && vx < lines[i].GetEndPoint()[0]
                                && vy > lines[j].GetStartPoint()[1] && vy < lines[j].GetEndPoint()[1])
                            {
                                points.Add(new PointF(vx, vy));
                            }
                        }
                    }
                }
            }

            // fix points
            var variance = 2;
            var xs = new List<float>();
            var ys = new List<float>();
            var nPoints = new List<PointF>();
            for (int i = 0; i < points.Count; i++)
            {
                var nx = points[i].X;
                var ny = points[i].Y;

                int m = 0;
                int n = 0;
                for (m = 0; m < xs.Count; m++)
                {
                    var x = xs[m];
                    if (x == nx) break;
                    else if (Math.Abs(x - nx) < variance)
                    {
                        nx = x;
                        break;
                    }
                }
                if (m == xs.Count) xs.Add(nx);

                for (n = 0; n < ys.Count; n++)
                {
                    var y = ys[n];
                    if (y == ny) break;
                    else if (Math.Abs(y - ny) < variance)
                    {
                        ny = y;
                        break;
                    }
                }
                if (n == ys.Count) ys.Add(ny);


                nPoints.Add(new PointF(nx, ny));
            }

            nPoints.Distinct(new PointFEqualityComparer());
            nPoints.Sort((p1, p2) => p1.Y == p2.Y ? (int)(p1.X - p2.X) : (int)(p2.Y - p1.Y));
            return nPoints;
        }

        private float FixAxis(float o)
        {
            return (float)Math.Round(o, 2);
        }

        public List<List<PointF>> MakePointsGroupInTable(List<PointF> points)
        {
            var pGroups = new List<List<PointF>>();

            var cur = new List<PointF>();
            foreach (var p in points)
            {
                int i;
                for (i = 0; i < pGroups.Count; i++)
                {
                    var g = pGroups[i];
                    if (!g.Find(pf => pf.X == p.X || pf.Y == p.Y).IsEmpty)
                    {
                        if (g.Find(pf => pf.X == p.X && pf.Y == p.Y).IsEmpty)
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
            // can use 2.pdf/page 2 test, see middle header
            var ulIdx = 0;
            var urIdx = 0;
            var llIdx = 0;
            var lrIdx = 0;

            while (ulIdx < points.Count - 3)
            {
                var madeRect = false;
                var ulx = points[ulIdx].X;
                var uly = points[ulIdx].Y;

                urIdx = ulIdx + 1;
                while (!madeRect)
                {
                    if (urIdx >= points.Count - 2)
                    {
                        goto end;
                    }

                    // Found upper right point
                    if (points[ulIdx].X < points[urIdx].X
                        && points[ulIdx].Y == points[urIdx].Y)
                    {
                        var urx = points[urIdx].X;
                        var ury = points[urIdx].Y;
                        llIdx = urIdx + 1;
                        while (llIdx < points.Count - 1 && !madeRect)
                        {
                            while (points[llIdx].X != ulx && llIdx < points.Count - 1) llIdx++;

                            if (llIdx == points.Count - 1)
                            {
                                // reach the last line, jump out
                                //goto end;
                                break;
                            }

                            // Found lower left point
                            var llx = points[llIdx].X;
                            var lly = points[llIdx].Y;
                            lrIdx = llIdx + 1;
                            while (lrIdx < points.Count)
                            {
                                if (points[lrIdx].X == urx && points[lrIdx].Y == lly)
                                {
                                    // Found lower right point
                                    madeRect = true;
                                    rects.Add(new Rectangle(llx, lly, urx, ury));
                                    ulIdx = urIdx;
                                    break;
                                }

                                lrIdx++;
                            }

                            if (madeRect) break;
                            else llIdx++;
                        }

                    }
                    else
                    {
                        // Switch row
                        ulIdx = urIdx;
                        break;
                    }

                    urIdx++;
                }

                // here madeRect must true
            }

            end:
            return rects;
        }

        private PdfTableCell MakeTable(List<Rectangle> rects, int level = 0)
        {
            if (rects == null || rects.Count == 0) return null;

            var cell = new PdfTableCell(Rotation);
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
                var rs = rects.Where(r => r.Left == x).ToList();
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
            if (rects.Count > 1 && level < MaxHierarchy)
            //if (rects.Count > 1)
            {
                for (int j = 0; j < cell.RealRows; j++)
                {
                    for (int i = 0; i < cell.RealCols; i++)
                    {
                        var ix = cell.Xs[i];
                        var iy = cell.Ys[j];
                        var iw = 0f;
                        var ih = 0f;
                        if (i == cell.RealCols - 1)
                        {
                            iw = cell.Rectangle.Right - ix;
                        }
                        else
                        {
                            iw = cell.Xs[i + 1] - ix;
                        }
                        if (j == cell.RealRows - 1)
                        {
                            ih = iy - cell.Rectangle.Bottom;
                        }
                        else
                        {
                            ih = iy - cell.Ys[j + 1];
                        }

                        //var innerRects = rects.Where(r => r.Left >= ix && r.Right <= ix + iw
                        //    && r.Top <= iy && r.Bottom >= iy - ih)
                        //    .ToList();

                        var innerRects = rects.Where(r =>
                        {
                            return
                            (r.Left > ix || NearlyEqual(r.Left, ix))
                            && (r.Right < ix + iw || NearlyEqual(r.Right, ix + iw))
                            && (r.Top < iy || NearlyEqual(r.Top, iy))
                            && (r.Bottom > iy - ih || NearlyEqual(r.Bottom, iy - ih));
                        }).ToList();
                        if (innerRects.Count > 0)
                        {
                            var innerCell = MakeTable(innerRects, level + 1);
                            cell.Children.Add(innerCell);
                        }
                    }
                }
            }
            else
            {
                cell.Text = GetResultantText(new RectangleSection(cell.Rectangle, Variance));
            }

            return cell;
        }
    }

    public class PointFEqualityComparer : IEqualityComparer<PointF>
    {
        public bool Equals(PointF x, PointF y)
        {
            return x.X == y.X && x.Y == y.Y;
        }

        public int GetHashCode(PointF obj)
        {
            return (int)obj.X ^ (int)obj.Y;
        }
    }

    public class RectangleSection : LocationTextExtractionStrategy.ITextChunkFilter
    {
        public Rectangle Rect { get; set; }

        public RectangleSection(Rectangle rect, int variance = 0)
        {
            Rect = new Rectangle(rect.Left - variance, rect.Bottom - variance, rect.Right + variance, rect.Top + variance);
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
        public PdfTableCell(int rotation = 0)
        {
            Rotation = rotation;
            Xs = new List<float>();
            Ys = new List<float>();
        }

        public int Rotation { get; }

        public int RealRows { get { return Ys.Count; } }
        public int RealCols { get { return Xs.Count; } }

        public int Rows
        {
            get
            {
                if (Rotation == 90)
                    return Xs.Count;
                return Ys.Count;
            }
        }

        public int Cols
        {
            get
            {
                if (Rotation == 90)
                    return Ys.Count;
                return Xs.Count;
            }
        }

        public List<float> Xs { get; set; }

        public List<float> Ys { get; set; }

        public List<PdfTableCell> Children { get; set; } = new List<PdfTableCell>();

        public Rectangle Rectangle { get; set; }

        public string Text { get; set; }

        public PdfTableCell Get(int row, int col)
        {
            if (Rotation == 0)
            {
                return Children[row * Cols + col];
            }
            else if (Rotation == 90)
            {
                return Children[(Cols - 1 - col) * Rows + row];
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
