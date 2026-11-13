using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Superpower1v5.Powers;
using Superpower1v5.Managers;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5;

public sealed partial class Plugin
{
    private readonly Dictionary<uint,(int health,int tick,DamageTypes_t type)> _healthBeforeDamage=new();
    private readonly DamageReceipts _damageReceipts=new();
    private static readonly HashSet<string> FirearmNames=new(StringComparer.OrdinalIgnoreCase)
    { "ak47","m4a1","m4a1_silencer","aug","sg556","famas","galilar","awp","ssg08","scar20","g3sg1","glock","hkp2000","usp_silencer","p250","tec9","fiveseven","cz75a","elite","deagle","revolver","mac10","mp9","mp7","mp5sd","ump45","p90","bizon","nova","xm1014","mag7","sawedoff","m249","negev" };
    public void ClearDamageTracking() { _healthBeforeDamage.Clear(); _damageReceipts.Clear(); }
    private void RegisterEvents()
    {
        RegisterEventHandler<EventRoundPrestart>((e,i)=> { Guard(()=> { Mode.PrepareRound(); ClearEntities(); }); return HookResult.Continue; },HookMode.Pre);
        RegisterEventHandler<EventRoundStart>((e,i)=> { Guard(Mode.StartRound); return HookResult.Continue; });
        RegisterEventHandler<EventRoundEnd>((e,i)=> { Guard(()=> { Mode.EndRound(e.Winner); ClearEntities(); }); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerSpawn>((e,i)=> {
            if (e.Userid is { IsValid: true } p)
            {
                var raw=p.EntityHandle.Raw;
                Server.NextFrame(()=>Guard(()=> { if (Players.Resolve(raw) is { } valid) Mode.Spawn(valid); }));
            }
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerTeam>((e,i)=> {
            Guard(()=> { if (!e.Disconnect && e.Userid is { } p) Mode.TeamChanged(p,e.Team); }); return HookResult.Continue;
        });
        RegisterEventHandler<EventWeaponFire>((e,i)=> { Guard(()=> { if (Mode.IsBoss(e.Userid)) Powers.Fire(); }); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerJump>((e,i)=> { Guard(()=> { if (Mode.IsBoss(e.Userid)) Powers.Jump(); }); return HookResult.Continue; });
        RegisterEventHandler<EventBulletImpact>((e,i)=> { Guard(()=> { if (Mode.IsBoss(e.Userid)) Powers.Impact(e.X,e.Y,e.Z); }); return HookResult.Continue; });
        RegisterEventHandler<EventSmokegrenadeDetonate>((e,i)=> { Guard(()=>Powers.Smoke(e.X,e.Y,e.Z)); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerHurt>((e,i)=> { Guard(()=>PlayerHurt(e)); return HookResult.Continue; });
        RegisterEventHandler<EventPlayerDeath>((e,i)=> {
            Guard(()=> {
                if (!Mode.Live) return;
                if (Mode.IsBoss(e.Userid)) { Broadcast("The superpowered player has been eliminated!"); Powers.Clear(); }
                else if (!Mode.Opponents.Any(p=>Players.AlivePawn(p) is not null)) Broadcast("All opponents have been eliminated!");
            });
            return HookResult.Continue;
        });
    }
    private DamageEvent? Describe(CCSPlayerPawn p,CTakeDamageInfo info)
    {
        var victim=Players.Controller(p); if (!Players.Playing(victim)) return null;
        if (Damage.SourcePawn is { } source) info.Attacker.Raw=source;
        var attacker=Players.Controller(info.Attacker.Value);
        var bossVictim=Mode.IsBoss(victim);
        var bossAttacker=Mode.IsBoss(attacker) && attacker!.TeamNum!=victim!.TeamNum;
        return bossVictim || bossAttacker ? new(victim!,attacker,info,bossVictim,bossAttacker,Damage.Synthetic) : null;
    }
    private HookResult BeforeDamage(CCSPlayerPawn p,CTakeDamageInfo info)
    {
        if (!Mode.CanUsePowers || info.Damage<=0 || Describe(p,info) is not { } hit) return HookResult.Continue;
        _healthBeforeDamage[p.EntityHandle.Raw]=(p.Health,Server.TickCount,info.BitsDamageType);
        return Powers.BeforeDamage(hit) ? HookResult.Handled : HookResult.Continue;
    }
    private void PlayerHurt(EventPlayerHurt hurt)
    {
        // Real weapon damage can supply a null native result. player_hurt is authoritative
        // for primary hits, so the same hit is never also processed by the native post hook.
        if (!Mode.Live || Damage.Synthetic || hurt.DmgHealth<=0 || !Players.Playing(hurt.Userid)) return;
        var victim=hurt.Userid!; var attacker=hurt.Attacker;
        var bossVictim=Mode.IsBoss(victim); var bossAttacker=Mode.IsBoss(attacker) && attacker!.TeamNum!=victim.TeamNum;
        if (!bossVictim && !bossAttacker) return;
        var healthBefore=100;
        var type=FirearmNames.Contains(hurt.Weapon.Replace("weapon_","",StringComparison.OrdinalIgnoreCase)) ? DamageTypes_t.DMG_BULLET : DamageTypes_t.DMG_GENERIC;
        if (_healthBeforeDamage.Remove(victim.PlayerPawn.Raw,out var before) && Server.TickCount-before.tick<=1)
        { healthBefore=before.health; type=before.type; }
        var hit=new DamageEvent(victim,attacker,null,bossVictim,bossAttacker,false,type);
        DeliverDamage(hit,Math.Min(hurt.DmgHealth,Math.Max(0,healthBefore)),hurt.Health,true);
    }
    private void AfterDamage(CCSPlayerPawn p,CTakeDamageInfo info,CTakeDamageResult result)
    {
        // Some real weapon calls pass null here. The event remains a fallback; when both
        // exist, receipts ensure that healing and knockback are applied only once.
        if (!DamageService.HasResult(info,result) || p is null || p.Handle==IntPtr.Zero || !Mode.Live || result.HealthLost<=0 || Describe(p,info) is not { } hit) return;
        var lost=Math.Min(result.HealthLost,Math.Max(0,result.HealthBefore));
        DeliverDamage(hit,lost,Math.Max(0,result.HealthBefore-lost),false);
    }
    private void DeliverDamage(DamageEvent hit,int lost,int healthAfter,bool fromEvent)
    {
        if (!_damageReceipts.Accept(hit.Victim.PlayerPawn.Raw,hit.Attacker?.EntityHandle.Raw??uint.MaxValue,Server.TickCount,lost,healthAfter,fromEvent)) return;
        Powers.AfterDamage(new(hit,lost));
    }
}
