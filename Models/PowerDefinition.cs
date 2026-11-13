namespace Superpower1v5.Models;

public enum PowerKind { Passive, Activated, Triggered }

public sealed record PowerDefinition(string Id, string Name, string Description,
    PowerKind Kind, string[]? IncompatibleWith = null);

public static class PowerCatalog
{
    public static readonly IReadOnlyList<PowerDefinition> All =
    [
        new("super_speed", "Super Speed", "Move at 2.5 times normal speed.", PowerKind.Passive),
        new("vampirism", "Vampirism", "Heal for 100% of health damage dealt, capped at 100 HP.", PowerKind.Triggered),
        new("invisibility", "Invisibility", "Sound makes you visible. Fade out after staying quiet; shift and crouch walking stay silent.", PowerKind.Passive),
        new("teleport", "Teleport", "Activate to blink to the furthest safe floor toward your aim, up to the map trace limit.", PowerKind.Activated),
        new("explosive_bullets", "Explosive Bullets", "Bullet impacts cause powerful 55-damage blasts with line-of-sight falloff.", PowerKind.Triggered),
        new("infinite_ammo", "Infinite Ammo", "Your firearm magazine automatically refills.", PowerKind.Passive),
        new("rapid_fire", "Rapid Fire", "Your firearm's server firing delay is shortened.", PowerKind.Triggered),
        new("low_gravity", "Low Gravity", "Fall more slowly with reduced personal gravity.", PowerKind.Passive),
        new("wall_vision", "Wall Vision", "Automatically see opponent outlines through walls, plus radar and bearings, while you have this power.", PowerKind.Passive),
        new("knockback", "Knockback", "Bullet hits launch opponents away from you.", PowerKind.Triggered),
        new("second_chance", "Second Chance", "Once per round, survive a lethal hit and rewind your position and health by three seconds.", PowerKind.Triggered),
        new("bhop", "Bhop", "Hold jump to automatically bunny hop while preserving momentum.", PowerKind.Passive),
        new("sandevistan", "Sandevistan", "Activate to freeze every opponent's movement and attacks for 10 seconds.", PowerKind.Activated),
        new("pyromaniac", "Pyromaniac", "Bullet impacts ignite temporary molotov-style fire pools.", PowerKind.Triggered),
        new("random_explosion", "Random Kill", "Instantly kill a random living player, including yourself. Cooldown: 20 seconds.", PowerKind.Activated),
        new("tiny", "Tiny", "Become half-sized, with a lower camera and smaller collision bounds.", PowerKind.Passive),
        new("juggernaut", "Juggernaut", "Start with 500 health while you have this power.", PowerKind.Passive),
        new("cluster_grenade", "Cluster Grenade", "Each thrown utility splits into four projectiles of the same type.", PowerKind.Triggered)
    ];

    public static PowerDefinition Get(string id) => All.First(p => p.Id == id);
    public static bool Compatible(PowerDefinition a, PowerDefinition b) =>
        a.Id != b.Id && !(a.IncompatibleWith?.Contains(b.Id) ?? false) &&
        !(b.IncompatibleWith?.Contains(a.Id) ?? false);
}
