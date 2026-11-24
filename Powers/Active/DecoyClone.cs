using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Powers.Active;

public sealed class DecoyClone() : Power("decoy_clone")
{
    private static readonly TraceOptions Terrain=new() { InteractsWith=Masks.PlayerSolidBrushOnly|Contents.PhysicsProp };
    private uint? _clone;
    private uint? _weaponModel;
    private DecoyLocomotion? _locomotion;
    private float _dx,_dy,_scale;
    private const float WalkSpeed=130;
    private const float StepDistance=54;
    internal const uint AlwaysTransmit=2048;
    private float _distanceSinceStep;
    private string _footstepEvent="T_Concrete.StepLeft";
#if SERVER_SMOKE
    internal uint? HeldWeaponModel=>_weaponModel;
    internal uint LastFootstepSound { get; private set; }
    internal bool LocomotionReady { get; private set; }
#endif
    private double _until;
    private bool _shot;
    public override bool IsActive => _clone.HasValue;
    public override string? Status => IsActive ? "clone deployed" : null;

    public override bool Activate(PowerContext context)
    {
        if (context.Pawn is not { AbsOrigin:{ } feet } owner || IsActive) return false;
        var ownerNode=owner.CBodyComponent?.SceneNode;
        var model=ownerNode?.GetSkeletonInstance().ModelState.ModelName;
        if (string.IsNullOrEmpty(model) || ownerNode is null) return false;
        var yaw=owner.EyeAngles.Y;
        _dx=MathF.Cos(yaw*MathF.PI/180);
        _dy=MathF.Sin(yaw*MathF.PI/180);
        var start=new Vector(feet.X,feet.Y,feet.Z+2);
        var ahead=new Vector(start.X+_dx*36,start.Y+_dy*36,start.Z);
        var clear=GameTrace.TraceHullShape(start,ahead,owner.Collision.Mins,owner.Collision.Maxs,owner,Terrain);
        if (clear.IsAllSolid || clear.DidHit()) { context.Tell("Decoy Clone needs room in front of you."); return false; }
        if (Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic") is not { IsValid:true } decoy) return false;
        _clone=decoy.EntityHandle.Raw;
        context.Plugin.TrackEntity(_clone.Value);
        using var keys=new CEntityKeyValues();
        // Spawn in place so the first visibility bounds match the visible prop.
        var angles=new QAngle(0,yaw,0);
        keys.SetString("model",model);
        keys.SetVector("origin",ahead);
        keys.SetAngle("angles",angles);
        keys.SetUInt("spawnflags",AlwaysTransmit);
        keys.SetInt("solid",2);
        keys.SetBool("use_animgraph",true);
        keys.SetBool("AnimateOnServer",true);
        decoy.DispatchSpawn(keys);
        decoy.AnimatedEveryTick=true;
        decoy.AnimGraphUpdateEnabled=true;
        decoy.ShouldUpdateTransformations=true;
        decoy.UseAnimGraph=true;
        decoy.GraphControllerEnabled=true;
        var ownerScale=ownerNode.Scale;
        _scale=float.IsFinite(ownerScale) && ownerScale>0 ? ownerScale : 1;
        _locomotion=new(owner.WeaponServices?.ActiveWeapon.Value?.As<CCSWeaponBase>());
        if (decoy.CBodyComponent?.SceneNode is { } node)
        {
            node.Scale=_scale;
            node.AbsScale=_scale;
            PlayerScale.DirtyBody(decoy);
        }
        decoy.TakesDamage=true;
        decoy.Health=1;
        decoy.Teleport(ahead,angles,new Vector(0,0,0));
        _footstepEvent=owner.TeamNum==3 ? "CT_Concrete.StepLeft" : "T_Concrete.StepLeft";
        _distanceSinceStep=0;
        AttachHeldWeapon(context,owner,decoy,ahead,angles);
        _until=context.Now+Settings(context).Duration;
        _shot=false;
        context.Plugin.CloneHits.Add(_clone.Value,()=>OnShot(context));
        return true;
    }

    private void AttachHeldWeapon(PowerContext context,CCSPlayerPawn owner,CDynamicProp decoy,Vector origin,QAngle angles)
    {
        var weapon=owner.WeaponServices?.ActiveWeapon.Value;
        var model=weapon?.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
        if (string.IsNullOrEmpty(model) ||
            Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic") is not { IsValid:true } copy) return;
        var raw=copy.EntityHandle.Raw;
        _weaponModel=raw;
        context.Plugin.TrackEntity(raw);
        using var keys=new CEntityKeyValues();
        // Weapon models carry physics prop data. Spawn a non-solid display prop
        // with a compatible model before assigning the held weapon's model.
        keys.SetString("model",decoy.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
        keys.SetVector("origin",origin);
        keys.SetAngle("angles",angles);
        keys.SetUInt("spawnflags",AlwaysTransmit);
        keys.SetInt("solid",0);
        copy.DispatchSpawn(keys);
        if (!copy.IsValid) { context.Plugin.RemoveTrackedEntity(raw); _weaponModel=null; return; }
        copy.SetModel(model);
        copy.Teleport(origin,angles,null);
        var attachment=owner.LeftHanded ? "weapon_hand_l" : "weapon_hand_r";
        context.Plugin.Defer(()=> {
            if (_weaponModel!=raw || !copy.IsValid || !decoy.IsValid) return;
            copy.AcceptInput("FollowEntity",decoy,copy,"!activator");
            context.Plugin.Defer(()=> {
                if (_weaponModel!=raw || !copy.IsValid || !decoy.IsValid) return;
                if (copy.CBodyComponent?.SceneNode?.PParent?.Owner?.Handle!=decoy.Handle)
                {
                    context.Plugin.RemoveTrackedEntity(raw);
                    _weaponModel=null;
                    return;
                }
                // FollowEntity establishes the parent. Use its hand attachment for
                // the weapon model instead of merging unrelated bones.
                copy.Effects&=~((1u<<0)|(1u<<7));
                Utilities.SetStateChanged(copy,"CBaseEntity","m_fEffects");
                copy.AcceptInput("SetParentAttachment",decoy,copy,attachment);
                copy.Collision.SolidType=SolidType_t.SOLID_NONE;
            });
        });
    }

    public override void Tick(PowerContext context)
    {
        if (_clone is not { } raw || _shot) return;
        if (context.Now>=_until || Players.Entity<CDynamicProp>(raw) is not { AbsOrigin:{ } pos } clone)
        { Cleanup(context); return; }
        var distance=WalkSpeed*Server.TickInterval;
        // Step up stairs and follow the floor. Stop at walls and ledges.
        var upper=new Vector(pos.X,pos.Y,pos.Z+18);
        var forward=new Vector(upper.X+_dx*distance,upper.Y+_dy*distance,upper.Z);
        var mins=new Vector(-14*_scale,-14*_scale,0); var maxs=new Vector(14*_scale,14*_scale,54*_scale);
        var path=GameTrace.TraceHullShape(upper,forward,mins,maxs,clone,Terrain);
        if (path.IsAllSolid || path.DidHit()) { StopWalking(clone); return; }
        var floor=GameTrace.TraceHullShape(forward,new(forward.X,forward.Y,pos.Z-30),mins,maxs,clone,Terrain);
        if (floor.IsAllSolid || !floor.DidHit() || floor.Normal.Z<.7f) { StopWalking(clone); return; }
        var next=floor.EndPos; next.Z+=1;
        clone.Teleport(next,null,new Vector(WalkSpeed*_dx,WalkSpeed*_dy,0));
        UpdateLocomotion(clone,WalkSpeed/_scale);
        _distanceSinceStep+=distance;
        if (_distanceSinceStep<StepDistance*_scale) return;
        _distanceSinceStep=0;
        if (context.Plugin.Config.EnableSounds)
        {
            var sound=clone.EmitSound(_footstepEvent);
#if SERVER_SMOKE
            LastFootstepSound=sound;
#endif
        }
    }

    private void UpdateLocomotion(CDynamicProp clone,float speed)
    {
        var ready=_locomotion?.Update(clone,speed,_scale)==true;
#if SERVER_SMOKE
        LocomotionReady=ready;
#endif
    }

    private void StopWalking(CDynamicProp clone)
    {
        UpdateLocomotion(clone,0);
        _distanceSinceStep=0;
        if (clone.AbsVelocity.X!=0 || clone.AbsVelocity.Y!=0)
            clone.Teleport(null,null,new Vector(0,0,0));
    }

    private void OnShot(PowerContext context)
    {
        if (_shot || _clone is not { } raw) return;
        _shot=true;
        // Remove the target only after the engine has finished processing this bullet.
        context.Plugin.Defer(()=> {
            if (_clone!=raw) return;
            var feet=Players.Entity<CDynamicProp>(raw)?.AbsOrigin;
            if (feet is not null && context.Pawn is { } owner &&
                GrenadeFactory.Create("flashbang_projectile",new(feet.X,feet.Y,feet.Z+48*_scale),new(0,0,0),owner) is { } flash)
            {
                var handle=flash.EntityHandle.Raw;
                context.Plugin.TrackEntity(handle);
                context.Plugin.SuppressCluster(handle);
                flash.DetonateTime=Server.CurrentTime;
                flash.AcceptInput("Detonate");
                context.Plugin.AddTimer(2,()=>context.Plugin.RemoveTrackedEntity(handle),CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            }
            Cleanup(context);
        });
    }

    public override void Cleanup(PowerContext context)
    {
        if (_clone is not { } raw) return;
        if (_weaponModel is { } weapon) context.Plugin.RemoveTrackedEntity(weapon);
        _weaponModel=null;
        _locomotion=null;
        context.Plugin.CloneHits.Remove(raw);
        context.Plugin.RemoveTrackedEntity(raw);
        _clone=null;
        _shot=false;
    }
}
