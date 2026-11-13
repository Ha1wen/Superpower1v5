using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Superpower1v5.Support;

namespace Superpower1v5.GameMode;

public sealed class ModeController(Plugin plugin)
{
    private readonly Dictionary<string,(ConVarBase cvar,string original)> _cvars=new();
    private uint? _bossRaw;
    private uint? _requestedBoss;
    private bool _prepared,_changing,_restartQueued,_waitingForPlayers=true;
    private double _lastRestart=-100;
    public bool Live { get; private set; }
    public int Generation { get; private set; }
    public CCSPlayerController? Boss => _bossRaw is { } raw ? Players.Resolve(raw) : null;
    public CsTeam BossTeam => CsTeam.Terrorist;
    public CsTeam OpponentTeam => CsTeam.CounterTerrorist;
    public CCSGameRules? Rules => Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
    public bool Warmup => Rules?.WarmupPeriod ?? true;
    public bool CanUsePowers => CanUsePlayerPowers(Boss);
    public bool CanUsePlayerPowers(CCSPlayerController? player) => plugin.Config.Enabled && Live && (!Warmup || plugin.Config.EnableDuringWarmup) && Rules?.FreezePeriod==false && Players.Playing(player) && plugin.Powers.HasPowers(player) && Players.AlivePawn(player) is not null;
    public IEnumerable<CCSPlayerController> Opponents => Eligible().Where(p=>p.Team==OpponentTeam && !IsBoss(p));
    public bool IsBoss(CCSPlayerController? p) => Players.Connected(p) && p!.EntityHandle.Raw==_bossRaw;
    public List<CCSPlayerController> Eligible() => Utilities.GetPlayers().Where(p=>Players.Playing(p) && (!p.IsBot || plugin.Config.AllowBots)).OrderBy(p=>p.IsBot).ThenBy(p=>p.Slot).ToList();
    private bool Ready() => Eligible().Count>=2 && (plugin.Config.RotateSuperpoweredPlayer || Eligible().Any(p=>p.Team==BossTeam));
    public bool CanJoinT(CCSPlayerController player)
    {
        var incumbent=Eligible().FirstOrDefault(p=>p.Team==BossTeam && p.EntityHandle.Raw!=player.EntityHandle.Raw);
        return incumbent is null || incumbent.IsBot && !player.IsBot;
    }
    public void EnforceCvars()
    {
        if (!plugin.Config.Enabled) return;
        foreach (var (name,value) in new[] { ("mp_autoteambalance","false"),("mp_limitteams","0"),("mp_autokick","false"),("mp_halftime","false") })
        {
            var c=ConVar<int>.Find(name); if (c is null) continue;
            if (!_cvars.ContainsKey(name)) _cvars[name]=(c,c.ValueAsString);
            c.ValueAsString=value;
        }
    }
    public void RestoreCvars()
    { foreach (var entry in _cvars.Values) if (entry.cvar.IsValid) entry.cvar.ValueAsString=entry.original; _cvars.Clear(); }
    private static void Move(CCSPlayerController p,CsTeam team)
    { if (p.Team!=team) { if (team==CsTeam.Spectator) p.ChangeTeam(team); else p.SwitchTeam(team); } }
    public void PrepareRound()
    {
        ResetRound(); EnforceCvars(); _waitingForPlayers=true;
        if (!plugin.Config.Enabled || Warmup && !plugin.Config.EnableDuringWarmup) return;
        var players=Eligible();
        var previous=Boss is { } old ? Players.Key(old) : null;
        var requested=_requestedBoss is { } requestedRaw ? players.FirstOrDefault(p=>p.EntityHandle.Raw==requestedRaw) : null;
        var roster=RosterPlanner.Plan(players.Select(p=>new Candidate(Players.Key(p),p.IsBot,p.TeamNum)).ToArray(),plugin.Config.AllowBots,plugin.Config.MaxOpponents,previous,plugin.Config.RotateSuperpoweredPlayer && requested is null);
        _requestedBoss=null;
        if (roster is null) { _bossRaw=null; return; }
        var boss=players.First(p=>Players.Key(p)==roster.Boss); _bossRaw=boss.EntityHandle.Raw;
        _changing=true;
        try
        {
            Move(boss,BossTeam);
            foreach (var p in players.Where(p=>!IsBoss(p))) Move(p,roster.Opponents.Contains(Players.Key(p)) ? OpponentTeam : CsTeam.Spectator);
            if (!plugin.Config.AllowBots)
                foreach (var p in Utilities.GetPlayers().Where(p=>Players.Playing(p)&&p.IsBot)) Move(p,CsTeam.Spectator);
        }
        finally { _changing=false; }
        try
        {
            plugin.Powers.Roll(true); _prepared=true; _waitingForPlayers=false;
            if (plugin.Config.DebugLogging) plugin.Logger.LogInformation("Private power round prepared for the T boss {Boss}",boss.PlayerName);
        }
        catch (Exception e) { plugin.Logger.LogError(e,"Cannot select power pair"); _waitingForPlayers=true; }
    }
    private void ResetHealth(CCSPlayerController p)
    {
        if (Players.AlivePawn(p) is not { } pawn) return;
        var health=plugin.Powers.GetLoadout(p)?.Has("juggernaut")==true ? 500 : 100;
        pawn.MaxHealth=health; pawn.Health=health;
        Utilities.SetStateChanged(pawn,"CBaseEntity","m_iMaxHealth"); Utilities.SetStateChanged(pawn,"CBaseEntity","m_iHealth");
    }
    public void StartRound()
    {
        if (!_prepared) PrepareRound(); if (!_prepared) return;
        Live=true; foreach (var p in Eligible()) ResetHealth(p);
        plugin.Powers.ApplyToPawn(); plugin.Feedback.Round();
    }
    public void Spawn(CCSPlayerController p)
    {
        if (!plugin.Config.Enabled || !Players.Playing(p)) return;
        if (Live && plugin.Config.EveryoneHasPowers && !plugin.Powers.HasPowers(p)) { plugin.Powers.Roll(true,p); plugin.Feedback.Loadout(p); }
        ResetHealth(p); plugin.Powers.ApplyToPawn(p);
    }
    public void EndRound(int winner)
    {
        if (Live)
        {
            plugin.Broadcast(winner==(int)BossTeam ? "THE T BOSS WINS THE ROUND!" : winner==(int)OpponentTeam ? "THE CT PLAYERS WIN THE ROUND!" : "Round ended in a draw.");
            plugin.Feedback.RevealRound();
        }
        ResetRound();
    }
    public void ResetRound() { Live=false; _prepared=false; Generation++; plugin.ClearDamageTracking(); plugin.Powers.Clear(); }
    public void MapEnd() { ResetRound(); _bossRaw=null; _requestedBoss=null; _restartQueued=false; _waitingForPlayers=true; }
    public void RequestBoss(CCSPlayerController p)
    {
        if (!Players.Playing(p) || p.IsBot && !plugin.Config.AllowBots) throw new InvalidOperationException("Select an eligible player.");
        _changing=true;
        try { foreach (var other in Eligible().Where(other=>other.Team==BossTeam && other.EntityHandle.Raw!=p.EntityHandle.Raw)) Move(other,OpponentTeam); Move(p,BossTeam); }
        finally { _changing=false; }
        ResetRound(); _bossRaw=p.EntityHandle.Raw; _requestedBoss=_bossRaw; _waitingForPlayers=true; QueueRestart();
    }
    public void BossLeft()
    {
        if (Live) plugin.Feedback.RevealRound();
        ResetRound(); _bossRaw=null; _waitingForPlayers=true;
        plugin.Broadcast(plugin.Config.RotateSuperpoweredPlayer ? "The boss left. A new boss will be selected next round." : "The T boss left their team. Join T to become the boss.");
        if (Ready()) QueueRestart();
    }
    public void TeamChanged(CCSPlayerController p,int team)
    {
        if (_changing || !plugin.Config.Enabled || !Players.Connected(p)) return;
        if (team is not (2 or 3)) plugin.Powers.Stop(p);
        if (IsBoss(p) && team!=(int)BossTeam) BossLeft();
        if (team==2)
        {
            if (!CanJoinT(p))
            {
                _changing=true; try { Move(p,Opponents.Count()<plugin.Config.MaxOpponents ? OpponentTeam : CsTeam.Spectator); } finally { _changing=false; }
                p.PrintToChat(Plugin.Prefix+"T already has a boss. Join CT until the T slot is free."); return;
            }
            _changing=true;
            try { foreach (var other in Eligible().Where(other=>other.Team==BossTeam && other.EntityHandle.Raw!=p.EntityHandle.Raw)) Move(other,OpponentTeam); }
            finally { _changing=false; }
            if (!IsBoss(p)) { ResetRound(); _bossRaw=p.EntityHandle.Raw; _waitingForPlayers=true; QueueRestart(); }
        }
        else if (team==3 && Opponents.Count()>plugin.Config.MaxOpponents)
        { _changing=true; try { Move(p,CsTeam.Spectator); } finally { _changing=false; } }
        if (_waitingForPlayers && Ready()) QueueRestart();
    }
    public void Maintenance()
    {
        if (!plugin.Config.Enabled) return;
        if (Live && (!Players.Playing(Boss) || Boss!.Team!=BossTeam)) BossLeft();
        if (Live) plugin.Powers.ReconcileEveryone();
        if (_waitingForPlayers && !Live && !_prepared && !Warmup && Ready()) QueueRestart();
    }
    public void QueueRestart()
    {
        if (_restartQueued || !plugin.Config.Enabled || Warmup || Server.CurrentTime-_lastRestart<5) return;
        _restartQueued=true; _lastRestart=Server.CurrentTime;
        plugin.AddTimer(1,()=> { _restartQueued=false; if (plugin.Config.Enabled) Server.ExecuteCommand("mp_restartgame 1"); },CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }
}
