using System.Windows;
using System.Windows.Media.Animation;

namespace Reminder.App.UI.Interactions;

internal static class UiMotion
{
    private const string ForceAnimationsSwitch = "Reminder.ForceAnimations";
    private const int MaximumFrameRate = 60;

    public static bool AreAnimationsEnabled =>
        SystemParameters.ClientAreaAnimation ||
        AppContext.TryGetSwitch(ForceAnimationsSwitch, out var forceAnimations) &&
        forceAnimations;

    public static void LimitFrameRate(Timeline animation)
    {
        Timeline.SetDesiredFrameRate(animation, MaximumFrameRate);
    }
}
