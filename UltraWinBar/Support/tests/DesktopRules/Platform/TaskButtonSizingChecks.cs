using System;
using UltraWinBar.Controls;

// Task button sizing of a horizontal panel. Regression: after a switch to a desktop whose list had not been laid out
// yet, the list's width of 0 took the "no room" branch and every button came out half size on a nearly empty panel.
internal static class TaskButtonSizingChecks
{
    internal static void Run()
    {
        const double defaultWidth = 160, minWidth = 45, margin = 6;

        var roomy = TaskButtonSizing.Compute(1604, 4, 1, defaultWidth, minWidth, margin)
            ?? throw new Exception("A laid-out panel must be sized.");
        if (roomy != new TaskButtonSizing(166, 0, 4, false))
            throw new Exception($"With room to spare buttons keep the default width: {roomy}");

        if (TaskButtonSizing.Compute(0, 4, 1, defaultWidth, minWidth, margin) != null)
            throw new Exception("An unknown width (not laid out yet) must leave the buttons alone, not shrink them to half size.");

        var crowded = TaskButtonSizing.Compute(120, 4, 1, defaultWidth, minWidth, margin)
            ?? throw new Exception("A narrow panel must be sized.");
        if (crowded != new TaskButtonSizing(83, 0, 4, true))
            throw new Exception($"Below the minimum width buttons are half the default and the list scrolls: {crowded}");

        var shrunk = TaskButtonSizing.Compute(603.5, 4, 1, defaultWidth, minWidth, margin)
            ?? throw new Exception("A full row must be sized.");
        if (shrunk != new TaskButtonSizing(150, 3, 4, false))
            throw new Exception($"A full row shrinks the buttons and spreads the floored pixels over the columns: {shrunk}");

        var twoRows = TaskButtonSizing.Compute(1000, 10, 2, defaultWidth, minWidth, margin)
            ?? throw new Exception("Two rows must be sized.");
        if (twoRows.ButtonsPerRow != 5 || twoRows.ButtonWidth != 166)
            throw new Exception($"Ten tasks on two rows are five per row: {twoRows}");

        if (TaskButtonSizing.Compute(1000, 3, 0, defaultWidth, minWidth, margin)?.ButtonsPerRow != 3)
            throw new Exception("A panel reporting no rows must not divide by zero.");
        Console.WriteLine("PASS: task buttons keep the default width with room to spare, shrink to fit a full row, go half size only on a genuinely narrow panel, and are left alone while the panel has no width yet.");
    }
}
