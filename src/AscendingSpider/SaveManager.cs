using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using AscendingSpider.Domain;

namespace AscendingSpider;

public class SaveSettings
{
    public string Language { get; set; } = "EN";
    public float Volume { get; set; } = 0.5f;
    public int Theme { get; set; } = 0;
    public int CardBack { get; set; } = 0;
    
    public int Wins { get; set; } = 0;
    public double BestTimeEasy { get; set; } = 0;
    public double BestTimeMedium { get; set; } = 0;
    public double BestTimeHard { get; set; } = 0;
    
    public double CurrentGameTime { get; set; } = 0;
    public bool HasSeenTutorial { get; set; } = false;
    
    public bool AchFirstWin { get; set; } = false;
    public bool AchNoUndo { get; set; } = false;
    public bool AchNoHint { get; set; } = false;
    public bool AchSpeedrun { get; set; } = false;
    public bool AchHardMode { get; set; } = false;
    public bool AchDedicated { get; set; } = false;

    public List<double> LeaderboardStandard { get; set; } = new();
    public List<double> LeaderboardBlitz { get; set; } = new();
}

public static class SaveManager
{
    private static readonly string SaveDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "AscendingSpider");
    
    private static readonly string ProgressPath = Path.Combine(SaveDir, "save.json");
    private static readonly string SettingsPath = Path.Combine(SaveDir, "settings.json");

    public static SaveSettings Settings { get; private set; } = new();

    public static void LoadSettings()
    {
        if (File.Exists(SettingsPath))
        {
            try
            {
                string json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<SaveSettings>(json) ?? new SaveSettings();
            }
            catch { Settings = new SaveSettings(); }
        }
    }

    public static void SaveSettings()
    {
        if (!Directory.Exists(SaveDir)) Directory.CreateDirectory(SaveDir);
        string json = JsonSerializer.Serialize(Settings);
        File.WriteAllText(SettingsPath, json);
    }

    public static bool HasSave() => File.Exists(ProgressPath);

    public static void Save(
        GameState state,
        int faceDownPerColumn,
        double? currentGameTime = null,
        bool usedUndoThisGame = false,
        bool usedHintThisGame = false)
    {
        if (!Directory.Exists(SaveDir)) Directory.CreateDirectory(SaveDir);

        var data = new SaveData
        {
            SaveVersion = SaveData.CurrentVersion,
            UsedUndoThisGame = usedUndoThisGame,
            UsedHintThisGame = usedHintThisGame,
            SelectedSuit = (int)state.SelectedSuit,
            Mode = (int)state.Mode,
            EndlessLoops = state.EndlessLoops,
            FaceDownPerColumn = faceDownPerColumn,
            CompletedSequences = state.CompletedSequences,
            TotalCompletedSequences = state.TotalCompletedSequences,
            NextCardId = state.NextCardId,
            CurrentGameTime = currentGameTime,
            Stock = state.Stock.Select(c => new CardDto { Rank = (int)c.Rank, Id = c.Id }).ToList(),
            Tableau = state.Tableau.Select(p => 
            {
                var list = new List<CardDto>();
                for (int i = 0; i < p.Count; i++)
                {
                    var c = p.Cards[i];
                    list.Add(new CardDto { Rank = (int)c.Rank, Id = c.Id, IsFaceDown = i < p.FaceDownCount });
                }
                return list;
            }).ToList()
        };

        string json = JsonSerializer.Serialize(data);
        File.WriteAllText(ProgressPath, json);
    }

    public static (GameState State, int FaceDownPerColumn,
        double? CurrentGameTime, bool UsedUndoThisGame, bool UsedHintThisGame)? Load()
    {
        if (!HasSave()) return null;

        try
        {
            string json = File.ReadAllText(ProgressPath);
            var data = JsonSerializer.Deserialize<SaveData>(json);
            if (data == null) return null;

            Suit selectedSuit = Enum.IsDefined(typeof(Suit), data.SelectedSuit ?? (int)Suit.Spades)
                ? (Suit)(data.SelectedSuit ?? (int)Suit.Spades)
                : Suit.Spades;
            GameMode mode = Enum.IsDefined(typeof(GameMode), data.Mode)
                ? (GameMode)data.Mode
                : GameMode.Standard;
            var state = new GameState(mode, selectedSuit);
            state.CompletedSequences = Math.Max(0, data.CompletedSequences);
            // The current game uses one selected suit.
            state.EndlessLoops = data.EndlessLoops;
            // Current one-suit saves use 2/3/5 hidden cards. Preserve these
            // values for current and unversioned one-suit saves; older layouts
            // retain their difficulty migration.
            state.FaceDownPerColumn = data.SaveVersion >= 1
                || (data.SuitCount <= 1 && (data.FaceDownPerColumn is 2 or 3 or 5))
                ? (data.FaceDownPerColumn is 2 or 3 or 5 ? data.FaceDownPerColumn : 3)
                : data.FaceDownPerColumn switch
                {
                    2 => 2, // previous two-suit Easy
                    3 => 2, // previous one-suit Easy
                    4 => 5, // previous two-suit Hard
                    5 => 3, // previous one-suit Medium
                    7 => 5, // previous one-suit Hard
                    _ => 3
                };

            // Normalize all cards from legacy multi-suit saves to the selected
            // suit so old checkpoints obey the current single-suit rules.
            foreach (var c in data.Stock ?? new List<CardDto>())
                state.Stock.Add(new Card(selectedSuit, (Rank)c.Rank, c.Id));

            for (int i = 0; i < Math.Min(10, data.Tableau?.Count ?? 0); i++)
            {
                foreach (var c in data.Tableau[i] ?? new List<CardDto>())
                {
                    var card = new Card(selectedSuit, (Rank)c.Rank, c.Id);
                    if (c.IsFaceDown) state.Tableau[i].AddFaceDown(card);
                    else state.Tableau[i].AddFaceUp(card);
                }
            }

            // Old save files do not contain these fields. Reconstruct safe defaults
            // from the counters and currently active card identifiers.
            state.TotalCompletedSequences = data.TotalCompletedSequences > 0
                ? data.TotalCompletedSequences
                : data.EndlessLoops * 8 + data.CompletedSequences;
            int maxActiveCardId = state.Stock.Select(c => c.Id)
                .Concat(state.Tableau.SelectMany(p => p.Cards).Select(c => c.Id))
                .DefaultIfEmpty(0)
                .Max();
            state.NextCardId = data.NextCardId > maxActiveCardId
                ? data.NextCardId
                : maxActiveCardId + 1;

            // Older saves cannot prove that no undo/hint was used. Disqualify
            // those legacy sessions rather than granting achievements unfairly.
            bool usedUndoThisGame = data.UsedUndoThisGame ?? true;
            bool usedHintThisGame = data.UsedHintThisGame ?? true;
            return (state, state.FaceDownPerColumn,
                data.CurrentGameTime, usedUndoThisGame, usedHintThisGame);
        }
        catch { return null; }
    }

    public static void DeleteSave()
    {
        if (File.Exists(ProgressPath)) File.Delete(ProgressPath);
    }

    public static void AddScore(GameMode mode, double timeTaken)
    {
        if (mode == GameMode.Standard)
        {
            Settings.LeaderboardStandard.Add(timeTaken);
            Settings.LeaderboardStandard.Sort();
            if (Settings.LeaderboardStandard.Count > 10) Settings.LeaderboardStandard.RemoveRange(10, Settings.LeaderboardStandard.Count - 10);
        }
        else if (mode == GameMode.Blitz)
        {
            Settings.LeaderboardBlitz.Add(timeTaken);
            Settings.LeaderboardBlitz.Sort();
            if (Settings.LeaderboardBlitz.Count > 10) Settings.LeaderboardBlitz.RemoveRange(10, Settings.LeaderboardBlitz.Count - 10);
        }
    }
}

public class SaveData
{
    public const int CurrentVersion = 2;

    public int SaveVersion { get; set; } = 0;
    // Read-only compatibility field for difficulty migration from old saves.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SuitCount { get; set; }
    public bool? UsedUndoThisGame { get; set; }
    public bool? UsedHintThisGame { get; set; }
    public int? SelectedSuit { get; set; }
    public int Mode { get; set; }
    public int EndlessLoops { get; set; }
    public int FaceDownPerColumn { get; set; }
    public int CompletedSequences { get; set; }
    public int TotalCompletedSequences { get; set; }
    public int NextCardId { get; set; }
    // Nullable for backward compatibility with saves created before Blitz timer persistence.
    public double? CurrentGameTime { get; set; }
    public List<CardDto> Stock { get; set; } = new();
    public List<List<CardDto>> Tableau { get; set; } = new();
}

public class CardDto
{
    public int Rank { get; set; }
    public int Id { get; set; }
    public bool IsFaceDown { get; set; }
}