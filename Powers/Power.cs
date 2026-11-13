using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Configuration;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5.Powers;

public sealed record DamageEvent(CCSPlayerController Victim, CCSPlayerController? Attacker,
    CTakeDamageInfo? Info, bool BossVictim, bool BossAttacker, bool Synthetic, DamageTypes_t? DamageType = null)
{
    public bool Bullet => ((DamageType ?? Info?.BitsDamageType ?? DamageTypes_t.DMG_GENERIC) & (DamageTypes_t.DMG_BULLET | DamageTypes_t.DMG_BUCKSHOT)) != 0;
}
public sealed record DamageDealt(DamageEvent Hit, int HealthLost);
public sealed class PowerContext(Plugin plugin)
{
    public Plugin Plugin { get; } = plugin;
    public CCSPlayerController? Boss => Plugin.Mode.Boss;
    public CCSPlayerPawn? Pawn => Players.AlivePawn(Boss);
    public double Now => Server.CurrentTime;
    public RoundLoadout Loadout => Plugin.Powers.Loadout!;
    public IEnumerable<CCSPlayerController> Opponents => Plugin.Mode.Opponents;
    public PowerSettings Settings(string id) => Plugin.Config.Powers[id];
    public void Tell(string text) { if (Players.Connected(Boss)) Boss!.PrintToChat(Plugin.Prefix + text); }
}
public abstract class Power(string id)
{
    public string Id { get; } = id;
    public PowerDefinition Definition => PowerCatalog.Get(Id);
    public virtual bool IsActive => false;
    protected PowerSettings Settings(PowerContext c) => c.Settings(Id);
    public virtual void Apply(PowerContext c) { }
    public virtual void Tick(PowerContext c) { }
    public virtual void SlowTick(PowerContext c) { }
    public virtual void PostThink(PowerContext c) { }
    public virtual void BulletImpact(PowerContext c, CounterStrikeSharp.API.Modules.Utils.Vector impact) { }
    public virtual void Smoke(PowerContext c, CounterStrikeSharp.API.Modules.Utils.Vector origin) { }
    public virtual void Projectile(PowerContext c,CBaseCSGrenadeProjectile projectile) { }
    public virtual void WeaponFire(PowerContext c) { }
    public virtual void Jump(PowerContext c) { }
    public virtual void ModifyDamage(PowerContext c, DamageEvent hit) { }
    public virtual void AfterDamage(PowerContext c, DamageDealt hit) { }
    public virtual bool PreventLethal(PowerContext c, DamageEvent hit) => false;
    public virtual bool Activate(PowerContext c) => false;
    public virtual void Cleanup(PowerContext c) => c.Plugin.Effects.RemoveSource(Id);
}
