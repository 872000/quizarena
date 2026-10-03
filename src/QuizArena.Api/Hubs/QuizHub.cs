using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QuizArena.Api.Data;
using QuizArena.Api.Models;
using QuizArena.Api.Services;

namespace QuizArena.Api.Hubs;

/// <summary>
/// Thin SignalR adapter over <see cref="QuizGameService"/>. All game rules
/// live in the service (unit-tested); the hub only moves data between the
/// service and connected clients.
/// </summary>
public sealed class QuizHub : Hub
{
    private readonly QuizGameService _game;
    private readonly QuizArenaContext _db;

    public QuizHub(QuizGameService game, QuizArenaContext db)
    {
        _game = game;
        _db = db;
    }

    public async Task<string> CreateRoom(string roomName, string playerName, Guid questionSetId)
    {
        var set = await _db.QuestionSets.FindAsync(questionSetId);
        if (set is null)
        {
            throw new HubException("Unknown question set.");
        }

        var room = _game.CreateRoom(roomName, playerName, set, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        return room.Code;
    }

    public async Task JoinRoom(string code, string playerName)
    {
        try
        {
            _game.JoinRoom(code, playerName, Context.ConnectionId);
        }
        catch (RoomNotFoundException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, code.ToUpperInvariant());
    }

    public Task StartGame(string code)
    {
        try
        {
            _game.StartGame(code, Context.ConnectionId);
        }
        catch (RoomNotFoundException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task<AnswerResult> SubmitAnswer(string code, int questionIndex, int optionIndex) =>
        Task.FromResult(_game.SubmitAnswer(code, Context.ConnectionId, questionIndex, optionIndex));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _game.RemovePlayer(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
