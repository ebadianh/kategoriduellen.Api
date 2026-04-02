using System.Collections.Concurrent;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var games = new ConcurrentDictionary<Guid, GameState>();

// ---------------------------
// CREATE GAME
// ---------------------------
app.MapPost("/games", (string hostName, List<string> categories) =>
{
  var gameId = Guid.NewGuid();
  var hostId = Guid.NewGuid();

  var state = new GameState
  {
    GameId = gameId,
    Status = GameStatus.WaitingForPlayers,
    Categories = categories,
    Players = new List<Player>
        {
            new Player(hostId, hostName)
        }
  };

  games[gameId] = state;

  return Results.Created($"/games/{gameId}", new { gameId, playerId = hostId });
});

// ---------------------------
// JOIN GAME
// ---------------------------
app.MapPost("/games/{gameId:guid}/join", (Guid gameId, string playerName) =>
{
  if (!games.TryGetValue(gameId, out var state))
    return Results.NotFound();

  if (state.Status != GameStatus.WaitingForPlayers)
    return Results.BadRequest("Game already started");

  var playerId = Guid.NewGuid();
  state.Players.Add(new Player(playerId, playerName));

  return Results.Ok(new { gameId, playerId });
});

// ---------------------------
// START GAME (GENERATE LETTER)
// ---------------------------
app.MapPost("/games/{gameId:guid}/start", (Guid gameId) =>
{
  if (!games.TryGetValue(gameId, out var state))
    return Results.NotFound();

  if (state.Players.Count < 2)
    return Results.BadRequest("Need at least 2 players");

  state.Status = GameStatus.InRound;
  state.CurrentLetter = LetterGenerator.RandomLetter();
  state.Answers = new Dictionary<Guid, Dictionary<string, string>>();

  return Results.Ok(new { state.CurrentLetter });
});

// ---------------------------
// SUBMIT ANSWERS
// ---------------------------
app.MapPost("/games/{gameId:guid}/answers", (Guid gameId, SubmitAnswersRequest req) =>
{
  if (!games.TryGetValue(gameId, out var state))
    return Results.NotFound();

  if (state.Status != GameStatus.InRound)
    return Results.BadRequest("Round not active");

  state.Answers[req.PlayerId] = req.Answers;

  // Auto-finish if all players answered
  if (state.Answers.Count == state.Players.Count)
    state.Status = GameStatus.RoundFinished;

  return Results.Ok();
});

// ---------------------------
// FINISH ROUND (CALCULATE POINTS)
// ---------------------------
app.MapPost("/games/{gameId:guid}/finish-round", (Guid gameId) =>
{
  if (!games.TryGetValue(gameId, out var state))
    return Results.NotFound();

  if (state.Status != GameStatus.RoundFinished)
    return Results.BadRequest("Round not finished yet");

  var roundResult = Scoring.Calculate(state);
  state.Scoreboard = roundResult.Scoreboard;

  return Results.Ok(roundResult);
});

// ---------------------------
// GET GAME STATE
// ---------------------------
app.MapGet("/games/{gameId:guid}", (Guid gameId) =>
{
  if (!games.TryGetValue(gameId, out var state))
    return Results.NotFound();

  return Results.Ok(state);
});

app.Run();


// ---------------------------
// MODELS
// ---------------------------
public enum GameStatus { WaitingForPlayers, InRound, RoundFinished }

public record Player(Guid PlayerId, string Name);

public record SubmitAnswersRequest(Guid PlayerId, Dictionary<string, string> Answers);

public class GameState
{
  public Guid GameId { get; set; }
  public GameStatus Status { get; set; }
  public List<Player> Players { get; set; } = new();
  public List<string> Categories { get; set; } = new();
  public string CurrentLetter { get; set; } = "";
  public Dictionary<Guid, Dictionary<string, string>> Answers { get; set; } = new();
  public Dictionary<Guid, int> Scoreboard { get; set; } = new();
}

// ---------------------------
// LETTER GENERATOR
// ---------------------------
public static class LetterGenerator
{
  private static readonly Random _rnd = new();
  private static readonly string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

  public static string RandomLetter() =>
      Letters[_rnd.Next(Letters.Length)].ToString();
}

// ---------------------------
// SCORING LOGIC
// ---------------------------
public static class Scoring
{
  public static RoundResult Calculate(GameState state)
  {
    var scores = state.Scoreboard ?? new Dictionary<Guid, int>();

    foreach (var category in state.Categories)
    {
      var answers = state.Answers
          .ToDictionary(x => x.Key, x => x.Value.GetValueOrDefault(category, ""));

      var grouped = answers
          .GroupBy(x => x.Value.Trim().ToLower())
          .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToList());

      foreach (var group in grouped)
      {
        var word = group.Key;
        var players = group.Value;

        if (string.IsNullOrWhiteSpace(word))
        {
          // Tomt svar → 0 poäng
          continue;
        }

        if (players.Count == 1)
        {
          // Unikt ord → 20 poäng
          scores[players[0]] = scores.GetValueOrDefault(players[0]) + 20;
        }
        else if (players.Count > 1)
        {
          // Samma ord → 5 poäng var
          foreach (var p in players)
            scores[p] = scores.GetValueOrDefault(p) + 5;
        }
      }
    }

    return new RoundResult(scores);
  }
}

public record RoundResult(Dictionary<Guid, int> Scoreboard);
