using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;

namespace Superpower1v5.Powers.Combat;

public sealed class ClusterGrenade() : Power("cluster_grenade")
{
    private readonly HashSet<uint> _processed=[];
    private readonly HashSet<uint> _children=[];
    public int ChildCount => _children.Count;
    public override void Projectile(PowerContext context,CBaseCSGrenadeProjectile projectile)
    {
        if (context.Pawn is not { } owner || projectile.Thrower.Raw!=owner.EntityHandle.Raw ||
            !GrenadeFactory.Supported(projectile.DesignerName) || !_processed.Add(projectile.EntityHandle.Raw) || _children.Contains(projectile.EntityHandle.Raw)) return;
        Prune(context);
        if (_children.Count+3>Settings(context).MaxEntities) { context.Tell("Cluster Grenade: too many active copies."); return; }
        if (projectile.AbsOrigin is not { } origin) return;
        var velocity=Players.Copy(projectile.AbsVelocity);
        foreach (var degrees in new[] { -12f,-4f,4f,12f })
        {
            var angle=degrees*MathF.PI/180;
            var split=new Vector(velocity.X*MathF.Cos(angle)-velocity.Y*MathF.Sin(angle),velocity.X*MathF.Sin(angle)+velocity.Y*MathF.Cos(angle),velocity.Z);
            if (degrees==-12) { projectile.Teleport(null,null,split); continue; }
            var item=projectile.DesignerName=="molotov_projectile" && projectile.As<CMolotovProjectile>().IsIncGrenade ? (ushort)48 : projectile.ItemIndex;
            var copy=GrenadeFactory.Create(projectile.DesignerName,Players.Copy(origin),split,owner,item);
            if (copy is null) continue;
            _children.Add(copy.EntityHandle.Raw); _processed.Add(copy.EntityHandle.Raw); context.Plugin.TrackEntity(copy.EntityHandle.Raw);
            if (projectile.DetonateTime>context.Now) copy.DetonateTime=projectile.DetonateTime;
            if (projectile.Damage>0) copy.Damage=projectile.Damage;
        }
    }
    private void Prune(PowerContext context)
    {
        foreach (var raw in _children.Where(raw=>Players.Entity<CBaseCSGrenadeProjectile>(raw) is not { IsValid:true }).ToArray())
        { _children.Remove(raw); context.Plugin.RemoveTrackedEntity(raw); }
        _processed.RemoveWhere(raw=>Players.Entity<CBaseCSGrenadeProjectile>(raw) is not { IsValid:true });
    }
    public override void SlowTick(PowerContext context) => Prune(context);
    public override void Cleanup(PowerContext context)
    { foreach (var raw in _children.ToArray()) context.Plugin.RemoveTrackedEntity(raw); _children.Clear(); _processed.Clear(); }
}
