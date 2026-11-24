#if SERVER_SMOKE
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Support;
using Superpower1v5.Powers.Active;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private void ScheduleCloneProbe()
    {
        BootstrapUpdateBots();
        uint decoy=0, weaponCopy=0; string? heldModel=null; Vector? start=null; float yaw=0; int flashes=0; float shotTime=0,flashTime=0;
        var handOffsets=new HashSet<(int X,int Y,int Z)>();
        CCSPlayerController Boss()=>Mode.Boss!;
        CCSPlayerController Ct()=>Mode.Opponents.First();
        CCSPlayerController Combo()=>Mode.Opponents.Skip(1).First();
        CCSPlayerController Replaced()=>Mode.Opponents.Skip(2).First();
        CCSPlayerPawn Pawn()=>Players.AlivePawn(Boss())!;
        bool Has(CCSPlayerController player,string name)=>Players.AlivePawn(player)!.WeaponServices!.MyWeapons
            .Any(h=>h.Value is { } item && item.DesignerName=="weapon_"+name);
        void Throw(CCSPlayerController player,string name)=>new EventGrenadeThrown(true) { Userid=player,Weapon=name }.FireEvent(false);
        RegisterEventHandler<EventFlashbangDetonate>((e,i)=> { if (e.Userid?.Slot==Boss().Slot) { flashes++; flashTime=Server.CurrentTime; } return HookResult.Continue; });
        void ActivateClone()
        {
            var pawn=Pawn();
            for (var angle=0;angle<360;angle+=30)
            {
                var origin=pawn.AbsOrigin!; var radians=angle*MathF.PI/180;
                var from=new Vector(origin.X,origin.Y,origin.Z+8);
                var to=new Vector(from.X+160*MathF.Cos(radians),from.Y+160*MathF.Sin(radians),from.Z);
                var path=GameTrace.TraceHullShape(from,to,new(-16,-16,0),new(16,16,60),pawn,new() { InteractsWith=Masks.PlayerSolidBrushOnly });
                if (path.DidHit() || path.IsAllSolid) continue;
                pawn.EyeAngles.Y=angle; yaw=radians;
                Powers.Activate(0,Boss());
                if (CloneHits.Count==1) break;
            }
            Require(CloneHits.Count==1,"no clone created");
            decoy=CloneHits.Keys.Single();
            var prop=Players.Entity<CDynamicProp>(decoy)!;
            start=Players.Copy(prop.AbsOrigin!);
            Require(prop.RenderMode==RenderMode_t.kRenderNormal && prop.Render.A==255 &&
                prop.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.MeshGroupMask!=0,"clone model is hidden");
            Require(prop.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName==
                pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName,"wrong player model");
        }
        UpdateAt(10,"Blood Pact heals above 500 HP and Cluster Grenade starts a delayed refund",()=> {
            Config.EveryoneHasPowers=true; Powers.ReconcileEveryone();
            Powers.NativeProbePair("blood_pact","juggernaut");
            var target=Players.AlivePawn(Ct())!; target.Health=100;
            Damage.Deal(Ct().EntityHandle.Raw,Boss().EntityHandle.Raw,20);
            Require(Pawn().Health==520 && Pawn().MaxHealth==500,$"health={Pawn().Health}");
            var baseSpeed=Pawn().VelocityModifier;
            Powers.NativeProbePair("blood_pact","teleport");
            var opponents=Mode.Opponents.Skip(3).Take(2).ToArray();
            foreach (var opponent in opponents)
            {
                Damage.Deal(opponent.EntityHandle.Raw,Boss().EntityHandle.Raw,500);
                Require(Players.AlivePawn(opponent) is null,"the test kill did not land");
            }
            Require(Math.Abs(Pawn().VelocityModifier-baseSpeed*1.1f)<.01,"Blood Pact did not grant a single ten-percent kill boost");
            Powers.NativeProbePair("teleport","infinite_ammo");
            Require(Math.Abs(Pawn().VelocityModifier-baseSpeed)<.01,"Blood Pact speed leaked after cleanup");
            Powers.NativeProbePair("cluster_grenade","blood_pact",player:Ct());
            Ct().RemoveWeapons(); Throw(Ct(),"incgrenade");
            Combo().RemoveWeapons(); Powers.NativeProbePair("infinite_ammo","cluster_grenade",player:Combo());
            Throw(Combo(),"weapon_smokegrenade");
            Replaced().RemoveWeapons(); Powers.NativeProbePair("cluster_grenade","teleport",player:Replaced());
            Throw(Replaced(),"decoy"); Powers.NativeProbePair("teleport","blood_pact",player:Replaced());
        });
        UpdateAt(11,"Decoy uses the owner's model and snapshots the held rifle",()=> {
            Boss().RemoveWeapons();
            var rifle=Boss().GiveNamedItem<CBasePlayerWeapon>("weapon_ak47")!;
            Pawn().WeaponServices!.ActiveWeapon.Raw=rifle.EntityHandle.Raw;
            heldModel=rifle.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;
            Powers.NativeProbePair("decoy_clone","infinite_ammo"); ActivateClone();
            var pistol=Boss().GiveNamedItem<CBasePlayerWeapon>("weapon_glock")!;
            Pawn().WeaponServices!.ActiveWeapon.Raw=pistol.EntityHandle.Raw;
            ConVar<int>.Find("ammo_grenade_limit_flashbang")!.ValueAsString="2";
            Boss().GiveNamedItem("weapon_flashbang"); // One flash remains from an initial stack of two.
            Throw(Boss(),"flashbang"); Pawn().EyeAngles.Y+=120;
        });
        for (var frame=0;frame<8;frame++)
        {
            AddTimer(11.2f+frame*.1f,()=> {
                if (Powers.Active[0] is not DecoyClone clone || clone.HeldWeaponModel is not { } weapon ||
                    Players.Entity<CDynamicProp>(weapon)?.AbsOrigin is not { } hand ||
                    Players.Entity<CDynamicProp>(decoy)?.AbsOrigin is not { } body) return;
                handOffsets.Add(((int)MathF.Round((hand.X-body.X)*4),
                    (int)MathF.Round((hand.Y-body.Y)*4),(int)MathF.Round((hand.Z-body.Z)*4)));
            },TimerFlags.STOP_ON_MAPCHANGE);
        }
        UpdateAt(12.5f,"Decoy transmits its model and walking poses and a native bullet trace hits its body",()=> {
            var prop=Players.Entity<CDynamicProp>(decoy)!; var pos=prop.AbsOrigin!;
            var along=(pos.X-start!.X)*MathF.Cos(yaw)+(pos.Y-start.Y)*MathF.Sin(yaw);
            var cross=(pos.Y-start.Y)*MathF.Cos(yaw)-(pos.X-start.X)*MathF.Sin(yaw);
            Require(along>10 && Math.Abs(cross)<1,$"displacement along={along}, across={cross}, start={start}, end={pos}");
            var from=new Vector(pos.X-32*MathF.Cos(yaw),pos.Y-32*MathF.Sin(yaw),pos.Z+42);
            var to=new Vector(pos.X+32*MathF.Cos(yaw),pos.Y+32*MathF.Sin(yaw),pos.Z+42);
            var hit=GameTrace.TraceEndShape(from,to,Pawn(),new() { InteractsWith=Masks.Shot });
            Require(prop.RenderMode==RenderMode_t.kRenderNormal && prop.Render.A==255 &&
                prop.CBodyComponent?.SceneNode?.PParent is null,"clone visibility depended on a hidden parent");
            var clone=(DecoyClone)Powers.Active[0];
            var copy=Players.Entity<CDynamicProp>(clone.HeldWeaponModel??0);
            var node=copy?.CBodyComponent?.SceneNode;
            Require(copy is { IsValid:true, RenderMode:RenderMode_t.kRenderNormal } &&
                node is not null && node.PParent?.Owner?.Handle==prop.Handle &&
                node.GetSkeletonInstance().ModelState.ModelName==heldModel &&
                copy.Collision.SolidType==SolidType_t.SOLID_NONE && (copy.AbsOrigin?.Z??0)>pos.Z+10,
                $"weapon child valid={copy?.IsValid}, attachment={node?.ParentAttachmentOrBone}, parent={node?.PParent?.Owner?.Index}, model={node?.GetSkeletonInstance().ModelState.ModelName}, solid={copy?.Collision.SolidType}, prop={pos}, weapon={copy?.AbsOrigin}");
            weaponCopy=copy!.EntityHandle.Raw;
            Require(clone.LastFootstepSound!=0,"native footstep sound did not start");
            var graph=prop.CBodyComponent!.As<CBodyComponentBaseAnimGraph>().AnimationController;
            Require((prop.Spawnflags&copy.Spawnflags&DecoyClone.AlwaysTransmit)!=0,"decoy body or held weapon can be culled by the network");
            Require(clone.LocomotionReady && graph.SerializePoseRecipeAG2Dynamic.Count>0 && handOffsets.Count>1,
                $"animation bound={clone.LocomotionReady}, pose recipe bytes={graph.SerializePoseRecipeAG2Dynamic.Count}, distinct hand positions={handOffsets.Count}");
            var target=Players.Entity<CDynamicProp>(decoy)!;
            Require(hit.DidHit() && hit.HitEntity().Handle==target.Handle,$"trace missed decoy hitbox at {target.AbsOrigin} (pawn {pos})");
            HitClone(target,Pawn(),DamageTypes_t.DMG_BLAST);
        });
        UpdateAt(13,"Infinite Ammo returns a flashbang and a bullet triggers the surviving clone",()=> {
            Require(Has(Combo(),"smokegrenade"),"combined loadout did not use the immediate refund");
            Combo().RemoveWeapons();
            var weapons=Pawn().WeaponServices!;
            var flash=weapons.MyWeapons.Select(h=>h.Value).FirstOrDefault(w=>w?.DesignerName=="weapon_flashbang");
            Require(flash?.VData is { } data && weapons.Ammo[data.PrimaryAmmoType]==2,"flashbang stack was not restored to two");
            Require(CloneHits.ContainsKey(decoy),"non-bullet damage removed clone");
            var prop=Players.Entity<CDynamicProp>(decoy)!;
            shotTime=Server.CurrentTime;
            HitClone(prop,Pawn(),DamageTypes_t.DMG_BULLET); HitClone(prop,Pawn(),DamageTypes_t.DMG_BULLET);
        });
        UpdateAt(15,"Shot clone produced exactly one native flashbang detonation",()=> {
            Require(CloneHits.Count==0 && Players.Entity<CDynamicProp>(decoy) is null && Players.Entity<CDynamicProp>(weaponCopy) is null && flashes==1 && flashTime-shotTime<.4f,$"clones={CloneHits.Count}, flashes={flashes}, delay={flashTime-shotTime}");
        });
        UpdateAt(16,"A second clone can be tested for natural expiry",()=> {
            Powers.NativeProbePair("decoy_clone","cluster_grenade"); ActivateClone();
        });
        UpdateAt(25.5f,"An untouched clone remains alive before its ten seconds expire",()=>Require(CloneHits.ContainsKey(decoy),"expired early"));
        UpdateAt(26.5f,"An untouched clone disappears after ten seconds without flashing",()=>Require(CloneHits.Count==0 && Players.Entity<CDynamicProp>(decoy) is null && flashes==1,"clone lifetime or silent expiry failed"));
        UpdateAt(39,"Cluster Grenade has not refunded early",()=>Require(!Has(Ct(),"incgrenade"),"early refund"));
        UpdateAt(41,"Cluster Grenade returns the original incendiary type after 30 seconds",()=>Require(Has(Ct(),"incgrenade"),"wrong or missing refund"));
        UpdateAt(41.1f,"One throw earns one refund and power replacement cancels pending grenades",()=> {
            Require(!Has(Combo(),"smokegrenade") && !Has(Replaced(),"decoy"),"duplicate or stale refund");
        });
        AddTimer(42,FinishUpdateProbe,TimerFlags.STOP_ON_MAPCHANGE);
    }

    private static unsafe void HitClone(CBaseEntity clone,CCSPlayerPawn attacker,DamageTypes_t type)
    {
        var infoMemory=NativeMemory.AllocZeroed((nuint)Schema.GetClassSize("CTakeDamageInfo"));
        var resultMemory=NativeMemory.AllocZeroed((nuint)Schema.GetClassSize("CTakeDamageResult"));
        try
        {
            var info=new CTakeDamageInfo((nint)infoMemory);
            var result=new CTakeDamageResult((nint)resultMemory);
            info.Attacker.Raw=attacker.EntityHandle.Raw; info.Inflictor.Raw=attacker.EntityHandle.Raw;
            info.Damage=10; info.OriginalDamage=10; info.BitsDamageType=type;
            Schema.SetSchemaValue(result.Handle,"CTakeDamageResult","m_pOriginatingInfo",info.Handle);
            VirtualFunctions.CBaseEntity_TakeDamageOld(clone,info,result);
        }
        finally { NativeMemory.Free(infoMemory); NativeMemory.Free(resultMemory); }
    }
}
#endif
