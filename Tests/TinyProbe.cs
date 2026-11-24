#if SERVER_SMOKE
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private void ScheduleTinyProbe()
    {
        BootstrapUpdateBots();
        CCSPlayerController Boss()=>Mode.Boss!;
        CCSPlayerPawn Shooter()=>Players.AlivePawn(Boss())!;
        CCSPlayerController Target()=>Mode.Opponents.First();
        CCSPlayerPawn Victim()=>Players.AlivePawn(Target())!;
        CCSWeaponBaseGun? gun=null;
        Vector? impact=null;
        int hitGroup=0;
        var primaryAttack=new Lazy<MemoryFunctionWithReturn<IntPtr,int>>(()=>new(
            "40 56 57 41 54 48 83 EC 50 48 8B F9 E8 ? ? ? ? 48 8B F0 48 85 C0 0F 84 ? ? ? ? 8B 88"));
        RegisterEventHandler<EventBulletImpact>((e,_)=>{
            if (Mode.IsBoss(e.Userid)) impact=new(e.X,e.Y,e.Z);
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerHurt>((e,_)=>{
            if (Mode.IsBoss(e.Attacker) && e.Userid?.Slot==Target().Slot) hitGroup=e.Hitgroup;
            return HookResult.Continue;
        });
        Vector Eye()=>new(Shooter().AbsOrigin!.X+Shooter().ViewOffset.X,Shooter().AbsOrigin!.Y+Shooter().ViewOffset.Y,Shooter().AbsOrigin!.Z+Shooter().ViewOffset.Z);
        void Shoot(HitGroup_t desired)
        {
            var shooter=Shooter(); var victim=Victim(); var eyes=Eye(); var feet=victim.AbsOrigin!;
            var points=new List<Vector>();
            for (var y=-30f;y<=30;y+=1)
            for (var z=-10f;z<=105;z+=1)
            {
                var candidate=new Vector(feet.X,feet.Y+y,feet.Z+z);
                var ray=GameTrace.TraceEndShape(eyes,candidate,shooter,new() { InteractsWith=Contents.Player|Contents.Hitbox });
                if (ray.DidHit() && ray.HitEntity().Handle==victim.Handle && UnlimitedWallbang.HitGroup(ray)==desired)
                    points.Add(candidate);
            }
            Require(points.Count>0,$"no {desired} aim point, victim at {feet} ({victim.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName})");
            var center=new Vector(feet.X,points.Average(p=>p.Y),points.Average(p=>p.Z));
            var target=points.MinBy(p=>Players.Distance(center,p))!;
            var dx=target.X-eyes.X; var dy=target.Y-eyes.Y;
            var pitch=-MathF.Atan2(target.Z-eyes.Z,MathF.Sqrt(dx*dx+dy*dy))*180/MathF.PI;
            var yaw=MathF.Atan2(dy,dx)*180/MathF.PI;
            shooter.Teleport(null,new QAngle(pitch,yaw,0),null);
            shooter.EyeAngles.X=shooter.V_angle.X=pitch; shooter.EyeAngles.Y=shooter.V_angle.Y=yaw;
            RecoilControl.Reset(shooter);
            gun!.AccuracyPenalty=0; gun.Clip1=30; gun.NextPrimaryAttackTick=Server.TickCount-1;
            shooter.WeaponServices!.ActiveWeapon.Raw=gun.EntityHandle.Raw;
            shooter.WeaponServices.As<CCSPlayer_WeaponServices>().NextAttack=0;
            victim.Health=5000; victim.ArmorValue=0;
            hitGroup=0; impact=null;
            primaryAttack.Value.Invoke(gun.Handle);
            Require(gun.Clip1==29 && hitGroup==(int)desired,
                $"requested {desired}, registered {hitGroup}, HP={victim.Health}, eye={eyes}, end={target}, impact={impact}, node scale={shooter.CBodyComponent!.SceneNode!.Scale}/{shooter.CBodyComponent.SceneNode.ClientLocalScale}");
        }
        UpdateAt(10,"Set up a rifle and stationary target at a clear test location",()=>{
            foreach (var player in Mode.Eligible())
            {
                var pawn=Players.AlivePawn(player)!; pawn.MoveType=pawn.ActualMoveType=MoveType_t.MOVETYPE_NONE;
                pawn.Teleport(null,null,new Vector(0,0,0));
            }
            Server.ExecuteCommand("sv_cheats 1; sv_unlag 0; weapon_accuracy_nospread 1");
            ConVar<bool>.Find("weapon_accuracy_nospread")!.ValueAsString="1";
            Powers.NativeProbePair("teleport","infinite_ammo");
            Boss().RemoveWeapons();
            gun=Boss().GiveNamedItem<CCSWeaponBaseGun>("weapon_ak47")!;
            Shooter().WeaponServices!.ActiveWeapon.Raw=gun.EntityHandle.Raw;
            var position=new Vector(-696.84f,-806.62f,118.46f);
            Shooter().Teleport(position,null,new Vector(0,0,0));
            var eye=Eye();
            Vector? delta=null;
            for(var yaw=0;yaw<360;yaw+=15)
            {
                var a=yaw*MathF.PI/180; var offset=new Vector(320*MathF.Cos(a),320*MathF.Sin(a),0);
                var path=GameTrace.TraceHullShape(eye,new(eye.X+offset.X,eye.Y+offset.Y,eye.Z),new(-16,-16,-16),new(16,16,20),Shooter(),new(){InteractsWith=Masks.PlayerSolidBrushOnly});
                if (path.DidHit() || path.IsAllSolid) continue;
                delta=offset; break;
            }
            Require(delta is not null,"no clear rifle line");
            Victim().Teleport(new Vector(position.X+delta!.X,position.Y+delta.Y,position.Z),new QAngle(0,0,0),new Vector(0,0,0));
        });
        UpdateAt(11,"Normal rifle registers a native headshot",()=>Shoot(HitGroup_t.HITGROUP_HEAD));
        float standingEye=0;
        UpdateAt(11.25f,"Normal rifle registers a native chest shot",()=>Shoot(HitGroup_t.HITGROUP_CHEST));
        UpdateAt(11.5f,"Equip Tiny and lower the server's standing firing origin",()=>{
            standingEye=Shooter().ViewOffset.Z;
            Powers.NativeProbePair("tiny","infinite_ammo");
            Require(Math.Abs(Shooter().ViewOffset.Z-standingEye*.5f)<.1f,"standing eye height did not follow scale");
        });
        UpdateAt(12,"Tiny rifle registers a native headshot",()=>Shoot(HitGroup_t.HITGROUP_HEAD));
        UpdateAt(13,"Tiny rifle registers a native chest shot",()=>Shoot(HitGroup_t.HITGROUP_CHEST));
        UpdateAt(14,"Crouched Tiny also registers a native headshot",()=>{
            var movement=Shooter().MovementServices!.As<CCSPlayer_MovementServices>();
            movement.DuckAmount=1; movement.Ducked=true;
            Powers.Tick();
            Require(Math.Abs(Shooter().ViewOffset.Z-23)<.1f,"crouch eye height did not follow scale");
            Shoot(HitGroup_t.HITGROUP_HEAD);
        });
        UpdateAt(14.5f,"Dropping Tiny restores the unscaled standing firing origin",()=>{
            var movement=Shooter().MovementServices!.As<CCSPlayer_MovementServices>();
            movement.DuckAmount=0; movement.Ducked=false;
            Powers.NativeProbePair("teleport","infinite_ammo");
            Require(Math.Abs(Shooter().ViewOffset.Z-standingEye)<.1f,"original aim height did not restore");
        });
        AddTimer(15,FinishUpdateProbe,TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
