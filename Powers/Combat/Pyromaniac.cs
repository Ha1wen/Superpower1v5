using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Powers.Combat;

public sealed class Pyromaniac() : Power("pyromaniac")
{
    private sealed class Pool(Vector position,uint visual,double until)
    { public Vector Position { get; }=position; public uint Visual { get; }=visual; public double Until { get; set; }=until; }
    private readonly List<Pool> _pools=[];
    private double _nextImpact,_nextDamage;
    public int PoolCount => _pools.Count;
    public override void BulletImpact(PowerContext context,Vector impact)
    {
        if (context.Now<_nextImpact || context.Pawn is not { } boss) return;
        _nextImpact=context.Now+Settings(context).Interval;
        var above=new Vector(impact.X,impact.Y,impact.Z+16);
        var ground=GameTrace.TraceEndShape(above,new(impact.X,impact.Y,impact.Z-192),boss,new() { InteractsWith=Masks.SolidBrushOnly });
        if (!ground.DidHit() || ground.IsAllSolid || ground.Normal.Z<.55f) return;
        var position=ground.EndPos; position.Z+=3;
        var existing=_pools.FirstOrDefault(pool=>Players.Distance(pool.Position,position)<Settings(context).Radius*.65f);
        if (existing is not null) { existing.Until=context.Now+Settings(context).Duration; return; }
        while (_pools.Count>=Settings(context).MaxEntities) Remove(context,_pools[0]);
        var visual=context.Plugin.Feedback.Particle("particles/inferno_fx/molotov_fire01.vpcf",position);
        if (visual==0) return;
        _pools.Add(new(position,visual,context.Now+Settings(context).Duration));
    }
    public override void SlowTick(PowerContext context)
    {
        foreach (var pool in _pools.Where(p=>p.Until<=context.Now).ToArray()) Remove(context,pool);
        if (_pools.Count==0 || context.Now<_nextDamage || context.Boss is not { } boss || context.Pawn is not { } owner) return;
        const float interval=.25f;
        _nextDamage=context.Now+interval;
        foreach (var opponent in context.Opponents.ToArray())
        {
            if (Players.AlivePawn(opponent) is not { } pawn || pawn.AbsOrigin is not { } position) continue;
            var center=new Vector(position.X,position.Y,position.Z+16);
            if (!_pools.Any(pool=>Math.Abs(position.Z-pool.Position.Z)<72 &&
                Players.Distance(pool.Position,position)<=Settings(context).Radius && SafeTeleport.Visible(pool.Position,center,owner))) continue;
            context.Plugin.Damage.Deal(opponent.EntityHandle.Raw,boss.EntityHandle.Raw,Settings(context).Strength*interval,DamageTypes_t.DMG_BURN);
        }
    }
    public override void Smoke(PowerContext context,Vector origin)
    { foreach (var pool in _pools.Where(p=>Players.Distance(p.Position,origin)<=Settings(context).Radius+144).ToArray()) Remove(context,pool); }
    private void Remove(PowerContext context,Pool pool)
    { context.Plugin.RemoveTrackedEntity(pool.Visual); _pools.Remove(pool); }
    public override void Cleanup(PowerContext context)
    { foreach (var pool in _pools.ToArray()) Remove(context,pool); }
}
