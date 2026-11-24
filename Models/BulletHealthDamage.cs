using CounterStrikeSharp.API.Core;

namespace Superpower1v5.Models;

public static class BulletHealthDamage
{
    public static float Estimate(float damage,HitGroup_t group,float headMultiplier,float teamScale,int armor,bool helmet,float armorRatio)
        => Split(damage,group,headMultiplier,teamScale,armor,helmet,armorRatio).Health;

    public static (float Health,float Armor) Split(float damage,HitGroup_t group,float headMultiplier,float teamScale,int armor,bool helmet,float armorRatio)
    {
        var multiplier=group switch {
            HitGroup_t.HITGROUP_HEAD => headMultiplier,
            HitGroup_t.HITGROUP_STOMACH => 1.25f,
            HitGroup_t.HITGROUP_LEFTLEG or HitGroup_t.HITGROUP_RIGHTLEG => .75f,
            _ => 1f
        };
        damage*=multiplier*teamScale;
        var armored=group is HitGroup_t.HITGROUP_CHEST or HitGroup_t.HITGROUP_STOMACH or HitGroup_t.HITGROUP_LEFTARM or HitGroup_t.HITGROUP_RIGHTARM or HitGroup_t.HITGROUP_NECK || group==HitGroup_t.HITGROUP_HEAD && helmet;
        if (armor<=0 || !armored) return (damage,0);
        var health=damage*Math.Clamp(armorRatio*.5f,0,1);
        var absorbed=(damage-health)*.5f;
        return absorbed>armor ? (Math.Max(0,damage-armor*2),armor) : (health,absorbed);
    }
}
