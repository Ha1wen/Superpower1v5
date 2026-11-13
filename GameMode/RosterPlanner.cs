namespace Superpower1v5.GameMode;

public sealed record Candidate(string Id,bool Bot,int Team);
public sealed record RoundRoster(string Boss,IReadOnlyList<string> Opponents,IReadOnlyList<string> Benched);

public static class RosterPlanner
{
    public static RoundRoster? Plan(IReadOnlyList<Candidate> players,bool allowBots,int maxOpponents,string? incumbent=null)
    {
        var eligible=players.Where(p=>p.Team is 2 or 3 && (allowBots || !p.Bot)).OrderBy(p=>p.Bot).ToList();
        if (eligible.Count<2) return null;
        var terrorists=eligible.Where(p=>p.Team==2).ToArray();
        if (terrorists.Length==0) return null;
        var retained=terrorists.FirstOrDefault(p=>p.Id==incumbent);
        var selected=retained is { Bot:false } ? retained : terrorists.FirstOrDefault(p=>!p.Bot) ?? retained ?? terrorists[0];
        var boss=selected.Id;
        var opponents=eligible.Where(p=>p.Id!=boss).Take(Math.Clamp(maxOpponents,1,5)).Select(p=>p.Id).ToArray();
        var benched=eligible.Where(p=>p.Id!=boss && !opponents.Contains(p.Id)).Select(p=>p.Id).ToArray();
        return new(boss,opponents,benched);
    }
}
