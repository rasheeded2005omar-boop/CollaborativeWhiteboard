namespace CollaborativeWhiteboard.Models;

public class Point
{
    public double X { get; set; }
    public double Y { get; set; }
}

// ضربة ريشة كاملة
public class Stroke
{
    public string Id        { get; set; } = Guid.NewGuid().ToString("N");
    public string Tool      { get; set; } = "pen";
    public string Color     { get; set; } = "#1e1e1e";
    public double Size      { get; set; } = 3;
    public List<Point> Points { get; set; } = new();
    public string UserId    { get; set; } = "";
    public string UserName  { get; set; } = "مجهول";
    public DateTime At      { get; set; } = DateTime.UtcNow;

    // نتيجة AI Shape Recognition (إن وُجدت)
    public ShapeResult? RecognizedShape { get; set; }
}

// Delta — نقاط جديدة فقط أثناء الرسم
public class StrokeDelta
{
    public string StrokeId      { get; set; } = "";
    public string Tool          { get; set; } = "pen";
    public string Color         { get; set; } = "#1e1e1e";
    public double Size          { get; set; } = 3;
    public List<Point> NewPoints { get; set; } = new();
    public bool IsComplete      { get; set; } = false;
    public string UserId        { get; set; } = "";
    public string UserName      { get; set; } = "مجهول";
    // كل النقاط (لازمة عند IsComplete لتحليل الشكل)
    public List<Point> AllPoints { get; set; } = new();
}

// نتيجة تعرف الشكل من AI
public class ShapeResult
{
    public string ShapeType     { get; set; } = "none"; // circle | rectangle | triangle | line | arrow | none
    public double Confidence    { get; set; } = 0;      // 0.0 → 1.0
    public string Label         { get; set; } = "";     // نص يظهر للمستخدم
    public List<Point> CorrectedPoints { get; set; } = new(); // النقاط المصحّحة هندسياً
    public string StrokeId      { get; set; } = "";
    public string UserName      { get; set; } = "";
}

// معلومات المستخدم
public class UserInfo
{
    public string ConnectionId  { get; set; } = "";
    public string UserId        { get; set; } = "";
    public string UserName      { get; set; } = "مجهول";
    public string Color         { get; set; } = "#185FA5";
    public DateTime JoinedAt    { get; set; } = DateTime.UtcNow;
}

// إحصائيات السبورة
public class BoardStats
{
    public int TotalStrokes     { get; set; }
    public int UserCount        { get; set; }
    public int RecognizedShapes { get; set; }
    public Dictionary<string, int> ShapeBreakdown { get; set; } = new();
}
