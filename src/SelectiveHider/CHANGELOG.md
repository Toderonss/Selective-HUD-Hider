# Changelog

## v1.0.5
- **Replaced** the old `MainStaminaVerticalOffset` parameter with the new `Enable extra stamina offset` checkbox. The old parameter was unstable and caused conflicts with other mods.
- **Fixed** integration with PeakStats — now the correct method of checking dependencies is used through `Chainloader.PluginInfos` (for passing moderation of ThunderStore).

## v1.0.4
- **Custom Stamina Stats** – completely redesigned module:
- Added numeric values for **all** statuses (Injury, Hunger, Cold, Poison, Crab, Curse, Drowsy, Weight, Hot, Thorns, Spores, Web, Arrow, FlyTrap, Petrify).
- Added digit display for **Extra Stamina** (extra stamina).
- The numbers are positioned under the corresponding stripes (including Petrify in the Extra Stamina Bar).
- The colors of the texts correspond to the colors of the status bars.
- **New setting:** `MainStaminaVerticalOffset' – allows you to raise the main strip of stamina up when Extra Stamina is active, for better visual separation.
- **Integration with PhotonPingCanvas** – added support for hiding the ping interface (PhotonPingGUI mod).

## v1.0.3
- Integration with PeakStats (fixed an issue with other players' stamina bars disappearing).

## v1.0.2
- Fixed a bug where **Letterbox** would not hide after exiting Clean Mode.

## v1.0.1
- Fixed a bug where **Spectating** didn't hide when you returned to normal after losing consciousness.

## v1.0.0 
- The first beta release of the mod