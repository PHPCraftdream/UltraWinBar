using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTray;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace UltraWinBar.Utilities
{
    #region Enums
    public enum InvertIconsOption
    {
        WhenNeededByTheme,
        Always,
        Never
    }

    public enum MultiMonOption
    {
        AllTaskbars,
        SameAsWindow,
        SameAsWindowAndPrimary
    }

    public enum TaskMiddleClickOption
    {
        DoNothing,
        OpenNewInstance,
        CloseTask
    }
    public enum TaskWheelActionOption
    {
        DoNothing,
        ShowHideWindow
    }

    public enum ClockClickOption
    {
        DoNothing,
        OpenAeroCalendar,
        OpenModernCalendar,
        OpenNotificationCenter,
    }

    public enum WinNumHotkeysOption
    {
        WindowsDefault,
        SwitchTasks,
        InvokeQuickLaunch,
    }

    public enum NotifyIconBehavior
    {
        HideWhenInactive,
        AlwaysHide,
        AlwaysShow,
        Remove
    }

    /// <summary>
    /// How a window is identified for a taskbar assignment: chosen per drag by whether
    /// Ctrl is held, not a global setting.
    /// </summary>
    public enum TaskAssignmentMode
    {
        /// <summary>
        /// Group by executable (Ctrl+drag), so every window of an application shares one taskbar.
        /// </summary>
        ExecutablePath,

        /// <summary>
        /// Identify one window lifetime (plain drag). The enum name stays for JSON compatibility.
        /// </summary>
        WindowClassAndTitle
    }
    #endregion

    #region Structs
    public struct NotifyIconBehaviorSetting
    {
        public string Identifier {  get; set; }
        public NotifyIconBehavior Behavior { get; set; }
    }

    public struct TaskbarAssignment
    {
        public Guid DesktopId { get; set; }
        public string Identifier { get; set; }
        public AppBarEdge Edge { get; set; }
        public TaskAssignmentMode Mode { get; set; }
    }

    public struct EdgeSizeSetting
    {
        public AppBarEdge Edge { get; set; }
        public int Size { get; set; }
    }

    public struct QuickLaunchAssignment
    {
        public string Path { get; set; }
        public AppBarEdge Edge { get; set; }
    }

    public struct TaskOrderEntry
    {
        public Guid DesktopId { get; set; }
        public AppBarEdge Edge { get; set; }
        public string Identifier { get; set; }
    }
    #endregion
}
