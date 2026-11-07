# spen-mouse

An air mouse for Windows, powered by the Galaxy S Pen. Hold the button, wave the pen, the cursor follows. Tap the button to click.

Inspired by [AirPointer](https://github.com/musubipapi/airpointer), which did this with a closed AirPods case and a lot of reverse engineering. The S Pen version is easier: Samsung publishes an [SDK](https://developer.samsung.com/galaxy-spen-remote/s-pen-remote-sdk.html) that streams the pen's motion to the phone.

Two parts: an Android app that forwards the pen's air-motion deltas and button presses over UDP, and a Windows console app that turns them into cursor movement with `SendInput`. Under 150 lines of Kotlin and under 100 of C#.

Status: works. Hold the pen button to move, tap it to click, release it to reposition your arm (the cursor stays put, like lifting a mouse). See `NOTES.md` for the build log.

## Windows receiver

    cd windows
    dotnet run            # defaults: gain 40, smoothing 0.5
    dotnet run -- 60 0.3  # more gain, less lag

In another terminal, `.ake-pen.ps1` sends a circle and a click to prove the pipe works before the phone is involved.

## Android forwarder

Needs the [S Pen Remote SDK](https://developer.samsung.com/galaxy-spen-remote/s-pen-remote-sdk.html)
(free Samsung developer login). Unzip it and copy `spenremote-v1.0.1.jar` and `sdk-v1.0.0.jar` into
`android/app/libs/`. They are git-ignored. Then open `android/` in Android Studio, or:

    cd android
    ./gradlew installDebug

Type the laptop's IP into the box, hold the pen button, wave. The phone and laptop must be on the same Wi-Fi.

## Things that bit

- **Android 16+ blocks LAN traffic** until the app holds the `ACCESS_LOCAL_NETWORK` runtime permission. Accept the prompt on first launch, or every packet silently fails.
- **The pen hangs up after a minute** if an app keeps the SDK's air-motion listener registered: that puts the pen's gyro in always-on mode and drains it. This app registers for motion only while the button is in use. Docking the pen resets its mode; one button press re-arms it.
- **Decimal commas.** The protocol is text, so both ends format and parse with an invariant locale.

The full build log, including the wrong turns, is in [NOTES.md](NOTES.md).

## Prior art

- [musubipapi/airpointer](https://github.com/musubipapi/airpointer), the AirPods-case air mouse that started the idea.
- [th3-s7r4ng3r/SPEN-To-PC](https://github.com/th3-s7r4ng3r/SPEN-To-PC), phone-to-PC S Pen gestures over TCP.
