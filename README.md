# AudioMix

AudioMix is a small Windows tool for a chat mix: one output device carries your voice chat (Teamspeak, Discord and the like), the other one carries the game audio. AudioMix links the two volumes in opposite directions, so a single slider decides whether you hear more of the chat or more of the game.

```
Volume of device 2 = 100 % - volume of device 1
```

If device 1 is at 30 %, device 2 runs at 70 %. The program reacts to every volume change from the outside, so you can simply use the normal Windows volume slider (or the volume keys and knobs on your keyboard, PC, sound card,...) for device 1. No dedicated mixer hardware needed. If you like, muting device 1 also syncs to device 2, while device 2 can still be separately muted (to mute voicechat while keeping game audio live, for example).

AudioMix runs without a main window in the notification area of the taskbar.

## Requirements

- Windows 10 or 11
- .NET 10 Desktop Runtime
- Two playback devices, see [Hardware setup](#hardware-setup)

## Usage

On the first start the settings window opens. There you choose

- device 1 (leading), select main audio playback device (game audio),
- device 2 (opposing), select chat audio device (e.g. Teamspeak or Discord),
- whether muting should be mirrored,
- whether AudioMix should start with Windows.

Changes take effect immediately, there is no save button. The two devices have to be different.

If you close the window, AudioMix keeps running in the tray. Double-click the icon or use "Settings ..." in the context menu to open it again, "Active" switches the linking on and off, and "Exit" quits the program. If you start AudioMix a second time, the window of the running instance opens instead.

With "Start with Windows" the program adds itself to the autostart of the current user (no administrator rights needed) and then starts without a window in the tray.

If one of the devices is not connected at the moment, for example a Bluetooth headset, AudioMix waits and carries on by itself as soon as the device is back.

## Hardware setup

AudioMix adjusts the volume of two separate playback devices, so game and chat each need their own output. That also means you hear them on two different devices. If you want both on the same headset, you have to bring the two outputs together again with a Y-splitter cable (two plugs in, one plug out for the headset).

AudioMix was developed with an external sound card like the Sound BlasterX G6 in mind, combined with the onboard audio of the mainboard: one carries the game audio, the other one the chat, and a Y-splitter merges both into the headset.

## Settings

Settings are stored in `%AppData%\AudioMix\settings.json`. If you delete the file, AudioMix starts with its default values again.

## License

AudioMix is licensed under the [GNU General Public License v3.0](LICENSE). Everyone may use, modify and share the code, but modified versions have to be released as open source under the GPL as well.

## App Icon

The application icon is part of the Small Flat Vectors icon pack and is distributed under the CC0 1.0 Universal license (public domain dedication).
