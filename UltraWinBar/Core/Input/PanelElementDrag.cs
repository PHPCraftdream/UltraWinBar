using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ManagedShell.AppBar;

namespace UltraWinBar.Utilities
{
    internal static class PanelElementDrag
    {
        private const string Format = "UltraWinBar.PanelElement";

        internal static void Run(Taskbar source, string glyph, Action<AppBarEdge> apply)
        {
            ManagedShell.Common.Logging.ShellLogger.Debug($"Panel element drag: started on {source.AppBarEdge}");
            var token = new object();
            var targets = Application.Current.Windows.OfType<Taskbar>()
                .Where(bar => bar.IsVisible).Select(bar => new Target(bar, glyph)).ToArray();
            AppBarEdge? selected = null;
            void Over(object sender, DragEventArgs e)
            {
                if (!ReferenceEquals(e.Data.GetData(Format), token)) return;
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                foreach (var target in targets) target.Highlight(ReferenceEquals(target.Bar, sender));
            }
            void Leave(object sender, DragEventArgs e)
            {
                foreach (var target in targets) target.Highlight(false);
            }
            void Drop(object sender, DragEventArgs e)
            {
                if (!ReferenceEquals(e.Data.GetData(Format), token)) return;
                selected = ((Taskbar)sender).AppBarEdge;
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
            DragDropEffects result;
            try
            {
                foreach (var target in targets)
                {
                    target.Bar.SetCurrentValue(UIElement.AllowDropProperty, true);
                    target.Bar.PreviewDragOver += Over;
                    target.Bar.PreviewDragEnter += Over;
                    target.Bar.PreviewDragLeave += Leave;
                    target.Bar.PreviewDrop += Drop;
                    target.Marker.IsOpen = true;
                }
                Mouse.Capture(null);
                result = DragDrop.DoDragDrop(source, new DataObject(Format, token), DragDropEffects.Move);
            }
            finally
            {
                foreach (var target in targets)
                {
                    target.Marker.IsOpen = false;
                    target.Bar.PreviewDragOver -= Over;
                    target.Bar.PreviewDragEnter -= Over;
                    target.Bar.PreviewDragLeave -= Leave;
                    target.Bar.PreviewDrop -= Drop;
                    target.Bar.SetCurrentValue(UIElement.AllowDropProperty, target.AllowedDrop);
                }
            }
            ManagedShell.Common.Logging.ShellLogger.Debug($"Panel element drag: completed result={result}, target={selected}");
            if (result == DragDropEffects.Move && selected.HasValue) apply(selected.Value);
        }

        private sealed class Target
        {
            internal readonly Taskbar Bar;
            internal readonly bool AllowedDrop;
            internal readonly Popup Marker;
            private readonly Border badge;

            internal Target(Taskbar bar, string glyph)
            {
                Bar = bar;
                AllowedDrop = bar.AllowDrop;
                badge = new Border
                {
                    CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2),
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = new TextBlock { Text = glyph + "  ↓", FontSize = 16, Foreground = Brushes.White }
                };
                Marker = new Popup
                {
                    PlacementTarget = bar, Placement = PlacementMode.Center,
                    AllowsTransparency = true, IsHitTestVisible = false, StaysOpen = true, Child = badge
                };
                Highlight(false);
            }

            internal void Highlight(bool active)
            {
                badge.Background = new SolidColorBrush(active ? Color.FromRgb(35, 104, 156) : Color.FromRgb(47, 60, 77));
                badge.BorderBrush = active ? Brushes.White : Brushes.SlateGray;
            }
        }
    }
}
