using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Support;

public sealed class PlayerScale
{
    private sealed class State(CCSPlayerPawn pawn)
    {
        public float Scale { get; }=pawn.CBodyComponent!.SceneNode!.Scale>0 ? pawn.CBodyComponent.SceneNode.Scale : 1;
        public float Absolute { get; }=pawn.CBodyComponent!.SceneNode!.AbsScale>0 ? pawn.CBodyComponent.SceneNode.AbsScale : 1;
        public float Local { get; }=pawn.CBodyComponent!.SceneNode!.ClientLocalScale>0 ? pawn.CBodyComponent.SceneNode.ClientLocalScale : 1;
        public Vector Mins { get; }=Players.Copy(pawn.Collision.Mins);
        public Vector Maxs { get; }=Players.Copy(pawn.Collision.Maxs);
        public Vector Capsule1 { get; }=Players.Copy(pawn.Collision.CapsuleCenter1);
        public Vector Capsule2 { get; }=Players.Copy(pawn.Collision.CapsuleCenter2);
        public float CapsuleRadius { get; }=pawn.Collision.CapsuleRadius;
        public Vector Origin { get; }=Players.Copy(pawn.AbsOrigin!);
        public float ViewHeight { get; }=pawn.ViewOffset.Z>8 ? pawn.ViewOffset.Z : 64;
    }
    private readonly Dictionary<uint,State> _states=new();
    public void Apply(CCSPlayerPawn pawn,float factor)
    {
        if (pawn.CBodyComponent?.SceneNode is not { } node) return;
        if (!_states.TryGetValue(pawn.EntityHandle.Raw,out var old)) _states[pawn.EntityHandle.Raw]=old=new(pawn);
        if (node.Scale!=old.Scale*factor)
        {
            node.Scale=old.Scale*factor; node.AbsScale=old.Absolute*factor; node.ClientLocalScale=old.Local*factor;
            DirtyBody(pawn);
        }
        var crouched=pawn.MovementServices?.As<CCSPlayer_MovementServices>().Ducked==true;
        var height=crouched ? Math.Min(old.Maxs.Z,54) : old.Maxs.Z;
        Set(pawn.Collision.Mins,old.Mins.X*factor,old.Mins.Y*factor,old.Mins.Z*factor);
        Set(pawn.Collision.Maxs,old.Maxs.X*factor,old.Maxs.Y*factor,height*factor);
        Set(pawn.Collision.CapsuleCenter1,old.Capsule1.X*factor,old.Capsule1.Y*factor,old.Capsule1.Z*factor);
        Set(pawn.Collision.CapsuleCenter2,old.Capsule2.X*factor,old.Capsule2.Y*factor,old.Capsule2.Z*factor);
        pawn.Collision.CapsuleRadius=old.CapsuleRadius*factor;
        pawn.ViewOffset.Z=(crouched ? Math.Min(old.ViewHeight,46) : old.ViewHeight)*factor;
        Utilities.SetStateChanged(pawn,"CBaseModelEntity","m_vecViewOffset");
    }
    public static void DirtyBody(CCSPlayerPawn pawn)
    {
        // Scale is a scene-node field: dirty the owning body component, not pawn + node offset.
        NativeAPI.SchemaSetStateChanged(pawn.Handle,(uint)Schema.GetSchemaOffset("CBaseEntity","m_CBodyComponent"),uint.MaxValue,uint.MaxValue);
        pawn.LastNetworkChange=Server.CurrentTime; pawn.IsSteadyState.Clear();
    }
    private static void Set(Vector vector,float x,float y,float z) { vector.X=x; vector.Y=y; vector.Z=z; }
    public void Clear()
    {
        foreach (var (raw,old) in _states)
        {
            var pawn=Players.Entity<CCSPlayerPawn>(raw);
            if (pawn is not { IsValid:true } || pawn.CBodyComponent?.SceneNode is not { } node) continue;
            if (pawn.Health>0 && pawn.AbsOrigin is { } current)
            {
                var full=GameTrace.TraceHullShape(current,current,old.Mins,old.Maxs,pawn,new() { InteractsWith=Masks.PlayerSolid });
                if (full.IsAllSolid)
                {
                    var safe=GameTrace.TraceHullShape(old.Origin,old.Origin,old.Mins,old.Maxs,pawn,new() { InteractsWith=Masks.PlayerSolid });
                    if (!safe.IsAllSolid) pawn.Teleport(old.Origin,null,new Vector(0,0,0));
                }
            }
            node.Scale=old.Scale; node.AbsScale=old.Absolute; node.ClientLocalScale=old.Local;
            Set(pawn.Collision.Mins,old.Mins.X,old.Mins.Y,old.Mins.Z); Set(pawn.Collision.Maxs,old.Maxs.X,old.Maxs.Y,old.Maxs.Z);
            Set(pawn.Collision.CapsuleCenter1,old.Capsule1.X,old.Capsule1.Y,old.Capsule1.Z);
            Set(pawn.Collision.CapsuleCenter2,old.Capsule2.X,old.Capsule2.Y,old.Capsule2.Z);
            pawn.Collision.CapsuleRadius=old.CapsuleRadius;
            pawn.ViewOffset.Z=old.ViewHeight; DirtyBody(pawn);
            Utilities.SetStateChanged(pawn,"CBaseModelEntity","m_vecViewOffset");
        }
        _states.Clear();
    }
}
