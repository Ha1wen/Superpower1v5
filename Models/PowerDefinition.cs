namespace Superpower1v5.Models;

public enum PowerKind { Passive, Activated, Triggered }

public sealed record PowerDefinition(string Id, string Name, string Description,
    PowerKind Kind, string[]? IncompatibleWith = null);

public static class PowerCatalog
{
    public static readonly IReadOnlyList<PowerDefinition> All =
    [
        new("super_speed", "Super Speed", "Move at 2.5 times normal speed.", PowerKind.Passive),
        new("blood_pact", "Blood Pact", "Heal for 100% of health damage dealt, even above maximum health. Your first enemy kill grants 10% more movement speed until you die or the round ends.", PowerKind.Triggered),
        new("invisibility", "Invisibility", "Sound makes you visible. Fade out after staying quiet; shift and crouch walking stay silent.", PowerKind.Passive),
        new("teleport", "Teleport", "Activate to blink to the furthest safe floor toward your aim, up to the map trace limit.", PowerKind.Activated),
        new("explosive_bullets", "Explosive Bullets", "Bullet impacts cause 80-damage blasts with a 250-unit radius and line-of-sight falloff.", PowerKind.Triggered),
        new("infinite_ammo", "Infinite Ammo", "Your firearm magazine refills and each grenade you throw is replaced.", PowerKind.Passive),
        new("rapid_fire", "Rapid Fire", "Fire faster without recoil. Normal weapon spread still applies.", PowerKind.Triggered),
        new("decoy_clone", "Decoy Clone", "Send a copy of yourself walking forward for 10 seconds. Shooting it sets off a flashbang.", PowerKind.Activated),
        new("wall_vision", "Wall Vision", "Automatically see opponent outlines through walls, plus radar and bearings, while you have this power.", PowerKind.Passive),
        new("knockback", "Knockback", "Bullet hits launch opponents away from you.", PowerKind.Triggered),
        new("second_chance", "Second Chance", "Once per round (per life in deathmatch), survive a lethal hit and rewind position and health by three seconds.", PowerKind.Triggered),
        new("bhop", "Bhop", "Hold jump to automatically bunny hop while preserving momentum.", PowerKind.Passive),
        new("sandevistan", "Sandevistan", "Activate to freeze every opponent's movement and attacks for 10 seconds.", PowerKind.Activated),
        new("pyromaniac", "Pyromaniac", "Bullet impacts ignite real Molotov fire that spreads and can be extinguished by smoke.", PowerKind.Triggered),
        new("random_explosion", "Random Kill", "Instantly kill a random living player, including yourself. Cooldown: 20 seconds.", PowerKind.Activated),
        new("tiny", "Tiny", "Become half-sized with smaller collision bounds.", PowerKind.Passive),
        new("juggernaut", "Juggernaut", "Start with 500 health while you have this power.", PowerKind.Passive),
        new("cluster_grenade", "Cluster Grenade", "Your original grenade keeps its trajectory and four extra copies fan out. The thrown grenade returns after 30 seconds.", PowerKind.Triggered),
        new("infinite_jump", "Infinite Jump", "Press jump again in midair to jump as many times as you like.", PowerKind.Passive),
        new("random_teleport", "Random Teleport", "Activate to teleport to a random safe place on the map. Cooldown: 20 seconds.", PowerKind.Activated),
        new("forcefield", "Forcefield", "Activate to block all damage for 10 seconds. Cooldown: 35 seconds.", PowerKind.Activated),
        new("unlimited_wallbang", "Unlimited Wallbang", "Fire through cover without losing damage to penetration.", PowerKind.Passive),
        new("thunder_god", "Thunder God", "Your Zeus fires straight along your crosshair across the map and recharges in 10 seconds.", PowerKind.Triggered),
        new("random_swap", "Random Swap", "Activate to trade positions with a random living player. Cooldown: 20 seconds.", PowerKind.Activated)
    ];

    public static PowerDefinition Get(string id) => All.First(p => p.Id == id);
    public static bool Compatible(PowerDefinition a, PowerDefinition b) =>
        a.Id != b.Id && !(a.IncompatibleWith?.Contains(b.Id) ?? false) &&
        !(b.IncompatibleWith?.Contains(a.Id) ?? false);
}
