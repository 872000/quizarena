using QuizArena.Api.Data;
using QuizArena.Api.Models;

namespace QuizArena.Api.Data;

public static class SeedData
{
    public static readonly Guid GeneralKnowledgeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid ScienceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static void Seed(QuizArenaContext db)
    {
        if (db.QuestionSets.Any())
        {
            return;
        }

        db.QuestionSets.Add(QuestionSet.Create(GeneralKnowledgeId, "General Knowledge", new[]
        {
            new Question("Which planet is known as the Red Planet?",
                new[] { "Venus", "Mars", "Jupiter", "Saturn" }, 1),
            new Question("What is the largest ocean on Earth?",
                new[] { "Atlantic", "Indian", "Arctic", "Pacific" }, 3),
            new Question("In which year did humans first land on the Moon?",
                new[] { "1965", "1969", "1972", "1975" }, 1),
            new Question("Which country invented paper?",
                new[] { "Egypt", "Greece", "China", "India" }, 2),
            new Question("How many continents are there on Earth?",
                new[] { "5", "6", "7", "8" }, 2),
            new Question("What is the currency of Japan?",
                new[] { "Won", "Yuan", "Yen", "Ringgit" }, 2),
            new Question("Which language has the most native speakers?",
                new[] { "English", "Spanish", "Hindi", "Mandarin Chinese" }, 3),
            new Question("The Great Barrier Reef is located off the coast of which country?",
                new[] { "Brazil", "Australia", "South Africa", "Thailand" }, 1),
            new Question("Which instrument has 88 keys?",
                new[] { "Guitar", "Piano", "Violin", "Flute" }, 1),
            new Question("What does 'www' stand for in a website address?",
                new[] { "World Wide Web", "World Web Window", "Web World Wide", "Wide World Web" }, 0)
        }));

        db.QuestionSets.Add(QuestionSet.Create(ScienceId, "Science Sprint", new[]
        {
            new Question("What is H2O commonly known as?",
                new[] { "Hydrogen", "Water", "Oxygen", "Helium" }, 1),
            new Question("Which force keeps us on the ground?",
                new[] { "Magnetism", "Friction", "Gravity", "Inertia" }, 2),
            new Question("What is the chemical symbol for gold?",
                new[] { "Go", "Gd", "Au", "Ag" }, 2),
            new Question("Which planet is closest to the Sun?",
                new[] { "Venus", "Mercury", "Mars", "Earth" }, 1),
            new Question("What gas do plants absorb from the atmosphere?",
                new[] { "Oxygen", "Nitrogen", "Carbon dioxide", "Hydrogen" }, 2),
            new Question("How many legs does a spider have?",
                new[] { "6", "8", "10", "12" }, 1),
            new Question("What is the hardest natural substance on Earth?",
                new[] { "Quartz", "Titanium", "Diamond", "Obsidian" }, 2),
            new Question("Which blood cells carry oxygen?",
                new[] { "White blood cells", "Platelets", "Red blood cells", "Plasma cells" }, 2),
            new Question("What is the boiling point of water at sea level?",
                new[] { "90°C", "100°C", "110°C", "120°C" }, 1),
            new Question("Which scientist proposed the theory of relativity?",
                new[] { "Newton", "Galileo", "Einstein", "Tesla" }, 2)
        }));

        db.SaveChanges();
    }
}
