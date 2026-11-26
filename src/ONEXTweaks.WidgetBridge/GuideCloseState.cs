// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal enum GuideCloseResult { Waiting, Closed, TimedOut }
/// <summary>Confirm the native Guide dismissal without sending a second toggle.</summary>
internal sealed class GuideCloseState(long startedAt)
{
    private long closedSince=-1;
    internal GuideCloseResult Observe(bool inputRedirected,long now)
    {
        if(inputRedirected)closedSince=-1;
        else
        {
            if(closedSince<0)closedSince=now;
            if(now-closedSince>=80)return GuideCloseResult.Closed;
        }
        return now-startedAt>=900?GuideCloseResult.TimedOut:GuideCloseResult.Waiting;
    }
}
