namespace Superpower1v5.Models;

// Time-based rather than tick-based: frame rate cannot change the fade duration.
public sealed class SilenceFade
{
    private double _lastNoise;
    public void Noise(double now) => _lastNoise=now;
    public byte Alpha(double now,double quietDelay,double fadeDuration) =>
        (byte)Math.Clamp((int)Math.Ceiling(255*(1-Math.Clamp((now-_lastNoise-quietDelay)/fadeDuration,0,1))),0,255);
}
