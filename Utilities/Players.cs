using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Superpower1v5.Support;

public static class Players
{
    public static T? Entity<T>(uint raw) where T : CEntityInstance => new CHandle<T>(raw).Value;
    public static bool Connected(CCSPlayerController? p) => p is { IsValid: true, IsHLTV: false } && p.Connected == PlayerConnectedState.Connected;
    public static bool Playing(CCSPlayerController? p) => Connected(p) && p!.TeamNum is 2 or 3;
    public static CCSPlayerPawn? AlivePawn(CCSPlayerController? p) => Connected(p) &&
        p!.PlayerPawn.Value is { IsValid: true, Health: > 0, LifeState: 0 } pawn ? pawn : null;
    public static CCSPlayerController? Resolve(uint raw)
    {
        var p = Entity<CCSPlayerController>(raw);
        return Connected(p) ? p : null;
    }
    public static string Key(CCSPlayerController p) => !p.IsBot && p.SteamID != 0 ? $"steam:{p.SteamID}" : $"entity:{p.EntityHandle.Raw}";
    public static CCSPlayerController? Controller(CBaseEntity? entity)
    {
        if (entity is not { IsValid: true }) return null;
        if (entity.DesignerName == "player") return entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (entity.DesignerName == "cs_player_controller") return entity.As<CCSPlayerController>();
        return null;
    }
    public static Vector Copy(Vector v) => new(v.X, v.Y, v.Z);
    public static float Distance(Vector a, Vector b) => MathF.Sqrt(MathF.Pow(a.X-b.X,2) + MathF.Pow(a.Y-b.Y,2) + MathF.Pow(a.Z-b.Z,2));
    public static void Heal(CCSPlayerController? p, int amount)
    {
        if (AlivePawn(p) is not { } pawn || amount <= 0) return;
        pawn.Health = Math.Min(Math.Clamp(pawn.MaxHealth,100,500), pawn.Health + amount);
        CounterStrikeSharp.API.Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
    }
    public static bool Firearm(CBasePlayerWeapon? w) => w is { IsValid: true } && w.VData is { MaxClip1: > 0 } && w.DesignerName != "weapon_taser";
}
