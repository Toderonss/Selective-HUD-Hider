# Selective HUD Hider v1.0.0

## 📖 Description
The Selective HUD Hider mod allows you to selectively hide elements of the game interface (HUD) to create a clean, minimalistic screen. It is ideal for creating screenshots, recording gameplay without an interface, or just for those who prefer a minimum number of distracting elements on the screen.

### Main features:
✅ Selective hiding - you can hide only the necessary elements

✅ Hotkey - quick mode switching (F1 by default)

✅ Custom Stamina Stats - digital display of stamina and statuses

✅ Integration with other mods - support for popular modifications

✅ Customizable - more than 20 controls

✅ Stable operation - optimized code without memory leaks

## 🚀 Installation
### Requirements:
- BepInEx 5.x or higher
- The REAK game (any version)

### Installation Steps:
1. Make sure that you have BepInEx installed.
2. Place the file SelectiveHider.dll to the BepInEx\plugins folder\
3. Start the game and the mod will automatically create a configuration file.

## ⚙️ Configuration
After the first launch of the game, the com file will appear in the BepInEx\config folder.yourname.SelectiveHider.cfg.

### Basic settings:
- Toggle Key - the key for switching the "Clean mode" (F1 by default)
- Check Interval - the frequency of item checks (1.0 recommended)
- Letterbox - enabling/disabling black bars (Cinema Mode)
- Basic HUD elements for hiding:
- BarGroup - strips of health, hunger, thirst
- Inventory - inventory
- Prompts - hints (E - take, F - use, etc.)
- DayNightText - day/night text
- Reticles - sights in the center of the screen
- TheFogRises / TheLavaRises - animations of events
- ConnectionLog - log of player connections
- The Hero interface at the beginning of the biome

### Custom Stamina Stats:
- Enabled - enable numeric values
- ShowPercentage - show percentages (%)
- FontSize - font size (16 by default)
- OutlineThickness - the thickness of the text outline

### 🔤 Font Information
#### Custom Font
This mod includes the Chewy font (Apache License 2.0) for displaying Custom Stamina Stats. The font was chosen for its excellent readability and playful style that matches the game's aesthetic.

**Key features:**
✅ Clear visibility even at small sizes

✅ Built-in AssetBundle - no separate installation needed

✅ Fallback system - uses game fonts if Chewy fails to load

✅ Apache 2.0 Licensed - free to use and distribute

**License notice:**
Chewy font is licensed under the Apache License 2.0. Full license text is included in the mod's installation folder (LICENSE-CHEWY.txt).

## 🎮 Use
### Basic control:
- F1 - switching between "Clean" and "Normal" mode
- All the elements are hidden according to your settings in the config
- Custom Stamina Stats is automatically enabled on game scenes
- Game scenes where the mod works:
    All levels (Level_0 - Level_20)
- Game zones and biomes

## ⚠️ Incompatibilities
### ❌ Does not work correctly with:
1. **PeakStats**
Problem: When a game event occurs (for example, connecting a new player to the server) with the mod pre-enabled, uncontrolled copying of the stamina begins during the game, which is accompanied by severe freezes.

Link: [PeakStats on Thunderstore](https://thunderstore.io/c/peak/p/nickklmao/PeakStats/)

2. **Leaderboard**
Problem: The time is not displayed correctly when activating the mod (hiding interface elements).

Link: [Leaderboard on Thunderstore](https://thunderstore.io/c/peak/p/CakeDevs/Leaderboard/)

3. **StaminaStats**
Problem: The inability to hide the numbers on the bar when activating the mod (or hiding is displayed with suspensions and crashes when in Clear mode for a long time)

Link: [StaminaStats on Thunderstore](https://thunderstore.io/c/peak/p/pixx/StaminaStats/)

### ✅ Works correctly with:
1. **DownedAwareness**
Function: Hides markers of players who have lost consciousness

Link: [DownedAwareness on Thunderstore](https://thunderstore.io/c/peak/p/LucydDemon/DownedAwareness/)

2. **BetterPingDistance**
Function: Hides distances from pings

Link: [BetterPingDistance on Thunderstore](https://thunderstore.io/c/peak/p/LucydDemon/BetterPingDistance/)

## 🖥️ Game settings
Screen resolution:
Minimum: 1024x768 (for correct display of CustomStaminaStats)

Recommended: 1920x1080 or higher

Important notes:
The mod automatically adapts to the screen resolution.

Custom Stamina Stats uses game fonts to preserve the style

When you change the scene, all settings are saved.

## 📸 Screenshots
### Clean Mode (HUD Hidden)
![Clean Mode Example](https://i.ibb.co/pgD1PVv/200FCF-1.jpg)

*All HUD elements are hidden, only gameplay remains*

### Normal Mode (HUD Visible)  
![Normal Mode Example](https://i.ibb.co/fGS7H9D8/209000-1.jpg)

*Default game interface with all elements*

### Custom Stamina Stats
![Custom Stamina Stats](https://i.ibb.co/k2TJRCKm/208B35-1.jpg)

*Digital display of stamina and status effects*

### Configuration Menu
![Config Menu](https://i.ibb.co/gFJz8VC9/209D7B-1.jpg)

*Mod configuration in BepInEx Config Manager*

## 🔧 Technical details
### Implementation features:
- Weak References - to prevent memory leaks
- Canvas Groups - for smooth hiding of animated elements
- Optimized checks - do not load the system
- Automatic cleaning - when changing scenes and completing the game

### Performance:
✅ Low resource consumption

✅ No lags when switching modes

✅ Stable performance in a multiplayer game

## 🤝 Support and feedback
If there are any problems:
Make sure that BepInEx is installed correctly.

Make sure there are no conflicts with other mods.

Check the BepInEx\LogOutput.log file for errors

### Contacts:
Author: NixiTsu

Version: 1.0.0

Support: Via comments on the fashion page

## 📝 License
The mod is distributed for free. Forbidden:

- Commercial use
- Distribution without specifying the author
- Making changes without author's permission
