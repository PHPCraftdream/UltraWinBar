using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Utilities;

internal static class ActivationChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        var guardType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.DesktopActivationGuard");
        var shouldMove = guardType?.GetMethod("ShouldMoveWindow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (shouldMove == null) throw new Exception("Desktop activation guard policy is missing.");
        bool CanMove(Guid source, Guid current, Guid owner, bool onCurrent, long age) =>
            (bool)shouldMove.Invoke(null, new object[] { source, current, owner, onCurrent, age });
        Guid originDesktop = Guid.NewGuid(), otherDesktop = Guid.NewGuid();
        if (!CanMove(originDesktop, originDesktop, otherDesktop, false, 200) ||
            CanMove(originDesktop, otherDesktop, otherDesktop, false, 200) ||
            CanMove(originDesktop, originDesktop, originDesktop, false, 200) ||
            CanMove(originDesktop, originDesktop, otherDesktop, true, 200) ||
            CanMove(originDesktop, originDesktop, otherDesktop, false, 6000))
            throw new Exception("Desktop activation guard may move a window after an intentional switch or stale launch.");
        Console.WriteLine("PASS: desktop activation policy only accepts fresh foreign-window activations on the originating desktop.");
        var recoverAfterSwitch = guardType.GetMethod("ShouldRecoverAfterSwitch", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var returnAfterMove = guardType.GetMethod("ShouldReturnAfterMove", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool CanRecover(Guid source, Guid current, Guid owner, long age, bool inputIntact) =>
            (bool)recoverAfterSwitch.Invoke(null, new object[] { source, current, owner, age, inputIntact });
        bool CanReturn(Guid source, Guid original, Guid current, Guid owner, long age, bool inputIntact) =>
            (bool)returnAfterMove.Invoke(null, new object[] { source, original, current, owner, age, inputIntact });
        if (!CanRecover(originDesktop, otherDesktop, otherDesktop, 200, true) ||
            CanRecover(originDesktop, otherDesktop, otherDesktop, 200, false) ||
            CanRecover(originDesktop, otherDesktop, originDesktop, 200, true) ||
            CanRecover(originDesktop, otherDesktop, otherDesktop, 6000, true) ||
            !CanReturn(originDesktop, otherDesktop, otherDesktop, originDesktop, 2000, true) ||
            CanReturn(originDesktop, otherDesktop, otherDesktop, originDesktop, 2000, false) ||
            CanReturn(originDesktop, otherDesktop, otherDesktop, otherDesktop, 2000, true) ||
            CanReturn(originDesktop, otherDesktop, originDesktop, originDesktop, 2000, true))
            throw new Exception("Desktop return policy may reverse an intentional switch or follow a stale window.");
        Console.WriteLine("PASS: desktop return policy requires the same launch, target window, and originating desktop.");
        var uniqueWindowPolicy = guardType.GetMethod("HasUniqueForeignWindow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool HasUniqueForeignWindow(int current, int remote) =>
            (bool)uniqueWindowPolicy.Invoke(null, new object[] { current, remote });
        if (!HasUniqueForeignWindow(0, 1) || HasUniqueForeignWindow(1, 1) ||
            HasUniqueForeignWindow(0, 2) || HasUniqueForeignWindow(0, 0))
            throw new Exception("Activation could pre-move an unrelated or ambiguous window.");
        Console.WriteLine("PASS: pre-move requires exactly one foreign window and no current window.");

        var completesDoubleClick = guardType.GetMethod("CompletesDoubleClick", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (completesDoubleClick == null) throw new Exception("Double-click completion decision is missing.");
        bool CompletesDoubleClick(bool previousOnDesktop, long previousAt, long now, int previousX, int previousY,
            int x, int y, uint doubleClickMs, int xTolerance, int yTolerance) =>
            (bool)completesDoubleClick.Invoke(null, new object[]
                { previousOnDesktop, previousAt, now, previousX, previousY, x, y, doubleClickMs, xTolerance, yTolerance });
        if (CompletesDoubleClick(false, 0, 100, 10, 10, 12, 11, 500, 4, 4))
            throw new Exception("A completed double-click was recognized without a preceding first click.");
        if (!CompletesDoubleClick(true, 0, 100, 10, 10, 12, 11, 500, 4, 4))
            throw new Exception("A same-position click inside the double-click window and tolerance was not recognized.");
        if (CompletesDoubleClick(true, 0, 600, 10, 10, 12, 11, 500, 4, 4))
            throw new Exception("A click after the double-click window elapsed was still recognized.");
        if (CompletesDoubleClick(true, 0, 100, 10, 10, 20, 11, 500, 4, 4))
            throw new Exception("A click outside the position tolerance was still recognized.");

        var hookThreadType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.DesktopActivationHookThread");
        if (hookThreadType == null) throw new Exception("Desktop activation hook thread host is missing.");
        object CreateHookThread(Func<bool> install, Action uninstall) =>
            Activator.CreateInstance(hookThreadType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new object[] { install, uninstall }, null);
        bool ThrowsFromFailedInstall(Func<bool> install)
        {
            try { CreateHookThread(install, () => { }); }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return true; }
            return false;
        }
        if (!ThrowsFromFailedInstall(() => false))
            throw new Exception("Hook thread host did not throw when install returned false.");
        if (!ThrowsFromFailedInstall(() => throw new InvalidOperationException("install failed")))
            throw new Exception("Hook thread host did not surface an install exception.");

        int callerThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        int installThreadId = -1, uninstallThreadId = -1;
        bool uninstallCalled = false;
        object host = CreateHookThread(
            () => { installThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId; return true; },
            () => { uninstallThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId; uninstallCalled = true; });
        if (installThreadId == -1) throw new Exception("Ctor returned before the install callback completed.");
        if (installThreadId == callerThreadId) throw new Exception("Install callback ran on the caller thread instead of a dedicated host thread.");
        var disposeStopwatch = System.Diagnostics.Stopwatch.StartNew();
        ((IDisposable)host).Dispose();
        disposeStopwatch.Stop();
        if (disposeStopwatch.ElapsedMilliseconds > 1500)
            throw new Exception("Disposing the hook thread host took too long; WM_QUIT may not have reached the message loop.");
        if (!uninstallCalled) throw new Exception("Disposing the hook thread host did not run the uninstall callback.");
        if (uninstallThreadId != installThreadId) throw new Exception("Uninstall did not run on the same thread that installed the hook.");
        ((IDisposable)host).Dispose();
        Console.WriteLine("PASS: double-click completion is a pure per-click decision; the hook thread host waits for install, runs callbacks off the caller thread, and disposes idempotently without deadlock.");
    }
}
