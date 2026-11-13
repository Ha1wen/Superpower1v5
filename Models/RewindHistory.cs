using System.Numerics;

namespace Superpower1v5.Models;

public sealed record RewindFrame(double Time,Vector3 Position,Vector3 Angles,Vector3 Velocity,int Health,int Armor);
public sealed class RewindHistory
{
    private readonly List<RewindFrame> _frames=[];
    public int Count => _frames.Count;
    public void Record(RewindFrame frame)
    {
        _frames.Add(frame); _frames.RemoveAll(old=>old.Time<frame.Time-10);
        if (_frames.Count>128) _frames.RemoveRange(0,_frames.Count-128);
    }
    public IEnumerable<RewindFrame> Candidates(double now,double seconds)
    {
        var target=now-seconds;
        return _frames.Where(f=>f.Time<=target).OrderByDescending(f=>f.Time).Concat(_frames.Where(f=>f.Time>target).OrderBy(f=>f.Time));
    }
    public void Clear() => _frames.Clear();
}
