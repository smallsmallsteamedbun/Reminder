using System.Windows;

namespace Reminder.App.UI.Interactions;

internal static class UiMotion
{
    private const string ForceAnimationsSwitch = "Reminder.ForceAnimations";

    public static bool AreAnimationsEnabled =>
        SystemParameters.ClientAreaAnimation ||
        AppContext.TryGetSwitch(ForceAnimationsSwitch, out var forceAnimations) &&
        forceAnimations;

}
