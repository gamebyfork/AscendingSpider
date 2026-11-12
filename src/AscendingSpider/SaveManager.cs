using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    public int Losses { get; set; } = 0;
    public double BestTimeEasy { get; set; } = 0;
    public double BestTimeMedium { get; set; } = 0;
    public double BestTimeHard { get; set; } = 0;
    
    public double CurrentGameTime { get; set; } = 0;
    public bool HasSeenTutorial { get; set; } = false;
    
    public bool AchFirstWin { get; set; } = false;
    public bool AchNoUndo { get; set; } = false;
    public bool AchNoHint { get; set; } = false;
    public bool AchSpeedrun { get; set; } = false;
    public bool Ach4Suits { get; set; } = false;
    public bool AchHardMode { get; set; } = false;
    public bool AchDedicated { get; set; } = false;
    public bool AchPerfectOrder { get; set; } = false;

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
        bool mixedSuitPlacementUsed = false,
        double? currentGameTime = null)
    {
        if (!Directory.Exists(SaveDir)) Directory.CreateDirectory(SaveDir);

        var data = new SaveData
        {
            SuitCount = state.SuitCount,
            Mode = (int)state.Mode,
            EndlessLoops = state.EndlessLoops,
            DealsMade = state.DealsMade,
            FaceDownPerColumn = faceDownPerColumn,
            CompletedSequences = state.CompletedSequences,
            TotalCompletedSequences = state.TotalCompletedSequences,
            CompletedSequenceSuits = state.CompletedSequenceSuits.Select(suit => (int)suit).ToList(),
            NextCardId = state.NextCardId,
            MixedSuitPlacementUsed = mixedSuitPlacementUsed,
            CurrentGameTime = currentGameTime,
            Stock = state.Stock.Select(c => new CardDto { Suit = (int)c.Suit, Rank = (int)c.Rank, Id = c.Id }).ToList(),
            Tableau = state.Tableau.Select(p => 
            {
                var list = new List<CardDto>();
                for (int i = 0; i < p.Count; i++)
                {
                    var c = p.Cards[i];
                    list.Add(new CardDto { Suit = (int)c.Suit, Rank = (int)c.Rank, Id = c.Id, IsFaceDown = i < p.FaceDownCount });
                }
                return list;
            }).ToList()
        };

        string json = JsonSerializer.Serialize(data);
        File.WriteAllText(ProgressPath, json);
    }

    public static (GameState State, int FaceDownPerColumn, bool MixedSuitPlacementUsed, double? CurrentGameTime)? Load()
    {
        if (!HasSave()) return null;

        try
        {
            string json = File.ReadAllText(ProgressPath);
            var data = JsonSerializer.Deserialize<SaveData>(json);
            if (data == null) return null;

            var state = new GameState(data.SuitCount, (GameMode)data.Mode);
            state.CompletedSequences = data.CompletedSequences;
            // Restore the actual suits of completed piles. Older save files did not
            // store this information, so retain the former Spades placeholder for
            // those already-completed piles only.
            foreach (int suitValue in (data.CompletedSequenceSuits ?? new List<int>()).Take(Math.Max(0, data.CompletedSequences)))
            {
                if (Enum.IsDefined(typeof(Suit), suitValue))
                    state.CompletedSequenceSuits.Add((Suit)suitValue);
                else
                    state.CompletedSequenceSuits.Add(Suit.Spades);
            }
            while (state.CompletedSequenceSuits.Count < Math.Max(0, data.CompletedSequences))
                state.CompletedSequenceSuits.Add(Suit.Spades);
            state.EndlessLoops = data.EndlessLoops;
            state.DealsMade = data.DealsMade;
            state.FaceDownPerColumn = data.FaceDownPerColumn > 0 ? data.FaceDownPerColumn : 5;

            foreach (var c in data.Stock ?? new List<CardDto>())
                state.Stock.Add(new Card((Suit)c.Suit, (Rank)c.Rank, c.Id));

            for (int i = 0; i < Math.Min(10, data.Tableau?.Count ?? 0); i++)
            {
                foreach (var c in data.Tableau[i] ?? new List<CardDto>())
                {
                    var card = new Card((Suit)c.Suit, (Rank)c.Rank, c.Id);
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

            // Если это сохранение создано до появления трекера, мы не можем
            // восстановить историю перемещений. Консервативно запрещаем достижение
            // в этой старой партии, чтобы его нельзя было получить нечестно.
            bool mixedSuitPlacementUsed = data.MixedSuitPlacementUsed ?? true;
            return (state, state.FaceDownPerColumn, mixedSuitPlacementUsed, data.CurrentGameTime);
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
    public int SuitCount { get; set; }
    public int Mode { get; set; }
    public int EndlessLoops { get; set; }
    public int DealsMade { get; set; }
    public int FaceDownPerColumn { get; set; }
    public int CompletedSequences { get; set; }
    public int TotalCompletedSequences { get; set; }
    public List<int>? CompletedSequenceSuits { get; set; }
    public int NextCardId { get; set; }
    // Nullable for backward compatibility: null means an older save without history.
    public bool? MixedSuitPlacementUsed { get; set; }
    // Nullable for backward compatibility with saves created before Blitz timer persistence.
    public double? CurrentGameTime { get; set; }
    public List<CardDto> Stock { get; set; } = new();
    public List<List<CardDto>> Tableau { get; set; } = new();
}

public class CardDto
{
    public int Suit { get; set; }
    public int Rank { get; set; }
    public int Id { get; set; }
    public bool IsFaceDown { get; set; }
}