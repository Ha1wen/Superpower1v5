using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Powers.Combat;

public sealed class Pyromaniac() : Power("pyromaniac")
{
    private sealed class Fire(Vector position,uint projectile,double created,double until)
    {
        public Vector Position { get; }=position;
        public uint Projectile { get; }=projectile;
        public uint? Inferno;
        public double Created { get; }=created;
        public double Until { get; }=until;
    }
    private readonly List<Fire> _fires=[];
    private double _nextImpact;
    public int PoolCount => _fires.Count;
    public int InfernoCount => _fires.Count(f=>f.Inferno is { } raw && Players.Entity<CInferno>(raw) is { IsValid:true });
    public override void BulletImpact(PowerContext c,Vector impact)
    {
        if (c.Now<_nextImpact || c.Pawn is not { } pawn) return;
        var settings=Settings(c);
        var ground=GameTrace.TraceEndShape(new(impact.X,impact.Y,impact.Z+16),new(impact.X,impact.Y,impact.Z-192),pawn,new() { InteractsWith=Masks.SolidBrushOnly });
        if (!ground.DidHit() || ground.IsAllSolid || ground.Normal.Z<.55f) return;
        var position=ground.EndPos; position.Z+=8;
        if (_fires.Any(f=>Players.Distance(f.Position,position)<96)) return;
        _nextImpact=c.Now+settings.Interval;
        while (_fires.Count>=settings.MaxEntities) Remove(c,_fires[0]);
        // Real armed Molotovs retain CS2's spreading, damage ramp, sound and smoke interaction.
        var projectile=GrenadeFactory.Create("molotov_projectile",position,new Vector(0,0,-160),pawn,46);
        if (projectile is not { IsValid:true }) return;
        var raw=projectile.EntityHandle.Raw;
        c.Plugin.TrackEntity(raw); c.Plugin.SuppressCluster(raw);
        _fires.Add(new(position,raw,c.Now,c.Now+settings.Duration));
    }
    public void InfernoStarted(PowerContext c,CInferno inferno)
    {
        if (inferno.AbsOrigin is not { } origin) return;
        var fire=_fires.Where(f=>f.Inferno is null && c.Now-f.Created<2 && Players.Distance(f.Position,origin)<128)
            .OrderBy(f=>Players.Distance(f.Position,origin)).FirstOrDefault();
        if (fire is null) return;
        if (inferno.OwnerEntity.IsValid && inferno.OwnerEntity.Raw!=c.Pawn?.EntityHandle.Raw && inferno.OwnerEntity.Raw!=fire.Projectile) return;
        fire.Inferno=inferno.EntityHandle.Raw; c.Plugin.TrackEntity(inferno.EntityHandle.Raw);
    }
    public override void SlowTick(PowerContext c)
    {
        foreach (var fire in _fires.ToArray())
            if (fire.Until<=c.Now || c.Now-fire.Created>2 &&
                (fire.Inferno is not { } raw || Players.Entity<CInferno>(raw) is not { IsValid:true })) Remove(c,fire);
    }
    public override void Smoke(PowerContext c,Vector origin)
    {
        foreach (var fire in _fires.Where(f=>Players.Distance(f.Position,origin)<=144).ToArray()) Remove(c,fire);
    }
    private void Remove(PowerContext c,Fire fire)
    {
        c.Plugin.RemoveTrackedEntity(fire.Projectile);
        if (fire.Inferno is { } raw) c.Plugin.RemoveTrackedEntity(raw);
        _fires.Remove(fire);
    }
    public override void Cleanup(PowerContext c) { foreach (var fire in _fires.ToArray()) Remove(c,fire); }
}
