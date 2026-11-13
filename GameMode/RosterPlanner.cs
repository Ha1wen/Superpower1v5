using Superpower1v5.Managers;

namespace Superpower1v5.GameMode;

public sealed record Candidate(string Id,bool Bot,int Team);
public sealed record RoundRoster(string Boss,IReadOnlyList<string> Opponents,IReadOnlyList<string> Benched);

public static class RosterPlanner
{
    public static RoundRoster? Plan(IReadOnlyList<Candidate> players,FairRotation rotation,bool allowBots,
        int maxOpponents,bool rotate,string? previous,string? forced)
    {
        var eligible=players.Where(p=>p.Team is 2 or 3 && (allowBots || !p.Bot)).OrderBy(p=>p.Bot).ToList();
        if (eligible.Count<2) return null;
        var humans=eligible.Where(p=>!p.Bot).ToList();
        var candidates=humans.Count>0 ? humans : eligible;
        var boss=rotation.Choose(candidates.Select(p=>p.Id).ToList(),rotate,previous,forced);
        var opponents=eligible.Where(p=>p.Id!=boss).Take(Math.Clamp(maxOpponents,1,5)).Select(p=>p.Id).ToArray();
        var benched=eligible.Where(p=>p.Id!=boss && !opponents.Contains(p.Id)).Select(p=>p.Id).ToArray();
        return new(boss,opponents,benched);
    }
}
