using QuizArena.Api.Models;
using QuizArena.Api.Services;

namespace QuizArena.Tests;

public sealed class QuizGameServiceTests
{
    private static readonly TimeSpan ShortQuestion = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ShortIntermission = TimeSpan.FromMilliseconds(100);

    private static QuestionSet TwoQuestionSet() => QuestionSet.Create(
        Guid.NewGuid(),
        "Test Set",
        new[]
        {
            new Question("2 + 2?", new[] { "3", "4", "5", "6" }, 1),
            new Question("Capital of France?", new[] { "London", "Berlin", "Paris", "Rome" }, 2)
        });

    private static QuizGameService NewService() => new()
    {
        DefaultQuestionDuration = ShortQuestion,
        DefaultIntermission = ShortIntermission
    };

    private static async Task<T> WaitFor<T>(TaskCompletionSource<T> tcs, string what)
    {
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(completed, tcs.Task), $"Timed out waiting for {what}.");
        return await tcs.Task;
    }

    [Fact]
    public void CreateRoom_GeneratesSixCharCode()
    {
        var game = NewService();
        var room = game.CreateRoom("Friday Quiz", "Host", TwoQuestionSet(), "conn-host");

        Assert.Equal(6, room.Code.Length);
        Assert.Equal(room.Code, room.Code.ToUpperInvariant());
        Assert.All(room.Code, c => Assert.Contains(c, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"));
        Assert.Equal(RoomStatus.Waiting, room.Status);
        Assert.Single(room.Players);
    }

    [Fact]
    public void CreateRoom_CodesAreUnique()
    {
        var game = NewService();
        var codes = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var room = game.CreateRoom($"Room {i}", "Host", TwoQuestionSet(), $"conn-{i}");
            Assert.True(codes.Add(room.Code), $"Duplicate code {room.Code}.");
        }
    }

    [Fact]
    public void CreateRoom_EmptyQuestionSet_Throws()
    {
        var game = NewService();
        var empty = QuestionSet.Create(Guid.NewGuid(), "Empty", Array.Empty<Question>());

        Assert.Throws<InvalidOperationException>(() => game.CreateRoom("R", "H", empty, "conn"));
    }

    [Fact]
    public void JoinRoom_AddsPlayer_DuplicateNamesSuffixed()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Alex", TwoQuestionSet(), "conn-host");

        game.JoinRoom(room.Code, "Alex", "conn-2");

        var names = room.Players.Values.Select(p => p.Name).ToList();
        Assert.Contains("Alex", names);
        Assert.Contains("Alex 2", names);
    }

    [Fact]
    public void JoinRoom_UnknownCode_Throws()
    {
        var game = NewService();
        Assert.Throws<RoomNotFoundException>(() => game.JoinRoom("ZZZZZZ", "Alex", "conn"));
    }

    [Fact]
    public void JoinRoom_AfterStart_Throws()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");
        game.StartGame(room.Code, "conn-host");

        Assert.Throws<InvalidOperationException>(() => game.JoinRoom(room.Code, "Late", "conn-late"));
    }

    [Fact]
    public void StartGame_NonHost_Throws()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");
        game.JoinRoom(room.Code, "Player", "conn-player");

        Assert.Throws<InvalidOperationException>(() => game.StartGame(room.Code, "conn-player"));
    }

    [Fact]
    public void StartGame_Twice_Throws()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");
        game.StartGame(room.Code, "conn-host");

        Assert.Throws<InvalidOperationException>(() => game.StartGame(room.Code, "conn-host"));
    }

    [Theory]
    [InlineData(0, true, 200)]    // instant correct: full speed bonus
    [InlineData(7.5, true, 150)]  // half the time left
    [InlineData(15, true, 100)]   // answered at the buzzer: base points only
    [InlineData(30, true, 100)]   // clamped: never below base for a correct answer
    [InlineData(0, false, 0)]     // wrong answer scores nothing
    public void ScoreAnswer_MatchesRules(double elapsedSeconds, bool correct, int expected)
    {
        var score = QuizGameService.ScoreAnswer(
            TimeSpan.FromSeconds(elapsedSeconds), TimeSpan.FromSeconds(15), correct);

        Assert.Equal(expected, score);
    }

    [Fact]
    public async Task FullGameFlow_QuestionTimerScoringAndLeaderboard()
    {
        var game = NewService();
        var startedTcs = new TaskCompletionSource<QuestionBroadcast>();
        var endedTcs = new TaskCompletionSource<QuestionResult>();
        var gameEndedTcs = new TaskCompletionSource<bool>();
        var startedCount = 0;

        game.QuestionStarted += (_, q) =>
        {
            startedCount++;
            startedTcs.TrySetResult(q);
            return Task.CompletedTask;
        };
        game.QuestionEnded += (_, r) => { endedTcs.TrySetResult(r); return Task.CompletedTask; };
        game.GameEnded += _ => { gameEndedTcs.TrySetResult(true); return Task.CompletedTask; };

        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");
        game.JoinRoom(room.Code, "Player", "conn-player");
        game.StartGame(room.Code, "conn-host");

        // Question 1 broadcast.
        var q1 = await WaitFor(startedTcs, "question 1 broadcast");
        Assert.Equal(0, q1.Index);
        Assert.Equal(2, q1.Total);
        Assert.Equal("2 + 2?", q1.Text);
        Assert.Equal(4, q1.Options.Length);

        // Host answers correctly at once; player answers wrong.
        var hostAnswer = game.SubmitAnswer(room.Code, "conn-host", 0, 1);
        Assert.True(hostAnswer.Accepted);
        Assert.True(hostAnswer.Correct);
        Assert.InRange(hostAnswer.PointsAwarded, 150, 200);

        var playerAnswer = game.SubmitAnswer(room.Code, "conn-player", 0, 0);
        Assert.True(playerAnswer.Accepted);
        Assert.False(playerAnswer.Correct);
        Assert.Equal(0, playerAnswer.PointsAwarded);

        // Double answer is rejected.
        var duplicate = game.SubmitAnswer(room.Code, "conn-host", 0, 1);
        Assert.False(duplicate.Accepted);

        // Timer expiry ends question 1 with the correct answer revealed.
        var r1 = await WaitFor(endedTcs, "question 1 result");
        Assert.Equal(1, r1.CorrectIndex);
        Assert.Equal("Host", r1.Leaderboard[0].Name);
        Assert.True(r1.Leaderboard[0].Score > 0);
        Assert.Equal(0, r1.Leaderboard[1].Score);

        // Question 2 starts after the intermission.
        startedTcs = new TaskCompletionSource<QuestionBroadcast>();
        endedTcs = new TaskCompletionSource<QuestionResult>();
        var q2 = await WaitFor(startedTcs, "question 2 broadcast");
        Assert.Equal(1, q2.Index);

        // Answer with a stale question index is rejected.
        var stale = game.SubmitAnswer(room.Code, "conn-player", 0, 1);
        Assert.False(stale.Accepted);

        var r2 = await WaitFor(endedTcs, "question 2 result");
        Assert.Equal(2, r2.CorrectIndex);

        // Last question -> game ends.
        await WaitFor(gameEndedTcs, "game end");
        Assert.Equal(RoomStatus.Finished, room.Status);
        Assert.Equal(2, startedCount);
    }

    [Fact]
    public async Task SubmitAnswer_BeforeStart_Rejected()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");

        var result = game.SubmitAnswer(room.Code, "conn-host", 0, 1);

        Assert.False(result.Accepted);
        await Task.CompletedTask;
    }

    [Fact]
    public void RemovePlayer_LastOneDeletesRoom()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");

        var result = game.RemovePlayer("conn-host");

        Assert.Null(result);
        Assert.Null(game.GetRoom(room.Code));
        Assert.Empty(game.ListRooms());
    }

    [Fact]
    public void RemovePlayer_HostLeaves_PromotesAnotherPlayer()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");
        game.JoinRoom(room.Code, "Player", "conn-player");

        game.RemovePlayer("conn-host");

        Assert.Equal("conn-player", room.HostConnectionId);
        Assert.NotNull(game.GetRoom(room.Code));
    }

    [Fact]
    public void ListRooms_OnlyWaitingRooms()
    {
        var game = NewService();
        var waiting = game.CreateRoom("Waiting", "H", TwoQuestionSet(), "c1");
        var live = game.CreateRoom("Live", "H", TwoQuestionSet(), "c2");
        game.StartGame(live.Code, "c2");

        var rooms = game.ListRooms();

        Assert.Single(rooms);
        Assert.Equal(waiting.Code, rooms[0].Code);
    }

    [Fact]
    public void BuildLeaderboard_SortsByScoreThenName()
    {
        var game = NewService();
        var room = game.CreateRoom("R", "Host", TwoQuestionSet(), "conn-host");
        game.JoinRoom(room.Code, "Zed", "c2");
        game.JoinRoom(room.Code, "Amy", "c3");
        room.Players["conn-host"].Score = 100;
        room.Players["c2"].Score = 100;
        room.Players["c3"].Score = 250;

        var board = QuizGameService.BuildLeaderboard(room);

        Assert.Equal("Amy", board[0].Name);
        Assert.Equal("Host", board[1].Name); // tie broken by name
        Assert.Equal("Zed", board[2].Name);
    }
}
