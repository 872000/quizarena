using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QuizArena.Api.Controllers;
using QuizArena.Api.Data;
using QuizArena.Api.Hubs;
using QuizArena.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QuizArenaContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<QuizGameService>();
builder.Services.AddSignalR();
builder.Services.AddControllers();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<QuizHub>("/quizhub");

// Wire game events to SignalR broadcasts.
var game = app.Services.GetRequiredService<QuizGameService>();
var hub = app.Services.GetRequiredService<IHubContext<QuizHub>>();

game.QuestionStarted += (room, q) =>
    hub.Clients.Group(room.Code).SendAsync("QuestionStarted", q);
game.QuestionEnded += (room, r) =>
    hub.Clients.Group(room.Code).SendAsync("QuestionEnded", r);
game.GameEnded += room =>
    hub.Clients.Group(room.Code).SendAsync("GameEnded", RoomsController.ToDto(room));
game.LobbyUpdated += room =>
    hub.Clients.Group(room.Code).SendAsync("LobbyUpdated", RoomsController.ToDto(room));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuizArenaContext>();
    db.Database.EnsureCreated();
    SeedData.Seed(db);
}

app.Run();

// Exposed for WebApplicationFactory integration tests.
public partial class Program
{
}
