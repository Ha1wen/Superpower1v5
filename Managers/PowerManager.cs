using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Models;
using Superpower1v5.Powers;
using Superpower1v5.Powers.Active;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Powers.Movement;
using Superpower1v5.Powers.Passive;

namespace Superpower1v5.Managers;

public sealed class PowerManager(Plugin plugin)
{
    private readonly SelectionEngine _selection = new(Random.Shared);
    private readonly PowerContext _context = new(plugin);
    private List<Power> _active = [];
    private uint? _appliedPawn;
    public RoundLoadout? Loadout { get; private set; }
    public IReadOnlyList<Power> Active => _active;
    public static Power Create(string id) => id switch
    {
        "super_speed" => new SuperSpeed(), "vampirism" => new Vampirism(), "invisibility" => new Invisibility(),
        "teleport" => new Teleport(), "explosive_bullets" => new ExplosiveBullets(), "infinite_ammo" => new InfiniteAmmo(),
        "rapid_fire" => new RapidFire(), "low_gravity" => new LowGravity(), "wall_vision" => new WallVision(),
        "knockback" => new Knockback(), "second_chance" => new SecondChance(),
        "bhop" => new Bhop(), "tiny" => new Tiny(), "sandevistan" => new Sandevistan(),
        "pyromaniac" => new Pyromaniac(), "random_explosion" => new RandomKill(),
        "juggernaut" => new Juggernaut(), "cluster_grenade" => new ClusterGrenade(),
        _ => throw new ArgumentException($"Unknown power: {id}")
    };
    public void Roll(bool newRound)
    {
        var pair = _selection.SelectPair(plugin.Config);
        CleanupInstances();
        if (newRound || Loadout is null) Loadout = new(pair); else Loadout.SetPair(pair);
        _active = pair.Select(p => Create(p.Id)).ToList();
        ApplyToPawn();
    }
#if SERVER_SMOKE
    public void NativeProbePair(string a,string b,Random? randomKillRandom=null)
    {
        CleanupInstances();
        Loadout=new([PowerCatalog.Get(a),PowerCatalog.Get(b)]);
        _active=Loadout.Powers.Select(p=>p.Id=="random_explosion" ? new RandomKill(randomKillRandom) : Create(p.Id)).ToList();
        ApplyToPawn();
    }
#endif
    public void ApplyToPawn()
    {
        if (!plugin.Mode.Live || _context.Pawn is not { } pawn || pawn.EntityHandle.Raw == _appliedPawn) return;
        _appliedPawn = pawn.EntityHandle.Raw;
        foreach (var power in _active) power.Apply(_context);
    }
    public void Tick()
    {
        if (!plugin.Mode.CanUsePowers) return;
        ApplyToPawn();
        foreach (var power in _active.ToArray()) power.Tick(_context);
    }
    public void SlowTick()
    {
        if (!plugin.Mode.CanUsePowers) return;
        foreach (var power in _active.ToArray()) power.SlowTick(_context);
    }
    public void Fire() { if (plugin.Mode.CanUsePowers) foreach (var p in _active.ToArray()) p.WeaponFire(_context); }
    public void Jump() { if (plugin.Mode.CanUsePowers) foreach (var p in _active.ToArray()) p.Jump(_context); }
    public void Impact(float x,float y,float z)
    { if (plugin.Mode.CanUsePowers) foreach (var p in _active.ToArray()) p.BulletImpact(_context,new(x,y,z)); }
    public void PostThink()
    { if (plugin.Mode.CanUsePowers) foreach (var p in _active.ToArray()) p.PostThink(_context); }
    public void Smoke(float x,float y,float z)
    { foreach (var p in _active.ToArray()) p.Smoke(_context,new(x,y,z)); }
    public void Projectile(CBaseCSGrenadeProjectile entity)
    { if (plugin.Mode.CanUsePowers) foreach (var power in _active.ToArray()) power.Projectile(_context,entity); }
    public bool BeforeDamage(DamageEvent hit)
    {
        foreach (var p in _active.ToArray()) p.ModifyDamage(_context,hit);
        // Test lethal protection after all modifiers, regardless of the random slot order.
        return _active.Any(p => p.PreventLethal(_context,hit));
    }
    public void AfterDamage(DamageDealt hit)
    { foreach (var p in _active.ToArray()) p.AfterDamage(_context,hit); }
    public void Activate(int slot)
    {
        if (!plugin.Mode.CanUsePowers || Loadout is not { } loadout || slot is < 0 or > 1) { _context.Tell("Abilities require a living boss in an active round."); return; }
        var p = _active[slot];
        if (p.Definition.Kind != PowerKind.Activated) { _context.Tell(p.Definition.Name+" works automatically."); return; }
        if (p.IsActive) { _context.Tell(p.Definition.Name+" is already active for the rest of this round."); return; }
        var remaining=loadout.Remaining(p.Id,_context.Now);
        if (remaining > 0) { _context.Tell($"{p.Definition.Name}: {remaining:0.0}s remaining."); return; }
        if (!p.Activate(_context)) return;
        // Immediate native damage can end the round and clear the manager's loadout.
        loadout.StartCooldown(p.Id,_context.Now,plugin.Config.Powers[p.Id].Cooldown);
        _context.Tell(p.Definition.Name+" activated!"); plugin.Feedback.Ability();
    }
    public void Give(string id,int? requestedSlot=null)
    {
        if (Loadout is null) throw new InvalidOperationException("No active loadout.");
        var definition=PowerCatalog.Get(id);
        if (!plugin.Config.IsAvailable(definition)) throw new InvalidOperationException("Power is disabled or blacklisted.");
        if (requestedSlot is < 0 or > 1) throw new ArgumentException("Slot must be 1 or 2.");
        if (Loadout.Has(id))
        {
            if (requestedSlot is { } target && Loadout.Powers[target].Id!=id)
            {
                Loadout.SetPair(Loadout.Powers.Reverse());
                (_active[0],_active[1])=(_active[1],_active[0]);
                _context.Tell($"{definition.Name} moved to slot {target+1}.");
            }
            return;
        }
        var slot=requestedSlot ?? (PowerCatalog.Compatible(Loadout.Powers[0],definition) ? 1 : 0);
        Replace(slot,definition);
    }
    private void Replace(int slot,PowerDefinition definition)
    {
        if (Loadout is null || !PowerCatalog.Compatible(Loadout.Powers[1-slot],definition)) throw new InvalidOperationException("Incompatible power pair.");
        _active[slot].Cleanup(_context);
        Loadout.Replace(slot,definition); var p=Create(definition.Id); _active[slot]=p;
        if (plugin.Mode.Live && _context.Pawn is not null) p.Apply(_context);
        _context.Tell($"Power {slot+1} is now {definition.Name}: {definition.Description}");
    }
    private void CleanupInstances()
    { foreach (var p in _active) p.Cleanup(_context); _active.Clear(); _appliedPawn=null; plugin.Effects.Clear(); }
    public void Clear() { CleanupInstances(); Loadout=null; }
    public string Status(string id)
    {
        var power=_active.FirstOrDefault(p=>p.Id==id);
        if (power?.IsActive==true) return "active until round ends";
        if (Loadout?.Consumed.Contains(id)==true) return "used this round";
        if (power?.Definition.Kind!=PowerKind.Activated) return "automatic";
        var remaining=Loadout?.Remaining(id,_context.Now) ?? 0;
        return remaining>0 ? $"{remaining:0.0}s" : "ready";
    }
}
