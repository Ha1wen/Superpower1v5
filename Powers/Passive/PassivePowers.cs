using Superpower1v5.Support;

namespace Superpower1v5.Powers.Passive;

public sealed class InfiniteAmmo() : Power("infinite_ammo")
{
    public override void Tick(PowerContext context)
    {
        var weapon = context.Pawn?.WeaponServices?.ActiveWeapon.Value;
        if (!Players.Firearm(weapon) || weapon!.Clip1 == weapon.VData!.MaxClip1) return;
        weapon.Clip1 = weapon.VData.MaxClip1;
        CounterStrikeSharp.API.Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
    }
}
