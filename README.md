# spen-mouse

An air mouse for Windows, powered by the Galaxy S Pen. Hold the button, wave the pen, the cursor follows. Tap the button to click.

Inspired by [AirPointer](https://github.com/musubipapi/airpointer), which did this with a closed AirPods case and a lot of reverse engineering. The S Pen version is easier: Samsung publishes an [SDK](https://developer.samsung.com/s-pen-sdk) that streams the pen's motion to the phone.

Status: Windows receiver works (tested with a fake sender). Android app not started. See `NOTES.md` for the build log.

## Windows receiver

    cd windows
    dotnet run            # defaults: gain 40, smoothing 0.5
    dotnet run -- 60 0.3  # more gain, less lag

In another terminal, `.ake-pen.ps1` sends a circle and a click to prove the pipe works before the phone is involved.
