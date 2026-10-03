using System.Text.Json;

namespace QuizArena.Api.Models;

public enum RoomStatus
{
    Waiting,
    Live,
    Finished
}

public sealed record Question(string Text, string[] Options, int CorrectIndex);

public sealed class QuestionSet
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string QuestionsJson { get; set; } = "[]";

    public IReadOnlyList<Question> GetQuestions() =>
        JsonSerializer.Deserialize<List<Question>>(QuestionsJson) ?? new List<Question>();

    public static QuestionSet Create(Guid id, string title, IEnumerable<Question> questions) => new()
    {
        Id = id,
        Title = title,
        QuestionsJson = JsonSerializer.Serialize(questions.ToList())
    };
}

public sealed class PlayerState
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public int AnsweredQuestionIndex { get; set; } = -1;
}

/// <summary>In-memory live state for one quiz room.</summary>
public sealed class RoomSession
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid QuestionSetId { get; set; }
    public string QuestionSetTitle { get; set; } = string.Empty;
    public IReadOnlyList<Question> Questions { get; set; } = Array.Empty<Question>();
    public RoomStatus Status { get; set; } = RoomStatus.Waiting;
    public string HostConnectionId { get; set; } = string.Empty;
    public Dictionary<string, PlayerState> Players { get; } = new();
    public int CurrentQuestionIndex { get; set; } = -1;
    public DateTimeOffset? QuestionStartedAtUtc { get; set; }
    public TimeSpan QuestionDuration { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan Intermission { get; set; } = TimeSpan.FromSeconds(3);
    public CancellationTokenSource? TimerCts { get; set; }
    public object Lock { get; } = new();
}
