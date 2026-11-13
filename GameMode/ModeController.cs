using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Managers;
using Superpower1v5.Support;

namespace Superpower1v5.GameMode;

public sealed class ModeController(Plugin plugin)
{
    private readonly FairRotation _rotation=new();
    private readonly Dictionary<string,(ConVarBase cvar,string original,string forced)> _cvars=new();
    private uint? _bossRaw;
    private string? _previousBoss, _forcedBoss;
    private bool _prepared, _changing, _restartQueued;
    private bool _waitingForPlayers=true;
    private double _lastRestart=-100;
    public bool Live { get; private set; }
    public int Generation { get; private set; }
    public CCSPlayerController? Boss => _bossRaw is { } raw ? Players.Resolve(raw) : null;
    public CsTeam BossTeam => plugin.Config.SuperpoweredPlayerTeam=="CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
    public CsTeam OpponentTeam => BossTeam==CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
    public CCSGameRules? Rules => Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
    public bool Warmup => Rules?.WarmupPeriod ?? true;
    public bool CanUsePowers => plugin.Config.Enabled && Live && (!Warmup || plugin.Config.EnableDuringWarmup) && Rules?.FreezePeriod==false && Players.AlivePawn(Boss) is not null;
    public IEnumerable<CCSPlayerController> Opponents => Utilities.GetPlayers().Where(p=>Players.Playing(p) && p.Team==OpponentTeam && !IsBoss(p));
    public bool IsBoss(CCSPlayerController? p) => Players.Connected(p) && p!.EntityHandle.Raw==_bossRaw;
    public List<CCSPlayerController> Eligible() => Utilities.GetPlayers().Where(p=>Players.Playing(p) && (!p.IsBot || plugin.Config.AllowBots))
        .OrderBy(p=>p.IsBot).ThenBy(p=>p.Slot).ToList();

    public void EnforceCvars()
    {
        if (!plugin.Config.Enabled) return;
        foreach (var (name,value) in new[] { ("mp_autoteambalance","false"),("mp_limitteams","0"),("mp_autokick","false"),("mp_halftime","false") })
        {
            var c=ConVar<int>.Find(name); if (c is null) continue;
            if (!_cvars.ContainsKey(name)) _cvars[name]=(c,c.ValueAsString,value);
            c.ValueAsString=value;
        }
    }
    public void RestoreCvars()
    {
        foreach (var (_,entry) in _cvars)
            if (entry.cvar.IsValid) entry.cvar.ValueAsString=entry.original;
        _cvars.Clear();
    }
    public void PrepareRound()
    {
        ResetRound(); EnforceCvars(); _waitingForPlayers=false;
        if (!plugin.Config.Enabled || (Warmup && !plugin.Config.EnableDuringWarmup)) { _waitingForPlayers=true; return; }
        var players=Eligible(); if (players.Count<2) { _waitingForPlayers=true; return; }
        var roster=RosterPlanner.Plan(players.Select(p=>new Candidate(Players.Key(p),p.IsBot,p.TeamNum)).ToList(),
            _rotation,plugin.Config.AllowBots,plugin.Config.MaxOpponents,plugin.Config.RotateSuperpoweredPlayer,_previousBoss,_forcedBoss)!;
        var boss=players.First(p=>Players.Key(p)==roster.Boss); _forcedBoss=null; _previousBoss=roster.Boss;
        _bossRaw=boss.EntityHandle.Raw;
        var opponents=players.Where(p=>roster.Opponents.Contains(Players.Key(p))).ToList();
        _changing=true;
        try
        {
            foreach (var p in players.Where(p=>p.EntityHandle.Raw!=_bossRaw))
                Move(p,opponents.Contains(p) ? OpponentTeam : CsTeam.Spectator);
            Move(boss,BossTeam);
            if (!plugin.Config.AllowBots)
                foreach (var bot in Utilities.GetPlayers().Where(p=>Players.Playing(p)&&p.IsBot)) Move(bot,CsTeam.Spectator);
        }
        finally { _changing=false; }
        try
        {
            plugin.Powers.Roll(true); _prepared=true;
            if (plugin.Config.DebugLogging) plugin.Logger.LogInformation("Superpower round prepared: boss={Boss}, team={Team}, opponents={Count}, powers={Powers}",
                boss.PlayerName,BossTeam,opponents.Count,string.Join(',',plugin.Powers.Loadout!.Powers.Select(p=>p.Id)));
        }
        catch (Exception e) { plugin.Logger.LogError(e,"Cannot select a power pair"); plugin.Broadcast("Mode waiting: enable at least two compatible powers in the config."); }
    }
    private static void Move(CCSPlayerController p,CsTeam team)
    {
        if (p.Team==team) return;
        if (team==CsTeam.Spectator) p.ChangeTeam(team); else p.SwitchTeam(team);
    }
    public void StartRound()
    {
        if (!_prepared) PrepareRound();
        if (!_prepared) return;
        Live=true;
        foreach (var p in Eligible()) ResetHealth(p);
        plugin.Powers.ApplyToPawn(); plugin.Feedback.Round();
    }
    public void Spawn(CCSPlayerController p)
    {
        if (!plugin.Config.Enabled || !Players.Playing(p)) return;
        ResetHealth(p);
        if (IsBoss(p)) plugin.Powers.ApplyToPawn();
    }
    private static void ResetHealth(CCSPlayerController p)
    {
        if (Players.AlivePawn(p) is not { } pawn) return;
        pawn.MaxHealth=100; pawn.Health=100;
        Utilities.SetStateChanged(pawn,"CBaseEntity","m_iMaxHealth"); Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");
    }
    public void EndRound(int winner)
    {
        if (Live) plugin.Broadcast(winner==(int)BossTeam ? "SUPERPOWER PLAYER WINS THE ROUND!" : winner==(int)OpponentTeam ? "THE OPPONENTS WIN THE ROUND!" : "Round ended in a draw.");
        ResetRound();
    }
    public void ResetRound()
    { Live=false; _prepared=false; Generation++; plugin.ClearDamageTracking(); plugin.Powers.Clear(); }
    public void MapEnd() { ResetRound(); _bossRaw=null; _previousBoss=null; _forcedBoss=null; _rotation.Clear(); _restartQueued=false; _waitingForPlayers=true; }
    public void RequestBoss(CCSPlayerController p)
    {
        if (!Players.Playing(p) || (p.IsBot && !plugin.Config.AllowBots)) throw new InvalidOperationException("Select an eligible player on T or CT.");
        if (Eligible().Any(x=>!x.IsBot) && p.IsBot) throw new InvalidOperationException("A human player must be selected while humans are eligible.");
        _forcedBoss=Players.Key(p); QueueRestart();
    }
    public void BossLeft()
    {
        if (!Live && !_prepared) return;
        plugin.Broadcast("The superpowered player left. Selecting a replacement."); ResetRound(); _bossRaw=null; _waitingForPlayers=true; QueueRestart();
    }
    public void TeamChanged(CCSPlayerController p,int team)
    {
        if (_changing || !plugin.Config.Enabled || !Players.Connected(p)) return;
        if (IsBoss(p) && team!=(int)BossTeam) { BossLeft(); return; }
        if (team is not (2 or 3) || !Live) return;
        _changing=true;
        try
        {
            var count=Opponents.Count(x=>x.EntityHandle.Raw!=p.EntityHandle.Raw);
            Move(p,(!p.IsBot||plugin.Config.AllowBots) && count<plugin.Config.MaxOpponents ? OpponentTeam : CsTeam.Spectator);
        }
        finally { _changing=false; }
    }
    public void Maintenance()
    {
        if (!plugin.Config.Enabled) return;
        if (Live && (!Players.Playing(Boss) || Boss!.Team!=BossTeam)) BossLeft();
        if (_waitingForPlayers && !Live && !_prepared && !Warmup && Eligible().Count>=2) QueueRestart();
    }
    public void QueueRestart()
    {
        if (_restartQueued || !plugin.Config.Enabled || Warmup || Server.CurrentTime-_lastRestart<5) return;
        _restartQueued=true; _lastRestart=Server.CurrentTime;
        plugin.AddTimer(1,()=> { _restartQueued=false; if (plugin.Config.Enabled) Server.ExecuteCommand("mp_restartgame 1"); },CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }
}
