using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Extensions;
using Microsoft.Extensions.Logging;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5;

public sealed partial class Plugin
{
    [ConsoleCommand("css_sp_random_boss","css_sp_random_boss [0|1|toggle]: random promotion next round"),RequiresPermissions("@css/generic")]
    public void RandomBossCommand(CCSPlayerController? p,CommandInfo cmd) => AdminAction(cmd,()=>
        SetOption(cmd,Config.RotateSuperpoweredPlayer,v=>Config.RotateSuperpoweredPlayer=v,"Random boss promotion",()=>{}," Takes effect next round; 0 keeps the chosen T player as boss."));
    [ConsoleCommand("css_sp_showpowers","css_sp_showpowers [0|1|toggle]: public power announcements during rounds"),RequiresPermissions("@css/generic")]
    public void ShowPowersCommand(CCSPlayerController? p,CommandInfo cmd) => AdminAction(cmd,()=>
        SetOption(cmd,Config.PublicPowerAnnouncements,v=>Config.PublicPowerAnnouncements=v,"Public powers",()=> {
            if (Mode.Live && Config.PublicPowerAnnouncements) foreach (var player in Powers.PoweredPlayers) Feedback.PublicLoadout(player);
        }," Powers are always revealed at round end."));
    [ConsoleCommand("css_sp_everyone","css_sp_everyone [0|1|toggle]: give every active T/CT player two powers"),RequiresPermissions("@css/generic")]
    public void EveryoneCommand(CCSPlayerController? p,CommandInfo cmd) => AdminAction(cmd,()=>
        SetOption(cmd,Config.EveryoneHasPowers,v=>Config.EveryoneHasPowers=v,"Powers for everyone",()=> {
            Powers.ReconcileEveryone(); Broadcast(Config.EveryoneHasPowers ? "Every active player now receives two powers." : "Only the boss receives powers.");
        }," Applies now and to future rounds; spectators are excluded."));
    private void SetOption(CommandInfo cmd,bool current,Action<bool> set,string label,Action apply,string detail)
    {
        if (cmd.ArgCount==1) { cmd.ReplyToCommand($"{label}: {(current ? 1 : 0)}. Use 0, 1 or toggle.{detail}"); return; }
        if (cmd.ArgCount!=2) throw new ArgumentException("Expected 0, 1 or toggle.");
        var value=ParseToggle(cmd.GetArg(1),current);
        set(value);
        try { Config.Update(); } catch { set(current); throw; }
        apply(); cmd.ReplyToCommand($"{label}: {(value ? 1 : 0)}; saved.{detail}");
    }
    internal static bool ParseToggle(string argument,bool current) => argument.ToLowerInvariant() switch {
        "1" or "on" or "true" => true, "0" or "off" or "false" => false, "toggle" => !current,
        _ => throw new ArgumentException("Expected 0, 1 or toggle.")
    };
    [ConsoleCommand("css_sp_enable","Enable the mode"),RequiresPermissions("@css/generic")]
    public void EnableCommand(CCSPlayerController? p,CommandInfo cmd)
    { Config.Enabled=true; Mode.EnforceCvars(); Mode.QueueRestart(); cmd.ReplyToCommand("Superpower 1v5 enabled. Waiting for the next round and at least two active players."); }
    [ConsoleCommand("css_sp_disable","Disable and clean up the mode"),RequiresPermissions("@css/generic")]
    public void DisableCommand(CCSPlayerController? p,CommandInfo cmd)
    { Config.Enabled=false; Mode.ResetRound(); ClearEntities(); Mode.RestoreCvars(); cmd.ReplyToCommand("Superpower 1v5 disabled; effects and server settings restored."); }
    [ConsoleCommand("css_sp_reload","Reload configuration"),RequiresPermissions("@css/generic")]
    public void ReloadCommand(CCSPlayerController? p,CommandInfo cmd)
    {
        try
        {
            var next=new Configuration.PluginConfig(); next.Reload(); next.Validate();
            // Validate before disrupting the current round.
            if (next.Enabled) new Managers.SelectionEngine(Random.Shared).SelectPair(next);
            Mode.ResetRound(); ClearEntities(); Mode.RestoreCvars(); Config=next;
            Mode.EnforceCvars(); Mode.QueueRestart(); cmd.ReplyToCommand("Superpower configuration reloaded.");
        }
        catch (Exception e) { Logger.LogError(e,"Config reload rejected"); cmd.ReplyToCommand("Reload rejected: "+e.Message); }
    }
    [ConsoleCommand("css_sp_setboss","css_sp_setboss <#userid|name>: select boss next round"),RequiresPermissions("@css/generic")]
    public void SetBossCommand(CCSPlayerController? p,CommandInfo cmd) => AdminAction(cmd,()=> {
        var target=FindPlayer(cmd,1); Mode.RequestBoss(target); cmd.ReplyToCommand("Next boss: "+target.PlayerName);
    });
    [ConsoleCommand("css_sp_reroll","Reroll the boss's two powers"),RequiresPermissions("@css/generic")]
    public void RerollCommand(CCSPlayerController? p,CommandInfo cmd) => AdminAction(cmd,()=> {
        if (!Mode.Live || Mode.Boss is null) throw new InvalidOperationException("No active boss.");
        Powers.Roll(false); Feedback.Round();
    });
    [ConsoleCommand("css_sp_give","css_sp_give <player> <power_id> [1|2]: replace a boss power"),RequiresPermissions("@css/generic")]
    public void GiveCommand(CCSPlayerController? p,CommandInfo cmd) => AdminAction(cmd,()=> {
        var target=FindPlayer(cmd,1);
        if (!Mode.Live || !Powers.HasPowers(target) || Players.AlivePawn(target) is null) throw new InvalidOperationException("Select a living powered player. Use css_sp_everyone 1 to give CT players powers.");
        if (cmd.ArgCount<3) throw new ArgumentException("Usage: css_sp_give <player> <power_id> [1|2]");
        var id=cmd.GetArg(2).ToLowerInvariant();
        if (!PowerCatalog.All.Any(x=>x.Id==id)) throw new ArgumentException("Unknown power ID; use css_sp_list.");
        int? slot=null;
        if (cmd.ArgCount>3)
        {
            if (!int.TryParse(cmd.GetArg(3),out var value) || value is < 1 or > 2) throw new ArgumentException("Slot must be 1 or 2.");
            slot=value-1;
        }
        Powers.Give(id,slot,target); cmd.ReplyToCommand("Player still has exactly two different powers.");
    });
    [ConsoleCommand("css_sp_remove","css_sp_remove <player>: clear boss effects and restart the challenge"),RequiresPermissions("@css/generic")]
    public void RemoveCommand(CCSPlayerController? p,CommandInfo cmd) => AdminAction(cmd,()=> {
        var target=FindPlayer(cmd,1);
        if (!Mode.IsBoss(target)) throw new InvalidOperationException("That player has no powers.");
        Mode.ResetRound(); ClearEntities(); Mode.QueueRestart(); cmd.ReplyToCommand("Boss powers removed; a new challenge round is scheduled.");
    });
    [ConsoleCommand("css_sp_list","List power IDs and configuration status"),RequiresPermissions("@css/generic")]
    public void ListCommand(CCSPlayerController? p,CommandInfo cmd) => PowersCommand(p,cmd);
    [ConsoleCommand("css_sp_debug","Print mode diagnostics"),RequiresPermissions("@css/generic")]
    public void DebugCommand(CCSPlayerController? p,CommandInfo cmd)
    {
        cmd.ReplyToCommand($"Superpower1v5 {ModuleVersion}; Enabled={Config.Enabled}; Live={Mode.Live}; Warmup={Mode.Warmup}; Generation={Mode.Generation}; RandomBoss={Config.RotateSuperpoweredPlayer}; PublicPowers={Config.PublicPowerAnnouncements}; Everyone={Config.EveryoneHasPowers}");
        var visible= p is null || Mode.IsBoss(p) || Config.PublicPowerAnnouncements;
        cmd.ReplyToCommand($"Boss={Mode.Boss?.PlayerName??"none"}; Eligible={Mode.Eligible().Count}; Opponents={Mode.Opponents.Count()}; Powers={(visible ? string.Join(',',Powers.Loadout?.Powers.Select(x=>x.Id)??[]) : "private")}");
        foreach (var player in Utilities.GetPlayers().Where(Players.Playing))
            cmd.ReplyToCommand($"#{player.UserId} {player.PlayerName}: team={player.Team}; HP={Players.AlivePawn(player)?.Health??0}; powers={Powers.GetLoadout(player)?.Powers.Count??0}");
    }
    private static CCSPlayerController FindPlayer(CommandInfo cmd,int index)
    {
        if (cmd.ArgCount<=index) throw new ArgumentException("A player name or #userid is required.");
        var text=cmd.GetArg(index); var matches=Utilities.GetPlayers().Where(Players.Connected).Where(x=>
            text.StartsWith('#') ? int.TryParse(text[1..],out var id)&&x.UserId==id : x.PlayerName.Equals(text,StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count==0 && !text.StartsWith('#')) matches=Utilities.GetPlayers().Where(Players.Connected).Where(x=>x.PlayerName.Contains(text,StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count==1 ? matches[0] : throw new ArgumentException(matches.Count==0 ? "No matching player." : "Ambiguous name; use #userid.");
    }
    private void AdminAction(CommandInfo cmd,Action action)
    { try { action(); } catch (Exception e) { cmd.ReplyToCommand("Superpower: "+e.Message); Logger.LogDebug(e,"Admin action rejected"); } }
}
