using System.Reflection;
using System.Text.Json;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using Superpower1v5;
using Superpower1v5.Configuration;
using Superpower1v5.GameMode;
using Superpower1v5.Managers;
using Superpower1v5.Models;
using Superpower1v5.Support;
using CounterStrikeSharp.API.Core;

if (args.Length==2 && args[0]=="--migrate-config")
{
    var migrated=JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(args[1]))!;
    migrated.Validate();
    File.WriteAllText(args[1],JsonSerializer.Serialize(migrated,new JsonSerializerOptions { WriteIndented=true }));
    Console.WriteLine("Configuration migrated to version "+migrated.Version+" with "+migrated.Powers.Count+" powers.");
    return 0;
}
var tests=new List<(string,Action)>();
void Check(bool success,string message) { if (!success) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Invalid input was accepted."); }
PluginConfig Default() { var c=new PluginConfig(); c.Validate(); return c; }

tests.Add(("All 18 powers have unique IDs and concrete implementations",()=> {
    Check(PowerCatalog.All.Count==18 && PowerCatalog.All.Select(p=>p.Id).Distinct().Count()==18,"Catalog count or duplicate IDs");
    foreach (var p in PowerCatalog.All) Check(PowerManager.Create(p.Id).Id==p.Id,"Factory missing "+p.Id);
}));
tests.Add(("Mode configuration repairs invalid and null input",()=> {
    var c=new PluginConfig { NormalHealth=999,SuperpoweredPlayerCount=6,PowersPerRound=9,AllowDuplicatePowers=true,
        MaxOpponents=88,SuperpoweredPlayerTeam="bogus",Powers=null!,PowerBlacklist=null! };
    c.Validate();
    Check(c.NormalHealth==100 && c.SuperpoweredPlayerCount==1 && c.PowersPerRound==2 && !c.AllowDuplicatePowers,"Mode invariant violated");
    Check(c.MaxOpponents==5 && c.SuperpoweredPlayerTeam=="T" && c.Powers.Count==18,"Defaults not repaired");
    c.Powers["teleport"].Distance=float.NaN; c.Powers["rapid_fire"].Strength=99; c.Validate();
    Check(c.Powers["teleport"].Distance==0 && c.Powers["rapid_fire"].Strength==1.6f,"Unsafe tuning accepted");
}));
tests.Add(("50,000 random rounds retain exactly two distinct compatible powers",()=> {
    var s=new SelectionEngine(new Random(42)); var c=Default(); var changed=false; string? last=null;
    for (var i=0;i<50000;i++) { var pair=s.SelectPair(c); Check(pair.Count==2 && PowerCatalog.Compatible(pair[0],pair[1]),"Invalid pair");
        var key=string.Join(',',pair.Select(p=>p.Id)); changed|=last is not null && last!=key; last=key; }
    Check(changed,"Reroll did not change powers");
}));
tests.Add(("50,000 rounds give every power equal probability in both slots",()=> {
    var s=new SelectionEngine(new Random(91)); var c=Default(); const int n=50000;
    var first=PowerCatalog.All.ToDictionary(p=>p.Id,_=>0); var second=PowerCatalog.All.ToDictionary(p=>p.Id,_=>0);
    for (var i=0;i<n;i++) { var pair=s.SelectPair(c); first[pair[0].Id]++; second[pair[1].Id]++; }
    foreach (var id in first.Keys)
    {
        Check(Math.Abs((double)first[id]/n-1.0/18)<.005,$"First slot biased: {id}: {first[id]}");
        Check(Math.Abs((double)second[id]/n-1.0/18)<.005,$"Second slot biased: {id}: {second[id]}");
    }
}));
tests.Add(("Blacklists, disabled powers and insufficient pools are safe",()=> {
    var c=Default(); c.PowerBlacklist=["teleport","wall_vision"]; c.Powers["super_speed"].Enabled=false;
    var s=new SelectionEngine(new Random(7));
    for (var i=0;i<1000;i++) Check(s.SelectPair(c).All(c.IsAvailable),"Unavailable power selected");
    foreach (var p in c.Powers.Values) p.Enabled=false;
    Reject(()=>s.SelectPair(c)); c.Powers["infinite_ammo"].Enabled=true; Reject(()=>s.SelectPair(c));
    c.Powers["low_gravity"].Enabled=true; Check(s.SelectPair(c).Count==2,"Two-power pool rejected");
}));
tests.Add(("Compatibility is symmetric and excludes duplicate IDs",()=> {
    var a=new PowerDefinition("a","A","",PowerKind.Passive,["b"]);
    var b=new PowerDefinition("b","B","",PowerKind.Passive);
    Check(!PowerCatalog.Compatible(a,b) && !PowerCatalog.Compatible(b,a) && !PowerCatalog.Compatible(a,a),"Compatibility failure");
}));
tests.Add(("One boss and up to five normal opponents; spectators never selected",()=> {
    var players=Enumerable.Range(0,8).Select(i=>new Candidate(i.ToString(),false,i==7 ? 1 : i==0 ? 2 : 3)).ToList();
    for (var i=0;i<40;i++) { var roster=RosterPlanner.Plan(players,true,5)!;
        Check(roster.Boss!="7" && roster.Opponents.Count==5 && !roster.Opponents.Contains(roster.Boss),"Roster invariant violated");
        Check(!roster.Opponents.Contains("7") && roster.Benched.Count==1,"Spectator or bench failure"); }
    Check(RosterPlanner.Plan([new("solo",false,2)],true,5) is null,"Solo challenge started");
    Check(RosterPlanner.Plan([new("a",false,2),new("b",false,3)],true,5)!.Opponents.Count==1,"1v1 failed");
}));
tests.Add(("The chosen T player stays boss and CT players are never promoted",()=> {
    var players=new[] {new Candidate("ct",false,3),new Candidate("boss",false,2),new Candidate("ct2",false,3)};
    for (var i=0;i<40;i++) Check(RosterPlanner.Plan(players,true,5,"boss")!.Boss=="boss","boss rotated");
    Check(RosterPlanner.Plan([new("ct1",false,3),new("ct2",false,3)],true,5) is null,"CT was automatically promoted");
    var multiple=new[] {new Candidate("first",false,2),new Candidate("incumbent",false,2),new Candidate("ct",false,3)};
    Check(RosterPlanner.Plan(multiple,true,5,"incumbent")!.Boss=="incumbent","incumbent lost T slot");
}));
tests.Add(("A human choosing T replaces a bot; bot exclusion and spectators remain respected",()=> {
    var players=new[] {new Candidate("bot",true,2),new Candidate("human",false,2),new Candidate("ct",false,3),new Candidate("spectator",false,1)};
    Check(RosterPlanner.Plan(players,true,5,"bot")!.Boss=="human","T human did not replace bot");
    Check(RosterPlanner.Plan([new("bot",true,2),new("ct",false,3)],false,5) is null,"excluded bot included");
    Check(RosterPlanner.Plan([new("bot1",true,2),new("bot2",true,3)],true,5) is not null,"Bot-only testing unsupported");
}));
tests.Add(("Power replacements retain exactly two distinct retained powers",()=> {
    var c=Default(); var s=new SelectionEngine(new Random(1)); var kept=PowerCatalog.Get("infinite_ammo");
    var state=new RoundLoadout([kept,PowerCatalog.Get("teleport")]);
    for (var i=0;i<5000;i++) { var next=s.Replacement(kept,state.Powers.Select(p=>p.Id),c); state.Replace(1,next);
        Check(state.Powers.Count==2 && state.Powers[0].Id==kept.Id && state.Powers[1].Id!=kept.Id,"Replacement broke loadout"); }
}));
tests.Add(("Cooldowns and one-use flags survive replacements and mid-round rerolls",()=> {
    var l=new RoundLoadout([PowerCatalog.Get("teleport"),PowerCatalog.Get("second_chance")]);
    l.StartCooldown("teleport",10,15); l.Consumed.Add("second_chance");
    Check(l.Remaining("teleport",20)==5 && l.Remaining("teleport",26)==0,"Cooldown clock failure");
    l.Replace(0,PowerCatalog.Get("super_speed")); l.Replace(0,PowerCatalog.Get("teleport"));
    l.SetPair([PowerCatalog.Get("second_chance"),PowerCatalog.Get("teleport")]);
    Check(l.Remaining("teleport",20)==5 && !l.Consumed.Add("second_chance"),"Reroll reset round progress");
    Reject(()=>new RoundLoadout([PowerCatalog.Get("teleport")]));
    Reject(()=>l.Replace(0,PowerCatalog.Get("teleport")));
}));
tests.Add(("Every administrator entry point requires CounterStrikeSharp permissions",()=> {
    var admin=typeof(Plugin).GetMethods().Where(m=>m.GetCustomAttributes<ConsoleCommandAttribute>().Any(a=>a.Command.StartsWith("css_sp_"))).ToList();
    Check(admin.Count==12,"Missing admin commands");
    foreach (var m in admin) Check(m.GetCustomAttributes<RequiresPermissions>().Any(),"Unprotected command: "+m.Name);
}));
tests.Add(("Default configuration round-trips all 18 power settings",()=> {
    var c=Default(); var json=JsonSerializer.Serialize(c); var copy=JsonSerializer.Deserialize<PluginConfig>(json)!; copy.Validate();
    Check(copy.Powers.Count==18 && copy.Powers["vampirism"].Strength==1 && copy.Powers["teleport"].Cooldown==15,"Config round-trip failure");
}));
tests.Add(("Removed powers cannot be selected, assigned, or retained in migrated config",()=> {
    var retired=new[] { "super_jump","double_jump","regeneration","damage_resistance","glass_cannon","freeze_shot","damage_reflection","lucky_shot","randomizer","yoru_flash" };
    var c=Default(); c.Version=1;
    foreach (var id in retired) { c.Powers[id]=new(); Reject(()=>PowerManager.Create(id)); }
    c.Powers["super_speed"].Strength=1.5f; c.Powers["vampirism"].Strength=.3f;
    c.Powers["teleport"].Distance=450; c.Powers["teleport"].Cooldown=7; c.Powers["explosive_bullets"].Strength=18;
    c.Validate();
    Check(retired.All(id=>!c.Powers.ContainsKey(id) && !PowerCatalog.All.Any(p=>p.Id==id)),"Removed power survived");
    Check(c.Version==7 && c.Powers["super_speed"].Strength==2.5f && c.Powers["vampirism"].Strength==1 && c.Powers["explosive_bullets"].Strength==55,"Balance migration failed");
    Check(c.Powers["teleport"].Distance==0 && c.Powers["teleport"].Cooldown==7,"Migration lost customized cooldown");
}));
tests.Add(("Unlimited teleport uses a finite world trace with no old 1200-unit clamp",()=> {
    var c=Default(); Check(SafeTeleport.TraceDistance(0)==32768,"Unlimited trace range");
    c.Powers["teleport"].Distance=16000; c.Validate();
    Check(c.Powers["teleport"].Distance==16000 && SafeTeleport.TraceDistance(16000)==16000,"Long distance clamped");
    c.Powers["teleport"].Distance=float.PositiveInfinity; c.Validate(); Check(c.Powers["teleport"].Distance==0,"Unsafe float accepted");
}));
tests.Add(("Production assembly excludes native probe and damage test injection",()=> {
    Check(typeof(PowerManager).GetMethod("NativeProbePair") is null,"Native probe present in production");
    Check(typeof(DamageService).GetMethod("NativeProbeHit") is null,"Damage test injector present in production");
    Check(typeof(DamageService).GetMethod("NativeProbeEntityHit") is null,"Entity damage test injector present in production");
    Check(typeof(Plugin).GetMethod("BeginNativeProbe",BindingFlags.NonPublic|BindingFlags.Instance) is null,"Smoke startup present in production");
}));

tests.Add(("Null native damage buffers are rejected before schema access",()=> {
    Check(!DamageService.HasResult(null,null),"Null wrappers accepted");
    Check(!DamageService.HasResult(new CTakeDamageInfo(IntPtr.Zero),new CTakeDamageResult(IntPtr.Zero)),"Null native pointers accepted");
    Check(!DamageService.HasResult(new CTakeDamageInfo((IntPtr)1),new CTakeDamageResult(IntPtr.Zero)),"Null result accepted");
}));
tests.Add(("Native damage and player_hurt apply effects exactly once in either order",()=> {
    var receipts=new DamageReceipts();
    Check(receipts.Accept(1,2,10,20,80,false),"native-only damage rejected");
    Check(!receipts.Accept(1,2,10,20,80,true),"native/event duplicate accepted");
    Check(receipts.Accept(1,2,10,20,60,true),"second hit rejected");
    Check(!receipts.Accept(1,2,10,20,60,false),"event/native duplicate accepted");
    Check(receipts.Accept(1,2,11,20,80,true),"next tick rejected");
    Check(receipts.Accept(1,3,11,20,80,false),"different attacker rejected");
    receipts.Clear(); Check(receipts.Accept(1,2,11,20,80,false),"round reset failed");
}));
tests.Add(("Version 2 migration adds retained new powers and preserves customized settings",()=> {
    var c=Default(); c.Version=2;
    var added=new[] { "bhop","sandevistan","pyromaniac","random_explosion","tiny" };
    foreach (var id in added) c.Powers.Remove(id);
    c.Powers["super_speed"].Strength=3; c.Powers["teleport"].Cooldown=8; c.Validate();
    Check(c.Version==7 && c.Powers.Count==18 && added.All(c.Powers.ContainsKey),"Missing new powers");
    Check(c.Powers["super_speed"].Strength==3 && c.Powers["teleport"].Cooldown==8,"Customized settings changed");
    Check(c.Powers["sandevistan"].Duration==10 && c.Powers["random_explosion"].Duration==0 && c.Powers["random_explosion"].Cooldown==20,"Requested durations changed");
}));
tests.Add(("New power tuning validates scale, entity limits, and effect durations",()=> {
    var c=Default(); c.Powers["tiny"].Strength=.01f; c.Powers["pyromaniac"].MaxEntities=999;
    c.Powers["sandevistan"].Duration=float.NaN; c.Powers["random_explosion"].Duration=5000;
    c.Validate();
    Check(c.Powers["tiny"].Strength==.5f && c.Powers["pyromaniac"].MaxEntities==16,"Unsafe scale/entity cap");
    Check(c.Powers["sandevistan"].Duration==10 && c.Powers["random_explosion"].Duration==0,"Invalid duration retained");
}));
tests.Add(("Wall Vision and Invisibility always validate to unlimited duration and no cooldown",()=> {
    var c=Default();
    foreach (var id in new[] { "wall_vision","invisibility" })
    {
        c.Powers[id].Duration=5; c.Powers[id].Cooldown=25;
        c.Validate(); Check(c.Powers[id].Duration==0 && c.Powers[id].Cooldown==0,"Timed effect survived: "+id);
    }
}));
tests.Add(("Version 3 JSON drops rarities and Yoru while preserving unrelated tuning",()=> {
    const string old="""
    {"ConfigVersion":3,"RarityWeights":{"Common":10000,"Rare":0,"Epic":0,"Legendary":0},
     "Powers":{"yoru_flash":{},"super_speed":{"Strength":3},"teleport":{"Cooldown":8},
     "invisibility":{"Duration":5,"Cooldown":25,"Strength":40},"wall_vision":{"Duration":6,"Cooldown":25}}}
    """;
    var c=JsonSerializer.Deserialize<PluginConfig>(old)!; c.Validate(); var json=JsonSerializer.Serialize(c);
    Check(c.Version==7 && c.Powers.Count==18 && !json.Contains("RarityWeights") && !json.Contains("yoru_flash"),"Removed configuration survived");
    Check(c.Powers["super_speed"].Strength==3 && c.Powers["teleport"].Cooldown==8 && c.Powers["invisibility"].Strength==40,"Customized settings lost");
    Check(typeof(PowerDefinition).GetProperty("Rarity") is null,"Rarity remains in the catalog model");
}));
tests.Add(("Version 4 migration makes explosions immediate with a 20-second cooldown",()=> {
    var c=Default(); c.Version=4;
    c.Powers["random_explosion"].Duration=20; c.Powers["random_explosion"].Cooldown=45;
    c.Powers["random_explosion"].Strength=600; c.Powers["teleport"].Cooldown=8;
    c.Validate();
    Check(c.Version==7 && c.Powers["random_explosion"].Duration==0 && c.Powers["random_explosion"].Cooldown==20,"Old fuse/cooldown survived");
    Check(c.Powers["random_explosion"].Strength==600 && c.Powers["teleport"].Cooldown==8,"Other tuning changed");
    var loadout=new RoundLoadout([PowerCatalog.Get("random_explosion"),PowerCatalog.Get("infinite_ammo")]);
    loadout.StartCooldown("random_explosion",10,c.Powers["random_explosion"].Cooldown);
    loadout.Replace(0,PowerCatalog.Get("teleport")); loadout.Replace(0,PowerCatalog.Get("random_explosion"));
    Check(loadout.Remaining("random_explosion",29.99)>.009 && loadout.Remaining("random_explosion",30)==0,"Cooldown boundary or replacement bypass");
}));
tests.Add(("Invisibility and Wall Vision are passive and require no activation",()=> {
    foreach (var id in new[] { "invisibility","wall_vision" })
        Check(PowerCatalog.Get(id).Kind==PowerKind.Passive,"Still activated: "+id);
    Check(PowerCatalog.Get("random_explosion").Kind==PowerKind.Activated,"Explosion activation changed");
}));
tests.Add(("Version 5 migration adds Juggernaut and Cluster Grenade and preserves optional rotation",()=> {
    var c=Default(); c.Version=5; c.RotateSuperpoweredPlayer=true; c.SuperpoweredPlayerTeam="CT";
    c.Powers.Remove("juggernaut"); c.Powers.Remove("cluster_grenade"); c.Powers["second_chance"].Duration=.1f;
    c.Powers["teleport"].Cooldown=8; c.Validate();
    Check(c.Version==7 && c.Powers.Count==18 && c.SuperpoweredPlayerTeam=="T" && c.RotateSuperpoweredPlayer,"team/config migration");
    Check(c.Powers["juggernaut"].Strength==500 && c.Powers["cluster_grenade"].Strength==4 && c.Powers["second_chance"].Duration==3,"new settings");
    Check(c.Powers["teleport"].Cooldown==8,"unrelated tuning changed");
}));
tests.Add(("Rewind picks a historical frame near three seconds and supports short histories",()=> {
    var h=new RewindHistory();
    for (var i=0;i<60;i++) h.Record(new(i*.1,new(i,0,0),default,default,100-i,20));
    var frame=h.Candidates(5.9,3).First();
    Check(frame.Time<=2.901 && frame.Time>=2.799 && frame.Position.X<=29 && frame.Health>=71,"wrong rewind age/state");
    Check(h.Candidates(1,3).First().Time==0,"short history did not use earliest safe sample");
    h.Clear(); Check(!h.Candidates(10,3).Any(),"history cleanup");
}));
tests.Add(("Rewind memory is bounded and never retains old-round history",()=> {
    var h=new RewindHistory();
    for (var i=0;i<5000;i++) h.Record(new(i*.01,default,default,default,500,100));
    Check(h.Count<=128 && h.Candidates(50,3).All(f=>f.Time>=39.9),"unbounded history");
    h.Clear(); Check(h.Count==0,"round cleanup failed");
}));
tests.Add(("Random promotion chooses eligible humans, changes boss, and supports an empty T side",()=> {
    Candidate[] players=[new("old",false,2),new("new",false,3),new("bot",true,3),new("spec",false,1)];
    for (var seed=0;seed<30;seed++)
    {
        var roster=RosterPlanner.Plan(players,true,5,"old",true,new Random(seed))!;
        Check(roster.Boss=="new" && roster.Opponents.Contains("old"),"Wrong promotion candidate");
        Check(!roster.Opponents.Contains("spec"),"Selected spectator");
    }
    Candidate[] ctOnly=[new("a",true,3),new("b",true,3)];
    Check(RosterPlanner.Plan(ctOnly,true,5) is null,"Manual mode promoted CT");
    Check(RosterPlanner.Plan(ctOnly,true,5,randomize:true) is not null,"Random mode needs an existing T");
    Check(RosterPlanner.Plan(players,true,5,"old")!.Boss=="old","Manual T ownership changed");
}));
tests.Add(("Invisibility reveals immediately, fades after silence, and restarts its quiet timer",()=> {
    var fade=new SilenceFade(); fade.Noise(10);
    Check(fade.Alpha(10,.5,1)==255 && fade.Alpha(10.5,.5,1)==255,"Noisy grace period");
    Check(fade.Alpha(11,.5,1)==128 && fade.Alpha(11.5,.5,1)==0,"Fade timing");
    fade.Noise(11.4); Check(fade.Alpha(11.5,.5,1)==255,"Sound did not reset fade");
    for (var i=0;i<100;i++) { fade.Noise(20+i*.1); Check(fade.Alpha(20+i*.1,.5,1)==255,"Sustained sound concealed"); }
    Check(fade.Alpha(100,.5,1)==0,"Never concealed after sustained noise");
}));
tests.Add(("Version 7 mode switches persist and invalid fade timing is repaired",()=> {
    var c=Default(); Check(!c.RotateSuperpoweredPlayer && !c.PublicPowerAnnouncements && !c.EveryoneHasPowers,"Unsafe mode defaults");
    c.RotateSuperpoweredPlayer=true; c.PublicPowerAnnouncements=true; c.EveryoneHasPowers=true;
    c.InvisibilityQuietDelay=float.NaN; c.InvisibilityFadeDuration=0; c.Validate();
    var copy=JsonSerializer.Deserialize<PluginConfig>(JsonSerializer.Serialize(c))!; copy.Validate();
    Check(copy.RotateSuperpoweredPlayer && copy.PublicPowerAnnouncements && copy.EveryoneHasPowers,"Switches lost on validation/roundtrip");
    Check(copy.InvisibilityQuietDelay==.5f && copy.InvisibilityFadeDuration==1,"Invalid fade timing retained");
}));
tests.Add(("Lethal bullet prediction includes head/stomach/leg scaling, helmet armor and low armor",()=> {
    Check(BulletHealthDamage.Estimate(35,HitGroup_t.HITGROUP_HEAD,4,1,0,false,1.55f)==140,"unarmored headshot missed");
    Check(BulletHealthDamage.Estimate(35,HitGroup_t.HITGROUP_STOMACH,4,1,0,false,2)==43.75f,"stomach scaling");
    Check(BulletHealthDamage.Estimate(35,HitGroup_t.HITGROUP_LEFTLEG,4,1,100,true,1)==26.25f,"leg armor applied");
    Check(BulletHealthDamage.Estimate(33,HitGroup_t.HITGROUP_HEAD,4,1,100,true,1.4f)<100,"nonlethal helmet hit predicted lethal");
    Check(BulletHealthDamage.Estimate(35,HitGroup_t.HITGROUP_HEAD,4,1,100,true,1.55f)>100,"lethal helmet hit missed");
    Check(BulletHealthDamage.Estimate(100,HitGroup_t.HITGROUP_CHEST,4,1,10,false,1)==80,"low armor depletion");
    Check(BulletHealthDamage.Estimate(35,HitGroup_t.HITGROUP_HEAD,4,.5f,0,false,2)==70,"team scaling");
}));
var failed=0;
foreach (var (name,action) in tests)
{
    try { action(); Console.WriteLine("PASS "+name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL "+name+": "+e.Message); }
}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} tests passed. Native CS2 gameplay is not covered by this runner.");
if (failed==0 && args.Length==2 && args[0]=="--write-config")
    File.WriteAllText(args[1],JsonSerializer.Serialize(Default(),new JsonSerializerOptions { WriteIndented=true }));
return failed==0 ? 0 : 1;
