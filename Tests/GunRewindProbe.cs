#if SERVER_SMOKE
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Support;
using GameTrace=CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private void ScheduleGunRewindProbe()
    {
        foreach (var entry in Config.Powers) entry.Value.Enabled=entry.Key is "teleport" or "infinite_ammo";
        StopProbeBots(true);
        var consumedByBullet=false; var watching=false; var fired=0; Vector? previous=null;
        CCSPlayerPawn? riflePawn=null; CBasePlayerWeapon? rifle=null;
        RegisterEventHandler<EventWeaponFire>((e,i)=> { if (watching) { fired++; Logger.LogInformation("GUN PROBE fired {Weapon} by {Who}",e.Weapon,e.Userid?.PlayerName); } return HookResult.Continue; });
        RegisterListener<Listeners.OnPlayerTakeDamagePre>((pawn,info)=> {
            if (watching && Mode.IsBoss(Players.Controller(pawn)))
            {
                Logger.LogInformation("GUN DAMAGE type={Type}, health={HP}, consumed={Consumed}, traceGroup={Trace}, original={Original}, damage={Damage}",info.BitsDamageType,pawn.Health,Powers.Loadout?.Consumed.Contains("second_chance"),info.GetHitGroup(),info.OriginalDamage,info.Damage);
                if ((info.BitsDamageType&DamageTypes_t.DMG_BULLET)!=0 && Powers.Loadout?.Consumed.Contains("second_chance")==true) consumedByBullet=true;
            }
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerHurt>((e,i)=> { if (watching) Logger.LogInformation("GUN HURT victim={Victim}, damage={Damage}, health={Health}, group={Group}",e.Userid?.PlayerName,e.DmgHealth,e.Health,e.Hitgroup); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerDeath>((e,i)=> { if (watching) Logger.LogInformation("GUN DEATH victim={Victim}, weapon={Weapon}, head={Head}",e.Userid?.PlayerName,e.Weapon,e.Headshot); return HookResult.Continue; });
        RegisterListener<Listeners.OnTick>(()=> {
            if (consumedByBullet && watching) { watching=false; StopProbeBots(true); }
            if (!watching || riflePawn is not { IsValid:true } enemy || Players.AlivePawn(Mode.Boss) is not { } target || target.AbsOrigin is not { } point) return;
            enemy.MoveType=MoveType_t.MOVETYPE_NONE; enemy.ActualMoveType=MoveType_t.MOVETYPE_NONE;
            if (enemy.Bot is { } bot)
            {
                bot.Enemy.Raw=target.EntityHandle.Raw; bot.IsEnemyVisible=true; bot.VisibleEnemyParts=255; bot.IsAimingAtEnemy=true;
                bot.LastSawEnemyTimestamp=Server.CurrentTime; bot.FirstSawEnemyTimestamp=Server.CurrentTime-5;
                bot.TargetSpot.X=point.X; bot.TargetSpot.Y=point.Y; bot.TargetSpot.Z=point.Z+60;
                bot.LookAtSpot.X=point.X; bot.LookAtSpot.Y=point.Y; bot.LookAtSpot.Z=point.Z+60;
                bot.LookAtSpotTimestamp=Server.CurrentTime;
            }
            if (rifle is { IsValid:true }) { enemy.WeaponServices!.ActiveWeapon.Raw=rifle.EntityHandle.Raw; enemy.WeaponServices.As<CCSPlayer_WeaponServices>().NextAttack=0; }
            enemy.MovementServices!.Buttons.ButtonStates[0]|=(ulong)PlayerButtons.Attack;
        });
        AddTimer(12,()=> {
            Powers.NativeProbePair("second_chance","infinite_ammo"); previous=Players.Copy(Players.AlivePawn(Mode.Boss)!.AbsOrigin!);
        },TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(15.3f,()=>Probe("Rifle-only bot scenario starts with a clear firing line",()=> {
            var boss=Players.AlivePawn(Mode.Boss)!; var shooter=Mode.Opponents.First(); var enemy=Players.AlivePawn(shooter)!;
            var before=Players.Copy(boss.AbsOrigin!);
            boss.MoveType=MoveType_t.MOVETYPE_NONE; boss.ActualMoveType=MoveType_t.MOVETYPE_NONE;
            boss.Teleport(new(before.X,before.Y,before.Z+80),null,new Vector(0,0,0));
            var origin=Players.Copy(boss.AbsOrigin!); Vector? spot=null;
            foreach (var distance in new[] { 700,550,400 })
            {
                for (var yaw=0;yaw<360 && spot is null;yaw+=30)
                {
                    var angle=yaw*MathF.PI/180; var x=origin.X+MathF.Cos(angle)*distance; var y=origin.Y+MathF.Sin(angle)*distance;
                    var ground=GameTrace.TraceEndShape(new(x,y,origin.Z+200),new(x,y,origin.Z-300),enemy,new() { InteractsWith=Masks.PlayerSolidBrushOnly });
                    if (!ground.DidHit() || ground.IsAllSolid || ground.Normal.Z<.7) continue;
                    var candidate=ground.EndPos; candidate.Z+=16;
                    var clear=GameTrace.TraceHullShape(candidate,candidate,enemy.Collision.Mins,enemy.Collision.Maxs,enemy,new() { InteractsWith=Masks.PlayerSolidBrushOnly });
                    if (clear.IsAllSolid || clear.DidHit() || !SafeTeleport.Visible(new(origin.X,origin.Y,origin.Z+60),new(candidate.X,candidate.Y,candidate.Z+60),boss)) continue;
                    spot=candidate;
                }
                if (spot is not null) break;
            }
            Require(spot is not null,"no open rifle firing point");
            Mode.Boss!.RemoveWeapons(); shooter.RemoveWeapons();
            var gun=shooter.GiveNamedItem<CBasePlayerWeapon>("weapon_ak47"); Require(gun is not null,"AK not given");
            enemy.WeaponServices!.ActiveWeapon.Raw=gun!.EntityHandle.Raw; gun.Clip1=30;
            riflePawn=enemy; rifle=gun;
            var aim=MathF.Atan2(origin.Y-spot!.Y,origin.X-spot.X)*180/MathF.PI;
            enemy.Teleport(spot,new QAngle(0,aim,0),new Vector(0,0,0)); enemy.EyeAngles.Y=aim;
            if (enemy.Bot is { } bot) { bot.LookAtSpot.X=origin.X; bot.LookAtSpot.Y=origin.Y; bot.LookAtSpot.Z=origin.Z+60; bot.LookAtSpotAttack=true; bot.LookAtSpotDuration=20; bot.LookAtSpotTimestamp=Server.CurrentTime; }
            boss.Health=100; boss.ArmorValue=0; boss.MoveType=MoveType_t.MOVETYPE_NONE; boss.ActualMoveType=MoveType_t.MOVETYPE_NONE;
            foreach (var name in new[] { "bot_knives_only","bot_pistols_only","bot_dont_shoot","bot_ignore_players","bot_ignore_enemies" }) if (ConVar<int>.Find(name) is { } v) { v.ValueAsString="0"; Logger.LogInformation("GUN CVAR {Name}={Value}",name,v.ValueAsString); }
            shooter.ExecuteClientCommandFromServer("slot1");
            watching=true; StopProbeBots(false);
        }),TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(25,()=> {
            StopProbeBots(true);
            Probe("Second Chance survives actual rifle bullets and keeps the round active",()=> {
                Require(consumedByBullet && Mode.Live && Players.AlivePawn(Mode.Boss) is not null && Config.Enabled,$"bullet protection failed: shots={fired}, consumed={Powers.Loadout?.Consumed.Contains("second_chance")}");
                Require(previous is not null && Players.Distance(Players.AlivePawn(Mode.Boss)!.AbsOrigin!,previous)<40,"real shot did not rewind position");
            });
            File.WriteAllText(Path.Combine(ProbeDirectory,"native-probe-results.json"),JsonSerializer.Serialize(_probeResults,new JsonSerializerOptions { WriteIndented=true }));
            Server.NextWorldUpdate(static ()=>Server.ExecuteCommand("quit")); Server.ExecuteCommand("css_plugins unload \"Superpower 1v5\"");
        },TimerFlags.STOP_ON_MAPCHANGE);
    }
}
#endif
