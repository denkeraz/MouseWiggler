# MouseWiggler

A lightweight, self-compiled Windows desktop utility that prevents system sleep and screen lock by simulating subtle, randomized mouse movement after a configurable period of user inactivity.

Built from scratch in C# (.NET Windows Forms) to guarantee zero third-party dependencies and full source-level auditability.

## Core Behavior & Architecture

- **Idle Detection (`GetLastInputInfo`):** Polls `user32.dll` every `500ms` to measure true hardware input idle time (mouse and keyboard).
- **Threshold Trigger:** Once system idle time meets or exceeds the user-configured timeout (default: `5` minutes, adjustable `1–120` minutes at runtime), jiggling begins.
- **Hardware-Level Movement (`SendInput`):** Injects relative `MOUSEEVENTF_MOVE` events (`±12px` at randomized `1.5s–3.5s` intervals) rather than setting cursor coordinates directly, ensuring Windows properly resets its internal sleep/lock timers.
- **Automatic Yield to User:** During jiggling, if `GetLastInputInfo` drops below `400ms` (indicating physical mouse or keyboard activity), jiggling immediately stops and resets the idle countdown.
- **UI & System Tray:** Single fixed-size WinForms window (`Program.cs` only, no `.Designer.cs` files) with live countdown status, manual Pause/Resume toggle, and optional minimize-to-system-tray behavior (`NotifyIcon`).

## Strict Development Constraints (For AI Assistants)

When modifying or extending this project, adhere strictly to these rules:

1. **Zero External Dependencies:** Do NOT add any NuGet packages or third-party DLLs. Use only built-in .NET BCL namespaces (`System.Windows.Forms`, `System.Drawing`, `System.Runtime.InteropServices`) and native Windows P/Invoke (`user32.dll`, `kernel32.dll`).
2. **Single-File Codebase:** Keep all application logic and UI construction inside `Program.cs` so the entire program remains easy to audit in one place.
3. **Clean Builds:** Maintain compatibility with nullable reference types (`<Nullable>enable</Nullable>`) and ensure `dotnet build` produces **0 warnings and 0 errors**.

## Build & Publish Commands

Run from `D:\DevelopmentWork\MouseWiggler`:

- **Run locally for testing:**
  ```bash
  dotnet run