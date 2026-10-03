using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizArena.Api.Data;
using QuizArena.Api.Models;
using QuizArena.Api.Services;

namespace QuizArena.Api.Controllers;

[ApiController]
[Route("api/questionsets")]
public sealed class QuestionSetsController : ControllerBase
{
    private readonly QuizArenaContext _db;

    public QuestionSetsController(QuizArenaContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(CancellationToken ct)
    {
        var sets = await _db.QuestionSets.ToListAsync(ct);
        return Ok(sets.Select(s => new
        {
            s.Id,
            s.Title,
            QuestionCount = s.GetQuestions().Count
        }).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var set = await _db.QuestionSets.FindAsync(new object[] { id }, ct);
        if (set is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            set.Id,
            set.Title,
            Questions = set.GetQuestions().Select((q, i) => new
            {
                Index = i,
                q.Text,
                q.Options,
                q.CorrectIndex
            })
        });
    }
}

[ApiController]
[Route("api/rooms")]
public sealed class RoomsController : ControllerBase
{
    private readonly QuizGameService _game;

    public RoomsController(QuizGameService game) => _game = game;

    [HttpGet]
    public ActionResult<IReadOnlyList<object>> List()
    {
        var rooms = _game.ListRooms();
        return Ok(rooms.Select(r => new
        {
            r.Code,
            r.Name,
            Status = r.Status.ToString(),
            r.QuestionSetTitle,
            PlayerCount = r.Players.Count
        }).ToList());
    }

    [HttpGet("{code}")]
    public ActionResult<object> Get(string code)
    {
        var room = _game.GetRoom(code);
        if (room is null)
        {
            return NotFound();
        }

        return Ok(ToDto(room));
    }

    public static object ToDto(RoomSession room) => new
    {
        room.Code,
        room.Name,
        Status = room.Status.ToString(),
        room.QuestionSetTitle,
        room.CurrentQuestionIndex,
        TotalQuestions = room.Questions.Count,
        Players = QuizGameService.BuildLeaderboard(room).Select(e => new { e.Name, e.Score })
    };
}
