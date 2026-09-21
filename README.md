# MControlTray

A lightweight Windows **system-tray controller for MSI laptops** - without the
heavy, self-updating MSI Center UI (`DCv2.exe`).

Right now it switches **User Scenarios** (Extreme Performance / Balanced /
ECO-Silent) instantly from the tray. More MSI controls are planned - see the
[Roadmap](#roadmap).

> Inspired by Linux's [MControlCenter](https://github.com/dmitry-s93/MControlCenter) -
> this is a Windows take, going through MSI's own service rather than raw EC access.

## Why

MSI Center's `DCv2.exe` is large and tends to update/reinstall itself at the
worst moments. But most of the time you only need to flip the **User Scenario**.
This app does exactly that - from the tray, instantly, with a tiny footprint.

## How it works

MSI Center's UI is a thin shell: clicking a User Scenario tile sends a small
command over a **localhost TCP socket** to the MSI background **service**, and
the service performs the real power/fan switch. This app replays that exact
command.

- **No administrator rights** required - it's just a localhost message, exactly
  like MSI's own UI sends.
- It does **not** poke the Embedded Controller directly, and it does **not**
  bundle, modify, or redistribute any MSI code or binaries.
- It only needs the MSI Center **background service** installed and running
  (the stable part - you don't need the `DCv2.exe` UI open).

## Requirements

- Windows x64
- An MSI laptop with MSI Center installed (background service running)
- Prebuilt release: nothing else - a single **~1-2 MB native `.exe`**, no .NET runtime needed (NativeAOT). Building from source: .NET 10 SDK + the Visual Studio C++ Desktop workload (the NativeAOT linker).

## Install

### winget

```
winget install Flakroup.MControlTray
```

*(pending acceptance into the winget community repository)*

### Manual

1. Download `MControlTray-win-x64.exe` from the [latest release](../../releases/latest).
2. Put it anywhere and run it - an icon appears in the system tray.
3. (Optional) Autostart at logon: press <kbd>Win</kbd>+<kbd>R</kbd>, type
   `shell:startup`, and drop a shortcut to the `.exe` there.

## Usage

- **Tray icon** - a colored circle showing the active mode (`E` / `B` / `S`).
  Left or right click opens the menu.
- **Global hotkey** - <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd>
  cycles Extreme Performance -> Balanced -> ECO/Silent -> ... It is configurable
  (see below) and can be turned off.
- **Single instance** - launching the app while it is already running does not
  add a second tray icon: the running instance reports itself with a balloon and
  the new process exits with code `0`. The guard is a per-session named mutex, so
  an unrelated program cannot keep the tray from starting.
- **Command line** (great for shortcuts / Stream Deck):

  ```
  MControlTray.exe --extreme
  MControlTray.exe --balanced
  MControlTray.exe --silent
  ```

  Exit codes: `0` switched, `1` the switch failed (is the MSI service running?),
  `2` unknown argument. A tray that is already running picks the change up, so
  its icon and the hotkey's cycle position stay in step with the machine.

### Configuration

`%AppData%\MControlTray\config.ini` is created on first run and can be opened
straight from the tray menu. **Restart the app after editing it.**

| Key     | Value                                                                                | Default            |
| ------- | ------------------------------------------------------------------------------------ | ------------------ |
| `cycle` | Modifiers (`Ctrl`, `Alt`, `Shift`, `Win`) plus a key (`A-Z`, `0-9`, `F1-F24`), or `none` | `Ctrl+Alt+Shift+P` |

A hotkey is system-wide, so the value is checked before it is registered: a
letter or a digit needs **two** modifiers (`Ctrl+C` would take copy away from
every application), a function key needs one, and `F12` is refused because
Windows reserves it for the debugger. Windows also reserves most `Win`
combinations for itself and may refuse them.

If the combination is already taken by another application, or the file cannot
be read or parsed, the tray says so with a balloon on startup and states which
hotkey it actually registered.

## Model compatibility - please read

The switch command was reverse-engineered and **verified on:**

- Board **15P2**, Intel **Core i7-14700HX**, **MSI Center 2.0.70**

The TCP **port** is read from the registry (model-independent), but the scenario
**Index** values and the receiver **component id** are currently hardcoded:

| Scenario            | `Index` |
| ------------------- | ------- |
| Extreme Performance | 1       |
| Balanced            | 2       |
| ECO / Silent        | 4       |

On other models these may differ. **Verify yours** like this:

1. Set the DWORD `IsDebug = 1` under
   `HKLM\SOFTWARE\Wow6432Node\MSI\MSI Center\Component\SDK` (it is read live, no
   restart needed).
2. Open MSI Center and switch between your scenarios.
3. Open `C:\ProgramData\MSI\MSI Center\Utility\Utility_<date>.txt` and look for
   `SendData >>` lines. The JSON payload contains `"Index":N` - note which `N`
   maps to which scenario, and the `DestID`.
4. Delete the `IsDebug` value afterwards (it did not exist originally).

If your values differ, adjust the constants in `Program.cs` - issues/PRs with
values for more models are very welcome.

## Protocol (for the curious / porters)

- **Endpoint:** TCP `127.0.0.1:<port>`, where `<port>` is the DWORD
  `Server Port` under `HKLM\SOFTWARE\Wow6432Node\MSI\MSI Center\Component\SDK`
  (MSI's hardcoded fallback is `32682`).
- **Frame:** `[DestID : int32 little-endian = 104][0x00][0x12][UTF-8 JSON]`
- **JSON:** `{"Index":N,"Performance":2,"Fan":0,"KB":-1,"PB":-1,"IsLoad":true}`

The Embedded Controller byte `0xD2` mirrors the active scenario
(`C4`=Extreme, `C1`=Balanced, `C2`=Silent) but writing it does **not** switch
the mode - it is a status mirror, not a trigger. The real switch is the command
above, handled by the service.

## Build from source

```
dotnet publish MControlTray.csproj -c Release -r win-x64
```

Run the tests with:

```
dotnet test test/MControlTray.Tests/MControlTray.Tests.csproj
```

Produces a single self-contained native `.exe` (~1-2 MB) via **NativeAOT** - no .NET
runtime required to run it. Building it needs the Visual Studio **C++ Desktop**
workload (NativeAOT uses the MSVC linker).

## Roadmap

Scope will grow into a broader MSI control tray. Planned / ideas (contributions welcome):

- [x] User Scenario switching (Extreme / Balanced / ECO-Silent)
- [x] Configurable global hotkey
- [ ] Cooler Boost toggle
- [ ] Custom fan curves
- [ ] Battery charge limit
- [ ] Live active-state read-back
- [ ] Per-model config (no recompile)

## Prior art & credits

Inspired by the Linux MSI community: [msi-ec](https://github.com/BeardOverflow/msi-ec)
(a kernel EC driver) and [MControlCenter](https://github.com/dmitry-s93/MControlCenter)
(a Qt GUI). This project targets **Windows** and goes through MSI's **official
service** rather than raw EC access.

## Disclaimer

Not affiliated with or endorsed by Micro-Star International Co., Ltd. (MSI).
"MSI", "MSI Center", and product names are trademarks of their respective
owners. This is an independent interoperability tool built from observed
behavior. Provided as-is, **use at your own risk**.

## License

[MIT](LICENSE)
