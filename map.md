# 📁 Project Map

## 🎯 For Technicians: JUST DOUBLE-CLICK

```
Double-click: FPGATestSystem.exe
```
**That's it! Everything opens and connects automatically.**

---

## What Happens Automatically

When you run `FPGATestSystem.exe`:

1. **MainApp** opens → starts generating test data
2. **BitStatusPanel** opens → connects and shows live data
3. **Both windows position themselves** on screen
4. **Everything is already running** - no buttons to press

---

## Folder Structure

```
PipedStatusProject/
│
├── 📄 PipedStatusProject.sln       ← For developers (Visual Studio)
│
├── 📁 MasterLauncher/bin/Release/
│   └── FPGATestSystem.exe          ⭐ RUN THIS
│
├── 📁 PipeListener/MainApp/bin/Release/
│   └── MainApp.exe                 (Auto-launched)
│
├── 📁 BitStatusPanel/BitStatusPanel/bin/Release/
│   └── BitStatusPanel.exe          (Auto-launched)
│
├── 📁 Schema/
│   ├── CbitSchema.xml              ← Data definition
│   └── BitSchema.xml               ← Alternative format
│
└── 📁 Shared/BitParser/            ← Core library (for developers)
```

---

## The Apps

| App | What It Does | You See |
|-----|--------------|---------|
| **FPGATestSystem.exe** | Launches everything | Small control window |
| **MainApp.exe** | Simulates data, provides pipe server | Data producer window |
| **BitStatusPanel.exe** | Shows live data with validation | Main display window |

---

## To Close

1. Click "Close All" in the FPGATestSystem window
   - OR -
2. Close the FPGATestSystem window (closes everything)

---

## For Developers

### Build:
```
dotnet build PipedStatusProject.sln --configuration Release
```

### Run individually with auto-start:
```
MainApp.exe --autostart
BitStatusPanel.exe --autoconnect
```

---

*Zero button presses. Just double-click and it works.*
