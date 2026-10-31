# Pressure haptics extension

This local extension adds a **Haptics** tab to Wellspring Control Center.
It connects real pressure measurements from Wellspring to a separate Windows
KMDF actuator transport. It has been physically tested on a 2019 16-inch
MacBook Pro (`MacBookPro16,1`, USB `05AC:0340`). Other models are not enabled
by the actuator INF.

## Components

- `src/AmtPtpConfigGui/HapticsPanel.*`: controls, live pressure, test buttons,
  saved settings, and optional sign-in startup.
- `src/WellspringHaptics`: elevated helper. Owns Wellspring's diagnostic feed,
  detects pressure crossings, and sends fixed click requests to the actuator.
- `src/T2HapticTransport`: actuator-only KMDF driver for interface `MI_03`.
  Pointer and keyboard interfaces are separate.

The panel runs without elevation. Its named pipe grants control only to the
current Windows account, administrators, and SYSTEM. Starting the helper or
changing its elevated sign-in task requires UAC. Startup is off by default.
The helper keeps running when the panel closes; **Stop helper** ends it.

## Behavior

The default threshold is 265 raw pressure units, with release at 70% of that
value. Two fresh samples above threshold trigger one waveform. Holding the
press does not repeat it; pressure must fall below the release threshold to
rearm. The helper also requires one confident non-palm contact and physical
button-down, imposes a 250 ms cooldown, and resets after a stale feed.

The raw scale is device-specific. The live meter clips visually at 1000 but
the numeric readout shows larger readings. Light/firm are captured waveform
names, not a guarantee of perceptibly different strength on every device.

Settings are stored in `%LOCALAPPDATA%\WellspringPTP\Haptics\settings.txt`.
Wellspring's diagnostic **Live** feed supports a single owner; while haptics
owns it, use the Haptics tab's meter rather than the separate Live checkbox.
Force Touch's mouse action should be disabled when only haptic feedback is
wanted. This extension does not automatically change pointer profiles.

## Build and validation

Install the .NET 8 SDK, then run:

```powershell
dotnet build src/AmtPtpConfigGui/AmtPtpConfigGui.csproj -c Release
dotnet src/WellspringHaptics/bin/Release/net8.0-windows/WellspringHaptics.dll --selftest
```

The panel build copies the helper beside its executable. For local deployment,
copy the complete output directory to a stable location before enabling startup.
The startup task refers to that location. The helper's CLI also supports
`--watch`, `--light`, `--firm`, `--list`, `--startup-on`, and `--startup-off`.

The actuator driver needs Visual Studio C++ tools plus a matching Windows SDK
and WDK. `src/T2HapticTransport/build.ps1` provides a command-line build;
`-Analyze` enables static analysis of local code. Driver signing and installation
are deliberately separate. Do not distribute a local private signing key,
certificate trust setup, or machine-specific driver backup with source.

## Experimental driver deployment

Current locally built drivers require appropriate signing/Test Mode. Installing
or testing kernel drivers can crash Windows or disrupt input. Keep a keyboard
or external mouse available and export the existing pointer package first.
Do not disable Test Mode while dependent experimental drivers are installed.
Revert each affected interface to its original driver before cleaning up test
trust. Never replace the composite parent, keyboard, or Apple USB host driver.

Sleep/wake recovery and long-term stability have not been established. If the
pressure feed disconnects, the helper stops; the panel then offers Start helper.

## Attribution

WellspringPTP is by Osaul4ik and derives from imbushuo/mac-precision-touchpad.
The captured actuator waveforms and protocol are adapted from André Eikmeyer's
`kaiT2en/KaiT2en-Fedora`, specifically
`modules/t2_trackpad_actuator/t2_trackpad_actuator.c` (GPL-2.0-only):
https://github.com/kaiT2en/KaiT2en-Fedora

The new actuator/helper source is GPL-2.0-only; see `LICENSE-GPL.md`.
