using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Models;
using Superpower1v5.Powers;
using Superpower1v5.Powers.Active;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Powers.Movement;
using Superpower1v5.Powers.Passive;
using Superpower1v5.Support;

namespace Superpower1v5.Managers;

public sealed record PowerReveal(string PlayerName,IReadOnlyList<PowerDefinition> Powers);
public sealed class PowerManager(Plugin plugin)
{
    private sealed class Owner(Plugin plugin,CCSPlayerController player)
    {
        public uint Raw { get; }=player.EntityHandle.Raw;
        public string Name { get; }=player.PlayerName;
        public PowerContext Context { get; }=new(plugin,player.EntityHandle.Raw);
        public EffectsManager Effects { get; }=new();
        public RoundLoadout? Loadout;
        public List<Power> Active=[];
        public uint? AppliedPawn;
        public bool Enabled=true;
    }
    private readonly SelectionEngine _selection=new(Random.Shared);
    private readonly Dictionary<uint,Owner> _owners=new();
    private readonly EffectsManager _emptyEffects=new();
    private EffectsManager[] _transmission=[];
    private Owner? Find(CCSPlayerController? player) => player is { IsValid:true } && _owners.TryGetValue(player.EntityHandle.Raw,out var owner) ? owner : null;
    private Owner Get(CCSPlayerController player)
    {
        if (_owners.TryGetValue(player.EntityHandle.Raw,out var owner)) return owner;
        _owners[player.EntityHandle.Raw]=owner=new(plugin,player);
        Volatile.Write(ref _transmission,_owners.Values.Select(o=>o.Effects).ToArray());
        return owner;
    }
    public RoundLoadout? Loadout => GetLoadout(plugin.Mode.Boss);
    public IReadOnlyList<Power> Active => GetActive(plugin.Mode.Boss);
    public RoundLoadout? GetLoadout(CCSPlayerController? player) => Find(player) is { Enabled:true } owner ? owner.Loadout : null;
    public IReadOnlyList<Power> GetActive(CCSPlayerController? player) => Find(player)?.Active ?? (IReadOnlyList<Power>)Array.Empty<Power>();
    public EffectsManager GetEffects(CCSPlayerController? player) => Find(player)?.Effects ?? _emptyEffects;
    public bool HasPowers(CCSPlayerController? player) => GetLoadout(player) is not null;
    public IEnumerable<CCSPlayerController> PoweredPlayers => _owners.Values.Where(o=>o.Enabled).Select(o=>Players.Resolve(o.Raw)).OfType<CCSPlayerController>().Where(Players.Playing);
    // Retain final pairs after deaths and toggles until the round-end reveal.
    public IReadOnlyList<PowerReveal> Reveals => _owners.Values.Where(o=>o.Loadout is not null).Select(o=>new PowerReveal(o.Name,o.Loadout!.Powers.ToArray())).ToArray();
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
    public void Roll(bool newRound,CCSPlayerController? player=null)
    {
        player ??=plugin.Mode.Boss;
        if (player is null) throw new InvalidOperationException("No power recipient.");
        var owner=Get(player); var pair=_selection.SelectPair(plugin.Config);
        Cleanup(owner); owner.Enabled=true;
        if (newRound || owner.Loadout is null) owner.Loadout=new(pair); else owner.Loadout.SetPair(pair);
        owner.Active=pair.Select(p=>Create(p.Id)).ToList(); Apply(owner);
        if (newRound && plugin.Mode.IsBoss(player) && plugin.Config.EveryoneHasPowers)
            foreach (var other in plugin.Mode.Eligible().Where(p=>!plugin.Mode.IsBoss(p))) Roll(true,other);
    }
#if SERVER_SMOKE
    public void NativeProbePair(string a,string b,Random? randomKillRandom=null,CCSPlayerController? player=null)
    {
        player ??=plugin.Mode.Boss;
        var owner=Get(player!); Cleanup(owner); owner.Enabled=true;
        owner.Loadout=new([PowerCatalog.Get(a),PowerCatalog.Get(b)]);
        owner.Active=owner.Loadout.Powers.Select(p=>p.Id=="random_explosion" ? new RandomKill(randomKillRandom) : Create(p.Id)).ToList(); Apply(owner);
    }
#endif
    public void ReconcileEveryone()
    {
        if (!plugin.Mode.Live) return;
        foreach (var owner in _owners.Values.ToArray())
            if (owner.Enabled && !plugin.Mode.IsBoss(Players.Resolve(owner.Raw)) && !plugin.Config.EveryoneHasPowers) { Cleanup(owner); owner.Enabled=false; }
        if (plugin.Config.EveryoneHasPowers)
            foreach (var player in plugin.Mode.Eligible().Where(p=>!HasPowers(p))) { Roll(true,player); plugin.Feedback.Loadout(player); }
    }
    private void Apply(Owner owner)
    {
        if (!plugin.Mode.Live || !owner.Enabled || !Players.Playing(owner.Context.Boss) || owner.Context.Pawn is not { } pawn || pawn.EntityHandle.Raw==owner.AppliedPawn) return;
        if (owner.Active.Count==0 && owner.Loadout is { } loadout) owner.Active=loadout.Powers.Select(p=>Create(p.Id)).ToList();
        owner.AppliedPawn=pawn.EntityHandle.Raw;
        foreach (var power in owner.Active.ToArray()) power.Apply(owner.Context);
    }
    public void ApplyToPawn(CCSPlayerController? player=null)
    {
        if (player is not null) { if (Find(player) is { } owner) Apply(owner); }
        else foreach (var owner in _owners.Values.ToArray()) Apply(owner);
    }
    private void Dispatch(Action<Power,PowerContext> action,CCSPlayerController? player=null,bool all=false)
    {
        var owners=all ? _owners.Values.ToArray() : Find(player ?? plugin.Mode.Boss) is { } owner ? new[] { owner } : [];
        foreach (var state in owners)
        {
            if (!state.Enabled || !plugin.Mode.CanUsePlayerPowers(state.Context.Boss)) continue;
            Apply(state);
            foreach (var power in state.Active.ToArray()) action(power,state.Context);
        }
    }
    public void Tick() => Dispatch((p,c)=>p.Tick(c),all:true);
    public void SlowTick() => Dispatch((p,c)=>p.SlowTick(c),all:true);
    public void Fire(CCSPlayerController? player=null) { Noise(player); Dispatch((p,c)=>p.WeaponFire(c),player); }
    public void Jump(CCSPlayerController? player=null) { Noise(player); Dispatch((p,c)=>p.Jump(c),player); }
    public void Noise(CCSPlayerController? player=null) => Dispatch((p,c)=>p.Noise(c),player);
    public void Footstep(CCSPlayerController? player)
    { if (Players.AlivePawn(player) is { } pawn && !Invisibility.QuietMovement(player!,pawn)) Noise(player); }
    public void Impact(float x,float y,float z,CCSPlayerController? player=null) => Dispatch((p,c)=>p.BulletImpact(c,new(x,y,z)),player);
    public void PostThink() => Dispatch((p,c)=>p.PostThink(c),all:true);
    public void Smoke(float x,float y,float z) => Dispatch((p,c)=>p.Smoke(c,new(x,y,z)),all:true);
    public void Projectile(CBaseCSGrenadeProjectile entity)
    {
        var thrower=Players.Controller(entity.Thrower.Value);
        if (thrower is not null) Dispatch((p,c)=>p.Projectile(c,entity),thrower);
    }
    private static DamageEvent ForOwner(DamageEvent hit,Owner owner) => hit with {
        BossVictim=hit.Victim.EntityHandle.Raw==owner.Raw,
        BossAttacker=hit.Attacker?.EntityHandle.Raw==owner.Raw && hit.Attacker.TeamNum!=hit.Victim.TeamNum
    };
    public bool BeforeDamage(DamageEvent hit)
    {
        var relevant=_owners.Values.Where(o=>o.Enabled && (o.Raw==hit.Victim.EntityHandle.Raw || o.Raw==hit.Attacker?.EntityHandle.Raw)).ToArray();
        foreach (var owner in relevant) foreach (var p in owner.Active.ToArray()) p.ModifyDamage(owner.Context,ForOwner(hit,owner));
        foreach (var owner in relevant) foreach (var p in owner.Active.ToArray()) if (p.PreventLethal(owner.Context,ForOwner(hit,owner))) return true;
        return false;
    }
    public void AfterDamage(DamageDealt hit)
    {
        Noise(hit.Hit.Victim);
        foreach (var owner in _owners.Values.Where(o=>o.Enabled && (o.Raw==hit.Hit.Victim.EntityHandle.Raw || o.Raw==hit.Hit.Attacker?.EntityHandle.Raw)).ToArray())
            foreach (var p in owner.Active.ToArray()) p.AfterDamage(owner.Context,new(ForOwner(hit.Hit,owner),hit.HealthLost));
    }
    public void Activate(int slot,CCSPlayerController? player=null)
    {
        player ??=plugin.Mode.Boss;
        if (Find(player) is not { Enabled:true,Loadout:{ } loadout } owner) return;
        var c=owner.Context;
        if (!plugin.Mode.CanUsePlayerPowers(player) || slot is <0 or >1) { c.Tell("Abilities require a living player in an active round."); return; }
        var p=owner.Active[slot];
        if (p.Definition.Kind!=PowerKind.Activated) { c.Tell(p.Definition.Name+" works automatically."); return; }
        if (p.IsActive) { c.Tell(p.Definition.Name+" is already active."); return; }
        var remaining=loadout.Remaining(p.Id,c.Now);
        if (remaining>0) { c.Tell($"{p.Definition.Name}: {remaining:0.0}s remaining."); return; }
        if (!p.Activate(c)) return;
        loadout.StartCooldown(p.Id,c.Now,plugin.Config.Powers[p.Id].Cooldown);
        c.Tell(p.Definition.Name+" activated!"); plugin.Feedback.Ability(player);
    }
    public void Give(string id,int? requestedSlot=null,CCSPlayerController? player=null)
    {
        player ??=plugin.Mode.Boss;
        if (Find(player) is not { Enabled:true,Loadout:{ } loadout } owner) throw new InvalidOperationException("That player has no powers. Enable css_sp_everyone first for CT players.");
        var definition=PowerCatalog.Get(id);
        if (!plugin.Config.IsAvailable(definition)) throw new InvalidOperationException("Power is disabled or blacklisted.");
        if (requestedSlot is <0 or >1) throw new ArgumentException("Slot must be 1 or 2.");
        if (loadout.Has(id))
        {
            if (requestedSlot is { } target && loadout.Powers[target].Id!=id)
            { loadout.SetPair(loadout.Powers.Reverse()); (owner.Active[0],owner.Active[1])=(owner.Active[1],owner.Active[0]); owner.Context.Tell($"{definition.Name} moved to slot {target+1}."); }
            return;
        }
        var slot=requestedSlot ?? (PowerCatalog.Compatible(loadout.Powers[0],definition) ? 1 : 0);
        if (!PowerCatalog.Compatible(loadout.Powers[1-slot],definition)) throw new InvalidOperationException("Incompatible power pair.");
        owner.Active[slot].Cleanup(owner.Context); loadout.Replace(slot,definition);
        var p=Create(id); owner.Active[slot]=p;
        if (plugin.Mode.Live && owner.Context.Pawn is not null) p.Apply(owner.Context);
        owner.Context.Tell($"Power {slot+1} is now {definition.Name}: {definition.Description}");
        if (plugin.Config.PublicPowerAnnouncements) plugin.Feedback.PublicLoadout(player!);
    }
    private static void Cleanup(Owner owner)
    { foreach (var p in owner.Active.ToArray()) p.Cleanup(owner.Context); owner.Active.Clear(); owner.AppliedPawn=null; owner.Effects.Clear(); }
    public void Stop(CCSPlayerController? player) { if (Find(player) is { } owner) Cleanup(owner); }
    public void Clear()
    {
        foreach (var owner in _owners.Values.ToArray()) Cleanup(owner);
        _owners.Clear(); _emptyEffects.Clear(); Volatile.Write(ref _transmission,[]);
    }
    public void FilterTransmission(CCheckTransmitInfoList clients)
    {
        var effects=Volatile.Read(ref _transmission);
        foreach (var effect in effects) effect.FilterOutlines(clients);
        foreach (var effect in effects) effect.FilterConcealed(clients);
    }
    public string Status(string id,CCSPlayerController? player=null)
    {
        var owner=Find(player ?? plugin.Mode.Boss); var power=owner?.Active.FirstOrDefault(p=>p.Id==id);
        if (power?.Status is { } status) return status;
        if (power?.IsActive==true) return "active until round ends";
        if (owner?.Loadout?.Consumed.Contains(id)==true) return "used this round";
        if (power?.Definition.Kind!=PowerKind.Activated) return "automatic";
        var remaining=owner?.Loadout?.Remaining(id,Server.CurrentTime) ?? 0;
        return remaining>0 ? $"{remaining:0.0}s" : "ready";
    }
}
