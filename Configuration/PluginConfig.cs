using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;
using Superpower1v5.Models;

namespace Superpower1v5.Configuration;

public sealed class PowerSettings
{
    public bool Enabled { get; set; } = true;
    public float Cooldown { get; set; }
    public float Duration { get; set; }
    public float Strength { get; set; }
    public float Chance { get; set; }
    public float Interval { get; set; } = 1;
    public float Radius { get; set; } = 140;
    public float Distance { get; set; } = 450;
    public int MaxEntities { get; set; } = 8;

    public static PowerSettings Defaults(string id) => id switch
    {
        "super_speed" => new() { Strength = 2.5f },
        "vampirism" => new() { Strength = 1f },
        "invisibility" => new() { Strength = 25 },
        "teleport" => new() { Cooldown = 15, Distance = 0 },
        "explosive_bullets" => new() { Strength = 55, Radius = 180, Interval = .15f },
        "infinite_ammo" => new(),
        "rapid_fire" => new() { Strength = 1.6f },
        "low_gravity" => new() { Strength = .35f },
        "wall_vision" => new(),
        "knockback" => new() { Strength = 800 },
        "second_chance" => new() { Strength = 100 },
        "bhop" => new() { Strength = 301.99f },
        "sandevistan" => new() { Duration = 10, Cooldown = 35 },
        "pyromaniac" => new() { Strength = 20, Duration = 7, Radius = 120, Interval = .5f, MaxEntities = 8 },
        "random_explosion" => new() { Strength = 500, Cooldown = 20 },
        "tiny" => new() { Strength = .5f },
        _ => new()
    };

    public void Validate(string id)
    {
        var d = Defaults(id);
        Cooldown = Safe(Cooldown, 0, 600, d.Cooldown);
        Duration = Safe(Duration, .1f, 60, Math.Max(.1f, d.Duration));
        if (id is "invisibility" or "wall_vision") { Duration = 0; Cooldown = 0; }
        if (id == "random_explosion") Duration = 0;
        Interval = Safe(Interval, .05f, 300, d.Interval);
        Radius = Safe(Radius, 32, 500, d.Radius);
        Distance = id == "teleport" && Distance == 0 ? 0 : Safe(Distance, 64, 32768, d.Distance);
        Chance = Safe(Chance, 0, 1, d.Chance);
        MaxEntities = Math.Clamp(MaxEntities,1,16);
        var range = id switch
        {
            "super_speed" => (1f, 5f),
            "rapid_fire" => (1f, 5f),
            "vampirism" => (0f, 1f),
            "invisibility" => (1f, 100f),
            "low_gravity" => (.15f, 1f),
            "explosive_bullets" => (1f, 250f),
            "knockback" => (50f, 2000f),
            "second_chance" => (1f, 100f),
            "bhop" => (200f, 500f),
            "pyromaniac" => (1f,100f),
            "random_explosion" => (100f,1000f),
            "tiny" => (.3f,.8f),
            _ => (0f, 1000f)
        };
        Strength = Safe(Strength, range.Item1, range.Item2, d.Strength);
    }

    private static float Safe(float value, float min, float max, float fallback) =>
        float.IsFinite(value) && value >= min && value <= max ? value : fallback;
}

public sealed class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("ConfigVersion")]
    public override int Version { get; set; } = 5;
    public bool Enabled { get; set; } = true;
    public string GameMode { get; set; } = "1v5";
    public int NormalHealth { get; set; } = 100;
    public int SuperpoweredPlayerCount { get; set; } = 1;
    public int PowersPerRound { get; set; } = 2;
    public int MaxOpponents { get; set; } = 5;
    public bool RotateSuperpoweredPlayer { get; set; } = true;
    public string SuperpoweredPlayerTeam { get; set; } = "T";
    public bool AllowBots { get; set; } = true;
    public bool AllowDuplicatePowers { get; set; }
    public List<string> PowerBlacklist { get; set; } = [];
    public bool EnableHUD { get; set; } = true;
    public bool EnableSounds { get; set; } = true;
    public bool DebugLogging { get; set; }
    public bool EnableDuringWarmup { get; set; }
    public Dictionary<string, PowerSettings> Powers { get; set; } =
        PowerCatalog.All.ToDictionary(p => p.Id, p => PowerSettings.Defaults(p.Id));

    public void Validate()
    {
        // These are mode invariants, not tunable advantages for the boss.
        NormalHealth = 100;
        SuperpoweredPlayerCount = 1;
        PowersPerRound = 2;
        AllowDuplicatePowers = false;
        GameMode = "1v5";
        MaxOpponents = Math.Clamp(MaxOpponents, 1, 5);
        SuperpoweredPlayerTeam = string.Equals(SuperpoweredPlayerTeam, "CT", StringComparison.OrdinalIgnoreCase) ? "CT" : "T";
        PowerBlacklist = (PowerBlacklist ?? []).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
        Powers ??= new();
        foreach (var retired in Powers.Keys.Where(id => !PowerCatalog.All.Any(p => p.Id == id)).ToArray()) Powers.Remove(retired);
        foreach (var power in PowerCatalog.All)
        {
            if (!Powers.TryGetValue(power.Id, out var setting) || setting is null)
                Powers[power.Id] = setting = PowerSettings.Defaults(power.Id);
            setting.Validate(power.Id);
        }
        if (Version < 2)
        {
            // Apply the requested balance revision while preserving cooldowns and unrelated settings.
            foreach (var id in new[] { "super_speed", "vampirism", "explosive_bullets", "low_gravity", "knockback" })
                Powers[id].Strength = PowerSettings.Defaults(id).Strength;
            Powers["teleport"].Distance = 0;
            Powers["explosive_bullets"].Radius = PowerSettings.Defaults("explosive_bullets").Radius;
            Version = 2;
        }
        if (Version < 5)
        {
            Powers["random_explosion"].Cooldown=20;
            Version=5;
        }
    }

    public bool IsAvailable(PowerDefinition power) => Powers[power.Id].Enabled && !PowerBlacklist.Contains(power.Id);
}
