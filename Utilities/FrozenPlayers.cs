using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Superpower1v5.Support;

public sealed class FrozenPlayers
{
    private sealed record WeaponState(int Primary,float PrimaryRatio,int Secondary,float SecondaryRatio);
    private sealed class State(CCSPlayerPawn pawn)
    {
        public MoveType_t Move { get; }=pawn.MoveType;
        public MoveType_t ActualMove { get; }=pawn.ActualMoveType;
        public Vector Position { get; }=Players.Copy(pawn.AbsOrigin!);
        public Vector Velocity { get; }=Players.Copy(pawn.AbsVelocity);
        public float? NextAttack { get; }=pawn.WeaponServices?.As<CCSPlayer_WeaponServices>().NextAttack;
        public Dictionary<uint,WeaponState> Weapons { get; }=new();
    }
    private readonly Dictionary<uint,State> _players=new();
    public int Count => _players.Count;
    public void Hold(CCSPlayerPawn pawn,double until)
    {
        var raw=pawn.EntityHandle.Raw;
        if (!_players.TryGetValue(raw,out var state)) _players[raw]=state=new(pawn);
        // FL_FROZEN also suppresses native damage in CS2. Lock movement and attack clocks
        // instead so Sandevistan targets remain vulnerable to bullets and other powers.
        pawn.MoveType=MoveType_t.MOVETYPE_NONE; pawn.ActualMoveType=MoveType_t.MOVETYPE_NONE;
        pawn.Teleport(state.Position,null,new Vector(0,0,0));
        pawn.Velocity.X=0; pawn.Velocity.Y=0; pawn.Velocity.Z=0;
        if (pawn.WeaponServices is { } weapons)
        {
            weapons.As<CCSPlayer_WeaponServices>().NextAttack=(float)until;
            foreach (var handle in weapons.MyWeapons)
            {
                if (handle.Value is not { IsValid:true } weapon) continue;
                state.Weapons.TryAdd(weapon.EntityHandle.Raw,new(weapon.NextPrimaryAttackTick,weapon.NextPrimaryAttackTickRatio,weapon.NextSecondaryAttackTick,weapon.NextSecondaryAttackTickRatio));
                weapon.NextPrimaryAttackTick=Server.TickCount+(int)Math.Ceiling(Math.Max(0,until-Server.CurrentTime)/Server.TickInterval);
                weapon.NextSecondaryAttackTick=weapon.NextPrimaryAttackTick;
                Utilities.SetStateChanged(weapon,"CBasePlayerWeapon","m_nNextPrimaryAttackTick");
                Utilities.SetStateChanged(weapon,"CBasePlayerWeapon","m_nNextSecondaryAttackTick");
            }
        }
        Utilities.SetStateChanged(pawn,"CBaseEntity","m_MoveType");
    }
    public void Clear()
    {
        foreach (var (raw,state) in _players)
        {
            var pawn=Players.Entity<CCSPlayerPawn>(raw);
            if (pawn is { IsValid:true })
            {
                pawn.MoveType=state.Move; pawn.ActualMoveType=state.ActualMove;
                if (pawn.Health>0) pawn.Teleport(null,null,state.Velocity);
                if (state.NextAttack is { } next && pawn.WeaponServices is { } service)
                    service.As<CCSPlayer_WeaponServices>().NextAttack=next;
                Utilities.SetStateChanged(pawn,"CBaseEntity","m_MoveType");
            }
            foreach (var (weaponRaw,clock) in state.Weapons)
            {
                var weapon=Players.Entity<CBasePlayerWeapon>(weaponRaw); if (weapon is not { IsValid:true }) continue;
                weapon.NextPrimaryAttackTick=Math.Max(Server.TickCount,clock.Primary); weapon.NextPrimaryAttackTickRatio=clock.PrimaryRatio;
                weapon.NextSecondaryAttackTick=Math.Max(Server.TickCount,clock.Secondary); weapon.NextSecondaryAttackTickRatio=clock.SecondaryRatio;
                Utilities.SetStateChanged(weapon,"CBasePlayerWeapon","m_nNextPrimaryAttackTick");
                Utilities.SetStateChanged(weapon,"CBasePlayerWeapon","m_nNextSecondaryAttackTick");
            }
        }
        _players.Clear();
    }
}
