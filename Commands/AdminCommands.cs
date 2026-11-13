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
        if (!Mode.Live || !Mode.IsBoss(target)) throw new InvalidOperationException("Only the current boss may receive powers. Use css_sp_setboss first.");
        if (cmd.ArgCount<3) throw new ArgumentException("Usage: css_sp_give <player> <power_id> [1|2]");
        var id=cmd.GetArg(2).ToLowerInvariant();
        if (!PowerCatalog.All.Any(x=>x.Id==id)) throw new ArgumentException("Unknown power ID; use css_sp_list.");
        int? slot=null;
        if (cmd.ArgCount>3)
        {
            if (!int.TryParse(cmd.GetArg(3),out var value) || value is < 1 or > 2) throw new ArgumentException("Slot must be 1 or 2.");
            slot=value-1;
        }
        Powers.Give(id,slot); cmd.ReplyToCommand("Boss still has exactly two different powers.");
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
        cmd.ReplyToCommand($"Superpower1v5 {ModuleVersion}; Enabled={Config.Enabled}; Live={Mode.Live}; Warmup={Mode.Warmup}; Generation={Mode.Generation}");
        var visible= p is null || Mode.IsBoss(p);
        cmd.ReplyToCommand($"Boss={Mode.Boss?.PlayerName??"none"}; Eligible={Mode.Eligible().Count}; Opponents={Mode.Opponents.Count()}; Powers={(visible ? string.Join(',',Powers.Loadout?.Powers.Select(x=>x.Id)??[]) : "private")}");
        foreach (var player in Utilities.GetPlayers().Where(Players.Playing))
            cmd.ReplyToCommand($"#{player.UserId} {player.PlayerName}: team={player.Team}; HP={Players.AlivePawn(player)?.Health??0}; powers={(Mode.IsBoss(player)?Powers.Loadout?.Powers.Count??0:0)}");
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
