# TECHNICIAN QUICK START GUIDE

## 🎯 For Technicians: ONE APP TO RUN EVERYTHING

### STEP 1: Double-click `FPGATestSystem.exe`

Location:
```
C:\Users\kagan\Desktop\PipedStatusProject\MasterLauncher\bin\Release\FPGATestSystem.exe
```

### STEP 2: Click "START AUTOMATED TEST"

That's it! Everything runs automatically.

---

## What Happens Automatically

When you click START, the system will:

```
┌─────────────────────────────────────────────────────────────┐
│  📋 → ⚙️ → ⚡ → 🌐 → ✓ → 📊 → ✅                            │
│                                                             │
│  1. Load Config      - Reads settings from config.json     │
│  2. Initialize       - Sets up all components              │
│  3. Power Check      - Verifies 3.3V, 5V, 12V rails        │
│  4. Connectivity     - Tests network and pipe connection   │
│  5. Data Validation  - Parses test data, checks format     │
│  6. Performance      - Runs 25 FPS stress test             │
│  7. Complete         - Shows final status                  │
│                                                             │
│  Status: PASSED ✓  or  FAILED ✗                            │
└─────────────────────────────────────────────────────────────┘
```

---

## Status Colors

| Color | Meaning |
|-------|---------|
| 🟢 Green | PASSED - Everything OK |
| 🟡 Yellow | RUNNING - Test in progress |
| 🔴 Red | FAILED - Problem detected |
| ⬜ Gray | NOT RUN - Waiting to start |

---

## If Something Fails

1. Look at the **red text** in the log at the bottom
2. Note which step failed (the step icon will be red)
3. Click **"Open Logs"** button to get detailed logs for support

---

## Buttons

| Button | What It Does |
|--------|--------------|
| **START AUTOMATED TEST** | Runs all tests automatically |
| **STOP** | Cancels the current test |
| **Open Logs** | Opens the logs folder |

---

## Configuration (For Advanced Users)

All settings are in `config.json`:

| Setting | Description |
|---------|-------------|
| `AutoRunOnStartup` | If `true`, tests start automatically when app opens |
| `StopOnFirstFailure` | If `true`, stops immediately when any test fails |
| `Schema.Path` | Path to the XML schema file |

---

## Application Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                    PipedStatusProject                            │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                    MasterLauncher.exe                       │ │
│  │           (TECHNICIANS USE THIS ONE!)                       │ │
│  │                                                             │ │
│  │   - Runs everything automatically                          │ │
│  │   - Shows pass/fail status                                 │ │
│  │   - No buttons to press except START                       │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                           │                                      │
│           ┌───────────────┼───────────────┐                      │
│           ▼               ▼               ▼                      │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐               │
│  │  MainApp    │  │BitStatusPanel│  │EthernetPerf │               │
│  │  (hidden)   │  │  (hidden)   │  │  (hidden)   │               │
│  └─────────────┘  └─────────────┘  └─────────────┘               │
│         │               │               │                        │
│         └───────────────┼───────────────┘                        │
│                         ▼                                        │
│                  ┌─────────────┐                                 │
│                  │  HARDWARE   │                                 │
│                  │    FPGA     │                                 │
│                  └─────────────┘                                 │
└──────────────────────────────────────────────────────────────────┘
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| App won't start | Make sure .NET Framework 4.7.2 is installed |
| "Config not found" | Copy `config.json` next to the .exe |
| All tests fail | Check hardware connection |
| Power test fails | Verify power supply voltages |
| Ping test fails | Check network cable |

---

## Contact Support

If tests fail repeatedly:
1. Click "Open Logs"
2. Find the most recent .log file
3. Send to engineering team

---

*For Technicians: Just double-click and click START. That's all!*
