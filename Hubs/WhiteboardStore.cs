using CollaborativeWhiteboard.Models;
using System.Collections.Concurrent;

namespace CollaborativeWhiteboard.Hubs;

public class WhiteboardStore
{
    private readonly List<Stroke> _strokes = new();
    private readonly ConcurrentDictionary<string, UserInfo> _users = new();
    private readonly object _lock = new();

    // إحصائيات الأشكال المعترف بها
    private readonly ConcurrentDictionary<string, int> _shapeStats = new();

    // ---- الضربات ----
    public void AddStroke(Stroke stroke)
    {
        lock (_lock) { _strokes.Add(stroke); }
        if (stroke.RecognizedShape?.ShapeType is string t && t != "none")
            _shapeStats.AddOrUpdate(t, 1, (_, v) => v + 1);
    }

    public IReadOnlyList<Stroke> GetAllStrokes()
    {
        lock (_lock) { return _strokes.ToList(); }
    }

    public void RemoveStroke(string strokeId)
    {
        lock (_lock) { _strokes.RemoveAll(s => s.Id == strokeId); }
    }

    public void ClearAll()
    {
        lock (_lock) { _strokes.Clear(); }
        _shapeStats.Clear();
    }

    // ---- المستخدمون ----
    public void AddUser(UserInfo user)    => _users[user.ConnectionId] = user;
    public void RemoveUser(string connId) => _users.TryRemove(connId, out _);
    public IReadOnlyList<UserInfo> GetUsers() => _users.Values.ToList();
    public UserInfo? GetUser(string connId)
        => _users.TryGetValue(connId, out var u) ? u : null;

    // ---- إحصائيات ----
    public BoardStats GetStats() => new()
    {
        TotalStrokes     = _strokes.Count,
        UserCount        = _users.Count,
        RecognizedShapes = _shapeStats.Values.Sum(),
        ShapeBreakdown   = new Dictionary<string, int>(_shapeStats)
    };
}
