using Microsoft.AspNetCore.Mvc;
using CollaborativeWhiteboard.Hubs;

namespace CollaborativeWhiteboard.Controllers;

public class HomeController : Controller
{
    private readonly WhiteboardStore _store;
    public HomeController(WhiteboardStore store) => _store = store;

    public IActionResult Index() => View();

    [HttpGet("/api/stats")]
    public IActionResult Stats() => Json(_store.GetStats());
}
