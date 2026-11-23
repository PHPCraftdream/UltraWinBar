# Modern Glass theme

The default theme (`Assets/Themes/Other/Modern Glass.xaml`): slate surfaces, matte glass and a neon accent, for the
panel and for every window and menu of the program. The older themes (Windows XP, Vista, 2000 and the others) stay in
the list of themes.

## Rules it is built on

Speed comes first, so the theme is made of plain brushes:

- no `Effect` (blur, drop shadow, glow): WPF draws them slowly;
- no gradients (the theme check refuses them in every theme) and no animations or storyboards: a state change is one
  property change and one repaint;
- no animated popups: the popups of menus and drop-downs are ordinary windows, so their corners are square;
- the only layered window (`AllowsTransparency`) is the panel, because the blur behind it needs one; the windows are
  ordinary ones.

`ModernThemeChecks` reads the three files of the theme and fails on any of these, and on a file of over 1000 lines.

## How the glass and the neon are drawn

- The accent colors everything: the surfaces are slate-900, 950 and 800 (Tailwind CSS, MIT) tinted with the accent (14,
  10 and 20 percent), the glass layers are the accent itself at 8, 15 and 22 percent (rest, pointer over, pressed) and
  the hairlines are the accent at 25 and 45 percent. A change of the accent changes the whole look.
- The desktop is blurred behind the panel and behind the windows (the blur DWM does, Windows 10 version 1803 or later,
  the "blur behind" setting of the settings window turns it off). The panel is the theme's layered window and the
  existing blur-behind machinery of the program blurs it; its fill is the tinted panel color at 65 percent, the task
  buttons are layers of the accent over it. A window is not layered: `ModernBackdrop` makes its client area part of the
  frame (`DwmExtendFrameIntoClientArea`), sets the plain DWM blur on it (not the acrylic one, which stutters when the
  window is dragged) and the window's own fill is the tinted base at 72 percent. Where DWM refuses the blur the windows
  paint themselves opaque; with the setting off, or under another theme, nothing is blurred and the fills are opaque.
- Neon: the accent color as a one-pixel border, and two rings of it at 25 and 12 percent outside the border, shown only
  on focus (buttons, boxes, drop-downs), on a checked box and on the slider's thumb under the pointer. The active task
  has a bar of the accent along its foot, and the panel a hairline of it on the side that faces the desktop.
- Text: slate-200, muted slate-400, faint slate-500; dark slate-950 on an accent-colored fill. The check keeps the text
  readable on the tinted surfaces of all six accents.
- Corners: 6 pixels for buttons and fields, 8 for cards, lists and trees (Tailwind `rounded-md` and `rounded-lg`).
- The window title bar is dark through DWM (`DWMWA_USE_IMMERSIVE_DARK_MODE`), set by `ModernTheme.Track` on every
  window of the program while the theme is chosen; DWM draws it, so it costs nothing.

## The accent

`Settings.AccentColor` is one of `cyan` (default), `violet`, `fuchsia`, `emerald`, `amber`, `rose`: the 400 and 500
shades of those Tailwind colors. The theme's dictionary holds the cyan brushes as defaults; `ModernTheme.Apply` (run
when the theme is read and when the accent or the blur setting changes, before the flat panel brushes of
`FlatTheme.Apply`, which derive from the surfaces it sets; `ModernTheme.ApplyPanel` after them) puts the chosen accent
and the tinted surfaces over them at the application level, and takes them away when another theme is chosen. The row to choose it is in
the settings under the theme, shown only for this theme. No restart is needed.

## Where it lives

- `Assets/Themes/Other/Modern Glass.xaml`: the palette, the brushes of the panel, the window style, and the system colors
  (`SystemColors.*BrushKey`) that plain Windows controls without a template of their own follow.
- `Assets/Resources/Brand/ModernControls.xaml` and `ModernMenus.xaml`: templates of buttons, check boxes, text boxes,
  group boxes, tabs, lists, scroll bars, the slider, tool tips, drop-downs, menus and trees. The theme merges them.
- `Assets/Resources/Brand/FlatControls.xaml`: the panel's templates, shared by all themes; the hairline of the panel
  (`FlatPanelEdge`) and the bar of the active task (`FlatActiveBar`) are transparent unless a theme colors them.
- `Core/Appearance/Themes/ModernTheme.cs`: the accent, the tinted surfaces and the dark title bar;
  `ModernBackdrop.cs`: the blur behind a window.
- The windows' own styles (`Shell/Dialogs/SettingsStyles.xaml` and the ones inside the calendar and place windows)
  extend the controls of the theme with `BasedOn="{StaticResource {x:Type ...}}"`; `Themes/Core/System.xaml` holds
  empty base styles for them so that every other theme keeps its look. The open settings window reads its styles again
  when a theme is chosen in it.

## Colors

The palette is the one of Tailwind CSS (MIT License, Copyright (c) Tailwind Labs, Inc.); only the color values are used.
