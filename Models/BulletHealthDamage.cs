using CounterStrikeSharp.API.Core;

namespace Superpower1v5.Models;

public static class BulletHealthDamage
{
    public static float Estimate(float damage,HitGroup_t group,float headMultiplier,float teamScale,int armor,bool helmet,float armorRatio)
    {
        var multiplier=group switch {
            HitGroup_t.HITGROUP_HEAD => headMultiplier,
            HitGroup_t.HITGROUP_STOMACH => 1.25f,
            HitGroup_t.HITGROUP_LEFTLEG or HitGroup_t.HITGROUP_RIGHTLEG => .75f,
            _ => 1f
        };
        damage*=multiplier*teamScale;
        var armored=group is HitGroup_t.HITGROUP_CHEST or HitGroup_t.HITGROUP_STOMACH or HitGroup_t.HITGROUP_LEFTARM or HitGroup_t.HITGROUP_RIGHTARM or HitGroup_t.HITGROUP_NECK || group==HitGroup_t.HITGROUP_HEAD && helmet;
        if (armor<=0 || !armored) return damage;
        var health=damage*Math.Clamp(armorRatio*.5f,0,1);
        return (damage-health)*.5f>armor ? Math.Max(0,damage-armor*2) : health;
    }
}
