#if SERVER_SMOKE
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Superpower1v5.Powers.Active;
using Superpower1v5.Powers.Combat;
using Superpower1v5.Support;

namespace Superpower1v5;
public sealed partial class Plugin
{
    private void ScheduleRoundOptionsProbe()
    {
        foreach (var entry in Config.Powers) entry.Value.Enabled=entry.Key is "teleport" or "infinite_ammo";
        StopProbeBots(true);
        uint ctRaw=0,spectatorRaw=0; string? firstBoss=null; CCSWeaponBase? reloadWeapon=null; bool keepReloading=false;
        bool radarSpotted=false; uint radar0=0,radar1=0;
        CCSPlayerController Ct() => Players.Resolve(ctRaw)!;
        void At(float time,string name,Action action) => AddTimer(time,()=>Probe(name,action),TimerFlags.STOP_ON_MAPCHANGE);
        RegisterListener<Listeners.OnTick>(()=> { if (keepReloading && reloadWeapon is { IsValid:true }) reloadWeapon.InReload=true; });
        At(12,"Server command enables powers for everyone",()=> {
            Require(Mode.Live && Mode.Opponents.Count()==5,"no live six-player round");
            firstBoss=Players.Key(Mode.Boss!); ctRaw=Mode.Opponents.First().EntityHandle.Raw;
            Server.ExecuteCommand("css_sp_everyone 1");
        });
        At(13,"Six players own distinct pairs and separate loadout objects; setting saved",()=> {
            Require(Config.EveryoneHasPowers && Powers.PoweredPlayers.Count()==6,"not everyone powered");
            var owners=Mode.Eligible();
            foreach (var owner in owners)
            {
                var pair=Powers.GetLoadout(owner)!.Powers;
                Require(pair.Count==2 && pair[0].Id!=pair[1].Id,"invalid pair");
                Powers.NativeProbePair("teleport","infinite_ammo",player:owner);
            }
            Require(!ReferenceEquals(Powers.Loadout,Powers.GetLoadout(Ct())),"shared loadout");
            var path=Path.Combine(Server.GameDirectory,"csgo/addons/counterstrikesharp/configs/plugins/Superpower1v5/Superpower1v5.json");
            Require(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("EveryoneHasPowers").GetBoolean(),"setting not persisted");
            foreach (var entry in Config.Powers) entry.Value.Enabled=true;
            Powers.GetLoadout(Ct())!.StartCooldown("teleport",Server.CurrentTime,15);
            Require(Powers.GetLoadout(Ct())!.Remaining("teleport",Server.CurrentTime)>14 && Powers.Loadout!.Remaining("teleport",Server.CurrentTime)==0,"shared cooldown");
        });
        At(13.3f,"CT Vampirism heals its owner from damage to the boss",()=> {
            Powers.NativeProbePair("vampirism","infinite_ammo",player:Ct());
            var pawn=Players.AlivePawn(Ct())!; pawn.Health=50;
            Damage.NativeProbeHit(Mode.Boss!.EntityHandle.Raw,ctRaw,20,DamageTypes_t.DMG_BULLET);
            Require(pawn.Health==70,"wrong heal owner"); Players.Heal(Mode.Boss,100); Players.Heal(Ct(),100);
        });
        At(13.6f,"Removing boss movement powers does not remove CT movement powers",()=> {
            Powers.NativeProbePair("super_speed","low_gravity"); Powers.NativeProbePair("super_speed","low_gravity",player:Ct());
            var pawn=Players.AlivePawn(Ct())!; var speed=pawn.VelocityModifier; var gravity=pawn.GravityScale;
            Powers.NativeProbePair("teleport","infinite_ammo");
            Require(Math.Abs(pawn.VelocityModifier-speed)<.001 && Math.Abs(pawn.GravityScale-gravity)<.001,"cross-owner cleanup");
            Powers.NativeProbePair("juggernaut","infinite_ammo",player:Ct()); Require(pawn.Health==500 && pawn.MaxHealth==500,"CT juggernaut");
        });
        At(14,"CT Second Chance prevents its own lethal damage and retains separate consumption",()=> {
            Powers.NativeProbePair("second_chance","infinite_ammo",player:Ct());
            Damage.NativeProbeHit(ctRaw,Mode.Boss!.EntityHandle.Raw,200,DamageTypes_t.DMG_GENERIC);
            Require(Players.AlivePawn(Ct()) is { Health:100 } && Powers.GetLoadout(Ct())!.Consumed.Contains("second_chance"),"CT lethal protection");
            Require(!Powers.Loadout!.Consumed.Contains("second_chance"),"boss consumption changed");
        });
        At(13.8f,"CT activated abilities freeze only enemies and use the CT player's own cooldown",()=> {
            Powers.NativeProbePair("sandevistan","teleport",player:Ct()); Powers.Activate(0,Ct());
            Require(((Sandevistan)Powers.GetActive(Ct())[0]).FrozenCount==1,"wrong enemies frozen");
            Require(Powers.GetLoadout(Ct())!.Remaining("sandevistan",Server.CurrentTime)>34 && Powers.Loadout!.Remaining("sandevistan",Server.CurrentTime)==0,"activation cooldown not owned");
        });
        At(14.1f,"Moving a powered player to spectators clears their effects",()=> {
            var player=Mode.Opponents.Skip(1).First(); spectatorRaw=player.EntityHandle.Raw;
            Powers.NativeProbePair("super_speed","low_gravity",player:player);
            player.ChangeTeam(CsTeam.Spectator);
        });
        At(14.2f,"A spectator's old pawn cannot regain powers on the next tick",()=> {
            var player=Players.Resolve(spectatorRaw)!; Powers.Tick();
            Require(player.Team==CsTeam.Spectator && !Mode.CanUsePlayerPowers(player) && Powers.GetActive(player).Count==0,"spectator regained effects");
            player.SwitchTeam(CsTeam.CounterTerrorist); player.Respawn();
        });
        At(14.4f,"Returning from spectators reapplies the player's own powers",()=> {
            var player=Players.Resolve(spectatorRaw)!;
            Require(Players.AlivePawn(player) is not null && Powers.GetActive(player).Count==2,"respawn did not reapply");
            Powers.NativeProbePair("teleport","infinite_ammo",player:player);
        });
        At(14.5f,"CT cluster grenade dispatches to the correct owner",()=> {
            Powers.NativeProbePair("cluster_grenade","infinite_ammo",player:Ct());
            var pawn=Players.AlivePawn(Ct())!; var pos=pawn.AbsOrigin!;
            Require(GrenadeFactory.Create("flashbang_projectile",new(pos.X,pos.Y,pos.Z+90),new(150,0,100),pawn,43) is not null,"projectile factory");
        });
        At(14.8f,"CT utility produces four real projectiles without recursion",()=> {
            var power=(ClusterGrenade)Powers.GetActive(Ct())[0];
            Require(power.ChildCount==3,"wrong cluster owner or count");
            Powers.NativeProbePair("teleport","infinite_ammo",player:Ct());
        });
        At(15,"Wall Vision supports several owners",()=> {
            var state=Players.AlivePawn(Mode.Boss)!.EntitySpottedState;
            radarSpotted=state.Spotted; radar0=state.SpottedByMask[0]; radar1=state.SpottedByMask[1];
            Powers.NativeProbePair("wall_vision","infinite_ammo"); Powers.NativeProbePair("wall_vision","infinite_ammo",player:Ct());
            Powers.NativeProbePair("wall_vision","infinite_ammo",player:Players.Resolve(spectatorRaw)); Powers.SlowTick();
        });
        At(15.4f,"Clearing one Wall Vision leaves the other owner's outline intact",()=> {
            Require(Effects.OutlineCount==5 && Powers.GetEffects(Ct()).OutlineCount==1,"wrong owner outlines");
            Powers.NativeProbePair("teleport","infinite_ammo"); Require(Powers.GetEffects(Ct()).OutlineCount==1,"other owner's outline removed");
            Powers.NativeProbePair("teleport","infinite_ammo",player:Ct());
            Require(Powers.GetEffects(Players.Resolve(spectatorRaw)).OutlineCount==1,"second CT outline removed");
            Powers.NativeProbePair("teleport","infinite_ammo",player:Players.Resolve(spectatorRaw));
            var state=Players.AlivePawn(Mode.Boss)!.EntitySpottedState;
            Require(state.SpottedByMask[0]==radar0 && state.SpottedByMask[1]==radar1 && state.Spotted==(radarSpotted || radar0!=0 || radar1!=0),"shared radar baseline leaked");
        });
        At(16,"Invisibility applies to boss and CT and starts visible",()=> {
            foreach (var player in Mode.Eligible())
            {
                var pawn=Players.AlivePawn(player)!; pawn.MoveType=MoveType_t.MOVETYPE_NONE; pawn.ActualMoveType=MoveType_t.MOVETYPE_NONE; pawn.Teleport(null,null,new Vector(0,0,0));
            }
            Powers.NativeProbePair("invisibility","infinite_ammo"); Powers.NativeProbePair("invisibility","infinite_ammo",player:Ct());
            Require(Effects.HiddenEntityCount==0 && Powers.GetEffects(Ct()).HiddenEntityCount==0,"initial reveal");
        });
        At(18,"Silence hides both bodies and equipment from other clients, preserving own view",()=> {
            Powers.SlowTick(); var boss=Mode.Boss!; var pawn=Players.AlivePawn(boss)!;
            Require(Effects.HiddenEntityCount>=2 && Powers.GetEffects(Ct()).HiddenEntityCount>=2,"body or equipment not concealed");
            Require(Effects.HiddenFrom(ctRaw,pawn.Index) && !Effects.HiddenFrom(boss.EntityHandle.Raw,pawn.Index),"wrong recipients");
            Require(pawn.Render.A==0 && Powers.Status("invisibility")=="hidden","fade did not finish");
        });
        At(18.1f,"Weapon-fire event reveals the shooter immediately without revealing other players",()=> {
            new EventWeaponFire(true) { Userid=Mode.Boss,Weapon="glock" }.FireEvent(false);
            Require(Effects.HiddenEntityCount==0 && Players.AlivePawn(Mode.Boss)!.Render.A==255,"shooting did not reveal");
            Require(Powers.GetEffects(Ct()).HiddenEntityCount>0,"shooting revealed CT");
        });
        At(18.9f,"The player visibly fades between full visibility and concealment",()=> {
            Powers.SlowTick(); var alpha=Players.AlivePawn(Mode.Boss)!.Render.A;
            Require(alpha>0 && alpha<255 && Effects.HiddenEntityCount==0,$"alpha={alpha}");
        });
        At(20,"Silence conceals again after shooting",()=>Require(Effects.HiddenEntityCount>0,"no reconcealment"));
        At(20.1f,"Reload event makes the player visible",()=> {
            var pawn=Players.AlivePawn(Mode.Boss)!;
            reloadWeapon=pawn.WeaponServices!.ActiveWeapon.Value!.As<CCSWeaponBase>();
            new EventWeaponReload(true) { Userid=Mode.Boss }.FireEvent(false); keepReloading=true; reloadWeapon.InReload=true;
            Require(Effects.HiddenEntityCount==0,"reload not revealed");
        });
        At(22,"A sustained reload prevents an early fade",()=> {
            Require(Effects.HiddenEntityCount==0 && Players.AlivePawn(Mode.Boss)!.Render.A==255,"concealed during reload");
            keepReloading=false; reloadWeapon!.InReload=false;
        });
        At(24,"Invisibility recovers after the reload finishes",()=>Require(Effects.HiddenEntityCount>0,"reload recovery"));
        At(24.1f,"Shift and crouch walking preserve concealment, including footstep events",()=> {
            var boss=Mode.Boss!; var pawn=Players.AlivePawn(boss)!;
            foreach (var button in new[] { PlayerButtons.Speed,PlayerButtons.Duck })
            {
                pawn.MovementServices!.Buttons.ButtonStates[0]=(ulong)button; pawn.Flags|=1;
                pawn.Teleport(null,null,new Vector(200,0,0)); Powers.Tick();
                new EventPlayerFootstep(true) { Userid=boss }.FireEvent(false);
                Require(Invisibility.QuietMovement(boss,pawn) && Effects.HiddenEntityCount>0,"silent movement revealed");
            }
            pawn.MovementServices!.Buttons.ButtonStates[0]=0; pawn.Teleport(null,null,new Vector(0,0,0));
        });
        At(24.3f,"Running reveals even without a footstep event",()=> {
            var pawn=Players.AlivePawn(Mode.Boss)!; pawn.IsWalking=false; pawn.Flags|=1; pawn.Flags&=~2u;
            pawn.Teleport(null,null,new Vector(240,0,0)); Powers.Tick();
            Require(Effects.HiddenEntityCount==0,"running stayed hidden"); pawn.Teleport(null,null,new Vector(0,0,0));
        });
        At(26,"Private loadout announcements never broadcast assigned powers",()=> {
            _probeBroadcasts.Clear(); Config.PublicPowerAnnouncements=false; Feedback.Loadout(Mode.Boss!); Feedback.Loadout(Ct());
            Require(_probeBroadcasts.Count==0,"private loadout broadcast"); Server.ExecuteCommand("css_sp_showpowers 1");
        });
        At(26.4f,"Public visibility command announces every current loadout and persists",()=> {
            Require(Config.PublicPowerAnnouncements && _probeBroadcasts.Count(s=>s.Contains("'s powers:"))==6,"public toggle announcement");
            Server.ExecuteCommand("css_sp_showpowers toggle");
        });
        At(27,"Visibility toggle returns to private mode",()=> {
            Require(!Config.PublicPowerAnnouncements,"public toggle failed");
            Server.ExecuteCommand("css_sp_everyone 0");
        });
        At(27.4f,"Disabling everyone removes CT effects and preserves boss powers and reveal history",()=> {
            Require(!Config.EveryoneHasPowers && Powers.PoweredPlayers.Count()==1 && Powers.HasPowers(Mode.Boss),"everyone disable");
            Require(Powers.GetEffects(Ct()).HiddenEntityCount==0 && Players.AlivePawn(Ct())!.Render.A==255,"CT concealment leaked");
            Require(Powers.Reveals.Count==6,"reveal history lost");
            Server.ExecuteCommand("css_sp_random_boss 1");
        });
        At(28,"Round-end reveal includes a dead boss and players whose powers were removed",()=> {
            Require(Config.RotateSuperpoweredPlayer,"promotion command"); _probeBroadcasts.Clear();
            Players.AlivePawn(Mode.Boss)!.CommitSuicide(false,true);
            if (Mode.Live) Require(Powers.Loadout is not null,"death discarded loadout");
            Mode.EndRound(3);
            Require(_probeBroadcasts.Count(s=>s.StartsWith("ROUND REVEAL |"))==6,"missing public round reveal");
            Mode.EndRound(3); Require(_probeBroadcasts.Count(s=>s.StartsWith("ROUND REVEAL |"))==6,"duplicate reveal");
            Mode.PrepareRound(); Mode.StartRound();
        });
        At(29,"Random promotion moves a different player to T and the former boss to CT",()=> {
            Require(Mode.Live && Players.Key(Mode.Boss!)!=firstBoss && Mode.Boss!.TeamNum==2 && Mode.Opponents.Count()==5,"random promotion failed");
            firstBoss=Players.Key(Mode.Boss!); Server.ExecuteCommand("css_sp_random_boss 0");
        });
        At(29.5f,"Disabling promotion keeps the selected T player on following rounds",()=> {
            Require(!Config.RotateSuperpoweredPlayer,"promotion disable failed"); Mode.PrepareRound(); Mode.StartRound();
            Require(Players.Key(Mode.Boss!)==firstBoss,"fixed T changed");
            Server.ExecuteCommand("css_sp_everyone 1");
        });
        At(30,"Everyone mode survives a round reset and per-owner freeze cleanup overlaps safely",()=> {
            Mode.PrepareRound(); Mode.StartRound(); Require(Powers.PoweredPlayers.Count()==6,"new round lost everyone");
            var pawn=Players.AlivePawn(Mode.Boss)!;
            var original=pawn.MoveType; var actual=pawn.ActualMoveType;
            pawn.MoveType=MoveType_t.MOVETYPE_WALK; pawn.ActualMoveType=MoveType_t.MOVETYPE_WALK;
            var a=new FrozenPlayers(); var b=new FrozenPlayers();
            a.Hold(pawn,Server.CurrentTime+5); b.Hold(pawn,Server.CurrentTime+5); a.Clear();
            Require(pawn.MoveType==MoveType_t.MOVETYPE_NONE,"first cleanup released shared freeze"); b.Clear(); Require(pawn.MoveType==MoveType_t.MOVETYPE_WALK,"last cleanup failed");
            pawn.MoveType=original; pawn.ActualMoveType=actual;
            Server.ExecuteCommand("css_sp_everyone 0");
        });
        AddTimer(31,()=> {
            Probe("No runtime error disabled the plugin",()=>Require(Config.Enabled && Mode.Live,"plugin disabled"));
            File.WriteAllText(Path.Combine(ProbeDirectory,"round-options-native-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
            Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit")); Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
