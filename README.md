<p align="center">
  <img src="src/ONEXTweaks.Widget/XboxGamingBar/Assets/OnexSquare44x44Logo.altform-unplated_targetsize-256.png" width="96" alt="ONEXTweaks logo">
</p>

<h1 align="center">ONEXTweaks</h1>

<p align="center"><strong>Quick settings built for the ONEXPLAYER 3.</strong><br>
Tune power, cooling, controls, and display settings without leaving your game.</p>

<p align="center">
  <a href="#take-a-tour">Widget tour</a> ·
  <a href="#theme-gallery">20 themes</a> ·
  <a href="#installation">Installation</a> ·
  <a href="#dependencies">Dependencies</a> ·
  <a href="#uninstall">Uninstall</a> ·
  <a href="#support-the-project">Support</a>
</p>

ONEXTweaks runs inside **Xbox Game Bar** on Windows 11 x64. Open it with **Win + G** and use touch, a mouse, or the built-in controller. It's designed for the Intel ONEXPLAYER 3. On Intel ONEXPLAYER 3, beta.5 checks the hardware features exposed by compatible OneXConsole components. Controls appear when the corresponding hardware responds.

## Take a tour

The tour uses Dracula with Glass Effect off. To compare palettes, open the [theme gallery](#theme-gallery).

### 🏠 Home

<p align="center">
  <img src="docs/images/home-tab.png" width="640" alt="ONEXTweaks Home with live performance readings, power and volume sliders, and quick settings tiles">
</p>

- **See how your handheld is doing.** Check processor and graphics load, temperature, power draw, and battery level at a glance.
- **Keep your favorite controls on Home.** Change power, brightness, and volume. Rearrange or hide tiles, switch profiles, and open Wi-Fi, Bluetooth, the sound mixer, or the Windows keyboard.
- **Put your shortcuts in the widget.** Add a key combination, app, or website as a tile. Built-in shortcuts cover Steam, media playback, and switching windows.
- **Manage the battery.** Set an upper charge limit for time spent plugged in. Use the keep-awake timer and optional dimming when you want to leave the handheld running.

### ⚡ Performance

<p align="center">
  <img src="docs/images/performance-tab.png" width="640" alt="Connected Performance tab showing the active profile, its PL1 and PL2 values, CPU controls, and frame-rate cap">
</p>

**The TDP card sets the processor's power limits in watts.** The two sliders let you set a steady limit for regular play and a higher limit for short bursts of work.

- **PL1 is the sustained power limit.** It's the main setting for a long gaming session. A lower PL1 can reduce heat and battery use. A higher one gives games more room to run at the cost of more heat and power.
- **PL2 is the short-burst power limit.** It gives the processor extra room to boost during brief spikes in demand, such as loading a busy scene. For example, **25 W PL1 / 28 W PL2** allows a short burst up to 28 W while sustained power stays around the 25 W limit. Actual power depends on the workload and temperature. [Intel's explanation of processor power limits](https://cdrdv2-public.intel.com/872188/872188-001.pdf)

Create a quieter profile for battery play (**DC**) and a faster one when plugged in (**AC**). Link a profile to a game to apply it automatically. Choose a Windows power mode, decide how aggressively the processor boosts to higher speeds, or set a **frame-rate cap** to stop a game rendering more frames than you want. Choose RTSS or the Intel limiter where your driver and game support it.

<details>
<summary>See the 25 W / 28 W example on Home</summary>

<p align="center">
  <img src="docs/images/power-limits.png" width="640" alt="Home power limits set to 25 watts for sustained use and 28 watts for short bursts">
</p>

</details>

### 🎮 Controller

<p align="center">
  <img src="docs/images/controller-tab.png" width="640" alt="Controller tab with profiles, controller mode, remapping, stick and trigger tuning, and gyro settings">
</p>

- **Choose how games see the controller.** **Hardware** uses the handheld's built-in controller directly. **Virtual Xbox 360** and **Virtual DualShock 4** use VIIPER and apply your software stick and trigger settings to games.
- **Map buttons by clicking the diagram.** Assign a button to another controller input, a key or key combo, or a mouse action. Map two physical buttons to separate clicks on the left and right halves of the virtual DualShock 4 touchpad.
- **See your inputs as you tune them.** A stick's **dead zone** is the small area around its center where movement is ignored. Use the live test to see stick drift, compare input before and after your settings, and check how far you need to pull either trigger to reach full input. Vibration strength is adjustable in supported modes.
- **Aim by moving the console.** Calibrate the gyro, then use motion to move the aim stick or mouse. The live test shows the console rotating in 3D and displays how fast it's turning. Virtual DualShock 4 can pass motion data directly to compatible games.

<details>
<summary>ONEXPLAYER, Xbox, and DualShock 4 remapping diagrams</summary>

<p align="center">
  <img src="docs/images/controller-remap.png" width="640" alt="Interactive front, rear, and top views of the ONEXPLAYER 3 for button remapping">
</p>

<p align="center">
  <img src="docs/images/xbox-controller.png" width="640" alt="Xbox controller diagram with the A button selected">
</p>

<p align="center">
  <img src="docs/images/dualshock-4.png" width="640" alt="DualShock 4 diagram with the right side of the touchpad selected">
</p>

</details>

<details>
<summary>Watch the stick, trigger, and gyro tests</summary>

**Sticks & triggers.** See both stick positions, separate LT and RT travel, and the numbers for each axis before and after dead-zone and trigger tuning.

<p align="center">
  <img src="docs/images/stick-test.gif" width="640" alt="Both stick plots and both trigger bars moving alongside their raw and tuned numeric readings">
</p>

**Gyro & motion.** Watch the handheld turn in 3D while the rate dials, accelerometer bars, and processed aiming values update alongside it.

<p align="center">
  <img src="docs/images/gyro-test.gif" width="640" alt="Full gyro test with the rotating 3D handheld, raw and calibrated rate gauges, acceleration bars, and aiming output values">
</p>

*Both tests use example input to show how the readings respond.*

</details>

### ⌨️ Keyboard / Mouse

<p align="center">
  <img src="docs/images/keyboard-mouse-tab.png" width="640" alt="Keyboard and Mouse tab with Mouse speed, Mouse precision, and Full Remapping tiles">
</p>

Use the sticks as a cursor and scroll wheel. Tune pointer speed and precision separately. Pick keyboard keys and mouse actions visually. A button can trigger a shortcut such as **Alt + Tab**, or several inputs in a row with a delay between them. Set a second action on the **Fn layer** in the same button editor.

<p align="center">
  <img src="docs/images/keyboard-layout.png" width="640" alt="Interactive keyboard with a number pad and several highlighted keys">
</p>

### 🖥️ Screen Colors

<p align="center">
  <img src="docs/images/screen-colors.gif" width="640" alt="Screen Colors sliders changing brightness and saturation alongside the reference image">
</p>

*The animation illustrates how color adjustments can look. Actual output depends on your display and graphics driver.*

- **Give each game its own look.** Use the reference image to judge changes to color, brightness, and contrast, then save them as a screen profile.
- **Tune the display to the game.** Resolution sets how many pixels the screen shows. Refresh rate sets how often the screen can update each second. Higher refresh rates can make motion look smoother. On supported setups, **VRR** lets the display match the game's frame rate, and **HDR** can show a wider range of brightness. Availability depends on the display, driver, and mode. [Windows refresh rate guide](https://support.microsoft.com/en-us/windows/hardware/display-graphics/change-the-refresh-rate-on-your-monitor-in-windows) · [Windows HDR guide](https://support.microsoft.com/en-gb/windows/hardware/display-graphics/hdr-settings-in-windows)
- **Keep display controls close.** Lock screen rotation from the widget, adjust supported Intel graphics features, or open OptiScaler from a tile in games where it's installed.

### 📊 Overlay

Put an RTSS overlay over the game and choose what it shows. Start with just **FPS** (frames per second), or add **frame time** (the time between frames), temperature, power use, and processor load. Frame time is useful for spotting a stutter that an average FPS number can hide. The editor updates a live preview with example readings as you change metrics, layout, font, size, colors, and background. Save a compact or detailed layout for use in games.

<p align="center">
  <img src="docs/images/overlay-preview.png" width="640" alt="RTSS custom overlay editor with an in-widget preview and controls for the font, layout, metrics, and background">
</p>

### 🌬️ Fan

<p align="center">
  <img src="docs/images/fan-curve.gif" width="640" alt="Custom fan curve with a moving control point and changing fan duty percentage at a selected temperature">
</p>

Let the system handle cooling in **Auto**, or set a custom **fan curve** that tells the fan how fast to run at different temperatures. For example, you can keep it slower at light loads and have it ramp up sooner during a demanding game. Save a fan profile for a specific game independently of its performance profile. The widget displays actual fan speed when hardware readings are available. The demo moves one temperature point to show how its fan duty setting reshapes the curve.

### 💡 Lighting

<p align="center">
  <img src="docs/images/lighting-motion.gif" width="640" alt="Animated lighting diagram showing Colorful Neon on the stick rings and a fade on the top light bar">
</p>

*Animated preview of the diagram with Colorful Neon and breathing colors.*

Select a zone on the diagram or adjust all zones together. Pick a solid color, control brightness, or try an effect such as a slow breathing fade or **Colorful Neon**. Zones include the stick rings, G button, top light bar, and the logo on a compatible Controller Bridge. Available effects depend on the zone.

<details>
<summary>See the lighting editor</summary>

<p align="center">
  <img src="docs/images/lighting-zones.png" width="640" alt="Lighting diagram with all zones linked">
</p>

</details>

### ⚙️ Settings

<p align="center">
  <img src="docs/images/settings-tab.png" width="640" alt="Settings tab with Tabs, Appearance, Navigation guide, and Drivers and components sections">
</p>

Choose your language, font, theme, and the optional glass finish. Hide or reorder tabs and pick which one opens first, or return to your last used tab. Use the navigation guide for touch and gamepad shortcuts. The **Drivers & components** section shows installed versions and links to official updates.

## Theme gallery

All 20 themes are available under **Settings → Appearance**. The screenshots show the same Appearance controls in each theme with Glass Effect off.

<details>
<summary>ONEXPLAYER, OLED, and Liquid Glass · 3 themes</summary>

ONEXPLAYER · OLED · Liquid Glass

<p align="center">
  <img src="docs/images/themes/onexplayer.png" width="310" alt="ONEXPLAYER theme in the Appearance settings">
  <img src="docs/images/themes/oled.png" width="310" alt="OLED theme in the Appearance settings">
</p>

<p align="center">
  <img src="docs/images/themes/liquid-glass.png" width="310" alt="Liquid Glass theme in the Appearance settings">
</p>

</details>

<details>
<summary>Catppuccin · 5 themes</summary>

Mocha · Macchiato · Frappé · Latte · Latte Soft (adapted)

<p align="center">
  <img src="docs/images/themes/catppuccin-mocha.png" width="310" alt="Mocha theme in the Appearance settings">
  <img src="docs/images/themes/catppuccin-macchiato.png" width="310" alt="Macchiato theme in the Appearance settings">
</p>

<p align="center">
  <img src="docs/images/themes/catppuccin-frappe.png" width="310" alt="Frappé theme in the Appearance settings">
  <img src="docs/images/themes/catppuccin-latte.png" width="310" alt="Latte theme in the Appearance settings">
</p>

<p align="center">
  <img src="docs/images/themes/catppuccin-latte-soft-adapted.png" width="310" alt="Latte Soft (adapted) theme in the Appearance settings">
</p>

</details>

<details>
<summary>Kanagawa and Miasma · 4 themes</summary>

Kanagawa Wave · Kanagawa Dragon · Kanagawa Lotus · Miasma

<p align="center">
  <img src="docs/images/themes/kanagawa-wave.png" width="310" alt="Kanagawa Wave theme in the Appearance settings">
  <img src="docs/images/themes/kanagawa-dragon.png" width="310" alt="Kanagawa Dragon theme in the Appearance settings">
</p>

<p align="center">
  <img src="docs/images/themes/kanagawa-lotus.png" width="310" alt="Kanagawa Lotus theme in the Appearance settings">
  <img src="docs/images/themes/miasma.png" width="310" alt="Miasma theme in the Appearance settings">
</p>

</details>

<details>
<summary>Dracula, Cobalt2, and Shades of Purple · 3 themes</summary>

Dracula · Cobalt2 · Shades of Purple

<p align="center">
  <img src="docs/images/themes/dracula.png" width="310" alt="Dracula theme in the Appearance settings">
  <img src="docs/images/themes/cobalt2.png" width="310" alt="Cobalt2 theme in the Appearance settings">
</p>

<p align="center">
  <img src="docs/images/themes/shades-of-purple.png" width="310" alt="Shades of Purple theme in the Appearance settings">
</p>

</details>

<details>
<summary>Console and Windows themes · 5 themes</summary>

Xbox 360 · PS4 · PS5 · Windows 11 Light · Windows 11 Dark

<p align="center">
  <img src="docs/images/themes/xbox-360.png" width="310" alt="Xbox 360 theme in the Appearance settings">
  <img src="docs/images/themes/ps4.png" width="310" alt="PS4 theme in the Appearance settings">
</p>

<p align="center">
  <img src="docs/images/themes/ps5.png" width="310" alt="PS5 theme in the Appearance settings">
  <img src="docs/images/themes/windows-11-light.png" width="310" alt="Windows 11 Light theme in the Appearance settings">
</p>

<p align="center">
  <img src="docs/images/themes/windows-11-dark.png" width="310" alt="Windows 11 Dark theme in the Appearance settings">
</p>

</details>

## Installation

> [!NOTE]
> Download the beta installer from [GitHub Releases](https://github.com/KiryaChainik/ONEXTweaks/releases).

1. **Check Game Bar.** Press **Win + G**. Install or update Xbox Game Bar from Microsoft Store if it doesn't open. Keep a compatible OneXConsole installation for the handheld's hardware controls. [Xbox Game Bar](https://apps.microsoft.com/detail/9NZKPSTSNW4P) · [ONEXPLAYER drivers and software](https://onexplayerstore.com/pages/drivers-and-faqs)
2. **Run Setup.** Download `ONEXTweaks-Setup-x64.exe` from the release and compare its SHA-256 (the file's fingerprint) with the one posted there. Open Setup from the Windows account you normally use, choose **Install**, and follow the Windows prompts.
3. **Open the widget.** If Setup asks you to restart, sign back in to the same Windows account and run the same installer again. Then open **Win + G → Widget menu → ONEXTweaks**.

<details>
<summary>How to check the installer's fingerprint</summary>

Open **Windows PowerShell** in the folder where you saved the installer and run:

```powershell
Get-FileHash .\ONEXTweaks-Setup-x64.exe -Algorithm SHA256
```

Compare the value in **Hash** with the SHA-256 shown on the release page.

</details>

**What to expect**

- **OneXConsole startup.** When the hardware checks pass, Setup disables the OneXConsole window's automatic launch if it was enabled. This keeps the two apps from competing for hardware controls. Its hardware components remain installed, and you can open the OneXConsole window manually.
- **Beta certificates.** On a first beta install, Setup may ask you to trust the widget's signing certificate. Compare the publisher and fingerprint with the release details. Windows may also show a SmartScreen prompt. Standard installation uses normal Windows signature checks.
- **Updating from an earlier beta.** Run the [installed ONEXTweaks uninstaller](#uninstall), then run the new Setup. Plan to set up your profiles again.

## Dependencies

**Have these ready before installation**

- **Windows 11 x64 and Xbox Game Bar.** Windows runs the helper processes and Game Bar hosts the widget. [Xbox Game Bar](https://apps.microsoft.com/detail/9NZKPSTSNW4P)
- **OneXConsole.** The manufacturer's app supplies the components PowerAgent needs for fan, power, and charging controls. The validated package is **0.10.4-fix3**. Use a compatible copy from the manufacturer. Setup can also use a separately provided OEM installer after verifying its signature and checksum. [ONEXPLAYER software](https://onexplayerstore.com/pages/drivers-and-faqs)

**Setup downloads and installs these if missing**

- **PawnIO 2.2.0** gives ONEXTweaks access to supported hardware readings and controls. [PawnIO release](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)
- **usbip-win2 0.9.8.1 x64** lets Windows connect to virtual controllers. Its installer can also install the required Visual C++ Runtime. [usbip-win2 release](https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1)
- **HidHide 1.5.230** hides the physical controller while the virtual one is active, which prevents a game from seeing each press twice. [HidHide release](https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0)
- **RivaTuner Statistics Server (RTSS) 7.3.7** displays the overlay in games and provides the RTSS frame-rate limiter. [RTSS download](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/)

**Included with ONEXTweaks**

- The widget and its MSIX framework packages, the Bridge that connects it to Windows, PowerAgent for hardware controls, Launcher, and DependencyManager. Bridge includes the .NET runtime it needs.
- **VIIPER 0.9.0** ships inside Bridge and provides virtual Xbox 360 and DualShock 4 controllers. [VIIPER release](https://github.com/Alia5/VIIPER/releases/tag/v0.9.0)

## Uninstall

1. Sign in to the Windows account you used to install the widget and close Game Bar.
2. Open **Windows PowerShell** and run the installed uninstaller:

   ```powershell
   $uninstaller = Join-Path $env:ProgramFiles 'ONEXTweaks\Tools\Installer\Uninstall-ONEXTweaksRelease.ps1'
   & $uninstaller -Uninstall
   ```

3. Approve the Windows administrator prompt when it appears.

The script releases the controller, backs up widget settings under `%LOCALAPPDATA%\ONEXTweaks\UninstallBackups`, and removes ONEXTweaks components. It restores the OneXConsole startup setting recorded at installation. Shared drivers, RTSS, and OneXConsole stay installed. To preview what will be removed, use `-Report` in place of `-Uninstall`. [Uninstall and rollback details (Russian)](tools/installer/README.md#удаление-и-откат)

## Support the project

If you use ONEXTweaks and would like to support its development, visit [Kirya Chainik on Patreon](https://www.patreon.com/cw/kiryachainik). The QR code opens the same page.

<p align="center">
  <a href="https://www.patreon.com/cw/kiryachainik"><img src="src/ONEXTweaks.Widget/XboxGamingBar/Assets/support-author-qr.png" width="152" height="152" alt="QR code for Kirya Chainik's Patreon page"></a>
</p>

## Acknowledgments

- Thanks to **enterTheVoidCode (Riff_Lash)** and the **ClawTweaks** contributors for the open Game Bar frontend and shared models that got ONEXTweaks started. Thanks also to **corando98** and the contributors to **GoTweaks**, the project ClawTweaks forked from. [ClawTweaks](https://github.com/enterTheVoidCode/ClawTweaks) · [GoTweaks](https://github.com/corando98/GoTweaks) · [Support Riff_Lash](https://ko-fi.com/rifflash)
- Thanks to **Alia5**, **vadimgrn**, **Nefarius**, and **namazso** for the work that makes virtual input and hardware access possible. [VIIPER](https://github.com/Alia5/VIIPER) · [usbip-win2](https://github.com/vadimgrn/usbip-win2) · [HidHide](https://github.com/nefarius/HidHide) · [PawnIO](https://github.com/namazso/PawnIO.Setup)
- Thanks to **Unwinder** and the **RTSS** team for the overlay and frame-rate limiter, **Intel** for the public Graphics Control Library interfaces, and the **OptiScaler** team for the in-game tool. [RTSS](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/) · [Intel IGCL](https://github.com/intel/drivers.gpu.control-library) · [OptiScaler](https://github.com/optiscaler/OptiScaler)
- Thanks to the artists and communities behind the palettes adapted for the widget. [Dracula](https://draculatheme.com/contribute) · [Catppuccin](https://catppuccin.com/) · [Kanagawa](https://github.com/rebelot/kanagawa.nvim) · [Miasma](https://github.com/xero/miasma.nvim) · [Cobalt2](https://github.com/wesbos/cobalt2-vscode) · [Shades of Purple](https://github.com/ahmadawais/shades-of-purple-vscode)

## Development and licenses

[Build, CI, and release process](docs/development.md) · [Third-party notices](third-party/NOTICE.txt) · [Widget source license (AGPL-3.0)](src/ONEXTweaks.Widget/LICENSE)

ONEXTweaks is an independent project for the ONEXPLAYER 3 created by [Kirya Chainik](https://github.com/KiryaChainik). If you reuse original ONEXTweaks code, please credit Kirya Chainik and link to the [project repository](https://github.com/KiryaChainik/ONEXTweaks).
