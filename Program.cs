using CollaborativeWhiteboard.Hubs;
using CollaborativeWhiteboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// SignalR — WebSocket
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 2 * 1024 * 1024; // 2MB
});

// Singleton services
builder.Services.AddSingleton<WhiteboardStore>();
builder.Services.AddSingleton<ShapeRecognitionService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<WhiteboardHub>("/whiteboardHub");

app.Run();
