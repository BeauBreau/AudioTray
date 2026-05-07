# Audio Tray

A lightweight Windows 11 system tray tool for switching favorite audio output and input devices. It is a portable `.exe`, so there is no installer.

## Run It

Double-click `AudioTray.exe`.

The app appears in the system tray. Right-click it to switch devices. Double-click the icon, or choose `Favorites...`, to pick which input and output devices appear in the tray menu. The Favorites window shows each device's Windows icon beside its name.

The `Favorites...` window also has a `Settings` tab where you can turn notifications on or off, choose whether Audio Tray runs when Windows starts, choose whether the tray uses the current output device icon or the app icon, open the Sound control panel to change device icons, and control update checks.

## How Favorites Work

Only devices you mark as favorites appear in the switching sections of the tray menu. The app stores favorites in:

```text
%APPDATA%\AudioTray\settings.json
```

## Exit

Right-click the tray icon and choose `Exit`.
