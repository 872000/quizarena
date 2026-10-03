using Microsoft.EntityFrameworkCore;
using QuizArena.Api.Models;

namespace QuizArena.Api.Data;

public sealed class QuizArenaContext : DbContext
{
    public QuizArenaContext(DbContextOptions<QuizArenaContext> options) : base(options)
    {
    }

    public DbSet<QuestionSet> QuestionSets => Set<QuestionSet>();
}
