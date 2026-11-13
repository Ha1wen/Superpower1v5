using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using GameTrace = CounterStrikeSharp.API.Modules.Utils.Trace;

namespace Superpower1v5.Support;

public static class SafeTeleport
{
    public const float MaximumTraceDistance = 32768;
    public static float TraceDistance(float configured) => configured == 0 ? MaximumTraceDistance : Math.Clamp(configured,64,MaximumTraceDistance);
    private static readonly TraceOptions Movement = new() { InteractsWith = Masks.PlayerSolid };
    public static bool TryBlink(CCSPlayerPawn p, float distance)
    {
        distance = TraceDistance(distance);
        if (p.AbsOrigin is not { } origin) return false;
        var pitch = p.EyeAngles.X * MathF.PI / 180; var yaw = p.EyeAngles.Y * MathF.PI / 180;
        var eyeHeight=Math.Clamp(p.ViewOffset.Z,8,96);
        var mins=Players.Copy(p.Collision.Mins); var maxs=Players.Copy(p.Collision.Maxs);
        var start = new Vector(origin.X, origin.Y, origin.Z + eyeHeight);
        var goal = new Vector(start.X + MathF.Cos(pitch)*MathF.Cos(yaw)*distance,
            start.Y + MathF.Cos(pitch)*MathF.Sin(yaw)*distance, start.Z - MathF.Sin(pitch)*distance);
        // Follow the aim ray rather than walking a standing body hull over every terrain bump.
        // The first solid surface bounds the blink; the landing still requires full body clearance.
        var sweep = GameTrace.TraceEndShape(start, goal, p, Movement);
        if (sweep.IsAllSolid) return false;
        var available = Math.Max(0, distance*sweep.Fraction-24);
        // Search backwards from the furthest reachable point, including ledges and elevated aim.
        // Bound the number of traces so an unbounded-range activation never stalls the server.
        var minimum = Math.Max(48,available-4096);
        for (var travel=available;travel>=minimum;travel-=64)
        {
            var fraction=travel/distance;
            foreach (var lift in new[] { 0,32,64 })
            {
                var candidate=new Vector(start.X+(goal.X-start.X)*fraction,start.Y+(goal.Y-start.Y)*fraction,start.Z+(goal.Z-start.Z)*fraction-eyeHeight+lift);
                var floor=GameTrace.TraceHullShape(candidate,new(candidate.X,candidate.Y,candidate.Z-1024),mins,maxs,p,Movement);
                if (!floor.DidHit() || floor.IsAllSolid || floor.Normal.Z<.7f) continue;
                var landing=floor.EndPos; landing.Z+=2;
                var clear=GameTrace.TraceHullShape(landing,landing,mins,maxs,p,Movement);
                if (clear.IsAllSolid || clear.DidHit() || Players.Distance(origin,landing)<48) continue;
                var sight=GameTrace.TraceEndShape(start,new(landing.X,landing.Y,landing.Z+eyeHeight),p,Movement);
                if (sight.IsAllSolid || sight.Fraction<.99f) continue;
                p.Teleport(landing,null,new Vector(0,0,0)); return true;
            }
        }
        return false;
    }
    public static bool Visible(Vector from, Vector to, CBaseEntity ignore)
    {
        var ray = GameTrace.TraceEndShape(from, to, ignore, new TraceOptions { InteractsWith = Masks.ShotBrushOnly });
        return !ray.IsAllSolid && ray.Fraction >= .97f;
    }
}
