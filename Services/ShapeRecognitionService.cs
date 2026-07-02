using CollaborativeWhiteboard.Models;

namespace CollaborativeWhiteboard.Services;

// ================================================================
//  ShapeRecognitionService — الميزة الذكية
//
//  تحلل نقاط أي ضربة ريشة وتحدد إذا كانت:
//    • دائرة      (circle)
//    • مستطيل     (rectangle)
//    • مثلث       (triangle)
//    • خط مستقيم  (line)
//    • سهم        (arrow)
//    • رسم حر     (none)
//
//  وتُعيد النقاط المصحّحة هندسياً (perfect shape)
// ================================================================
public class ShapeRecognitionService
{
    // حد أدنى لنسبة الثقة للتصحيح
    private const double ConfidenceThreshold = 0.72;

    // -------------------------------------------------------
    //  الدالة الرئيسية
    // -------------------------------------------------------
    public ShapeResult Recognize(List<Point> points, string strokeId = "", string userName = "")
    {
        if (points.Count < 4)
            return None(strokeId, userName);

        // جرب كل الأشكال واختر الأفضل
        var candidates = new[]
        {
            TryLine(points),
            TryCircle(points),
            TryRectangle(points),
            TryTriangle(points),
            TryArrow(points),
        };

        var best = candidates.MaxBy(r => r.Confidence)!;
        best.StrokeId = strokeId;
        best.UserName = userName;

        return best.Confidence >= ConfidenceThreshold ? best : None(strokeId, userName);
    }

    // ================================================================
    //  1. خط مستقيم
    // ================================================================
    private static ShapeResult TryLine(List<Point> pts)
    {
        // قياس متوسط الانحراف عن الخط المستقيم بين النقطة الأولى والأخيرة
        var p0 = pts.First();
        var pN = pts.Last();
        double len = Dist(p0, pN);
        if (len < 10) return new ShapeResult { ShapeType = "line", Confidence = 0 };

        double totalDev = pts.Sum(p => PointToLineDistance(p, p0, pN));
        double avgDev = totalDev / pts.Count;
        double confidence = Math.Max(0, 1.0 - avgDev / (len * 0.15));

        return new ShapeResult
        {
            ShapeType        = "line",
            Confidence       = confidence,
            Label            = $"خط مستقيم ({confidence:P0})",
            CorrectedPoints  = new List<Point> { p0, pN }
        };
    }

    // ================================================================
    //  2. دائرة — نقارن بـ bounding circle
    // ================================================================
    private static ShapeResult TryCircle(List<Point> pts)
    {
        var cx = pts.Average(p => p.X);
        var cy = pts.Average(p => p.Y);
        var radii = pts.Select(p => Dist(p, new Point { X = cx, Y = cy })).ToList();
        double avgR = radii.Average();
        if (avgR < 8) return new ShapeResult { ShapeType = "circle", Confidence = 0 };

        double stdDev = Math.Sqrt(radii.Average(r => Math.Pow(r - avgR, 2)));
        double confidence = Math.Max(0, 1.0 - stdDev / avgR);

        // النقاط المصحّحة: 64 نقطة على دائرة مثالية
        var corrected = Enumerable.Range(0, 64).Select(i =>
        {
            double angle = 2 * Math.PI * i / 64;
            return new Point { X = cx + avgR * Math.Cos(angle), Y = cy + avgR * Math.Sin(angle) };
        }).ToList();

        return new ShapeResult
        {
            ShapeType       = "circle",
            Confidence      = confidence,
            Label           = $"دائرة ({confidence:P0})",
            CorrectedPoints = corrected
        };
    }

    // ================================================================
    //  3. مستطيل — نستخدم convex hull + corner detection
    // ================================================================
    private static ShapeResult TryRectangle(List<Point> pts)
    {
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        if (w < 10 || h < 10) return new ShapeResult { ShapeType = "rectangle", Confidence = 0 };

        // كم نسبة النقاط قريبة من حواف المستطيل؟
        double margin = Math.Max(w, h) * 0.12;
        int near = pts.Count(p =>
            p.X - minX < margin || maxX - p.X < margin ||
            p.Y - minY < margin || maxY - p.Y < margin);

        double edgeFrac = (double)near / pts.Count;

        // هل المسار يغطي معظم محيط المستطيل؟
        bool hasAllCorners = HasCornerCoverage(pts, minX, minY, maxX, maxY, margin * 1.5);
        double confidence = edgeFrac * 0.6 + (hasAllCorners ? 0.4 : 0);

        // النقاط المصحّحة: المستطيل الكامل
        var corrected = new List<Point>
        {
            new() { X = minX, Y = minY }, new() { X = maxX, Y = minY },
            new() { X = maxX, Y = maxY }, new() { X = minX, Y = maxY },
            new() { X = minX, Y = minY }
        };

        return new ShapeResult
        {
            ShapeType       = "rectangle",
            Confidence      = confidence,
            Label           = $"مستطيل ({confidence:P0})",
            CorrectedPoints = corrected
        };
    }

    // ================================================================
    //  4. مثلث — نبحث عن 3 أركان بزوايا حادة
    // ================================================================
    private static ShapeResult TryTriangle(List<Point> pts)
    {
        // ابحث عن نقاط الانعطاف الحادة (corners)
        var corners = FindCorners(pts, angleThr: 40.0);
        if (corners.Count < 2 || corners.Count > 5)
            return new ShapeResult { ShapeType = "triangle", Confidence = 0 };

        // اختر أبعد 3 نقاط عن بعضها
        var hull = GetExtremePoints(pts, 3);
        if (hull.Count < 3)
            return new ShapeResult { ShapeType = "triangle", Confidence = 0 };

        double area = TriangleArea(hull[0], hull[1], hull[2]);
        if (area < 200) return new ShapeResult { ShapeType = "triangle", Confidence = 0 };

        // قياس انحراف النقاط عن أضلاع المثلث الثلاثة
        double totalDev = pts.Sum(p =>
            Math.Min(
                Math.Min(PointToLineDistance(p, hull[0], hull[1]),
                         PointToLineDistance(p, hull[1], hull[2])),
                         PointToLineDistance(p, hull[2], hull[0])));

        double perimeter = Dist(hull[0], hull[1]) + Dist(hull[1], hull[2]) + Dist(hull[2], hull[0]);
        double avgDev = totalDev / pts.Count;
        double confidence = Math.Max(0, 1.0 - avgDev / (perimeter * 0.1));

        var corrected = new List<Point> { hull[0], hull[1], hull[2], hull[0] };

        return new ShapeResult
        {
            ShapeType       = "triangle",
            Confidence      = confidence,
            Label           = $"مثلث ({confidence:P0})",
            CorrectedPoints = corrected
        };
    }

    // ================================================================
    //  5. سهم — خط + انعطافة حادة في النهاية
    // ================================================================
    private static ShapeResult TryArrow(List<Point> pts)
    {
        if (pts.Count < 8) return new ShapeResult { ShapeType = "arrow", Confidence = 0 };

        // الجزء الأول (80%) = خط مستقيم
        int split = (int)(pts.Count * 0.75);
        var body = pts.Take(split).ToList();
        var head = pts.Skip(split).ToList();

        var lineResult = TryLine(body);
        if (lineResult.Confidence < 0.7)
            return new ShapeResult { ShapeType = "arrow", Confidence = 0 };

        // الجزء الأخير يجب أن يكون قريباً من طرف الخط ومنعطفاً
        var arrowTip = pts.Last();
        double bodyLen = Dist(pts.First(), body.Last());
        double headLen = Dist(body.Last(), arrowTip);
        double ratio = headLen / (bodyLen + 0.001);

        double confidence = lineResult.Confidence * 0.7 +
                            (ratio is > 0.1 and < 0.5 ? 0.3 : 0);

        // رسم السهم المصحح
        var p0 = pts.First(); var pN = body.Last();
        double angle = Math.Atan2(pN.Y - p0.Y, pN.X - p0.X);
        double arrowSize = bodyLen * 0.18;
        var corrected = new List<Point>
        {
            p0, pN,
            new() { X = pN.X - arrowSize * Math.Cos(angle - 0.4), Y = pN.Y - arrowSize * Math.Sin(angle - 0.4) },
            pN,
            new() { X = pN.X - arrowSize * Math.Cos(angle + 0.4), Y = pN.Y - arrowSize * Math.Sin(angle + 0.4) }
        };

        return new ShapeResult
        {
            ShapeType       = "arrow",
            Confidence      = confidence,
            Label           = $"سهم ({confidence:P0})",
            CorrectedPoints = corrected
        };
    }

    // ================================================================
    //  مساعدات هندسية
    // ================================================================
    private static double Dist(Point a, Point b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static double PointToLineDistance(Point p, Point a, Point b)
    {
        double len = Dist(a, b);
        if (len < 1e-9) return Dist(p, a);
        return Math.Abs((b.Y - a.Y) * p.X - (b.X - a.X) * p.Y + b.X * a.Y - b.Y * a.X) / len;
    }

    private static List<Point> FindCorners(List<Point> pts, double angleThr)
    {
        var corners = new List<Point>();
        int step = Math.Max(1, pts.Count / 20);
        for (int i = step; i < pts.Count - step; i += step)
        {
            var prev = pts[i - step]; var curr = pts[i]; var next = pts[i + step];
            double a1 = Math.Atan2(curr.Y - prev.Y, curr.X - prev.X) * 180 / Math.PI;
            double a2 = Math.Atan2(next.Y - curr.Y, next.X - curr.X) * 180 / Math.PI;
            double diff = Math.Abs(a2 - a1);
            if (diff > 180) diff = 360 - diff;
            if (diff > angleThr) corners.Add(curr);
        }
        return corners;
    }

    private static List<Point> GetExtremePoints(List<Point> pts, int n)
    {
        var selected = new List<Point> { pts.First(), pts.Last() };
        if (n <= 2) return selected;

        // أضف النقطة الأبعد عن الخط الأول
        var p0 = selected[0]; var p1 = selected[1];
        var farthest = pts.MaxBy(p => PointToLineDistance(p, p0, p1))!;
        selected.Add(farthest);
        return selected;
    }

    private static double TriangleArea(Point a, Point b, Point c)
        => Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2.0;

    private static bool HasCornerCoverage(List<Point> pts, double x0, double y0,
                                           double x1, double y1, double margin)
    {
        bool tl = pts.Any(p => p.X - x0 < margin && p.Y - y0 < margin);
        bool tr = pts.Any(p => x1 - p.X < margin && p.Y - y0 < margin);
        bool bl = pts.Any(p => p.X - x0 < margin && y1 - p.Y < margin);
        bool br = pts.Any(p => x1 - p.X < margin && y1 - p.Y < margin);
        return tl && tr && bl && br;
    }

    private static ShapeResult None(string strokeId, string userName) => new()
    {
        ShapeType  = "none",
        Confidence = 0,
        Label      = "",
        StrokeId   = strokeId,
        UserName   = userName
    };
}
