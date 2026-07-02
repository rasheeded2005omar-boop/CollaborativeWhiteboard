using CollaborativeWhiteboard.Models;
using CollaborativeWhiteboard.Services;
using Microsoft.AspNetCore.SignalR;

namespace CollaborativeWhiteboard.Hubs;

// ================================================================
//  WhiteboardHub — WebSocket Hub
//
//  ميزات الـ Streaming:
//    1. Delta Streaming   → نقاط جديدة فقط أثناء الرسم
//    2. Late-join Replay  → إعادة بث كل الرسومات للمنضم الجديد
//
//  الميزة الذكية:
//    3. AI Shape Recognition → تحليل الضربة عند اكتمالها
//       وإرسال الشكل المصحّح هندسياً لجميع المستخدمين
// ================================================================
public class WhiteboardHub : Hub
{
    private readonly WhiteboardStore _store;
    private readonly ShapeRecognitionService _ai;

    private static readonly string[] UserColors =
        { "#185FA5","#A32D2D","#3B6D11","#854F0B","#534AB7","#993556","#0F6E56" };

    public WhiteboardHub(WhiteboardStore store, ShapeRecognitionService ai)
    {
        _store = store;
        _ai    = ai;
    }

    // ===========================================================
    //  اتصال جديد
    // ===========================================================
    public override async Task OnConnectedAsync()
    {
        var connId    = Context.ConnectionId;
        var userName  = Context.GetHttpContext()?.Request.Query["name"].ToString()
                        ?? "مجهول";
        var colorIdx  = Math.Abs(connId.GetHashCode()) % UserColors.Length;

        var user = new UserInfo
        {
            ConnectionId = connId,
            UserId       = connId,
            UserName     = userName,
            Color        = UserColors[colorIdx]
        };
        _store.AddUser(user);

        // أخبر الآخرين
        await Clients.Others.SendAsync("UserJoined", user);

        // أرسل للمنضم الجديد معلوماته وقائمة المتصلين
        await Clients.Caller.SendAsync("YourInfo", user);
        await Clients.Caller.SendAsync("ActiveUsers", _store.GetUsers());

        // -------------------------------------------------------
        //  Late-join Replay — إعادة بث كل الرسومات السابقة
        //  بما فيها الأشكال المصحّحة سابقاً
        // -------------------------------------------------------
        var allStrokes = _store.GetAllStrokes();
        if (allStrokes.Count > 0)
        {
            await Clients.Caller.SendAsync("ReplayStart", allStrokes.Count);
            const int batchSize = 20;
            for (int i = 0; i < allStrokes.Count; i += batchSize)
            {
                var batch = allStrokes.Skip(i).Take(batchSize).ToList();
                await Clients.Caller.SendAsync("ReplayBatch", batch);
                await Task.Delay(16);
            }
            await Clients.Caller.SendAsync("ReplayEnd");
        }

        // أرسل الإحصائيات الحالية
        await Clients.Caller.SendAsync("StatsUpdate", _store.GetStats());

        await base.OnConnectedAsync();
    }

    // ===========================================================
    //  انقطاع الاتصال
    // ===========================================================
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = _store.GetUser(Context.ConnectionId);
        _store.RemoveUser(Context.ConnectionId);
        if (user != null)
            await Clients.Others.SendAsync("UserLeft", user);
        await base.OnDisconnectedAsync(exception);
    }

    // ===========================================================
    //  Delta Streaming — أثناء الرسم
    // ===========================================================
    public async Task SendDelta(StrokeDelta delta)
    {
        var user = _store.GetUser(Context.ConnectionId);
        if (user == null) return;

        delta.UserId   = user.UserId;
        delta.UserName = user.UserName;

        // -------------------------------------------------------
        //  عند اكتمال الضربة → شغّل AI Shape Recognition
        // -------------------------------------------------------
        if (delta.IsComplete && delta.AllPoints.Count >= 4)
        {
            var shapeResult = _ai.Recognize(delta.AllPoints, delta.StrokeId, user.UserName);

            // احفظ الضربة (مع نتيجة الـ AI إن وُجدت)
            var stroke = new Stroke
            {
                Id              = delta.StrokeId,
                Tool            = delta.Tool,
                Color           = delta.Color,
                Size            = delta.Size,
                Points          = delta.AllPoints,
                UserId          = user.UserId,
                UserName        = user.UserName,
                RecognizedShape = shapeResult.ShapeType != "none" ? shapeResult : null
            };
            _store.AddStroke(stroke);

            // أرسل الـ delta أولاً (لإنهاء الرسمة الأصلية)
            await Clients.Others.SendAsync("ReceiveDelta", delta);

            // ثم أرسل نتيجة الـ AI لجميع المستخدمين (بما فيهم الراسم)
            if (shapeResult.ShapeType != "none")
            {
                await Clients.All.SendAsync("ShapeRecognized", shapeResult);
                // حدّث الإحصائيات
                await Clients.All.SendAsync("StatsUpdate", _store.GetStats());
            }
        }
        else
        {
            // أثناء الرسم — أرسل الـ delta بدون تحليل
            await Clients.Others.SendAsync("ReceiveDelta", delta);

            if (delta.IsComplete)
            {
                // ضربة قصيرة جداً (< 4 نقاط)
                var stroke = new Stroke
                {
                    Id       = delta.StrokeId,
                    Tool     = delta.Tool,
                    Color    = delta.Color,
                    Size     = delta.Size,
                    Points   = delta.AllPoints.Count > 0 ? delta.AllPoints : delta.NewPoints,
                    UserId   = user.UserId,
                    UserName = user.UserName
                };
                _store.AddStroke(stroke);
            }
        }
    }

    // ===========================================================
    //  تراجع
    // ===========================================================
    public async Task Undo()
    {
        var user = _store.GetUser(Context.ConnectionId);
        if (user == null) return;

        var last = _store.GetAllStrokes()
                         .LastOrDefault(s => s.UserId == user.UserId);
        if (last == null) return;

        _store.RemoveStroke(last.Id);
        await Clients.All.SendAsync("StrokeRemoved", last.Id);
        await Clients.All.SendAsync("StatsUpdate", _store.GetStats());
    }

    // ===========================================================
    //  مسح الكل
    // ===========================================================
    public async Task ClearAll()
    {
        _store.ClearAll();
        await Clients.All.SendAsync("BoardCleared");
        await Clients.All.SendAsync("StatsUpdate", _store.GetStats());
    }

    // ===========================================================
    //  حركة المؤشر
    // ===========================================================
    public async Task CursorMove(double x, double y)
    {
        var user = _store.GetUser(Context.ConnectionId);
        if (user == null) return;
        await Clients.Others.SendAsync("UserCursor", new
        {
            userId   = user.UserId,
            userName = user.UserName,
            color    = user.Color,
            x, y
        });
    }
}
