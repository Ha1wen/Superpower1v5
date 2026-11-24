#if SERVER_SMOKE
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Powers;
using Superpower1v5.Powers.Active;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private void UpdateAt(float time,string name,Action test) => AddTimer(time,()=>Probe(name,test),TimerFlags.STOP_ON_MAPCHANGE);
    private void FinishUpdateProbe()
    {
        File.WriteAllText(Path.Combine(ProbeDirectory,"native-probe-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
        Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit"));
        Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
    }
    private void BootstrapUpdateBots()
    {
        foreach (var entry in Config.Powers) entry.Value.Enabled=entry.Key is "teleport" or "infinite_ammo";
        StopProbeBots(true);
        if (!Mode.IsDeathmatch) ConVar<int>.Find("mp_ignore_round_win_conditions")!.ValueAsString="1";
        AddTimer(2,()=> {
            Server.ExecuteCommand("bot_join_after_player 0; bot_join_team any; bot_quota_mode normal; bot_quota 6");
            if (!Mode.IsDeathmatch) Server.ExecuteCommand("mp_warmup_end; mp_restartgame 1");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void ScheduleAbilityUpdateProbe()
    {
        BootstrapUpdateBots();
        PowerContext Context() => new(this,Mode.Boss!.EntityHandle.Raw);
        Vector? history=null; Vector? firePosition=null; CBasePlayerWeapon? clockWeapon=null; int originalClock=0;
        CBasePlayerWeapon? knife=null, carriedBomb=null;
        if (ProbeDirectory.Contains("bomb-transmit",StringComparison.OrdinalIgnoreCase))
        {
            var sawVisible=0;
            var sawHidden=0;
            var leaked=0;
            RegisterListener<Listeners.CheckTransmit>(clients=> {
                if (carriedBomb is not { IsValid:true } bomb) return;
                foreach (var (info,viewer) in clients)
                {
                    if (viewer is not { IsHLTV:true }) continue;
                    var raw=viewer.EntityHandle.Raw;
                    var hidden=Effects.HideCarriedBombFrom(raw,bomb.Index);
                    var always=System.Runtime.CompilerServices.Unsafe.BitCast<CFixedBitVecBase,nint>(info.TransmitAlways)!=0 &&
                        info.TransmitAlways.Contains(bomb.Index);
                    var transmitted=info.TransmitEntities.Contains(bomb.Index);
                    if (hidden)
                    {
                        Interlocked.Exchange(ref sawHidden,1);
                        if (transmitted || always) Interlocked.Exchange(ref leaked,1);
                    }
                    else if (transmitted) Interlocked.Exchange(ref sawVisible,1);
                }
            });
            UpdateAt(18,"Native SourceTV snapshots withhold C4 during invisibility and send it after reveal",()=> {
                Require(sawVisible==1 && sawHidden==1 && leaked==0,$"visible={sawVisible}, hidden={sawHidden}, leaked={leaked}");
            });
        }
        UpdateAt(10,"One T boss and five CT opponents run with the expanded catalog",()=> {
            Require(Mode.Live && Mode.Boss is not null && Mode.Opponents.Count()==5,"1v5 roster not live");
            foreach (var player in Mode.Eligible())
            {
                var pawn=Players.AlivePawn(player)!; pawn.MoveType=MoveType_t.MOVETYPE_NONE; pawn.ActualMoveType=MoveType_t.MOVETYPE_NONE;
                pawn.Teleport(null,null,new Vector(0,0,0));
            }
            foreach (var definition in Models.PowerCatalog.All)
            { Powers.NativeProbePair(definition.Id,definition.Id=="infinite_ammo" ? "teleport" : "infinite_ammo"); Powers.Tick(); Powers.PostThink(); }
            Require(Models.PowerCatalog.All.Count==24 && Config.Enabled,"new catalog apply failed");
        });
        UpdateAt(11,"Tiny scales its body and lowers standing and crouched aim height",()=> {
            Powers.NativeProbePair("teleport","infinite_ammo"); var pawn=Context().Pawn!; var standing=pawn.ViewOffset.Z;
            Powers.NativeProbePair("tiny","infinite_ammo"); var node=pawn.CBodyComponent!.SceneNode!;
            var movement=pawn.MovementServices!.As<CCSPlayer_MovementServices>(); var duck=movement.DuckAmount;
            movement.DuckAmount=0; Powers.Tick(); Powers.PostThink();
            Require(Math.Abs(node.Scale-.5f)<.01 && Math.Abs(node.ClientLocalScale-1)<.01,$"scale={node.Scale}, client={node.ClientLocalScale}");
            Require(Math.Abs(pawn.ViewOffset.Z-standing*.5f)<.01,$"standing eye {pawn.ViewOffset.Z}, baseline {standing}");
            movement.DuckAmount=1; pawn.ViewOffset.Z=46; Powers.Tick(); Powers.PostThink(); Powers.Fire(Mode.Boss);
            Require(Math.Abs(pawn.ViewOffset.Z-23)<.01,"crouch firing height"); movement.DuckAmount=duck; pawn.ViewOffset.Z=standing;
            Powers.NativeProbePair("teleport","infinite_ammo"); Require(Math.Abs(node.Scale-1)<.01 && Math.Abs(pawn.ViewOffset.Z-standing)<.01,"tiny cleanup");
        });
        UpdateAt(12,"Rapid Fire clears server recoil and camera punch without a global recoil change",()=> {
            var scale=ConVar<float>.Find("weapon_recoil_scale")?.Value;
            Powers.NativeProbePair("rapid_fire","infinite_ammo"); var pawn=Context().Pawn!;
            Mode.Boss!.RemoveWeapons();
            var rifle=Mode.Boss.GiveNamedItem<CBasePlayerWeapon>("weapon_ak47")!;
            pawn.WeaponServices!.ActiveWeapon.Raw=rifle.EntityHandle.Raw;
            pawn.AimPunchServices!.PredictableBaseAngle.X=15; pawn.AimPunchServices.PredictableBaseAngleVel.Y=8;
            pawn.AimPunchServices.UnpredictableBaseAngle.X=10; pawn.CameraServices!.CsViewPunchAngle.X=20;
            var weapon=pawn.WeaponServices!.ActiveWeapon.Value!.As<CCSWeaponBase>(); weapon.FlRecoilIndex=10; weapon.IRecoilIndex=10;
            Powers.Tick(); Powers.PostThink();
            Require(pawn.AimPunchServices.PredictableBaseAngle.X==0 && pawn.AimPunchServices.PredictableBaseAngleVel.Y==0 && pawn.AimPunchServices.UnpredictableBaseAngle.X==0 && pawn.CameraServices.CsViewPunchAngle.X==0 && weapon.FlRecoilIndex==0,
                $"recoil not cleared: base={pawn.AimPunchServices.PredictableBaseAngle.X}, vel={pawn.AimPunchServices.PredictableBaseAngleVel.Y}, hit={pawn.AimPunchServices.UnpredictableBaseAngle.X}, camera={pawn.CameraServices.CsViewPunchAngle.X}, weapon={weapon.DesignerName}:{weapon.FlRecoilIndex}");
            Require(ConVar<float>.Find("weapon_recoil_scale")?.Value==scale,"global recoil changed");
            clockWeapon=weapon; originalClock=Server.TickCount+30; weapon.NextPrimaryAttackTick=originalClock; Powers.Fire(Mode.Boss);
        });
        UpdateAt(12.15f,"Rapid Fire still shortens the next firing delay",()=>Require(clockWeapon is { IsValid:true } && clockWeapon.NextPrimaryAttackTick<originalClock,"firing delay unchanged"));
        UpdateAt(13,"Random Teleport lands at a different clear place and starts its cooldown",()=> {
            Powers.NativeProbePair("random_teleport","infinite_ammo"); var pawn=Context().Pawn!; var before=Players.Copy(pawn.AbsOrigin!);
            Powers.Activate(0,Mode.Boss);
            Require(Players.Distance(before,pawn.AbsOrigin!)>=256 && Powers.Loadout!.Remaining("random_teleport",Server.CurrentTime)>19,"random teleport or cooldown");
            var clear=GameTrace.TraceHullShape(pawn.AbsOrigin!,pawn.AbsOrigin!,pawn.Collision.Mins,pawn.Collision.Maxs,pawn,new() { InteractsWith=Masks.PlayerSolid });
            Require(!clear.IsAllSolid && !clear.DidHit(),"random destination blocked");
            var at=Players.Copy(pawn.AbsOrigin!); Powers.Activate(0,Mode.Boss); Require(Players.Distance(at,pawn.AbsOrigin!)<1,"cooldown bypass");
        });
        UpdateAt(14,"Invisibility starts a safe render-only fade",()=> {
            Config.InvisibilityQuietDelay=0; Config.InvisibilityFadeDuration=.3f;
            knife=Mode.Boss!.GiveNamedItem<CBasePlayerWeapon>("weapon_knife");
            carriedBomb=Mode.Boss.GiveNamedItem<CBasePlayerWeapon>("weapon_c4");
            Powers.NativeProbePair("invisibility","infinite_ammo");
        });
        UpdateAt(14.15f,"C4 visuals stop transmitting as soon as the carrier starts fading",()=> {
            Powers.SlowTick();
            var pawn=Context().Pawn!;
            Require(pawn.Render.A is >0 and <255 && Effects.HideCarriedBombFrom(Mode.Opponents.First().EntityHandle.Raw,carriedBomb!.Index),
                $"carrier alpha={pawn.Render.A}, bomb still visible during fade");
        });
        UpdateAt(14.4f,"Silent invisibility hides model/shadow and suppresses engine radar spotting",()=> {
            Powers.SlowTick(); var pawn=Context().Pawn!;
            Require(pawn.Render.A==0 && pawn.ShadowStrength==0 && Powers.IsConcealed(Mode.Boss),"not concealed");
            Require(knife is { IsValid:true,RenderMode:RenderMode_t.kRenderNone } &&
                carriedBomb is { IsValid:true,RenderMode:RenderMode_t.kRenderNone } &&
                (pawn.Effects&(1u<<10))!=0,"carried knife or C4 still drawn");
            pawn.EntitySpottedState.Spotted=true; pawn.EntitySpottedState.SpottedByMask[0]=uint.MaxValue; pawn.EntitySpottedState.SpottedByMask[1]=uint.MaxValue;
            Powers.Tick(); Powers.PostThink();
            Require(!pawn.EntitySpottedState.Spotted && pawn.EntitySpottedState.SpottedByMask[0]==0 && pawn.EntitySpottedState.SpottedByMask[1]==0,"radar leak");
        });
        UpdateAt(14.6f,"Concealment filters the carried C4, charm model and linked light for other viewers",()=> {
            var carrier=Mode.Boss!;
            var viewer=Mode.Opponents.First();
            var bomb=carriedBomb!;
            var light=Utilities.CreateEntityByName<CBaseEntity>("info_particle_system")!;
            var charm=Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic")!;
            light.DispatchSpawn();
            using var keys=new CEntityKeyValues();
            keys.SetString("model",Context().Pawn!.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName);
            keys.SetInt("solid",0);
            charm.DispatchSpawn(keys);
            var previousEffect=bomb.EffectEntity.Raw;
            try
            {
                bomb.EffectEntity.Raw=light.EntityHandle.Raw;
                charm.AcceptInput("SetParent",bomb,charm,"!activator");
                Powers.SlowTick();
                var other=viewer.EntityHandle.Raw;
                var self=carrier.EntityHandle.Raw;
                Require(Effects.HideCarriedBombFrom(other,bomb.Index) && Effects.HideCarriedBombFrom(other,light.Index) &&
                    Effects.HideCarriedBombFrom(other,charm.Index),"carried C4, charm or light could still reach another viewer");
                Require(!Effects.HideCarriedBombFrom(self,bomb.Index) && !Effects.HideCarriedBombFrom(self,light.Index) &&
                    !Effects.HideCarriedBombFrom(other,Context().Pawn!.Index),"concealment removed the owner inventory or player pawn");
            }
            finally
            {
                bomb.EffectEntity.Raw=previousEffect;
                charm.Remove();
                light.Remove();
            }
        });
        UpdateAt(15,"A planted bomb and unrelated owner-linked entities remain visible",()=> {
            var pawn=Context().Pawn!;
            var bomb=Utilities.CreateEntityByName<CPlantedC4>("planted_c4")!;
            bomb.OwnerEntity.Raw=pawn.EntityHandle.Raw; bomb.Teleport(pawn.AbsOrigin,null,null); bomb.DispatchSpawn();
            try
            {
                bomb.BombTicking=false; bomb.BombDefused=true;
                Powers.SlowTick();
                Require(!Effects.AttachedEntities(pawn).Any(e=>e.EntityHandle.Raw==bomb.EntityHandle.Raw) && bomb.Render.A==255 &&
                    !Effects.HideCarriedBombFrom(Mode.Opponents.First().EntityHandle.Raw,bomb.Index),"planted bomb hidden");
                new EventBombPlanted(true) { Userid=Mode.Boss }.FireEvent(false);
                Require(pawn.Render.A==255 && !Powers.IsConcealed(Mode.Boss) &&
                    !Effects.HideCarriedBombFrom(Mode.Opponents.First().EntityHandle.Raw,carriedBomb!.Index),"plant did not reveal");
                Require(knife?.RenderMode!=RenderMode_t.kRenderNone && carriedBomb?.RenderMode!=RenderMode_t.kRenderNone,"carried model did not restore");
            }
            finally { if (bomb.IsValid) bomb.Remove(); }
        });
        UpdateAt(16,"Invisibility survives repeated noisy/quiet transitions with a weapon swap",()=> {
            for (var n=0;n<30;n++) { Powers.Noise(Mode.Boss); Powers.SlowTick(); }
            Mode.Boss!.GiveNamedItem<CBasePlayerWeapon>("weapon_ak47");
            Require(Config.Enabled && Context().Pawn!.Render.A==255,"noise/swap failure");
        });
        UpdateAt(17,"Wall Vision cannot put a silent invisible player back on radar",()=> {
            Powers.SlowTick(); var ct=Mode.Opponents.First();
            Powers.NativeProbePair("wall_vision","infinite_ammo",player:ct); Powers.SlowTick(); Powers.PostThink();
            var pawn=Context().Pawn!;
            Require(Powers.IsConcealed(Mode.Boss) && !pawn.EntitySpottedState.Spotted && Powers.GetEffects(ct).OutlineCount==0,"wall vision exposed invisible player");
            Powers.NativeProbePair("teleport","infinite_ammo",player:ct);
            Powers.NativeProbePair("second_chance","teleport"); history=Players.Copy(Context().Pawn!.AbsOrigin!);
        });
        UpdateAt(20.3f,"Second Chance blocks a lethal hit immediately after Teleport and rejects overlapping blink",()=> {
            var pawn=Context().Pawn!; var moved=false;
            for (var yaw=0;yaw<360 && !moved;yaw+=30) { pawn.EyeAngles.X=0; pawn.EyeAngles.Y=yaw; moved=SafeTeleport.TryBlink(pawn,0); }
            Require(moved,"no open teleport direction"); var blink=Players.Copy(pawn.AbsOrigin!);
            Damage.NativeProbeHit(Mode.Boss!.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,200,DamageTypes_t.DMG_GENERIC);
            Require(pawn.Health>0 && Powers.Loadout!.Consumed.Contains("second_chance") && Powers.IsRecovering(Mode.Boss),"lethal protection failed");
            Require(Players.Distance(blink,pawn.AbsOrigin!)<1,"rewind moved inside damage callback");
            Require(!Powers.Active.OfType<Teleport>().Single().Activate(Context()),"teleport interrupted recovery");
        });
        UpdateAt(20.6f,"Deferred Second Chance returns to the pre-teleport historical position",()=> {
            Require(Context().Pawn is { } pawn && history is not null && Players.Distance(pawn.AbsOrigin!,history)<40 && Config.Enabled,"rewind position/liveness");
        });
        UpdateAt(22,"Second Chance handles an explicitly populated lethal head hitgroup",()=> {
            Powers.NativeProbePair("second_chance","teleport"); var pawn=Context().Pawn!; pawn.Health=100; pawn.ArmorValue=0;
            Damage.NativeProbeHit(Mode.Boss!.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,35,DamageTypes_t.DMG_BULLET,HitGroup_t.HITGROUP_HEAD);
            Require(pawn.Health>0 && Powers.Loadout!.Consumed.Contains("second_chance"),"known head hitgroup bypassed protection");
        });
        UpdateAt(23,"Forcefield blocks lethal damage before consuming Second Chance",()=> {
            Powers.NativeProbePair("second_chance","forcefield"); Powers.Activate(1,Mode.Boss); var pawn=Context().Pawn!;
            pawn.Health=100;
            Damage.NativeProbeHit(Mode.Boss!.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,200,DamageTypes_t.DMG_GENERIC);
            Require(pawn.Health==100 && !Powers.Loadout!.Consumed.Contains("second_chance") && Powers.IsInvulnerable(Mode.Boss),"forcefield priority/protection");
            Require(Powers.Loadout!.Remaining("forcefield",Server.CurrentTime)>34,"forcefield cooldown");
        });
        UpdateAt(33.3f,"Forcefield expires after ten seconds while its cooldown continues",()=> {
            Require(!Powers.IsInvulnerable(Mode.Boss) && Powers.Loadout!.Remaining("forcefield",Server.CurrentTime)>20,"duration/cooldown");
            var pawn=Context().Pawn!; pawn.Health=100;
            Damage.NativeProbeHit(Mode.Boss!.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,10,DamageTypes_t.DMG_GENERIC);
            Require(pawn.Health==90,"forcefield leaked after expiration");
        });
        UpdateAt(34,"Forcefield removal immediately restores vulnerability",()=> {
            Powers.NativeProbePair("forcefield","infinite_ammo"); Powers.Activate(0,Mode.Boss);
            Powers.NativeProbePair("teleport","infinite_ammo"); var pawn=Context().Pawn!; pawn.Health=100;
            Damage.NativeProbeHit(Mode.Boss!.EntityHandle.Raw,Mode.Opponents.First().EntityHandle.Raw,10,DamageTypes_t.DMG_GENERIC);
            Require(pawn.Health==90 && !Powers.IsInvulnerable(Mode.Boss),"removed shield leaked");
        });
        UpdateAt(35,"Infinite Jump preserves horizontal velocity and requires another press for each air jump",()=> {
            Powers.NativeProbePair("infinite_jump","infinite_ammo"); var pawn=Context().Pawn!;
            pawn.MoveType=MoveType_t.MOVETYPE_WALK; pawn.ActualMoveType=MoveType_t.MOVETYPE_WALK; pawn.Flags&=~1u;
            var buttons=pawn.MovementServices!.Buttons.ButtonStates;
            buttons[0]=0; Powers.Tick(); pawn.Teleport(null,null,new Vector(100,20,-200)); buttons[0]=(ulong)PlayerButtons.Jump; Powers.Tick();
            Require(Math.Abs(pawn.AbsVelocity.Z-301.99f)<.1 && Math.Abs(pawn.AbsVelocity.X-100)<.1,"first air jump");
            pawn.Teleport(null,null,new Vector(100,20,-50)); Powers.Tick(); Require(pawn.AbsVelocity.Z==-50,"held jump repeatedly boosted");
            buttons[0]=0; Powers.Tick(); buttons[0]=(ulong)PlayerButtons.Jump; Powers.Tick(); Require(pawn.AbsVelocity.Z>300,"second air jump");
            buttons[0]=0; pawn.MoveType=MoveType_t.MOVETYPE_NONE; pawn.ActualMoveType=MoveType_t.MOVETYPE_NONE;
        });
        UpdateAt(36,"Pyromaniac creates a real armed Molotov at a floor impact",()=> {
            Powers.NativeProbePair("pyromaniac","cluster_grenade"); var target=Players.AlivePawn(Mode.Opponents.First())!;
            var pos=target.AbsOrigin!; var floor=GameTrace.TraceEndShape(new(pos.X,pos.Y,pos.Z+16),new(pos.X,pos.Y,pos.Z-128),target,new() { InteractsWith=Masks.SolidBrushOnly });
            Require(floor.DidHit(),"no floor for fire"); firePosition=Players.Copy(floor.EndPos); target.Health=100;
            Powers.Impact(firePosition.X,firePosition.Y,firePosition.Z,Mode.Boss);
            Require(((Pyromaniac)Powers.Active[0]).PoolCount==1,"no armed fire pool");
        });
        UpdateAt(37.5f,"Pyromaniac spreads native inferno flames, damages enemies and avoids cluster recursion",()=> {
            var pyro=(Pyromaniac)Powers.Active[0];
            Require(pyro.InfernoCount==1 && Utilities.FindAllEntitiesByDesignerName<CInferno>("inferno").Any(f=>f.FireCount>1),"native spreading inferno missing");
            Require(Players.AlivePawn(Mode.Opponents.First()) is null || Players.AlivePawn(Mode.Opponents.First())!.Health<100,"native fire caused no damage");
            Require(((ClusterGrenade)Powers.Active[1]).ChildCount==0,"generated molotov split recursively");
            var smoke=GrenadeFactory.Create("smokegrenade_projectile",new(firePosition!.X,firePosition.Y,firePosition.Z+8),new Vector(0,0,0),Context().Pawn!,45)!;
            smoke.DetonateTime=Server.CurrentTime+.1f; TrackEntity(smoke.EntityHandle.Raw); SuppressCluster(smoke.EntityHandle.Raw);
        });
        UpdateAt(40,"Native smoke extinguishes Pyromaniac and owned fire cleans up",()=> {
            Require(((Pyromaniac)Powers.Active[0]).PoolCount==0,"fire survived smoke");
            Powers.NativeProbePair("teleport","infinite_ammo");
            Require(!Utilities.FindAllEntitiesByDesignerName<CInferno>("inferno").Any(),"owned fire leaked after cleanup");
        });
        UpdateAt(41,"Cluster Grenade adds four copies and preserves the original velocity",()=> {
            Powers.NativeProbePair("cluster_grenade","infinite_ammo");
            var pawn=Context().Pawn!; var feet=pawn.AbsOrigin!;
            var original=GrenadeFactory.Create("hegrenade_projectile",new(feet.X,feet.Y,feet.Z+72),new(200,0,100),pawn)!;
            try
            {
                var velocity=Players.Copy(original.AbsVelocity);
                Powers.Projectile(original); Powers.Projectile(original);
                Require(((ClusterGrenade)Powers.Active[0]).ChildCount==4,"wrong copy count");
                Require(Players.Distance(velocity,original.AbsVelocity)<.01f,"original trajectory altered");
            }
            finally { Powers.NativeProbePair("teleport","infinite_ammo"); original.Remove(); }
        });
        UpdateAt(42,"Random Swap trades the owner's location with one other living player",()=> {
            Powers.NativeProbePair("random_swap","infinite_ammo");
            var owner=Context().Pawn!; var origin=Players.Copy(owner.AbsOrigin!);
            var candidates=Mode.Eligible().Select(Players.AlivePawn).OfType<CCSPlayerPawn>()
                .Where(p=>p.Handle!=owner.Handle).Select(p=>(pawn:p,origin:Players.Copy(p.AbsOrigin!))).ToArray();
            Powers.Activate(0,Mode.Boss);
            Require(candidates.Count(p=>Players.Distance(p.pawn.AbsOrigin!,origin)<.5f &&
                Players.Distance(owner.AbsOrigin!,p.origin)<.5f)==1 &&
                Powers.Loadout!.Remaining("random_swap",Server.CurrentTime)>19,"swap or cooldown failed");
        });
        CCSPlayerPawn? wallbangVictim=null;
        float wallbangDamage=0;
        UpdateAt(43,"Unlimited Wallbang traces a bullet ray through a solid map wall",()=> {
            Powers.NativeProbePair("unlimited_wallbang","infinite_ammo");
            var player=Mode.Boss!; var shooter=Context().Pawn!;
            var victim=Players.AlivePawn(Mode.Opponents.First(p=>Players.AlivePawn(p) is not null))!;
            var rifle=player.GiveNamedItem<CBasePlayerWeapon>("weapon_ak47")!;
            shooter.WeaponServices!.ActiveWeapon.Raw=rifle.EntityHandle.Raw;
            var tSpawns=Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_player_terrorist");
            var ctSpawns=Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_player_counterterrorist");
            var pair=(from t in tSpawns from ct in ctSpawns
                let start=new Vector(t.AbsOrigin!.X,t.AbsOrigin.Y,t.AbsOrigin.Z+shooter.ViewOffset.Z)
                let end=new Vector(ct.AbsOrigin!.X,ct.AbsOrigin.Y,ct.AbsOrigin.Z+40)
                let trace=GameTrace.TraceEndShape(start,end,shooter,new() { InteractsWith=Masks.ShotBrushOnly })
                where trace.DidHit() && !trace.IsAllSolid && trace.Fraction is >.04f and <.85f
                select (origin:t.AbsOrigin,destination:ct.AbsOrigin)).First();
            shooter.Teleport(Players.Copy(pair.origin!),null,new Vector(0,0,0));
            victim.Teleport(Players.Copy(pair.destination!),null,new Vector(0,0,0));
            victim.Health=100; victim.ArmorValue=0;
            wallbangVictim=victim;
        });
        UpdateAt(43.5f,"Unlimited Wallbang uses the shot's actual impact direction",()=> {
            var shooter=Context().Pawn!; var origin=shooter.AbsOrigin!;
            var start=new Vector(origin.X,origin.Y,origin.Z+shooter.ViewOffset.Z);
            var destination=wallbangVictim!.AbsOrigin!;
            var end=new Vector(destination.X,destination.Y,destination.Z+40);
            var world=GameTrace.TraceEndShape(start,end,shooter,new() { InteractsWith=Masks.ShotBrushOnly });
            Require(world.DidHit() && !world.IsAllSolid,"wall does not block the shot");
            var hit=GameTrace.TraceEndShape(start,end,shooter,new() { InteractsWith=Contents.Player|Contents.Hitbox });
            Require(hit.HitEntity().Handle==wallbangVictim.Handle,"ray does not reach the intended victim");
            var weapon=shooter.WeaponServices!.ActiveWeapon.Value!.As<CCSWeaponBase>().VData!;
            var rangeDamage=UnlimitedWallbang.DamageAtRange(weapon.Damage,weapon.RangeModifier,Players.Distance(start,hit.EndPos));
            wallbangDamage=Models.BulletHealthDamage.Estimate(rangeDamage,UnlimitedWallbang.HitGroup(hit),weapon.HeadshotMultiplier,1,0,false,weapon.ArmorRatio);
            for (var i=0;i<2;i++) Powers.Impact(world.EndPos.X,world.EndPos.Y,world.EndPos.Z,Mode.Boss);
        });
        UpdateAt(44,"A blocked rifle shot deals full range-adjusted damage once",()=> {
            Require(wallbangVictim is not null && Math.Abs(100-wallbangVictim.Health-wallbangDamage)<2,
                $"expected about {wallbangDamage} damage, got {100-wallbangVictim?.Health}");
        });
        UpdateAt(44.1f,"Wallbang fallback uses normal headshot and armor scaling",()=> {
            var victim=wallbangVictim!; var target=Players.Controller(victim)!.EntityHandle.Raw;
            var source=Mode.Boss!.EntityHandle.Raw;
            var weapon=Context().Pawn!.WeaponServices!.ActiveWeapon.Value!.As<CCSWeaponBase>().VData!;
            victim.Health=100; victim.ArmorValue=0;
            Damage.DealBullet(target,source,10,HitGroup_t.HITGROUP_HEAD);
            var head=100-victim.Health;
            victim.Health=100; victim.ArmorValue=100;
            Damage.DealBullet(target,source,40,HitGroup_t.HITGROUP_CHEST);
            var chest=100-victim.Health; var armor=100-victim.ArmorValue;
            victim.Health=100; victim.ArmorValue=0;
            Require(Math.Abs(head-10*weapon.HeadshotMultiplier)<2 && Math.Abs(chest-20*weapon.ArmorRatio)<2 && armor is 4 or 5,
                $"head damage={head}, armored chest damage={chest}, armor spent={armor}, armor ratio={weapon.ArmorRatio}");
        });
        UpdateAt(44.3f,"Sandevistan still freezes opponents without spawning a screen fade",()=> {
            var fades=0;
            void ObserveFade(CEntityInstance entity) { if (entity.DesignerName=="env_fade") fades++; }
            var power=new Sandevistan(); var context=Context();
            RegisterListener<Listeners.OnEntitySpawned>(ObserveFade);
            try
            {
                Require(power.Activate(context) && power.FrozenCount>0,"opponents were not frozen");
                power.Cleanup(context);
                Require(fades==0,"Sandevistan spawned a screen fade");
            }
            finally
            {
                power.Cleanup(context);
                RemoveListener<Listeners.OnEntitySpawned>(ObserveFade);
            }
        });
        AddTimer(45,FinishUpdateProbe,TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void ScheduleDeathmatchUpdateProbe()
    {
        BootstrapUpdateBots();
        StopProbeBots(false);
        ConVar<int>.Find("bot_dont_shoot")!.ValueAsString="1";
        AddTimer(4,()=> { foreach (var player in Mode.Eligible()) player.Respawn(); },TimerFlags.STOP_ON_MAPCHANGE);
        uint playerRaw=0; string[] old=[];
        UpdateAt(10,"Native deathmatch gives every active player a private pair without a T boss",()=> {
            Require(Mode.IsDeathmatch && Mode.Live && Mode.Boss is null,"native deathmatch not active");
            Require(Mode.Eligible().Count>=2 && Mode.Eligible().All(p=>Powers.GetLoadout(p)?.Powers.Count==2),"missing deathmatch loadouts");
            foreach (var entry in Config.Powers) entry.Value.Enabled=entry.Key is "teleport" or "infinite_ammo" or "decoy_clone";
            foreach (var p in Mode.Eligible()) Logger.LogInformation("DM PROBE {Player}: hp={HP}, life={Life}, pos={Position}",p.PlayerName,p.PlayerPawn.Value?.Health,p.PlayerPawn.Value?.LifeState,p.PlayerPawn.Value?.AbsOrigin);
            var player=Mode.Eligible().First(p=>Players.AlivePawn(p) is not null); playerRaw=player.EntityHandle.Raw;
            Powers.NativeProbePair("teleport","infinite_ammo",player:player);
            Powers.GetLoadout(player)!.StartCooldown("teleport",Server.CurrentTime,500); Powers.GetLoadout(player)!.Consumed.Add("second_chance");
            old=Powers.GetLoadout(player)!.Powers.Select(p=>p.Id).ToArray();
            Players.AlivePawn(player)!.CommitSuicide(false,true);
            Require(Powers.GetActive(player).Count==0,"death retained active effects");
        });
        UpdateAt(14,"Deathmatch respawn rerolls the pair and resets cooldowns/one-use state",()=> {
            var player=Players.Resolve(playerRaw)!;
            Require(Players.AlivePawn(player) is not null,"native deathmatch did not respawn");
            var loadout=Powers.GetLoadout(player);
            Require(loadout is not null && !loadout.Powers.Select(p=>p.Id).ToHashSet().SetEquals(old),"old pair retained");
            Require(loadout!.Remaining("teleport",Server.CurrentTime)==0 && !loadout.Consumed.Contains("second_chance"),"previous-life state leaked");
            Require(Mode.Boss is null && Config.Enabled,"deathmatch became a boss round");
        });
        AddTimer(15,FinishUpdateProbe,TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
