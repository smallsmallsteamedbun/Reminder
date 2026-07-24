namespace Reminder.App.Logic.Models;

internal sealed class ReminderTermination
{
    public int? RemainingOccurrences { get; private set; }

    public void SetRemaining(int? remainingOccurrences)
    {
        if (remainingOccurrences is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingOccurrences));
        }

        RemainingOccurrences = remainingOccurrences;
    }

    public bool ConsumeOccurrence()
    {
        if (RemainingOccurrences is null)
        {
            return false;
        }

        RemainingOccurrences--;
        if (RemainingOccurrences > 0)
        {
            return false;
        }

        RemainingOccurrences = null;
        return true;
    }
}
