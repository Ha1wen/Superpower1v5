using CounterStrikeSharp.API;
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

    public static bool TrySwap(CCSPlayerPawn first, CCSPlayerPawn second)
    {
        if (first.EntityHandle.Raw==second.EntityHandle.Raw ||
            first.AbsOrigin is not { } firstOrigin || second.AbsOrigin is not { } secondOrigin) return false;
        var firstPosition=Players.Copy(firstOrigin);
        var secondPosition=Players.Copy(secondOrigin);
        if (Players.Distance(firstPosition,secondPosition)<.1f ||
            !CanSwapTo(first,second,secondPosition) || !CanSwapTo(second,first,firstPosition)) return false;
        first.Teleport(secondPosition,null,new Vector(0,0,0));
        second.Teleport(firstPosition,null,new Vector(0,0,0));
        return true;
    }

    private static bool CanSwapTo(CCSPlayerPawn pawn,CCSPlayerPawn outgoing,Vector position)
    {
        // The destination's current occupant leaves in the same update.
        var mins=pawn.Collision.Mins;
        var maxs=pawn.Collision.Maxs;
        var world=GameTrace.TraceHullShape(position,position,mins,maxs,pawn,new() { InteractsWith=Masks.PlayerSolidBrushOnly });
        if (world.DidHit()) return false;
        foreach (var player in Utilities.GetPlayers())
        {
            if (Players.AlivePawn(player) is not { } other || other.Handle==pawn.Handle || other.Handle==outgoing.Handle ||
                other.AbsOrigin is not { } origin) continue;
            var otherMins=other.Collision.Mins;
            var otherMaxs=other.Collision.Maxs;
            if (Overlaps(position.X,mins.X,maxs.X,origin.X,otherMins.X,otherMaxs.X) &&
                Overlaps(position.Y,mins.Y,maxs.Y,origin.Y,otherMins.Y,otherMaxs.Y) &&
                Overlaps(position.Z,mins.Z,maxs.Z,origin.Z,otherMins.Z,otherMaxs.Z)) return false;
        }
        return true;
    }

    private static bool Overlaps(float a,float minA,float maxA,float b,float minB,float maxB) =>
        a+minA<b+maxB && b+minB<a+maxA;

    public static bool TryRandom(CCSPlayerPawn pawn)
    {
        if (pawn.AbsOrigin is not { } origin) return false;
        // The nav mesh limits choices to places intended for players. Never sample world-space air.
        var areas=CCSNavArea.GetAllNavAreas();
        var candidates=new List<Vector>();
        if (areas.Count>0)
        {
            foreach (var area in areas.OrderBy(_=>Random.Shared.Next()).Take(96))
            {
                var center=area.Center;
                if (float.IsFinite(center.X) && float.IsFinite(center.Y) && float.IsFinite(center.Z)) candidates.Add(center);
            }
        }
        // Community maps without navigation still have valid player spawns.
        foreach (var name in new[] { "info_player_terrorist","info_player_counterterrorist","info_deathmatch_spawn" })
            candidates.AddRange(Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(name).Where(e=>e.AbsOrigin is not null).Select(e=>Players.Copy(e.AbsOrigin!)));
        foreach (var candidate in candidates.OrderBy(_=>Random.Shared.Next()).Take(128))
        {
            if (Players.Distance(origin,candidate)<256) continue;
            var above=new Vector(candidate.X,candidate.Y,candidate.Z+32);
            var ground=GameTrace.TraceHullShape(above,new(candidate.X,candidate.Y,candidate.Z-96),pawn.Collision.Mins,pawn.Collision.Maxs,pawn,Movement);
            if (!ground.DidHit() || ground.IsAllSolid || ground.Normal.Z<.7f) continue;
            var landing=ground.EndPos; landing.Z+=2;
            var clear=GameTrace.TraceHullShape(landing,landing,pawn.Collision.Mins,pawn.Collision.Maxs,pawn,Movement);
            if (clear.IsAllSolid || clear.DidHit()) continue;
            if (Utilities.GetPlayers().Any(p=>Players.AlivePawn(p) is { } other && other.EntityHandle.Raw!=pawn.EntityHandle.Raw && other.AbsOrigin is { } pos && Players.Distance(pos,landing)<64)) continue;
            pawn.Teleport(landing,null,new Vector(0,0,0));
            return true;
        }
        return false;
    }
}
