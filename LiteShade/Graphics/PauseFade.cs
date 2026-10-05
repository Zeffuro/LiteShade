using System;
using System.Diagnostics;

namespace LiteShade.Graphics;

internal struct PauseFade
{
    private bool _initialized;
    private bool _paused;
    private float _from;
    private float _duration;
    private long _started;

    public float GetAmount(bool paused, float seconds)
    {
        var target = paused ? 0f : 1f;
        if (!_initialized || seconds <= 0)
        {
            _initialized = true;
            _paused = paused;
            _duration = 0;
            return _from = target;
        }

        var current = _paused ? 0f : 1f;
        if (_duration > 0)
        {
            var amount = Math.Clamp((float)Stopwatch.GetElapsedTime(_started).TotalSeconds / _duration, 0f, 1f);
            current = float.Lerp(_from, current, amount);
            if (amount == 1) _duration = 0;
        }
        if (paused != _paused)
        {
            _from = current;
            _paused = paused;
            _started = Stopwatch.GetTimestamp();
            _duration = seconds;
        }

        return current;
    }
}
