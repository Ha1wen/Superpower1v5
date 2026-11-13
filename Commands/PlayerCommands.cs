using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Superpower1v5.Models;
using Superpower1v5.Support;

namespace Superpower1v5;

public sealed partial class Plugin
{
    [ConsoleCommand("css_power","Show your two powers")]
    public void PowerCommand(CCSPlayerController? p,CommandInfo cmd)
    {
        if (!Mode.IsBoss(p) || Powers.Loadout is not { } loadout) { cmd.ReplyToCommand(Prefix+"Only the current superpowered player has powers."); return; }
        for (var i=0;i<2;i++) cmd.ReplyToCommand($"{Prefix}{i+1}. {loadout.Powers[i].Name}: {loadout.Powers[i].Description}");
    }
    [ConsoleCommand("css_powers","List all powers; full descriptions are printed to your console")]
    public void PowersCommand(CCSPlayerController? p,CommandInfo cmd)
    {
        foreach (var power in PowerCatalog.All)
        {
            var line=$"{power.Id} | {power.Name} | {power.Description} | {(Config.IsAvailable(power)?"enabled":"disabled")}";
            if (Players.Connected(p)) p!.PrintToConsole(line); else cmd.ReplyToCommand(line);
        }
        if (Players.Connected(p)) cmd.ReplyToCommand(Prefix+$"All {PowerCatalog.All.Count} powers and descriptions were printed to your console.");
    }
    [ConsoleCommand("css_ability1","Activate power slot 1")]
    public void AbilityOne(CCSPlayerController? p,CommandInfo cmd) => ActivateCommand(p,cmd,0);
    [ConsoleCommand("css_ability2","Activate power slot 2")]
    public void AbilityTwo(CCSPlayerController? p,CommandInfo cmd) => ActivateCommand(p,cmd,1);
    private void ActivateCommand(CCSPlayerController? p,CommandInfo cmd,int slot)
    {
        if (!Mode.IsBoss(p)) { cmd.ReplyToCommand(Prefix+"Only the current superpowered player can activate abilities."); return; }
        Guard(()=>Powers.Activate(slot));
    }
    [ConsoleCommand("css_cooldowns","Show remaining ability cooldowns")]
    public void CooldownsCommand(CCSPlayerController? p,CommandInfo cmd)
    {
        if (!Mode.IsBoss(p) || Powers.Loadout is not { } loadout) { cmd.ReplyToCommand(Prefix+"You have no abilities."); return; }
        foreach (var power in loadout.Powers)
            cmd.ReplyToCommand(Prefix+power.Name+": "+Powers.Status(power.Id));
    }
    [ConsoleCommand("css_help","Superpower 1v5 instructions")]
    public void HelpCommand(CCSPlayerController? p,CommandInfo cmd)
    {
        cmd.ReplyToCommand(Prefix+"The T player is the boss and receives TWO private random powers. Join CT to oppose them. Juggernaut gives the boss 500 HP.");
        cmd.ReplyToCommand(Prefix+"!power | !powers | !ability1 | !ability2 | !cooldowns. Console binds: bind v css_ability1; bind b css_ability2");
    }
}
