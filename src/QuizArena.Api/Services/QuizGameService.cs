using System.Collections.Concurrent;
using QuizArena.Api.Models;

namespace QuizArena.Api.Services;

public sealed record QuestionBroadcast(
    int Index,
    int Total,
    string Text,
    string[] Options,
    DateTimeOffset EndsAtUtc,
    int TimeLimitSeconds);

public sealed record LeaderboardEntry(string Name, int Score);

public sealed record QuestionResult(
    int QuestionIndex,
    int CorrectIndex,
    IReadOnlyList<LeaderboardEntry> Leaderboard);

public sealed record AnswerResult(bool Accepted, bool Correct, int PointsAwarded, string Reason = "");

public sealed class RoomNotFoundException(string code)
    : Exception($"Room '{code}' does not exist.");

public sealed class QuizGameService
{
    private static readonly char[] CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    private const int CodeLength = 6;
    private const int BasePoints = 100;
    private const int MaxSpeedBonus = 100;

    private readonly ConcurrentDictionary<string, RoomSession> _rooms = new();

    public TimeSpan DefaultQuestionDuration { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan DefaultIntermission { get; set; } = TimeSpan.FromSeconds(3);

    public event Func<RoomSession, QuestionBroadcast, Task>? QuestionStarted;
    public event Func<RoomSession, QuestionResult, Task>? QuestionEnded;
    public event Func<RoomSession, Task>? GameEnded;
    public event Func<RoomSession, Task>? LobbyUpdated;

    public static int ScoreAnswer(TimeSpan elapsed, TimeSpan timeLimit, bool correct)
    {
        if (!correct)
        {
            return 0;
        }

        var remaining = Math.Clamp(1 - elapsed.TotalSeconds / timeLimit.TotalSeconds, 0, 1);
        return BasePoints + (int)Math.Round(MaxSpeedBonus * remaining);
    }

    public RoomSession CreateRoom(
        string name,
        string hostName,
        QuestionSet set,
        string hostConnectionId,
        TimeSpan? questionDuration = null)
    {
        if (set.GetQuestions().Count == 0)
        {
            throw new InvalidOperationException("Question set has no questions.");
        }

        var code = GenerateUniqueCode();
        var room = new RoomSession
        {
            Code = code,
            Name = name,
            QuestionSetId = set.Id,
            QuestionSetTitle = set.Title,
            Questions = set.GetQuestions(),
            HostConnectionId = hostConnectionId,
            QuestionDuration = questionDuration ?? DefaultQuestionDuration,
            Intermission = DefaultIntermission
        };
        room.Players[hostConnectionId] = new PlayerState { ConnectionId = hostConnectionId, Name = UniqueName(room, hostName) };

        if (!_rooms.TryAdd(code, room))
        {
            throw new InvalidOperationException("Room code collision; try again.");
        }

        _ = RaiseLobbyUpdated(room);
        return room;
    }

    public RoomSession JoinRoom(string code, string playerName, string connectionId)
    {
        var room = GetRoomOrThrow(code);
        lock (room.Lock)
        {
            if (room.Status != RoomStatus.Waiting)
            {
                throw new InvalidOperationException("Game already started; you cannot join.");
            }

            room.Players[connectionId] = new PlayerState
            {
                ConnectionId = connectionId,
                Name = UniqueName(room, playerName)
            };
        }

        _ = RaiseLobbyUpdated(room);
        return room;
    }

    public void StartGame(string code, string connectionId)
    {
        var room = GetRoomOrThrow(code);
        lock (room.Lock)
        {
            if (room.HostConnectionId != connectionId)
            {
                throw new InvalidOperationException("Only the host can start the game.");
            }

            if (room.Status != RoomStatus.Waiting)
            {
                throw new InvalidOperationException("Game already started.");
            }

            room.Status = RoomStatus.Live;
        }

        BeginQuestion(room, 0);
    }

    public AnswerResult SubmitAnswer(string code, string connectionId, int questionIndex, int optionIndex)
    {
        var room = GetRoomOrThrow(code);
        lock (room.Lock)
        {
            if (room.Status != RoomStatus.Live || room.QuestionStartedAtUtc is null)
            {
                return new AnswerResult(false, false, 0, "No active question.");
            }

            if (questionIndex != room.CurrentQuestionIndex)
            {
                return new AnswerResult(false, false, 0, "Question already closed.");
            }

            if (!room.Players.TryGetValue(connectionId, out var player))
            {
                return new AnswerResult(false, false, 0, "Unknown player.");
            }

            if (player.AnsweredQuestionIndex == questionIndex)
            {
                return new AnswerResult(false, false, 0, "Already answered.");
            }

            var question = room.Questions[questionIndex];
            if (optionIndex < 0 || optionIndex >= question.Options.Length)
            {
                return new AnswerResult(false, false, 0, "Invalid option.");
            }

            var elapsed = DateTimeOffset.UtcNow - room.QuestionStartedAtUtc.Value;
            var correct = optionIndex == question.CorrectIndex;
            var points = ScoreAnswer(elapsed, room.QuestionDuration, correct);

            player.Score += points;
            player.AnsweredQuestionIndex = questionIndex;

            return new AnswerResult(true, correct, points);
        }
    }

    /// <summary>Removes a disconnected player; deletes the room when empty.</summary>
    public RoomSession? RemovePlayer(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            lock (room.Lock)
            {
                if (!room.Players.Remove(connectionId))
                {
                    continue;
                }

                if (room.Players.Count == 0)
                {
                    room.TimerCts?.Cancel();
                    _rooms.TryRemove(room.Code, out _);
                    return null;
                }

                if (room.HostConnectionId == connectionId)
                {
                    room.HostConnectionId = room.Players.Keys.First();
                }
            }

            _ = RaiseLobbyUpdated(room);
            return room;
        }

        return null;
    }

    public RoomSession? GetRoom(string code) =>
        _rooms.TryGetValue(code.ToUpperInvariant(), out var room) ? room : null;

    public IReadOnlyList<RoomSession> ListRooms() =>
        _rooms.Values.Where(r => r.Status == RoomStatus.Waiting).OrderBy(r => r.Name).ToList();

    public static IReadOnlyList<LeaderboardEntry> BuildLeaderboard(RoomSession room) =>
        room.Players.Values
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new LeaderboardEntry(p.Name, p.Score))
            .ToList();

    private RoomSession GetRoomOrThrow(string code) =>
        GetRoom(code) ?? throw new RoomNotFoundException(code);

    private void BeginQuestion(RoomSession room, int index)
    {
        QuestionBroadcast broadcast;
        lock (room.Lock)
        {
            room.TimerCts?.Cancel();
            room.CurrentQuestionIndex = index;
            room.QuestionStartedAtUtc = DateTimeOffset.UtcNow;
            var question = room.Questions[index];
            broadcast = new QuestionBroadcast(
                index,
                room.Questions.Count,
                question.Text,
                question.Options,
                room.QuestionStartedAtUtc.Value.Add(room.QuestionDuration),
                (int)room.QuestionDuration.TotalSeconds);
            room.TimerCts = new CancellationTokenSource();
        }

        _ = RaiseQuestionStarted(room, broadcast);

        var cts = room.TimerCts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(room.QuestionDuration, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            EndQuestion(room, index);
        });
    }

    private void EndQuestion(RoomSession room, int index)
    {
        QuestionResult result;
        bool finished;
        lock (room.Lock)
        {
            if (room.Status != RoomStatus.Live || room.CurrentQuestionIndex != index)
            {
                return; // superseded by a newer question or game end
            }

            var question = room.Questions[index];
            result = new QuestionResult(index, question.CorrectIndex, BuildLeaderboard(room));
            finished = index + 1 >= room.Questions.Count;
            if (finished)
            {
                room.Status = RoomStatus.Finished;
                room.TimerCts?.Cancel();
            }
        }

        _ = RaiseQuestionEnded(room, result);

        if (finished)
        {
            _ = RaiseGameEnded(room);
            return;
        }

        var intermission = room.Intermission;
        _ = Task.Run(async () =>
        {
            await Task.Delay(intermission);
            lock (room.Lock)
            {
                if (room.Status != RoomStatus.Live || room.CurrentQuestionIndex != index)
                {
                    return;
                }
            }

            BeginQuestion(room, index + 1);
        });
    }

    private string GenerateUniqueCode()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = new string(Enumerable.Range(0, CodeLength)
                .Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)])
                .ToArray());
            if (!_rooms.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique room code.");
    }

    private static string UniqueName(RoomSession room, string desired)
    {
        var taken = new HashSet<string>(room.Players.Values.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(desired))
        {
            return desired;
        }

        var i = 2;
        while (taken.Contains($"{desired} {i}"))
        {
            i++;
        }

        return $"{desired} {i}";
    }

    private Task RaiseQuestionStarted(RoomSession room, QuestionBroadcast broadcast) =>
        QuestionStarted?.Invoke(room, broadcast) ?? Task.CompletedTask;

    private Task RaiseQuestionEnded(RoomSession room, QuestionResult result) =>
        QuestionEnded?.Invoke(room, result) ?? Task.CompletedTask;

    private Task RaiseGameEnded(RoomSession room) =>
        GameEnded?.Invoke(room) ?? Task.CompletedTask;

    private Task RaiseLobbyUpdated(RoomSession room) =>
        LobbyUpdated?.Invoke(room) ?? Task.CompletedTask;
}
