# Audio Tray

A lightweight Windows 11 system tray tool for switching favorite audio output and input devices. It is a portable `.exe`, so there is no installer.

## Run It

Double-click `AudioTray.exe`.

The app appears in the system tray. The tray icon follows the current Windows output device icon. Right-click it to switch devices. Double-click the icon, or choose `Favorites...`, to pick which input and output devices appear in the tray menu.

The `Favorites...` window also has a `Settings` tab where you can turn notifications on or off and choose whether Audio Tray runs when Windows starts.

## How Favorites Work

Only devices you mark as favorites appear in the switching sections of the tray menu. The app stores favorites in:

```text
%APPDATA%\AudioTray\settings.json
```

## Optional Startup

To start it when Windows starts:

1. Press `Win + R`.
2. Type `shell:startup`.
3. Put a shortcut to `AudioTray.exe` in that folder.

## Exit

Right-click the tray icon and choose `Exit`.
