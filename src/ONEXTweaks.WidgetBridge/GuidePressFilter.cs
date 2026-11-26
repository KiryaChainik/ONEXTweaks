// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal sealed class GuidePressFilter
{
    private bool held;
    private long releasedAt = -1;
    internal GuidePressFilter(bool initiallyHeld = false) { held = initiallyHeld; }
    internal bool IsHeld => held;

    internal bool Observe(bool valid, bool down, long now)
    {
        // Game Bar temporarily redirects controller input. A failed XInput read is
        // not a physical release and must never arm a second press.
        if (!valid) { releasedAt = -1; return false; }
        if (down)
        {
            releasedAt = -1;
            // Stable physical release is the debounce boundary. A second fixed
            // cooldown would discard legitimate quick presses after that release.
            if (held) return false;
            held = true; return true;
        }
        if (held)
        {
            if (releasedAt < 0) releasedAt = now;
            if (now - releasedAt >= 40) held = false;
        }
        return false;
    }
}
