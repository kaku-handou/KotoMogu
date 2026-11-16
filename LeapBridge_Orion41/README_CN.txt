KotoMogu LeapBridge (Orion 4.1) - Stage 1
======================================

STATUS: prototype, not yet connected to Unity.
Purpose: send Leap Motion Gen1 palm XYZ coordinates over local UDP port 8767.
Does not touch existing GestureServer UDP 8766.
No LeapSDK code or binaries included; uses your existing SDK installation.

1) Copy this LeapBridge_Orion41 folder into the KotoMogu repository root.
   Make sure GitHub Desktop current branch is feature/leap-gen1.
2) Open 'x64 Native Tools Command Prompt for VS 2022'.
3) Run build_windows.bat (drag the file into CMD and press Enter, or cd into folder and run it).
4) Open a SEPARATE regular CMD in this folder and run:
       py -3.11 udp_monitor.py
5) Open another CMD and run:
       build\Release\LeapBridge.exe
6) Connect one Gen1 device and move your hand above it. The monitor prints coordinates.
7) Ctrl+C in each console to stop.

If build fails, capture the full error. This project has not been verified
on your local Windows system, although it follows the uploaded SDK 4.1 examples.

Security: packets only target 127.0.0.1 (this PC). No external network.
Next stage: inspect Unity's KotoMoguGestureInput.cs and design its own input
adapter for position events and gentle interaction. Avoid sending position
messages directly to the existing event listener on port 8766.
