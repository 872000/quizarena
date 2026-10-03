using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using QuizArena.Api.Data;

namespace QuizArena.Tests;

public sealed class QuizArenaFactory : WebApplicationFactory<Program>
{
    // One shared in-memory store for every service provider this factory builds,
    // so startup seeding and request handling always see the same data.
    private readonly InMemoryDatabaseRoot _databaseRoot = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var root = _databaseRoot;
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<QuizArenaContext>));
            services.Remove(descriptor);
            services.AddDbContext<QuizArenaContext>(options =>
                options.UseInMemoryDatabase("QuizArenaTests", root));
        });
    }
}

public sealed class QuizArenaApiTests : IDisposable
{
    private readonly QuizArenaFactory _factory = new();
    private readonly HttpClient _client;
    private bool _disposed;

    public QuizArenaApiTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client.Dispose();
            _factory.Dispose();
            _disposed = true;
        }
    }

    [Fact]
    public async Task QuestionSets_ListsSeededSets()
    {
        var sets = await _client.GetFromJsonAsync<JsonElement>("/api/questionsets");

        Assert.Equal(2, sets.GetArrayLength());
        var titles = sets.EnumerateArray().Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Contains("General Knowledge", titles);
        Assert.Contains("Science Sprint", titles);
        Assert.All(sets.EnumerateArray(), e => Assert.Equal(10, e.GetProperty("questionCount").GetInt32()));
    }

    [Fact]
    public async Task QuestionSet_Detail_IncludesQuestions()
    {
        var sets = await _client.GetFromJsonAsync<JsonElement>("/api/questionsets");
        var id = sets.EnumerateArray().First().GetProperty("id").GetGuid();

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/questionsets/{id}");

        Assert.Equal(10, detail.GetProperty("questions").GetArrayLength());
        var first = detail.GetProperty("questions").EnumerateArray().First();
        Assert.Equal(4, first.GetProperty("options").GetArrayLength());
    }

    [Fact]
    public async Task QuestionSet_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/questionsets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rooms_EmptyInitially_UnknownCode404()
    {
        var rooms = await _client.GetFromJsonAsync<JsonElement>("/api/rooms");
        Assert.Empty(rooms.EnumerateArray());

        var response = await _client.GetAsync("/api/rooms/ABCDEF");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IndexPage_Served()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("QuizArena", html);
        Assert.Contains("signalr-mini.js", html);
    }
}
