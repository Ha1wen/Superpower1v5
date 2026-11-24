#if SERVER_SMOKE
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private void ScheduleThunderProbe()
    {
        BootstrapUpdateBots();
        CCSPlayerController Shooter()=>Mode.Boss!;
        CCSPlayerPawn Pawn()=>Players.AlivePawn(Shooter())!;
        CCSPlayerController Target()=>Mode.Opponents.First();
        CCSPlayerPawn Victim()=>Players.AlivePawn(Target())!;
        int actualShots=0; bool driving=false; Vector? near=null; var nativeDamage=new List<int>();
        RegisterEventHandler<EventWeaponFire>((e,i)=> {
            if (driving && e.Userid?.Slot==Shooter().Slot && e.Weapon is "weapon_taser" or "taser")
                actualShots++;
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerHurt>((e,i)=> {
            if (driving && e.Userid?.Slot==Target().Slot && e.Attacker?.Slot==Shooter().Slot) nativeDamage.Add(e.DmgHealth);
            return HookResult.Continue;
        });
        // Exercise this Windows test server's native CWeaponTaser primary attack;
        // bot AI does not choose the Zeus reliably. This binding is only in the test build.
        var primaryAttack=new Lazy<MemoryFunctionWithReturn<IntPtr,int>>(()=>new(
            "40 53 48 83 EC 40 48 8B 01 33 D2 48 8B D9 FF 90 B0 0B 00 00 45 33 C9 45 33 C0 0F 28 C8 48 8B CB"));
        CWeaponTaser? taser=null; CBasePlayerWeapon? pistol=null;
        void Pair()=>Powers.NativeProbePair("thunder_god","teleport");
        void Fire()=>new EventWeaponFire(true) { Userid=Shooter(),Weapon="taser" }.FireEvent(false);
        Vector Eyes()=>new(Pawn().AbsOrigin!.X+Pawn().ViewOffset.X,Pawn().AbsOrigin!.Y+Pawn().ViewOffset.Y,Pawn().AbsOrigin!.Z+Pawn().ViewOffset.Z);
        void Aim()
        {
            var start=Eyes(); var end=Victim().AbsOrigin!;
            var dx=end.X-start.X; var dy=end.Y-start.Y;
            var pitch=-MathF.Atan2(end.Z+40-start.Z,MathF.Sqrt(dx*dx+dy*dy))*180/MathF.PI;
            var yaw=MathF.Atan2(dy,dx)*180/MathF.PI;
            Pawn().Teleport(null,new QAngle(pitch,yaw,0),null);
            Pawn().EyeAngles.X=pitch; Pawn().EyeAngles.Y=yaw;
            Pawn().V_angle.X=pitch; Pawn().V_angle.Y=yaw;
        }
        UpdateAt(10,"Two independently owned Zeus weapons use the stock server data",()=> {
            foreach (var player in Mode.Eligible())
            {
                var pawn=Players.AlivePawn(player)!;
                pawn.MoveType=MoveType_t.MOVETYPE_NONE; pawn.ActualMoveType=MoveType_t.MOVETYPE_NONE;
                pawn.Teleport(null,null,new Vector(0,0,0));
            }
            Shooter().RemoveWeapons();
            pistol=Shooter().GiveNamedItem<CBasePlayerWeapon>("weapon_glock");
            taser=Shooter().GiveNamedItem<CWeaponTaser>("weapon_taser")!;
            Pawn().WeaponServices!.ActiveWeapon.Raw=taser.EntityHandle.Raw;
            var ordinary=Target().GiveNamedItem<CWeaponTaser>("weapon_taser")!;
            Require(ordinary.VData!.Range==taser.VData!.Range && taser.VData.Range<500,"weapon data overridden globally");
            Vector? spot=null;
            foreach (var area in CCSNavArea.GetAllNavAreas().Take(500))
            {
                var floor=area.Center; var start=new Vector(floor.X,floor.Y,floor.Z+100);
                for (var angle=0;angle<360;angle+=30)
                {
                    var a=angle*MathF.PI/180;
                    var end=new Vector(start.X+900*MathF.Cos(a),start.Y+900*MathF.Sin(a),start.Z);
                    var sight=GameTrace.TraceHullShape(start,end,new(-16,-16,-32),new(16,16,32),Pawn(),new() { InteractsWith=Masks.PlayerSolidBrushOnly });
                    if (sight.DidHit() || sight.IsAllSolid) continue;
                    Pawn().Teleport(new(start.X,start.Y,start.Z-Pawn().ViewOffset.Z),null,new Vector(0,0,0));
                    spot=new(end.X,end.Y,end.Z-40);
                    near=new(start.X+80*MathF.Cos(a),start.Y+80*MathF.Sin(a),start.Z-40);
                    break;
                }
                if (spot is not null) break;
            }
            Require(spot is not null,"no long test sightline");
            Victim().Teleport(spot,null,new Vector(0,0,0)); Victim().Health=2000; Victim().ArmorValue=0;
        });
        UpdateAt(11,"Ordinary Zeus gains no extended hit and Thunder God misses off crosshair",()=> {
            Aim(); primaryAttack.Value.Invoke(taser!.Handle);
            Require(taser.Clip1==0 && Victim().Health==2000,"stock Zeus hit at extended range");
            Pair(); Pawn().EyeAngles.Y+=15; Fire(); Require(Victim().Health==2000,"off-crosshair hit");
        });
        UpdateAt(12,"Centered Zeus hits beyond stock range exactly once, including duplicate fire events",()=> {
            Pair(); Aim(); taser!.AccuracyPenalty=1; taser.Clip1=1; taser.NextPrimaryAttackTick=Server.TickCount;
            primaryAttack.Value.Invoke(taser.Handle);
        });
        UpdateAt(12.25f,"Duplicate Zeus notification cannot apply a second hit",()=> {
            Fire();
            var lost=2000-Victim().Health;
            Require(lost==taser!.VData!.Damage && taser.Clip1==0,$"expected one shock for {taser.VData.Damage}, got {lost}");
            Pawn().WeaponServices!.ActiveWeapon.Raw=pistol!.EntityHandle.Raw;
        });
        UpdateAt(21.75f,"Holstered Zeus stays spent before ten seconds",()=>Require(taser!.Clip1==0,"early recharge"));
        UpdateAt(22.25f,"Holstered Zeus holds one ready shot at ten seconds",()=> {
            Require(taser!.Clip1==1 && taser.NextPrimaryAttackTick<=Server.TickCount,"failed to recharge");
            Require(Pawn().WeaponServices!.ActiveWeapon.Raw==pistol!.EntityHandle.Raw,"recharge forced a weapon switch");
        });
        UpdateAt(23,"Map geometry blocks a powered Zeus shot",()=> {
            var spawns=Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_player_counterterrorist");
            var start=Eyes(); var blocked=spawns.Select(s=>s.AbsOrigin!).First(v=>{
                var trace=GameTrace.TraceEndShape(start,new(v.X,v.Y,v.Z+40),Pawn(),new(){InteractsWith=Masks.ShotBrushOnly});
                return trace.DidHit() && trace.Fraction<.9f;
            });
            Victim().Teleport(Players.Copy(blocked),null,new Vector(0,0,0)); Victim().Health=2000;
            Pawn().WeaponServices!.ActiveWeapon.Raw=taser!.EntityHandle.Raw;
        });
        UpdateAt(23.25f,"A crosshair aligned with an opponent behind cover causes no shock",()=> {
            Aim(); Fire(); Require(Victim().Health==2000,"shot penetrated world geometry");
        });
        UpdateAt(24,"Set up a close-range native Zeus shot",()=> {
            Pair(); taser!.Clip1=1; taser.NextPrimaryAttackTick=Server.TickCount;
            Pawn().WeaponServices!.ActiveWeapon.Raw=taser.EntityHandle.Raw;
            Pawn().WeaponServices!.As<CCSPlayer_WeaponServices>().NextAttack=0;
            Victim().Teleport(near!,null,new Vector(0,0,0)); Victim().Health=10000; Aim();
        });
        UpdateAt(24.25f,"Native Zeus primary attack consumes the charge",()=> {
            Aim(); driving=true;
            primaryAttack.Value.Invoke(taser!.Handle);
            Require(taser.LastAttackTick>0 && taser.Clip1==0,"engine did not discharge");
        });
        UpdateAt(24.5f,"Native discharge deals exactly one shock and matches crosshair",()=> {
            Require(Victim().Health==10000-taser!.VData!.Damage,$"events={actualShots}, health={Victim().Health}, damage={string.Join(',',nativeDamage)}, eye={Eyes()}, angles={Pawn().EyeAngles}, target={Victim().AbsOrigin}");
        });
        UpdateAt(34.5f,"The same Zeus can fire again after ten seconds",()=> {
            Aim();
            Require(taser!.Clip1==1 && taser.NextPrimaryAttackTick<=Server.TickCount,"weapon not ready");
            primaryAttack.Value.Invoke(taser.Handle);
            Require(taser.Clip1==0,"engine did not fire the regenerated charge");
        });
        UpdateAt(34.75f,"Second native discharge causes exactly one shock",()=> {
            driving=false;
            Require(Victim().Health==10000-2*taser!.VData!.Damage,$"health={Victim().Health}, damage={string.Join(',',nativeDamage)}");
        });
        AddTimer(35,FinishUpdateProbe,TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
