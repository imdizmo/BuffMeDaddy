# BuffMeDaddy

An optional add-on for [AdminHammer](https://thunderstore.io/c/valheim/p/Jatify/AdminHammer/): adds a **Buffs** tab to the admin panel.

[Thunderstore](https://thunderstore.io/c/valheim/p/Jatify/BuffMeDaddy/) · [GitHub](https://github.com/imdizmo/BuffMeDaddy) · [jatify.com](https://jatify.com)

![The Buffs tab: pick an effect, give or remove it, switch off its time limit](https://raw.githubusercontent.com/imdizmo/BuffMeDaddy/main/screenshots/buffs.webp)

## What it does

- Every status effect in the game, sorted into Meads, Forsaken powers, Food & rest, Set & item effects, Ailments and Other, with search.
- **Give** or **Remove** the selected one (or right-click a slot to toggle it), **Remove all** to clear.
- **No time limit**, per effect: switch it on and that effect never runs out, whether you gave it here or drank the mead. Switch it off and it gets a fresh normal duration. Your choices are remembered.
- Gold edge = active, ∞ = no time limit; the selected effect shows its time left.

**Other players:** pick who at the top (**For:** you, or any player online). Giving works for anyone, even without mods. **No time limit** for someone else keeps the effect topped up from your game while you are both online; **Remove** stops that and lets it run out (an effect cannot be taken off another player's character).

On yourself it acts through the game's status-effect system (no console cheats). Like the rest of AdminHammer, only for the host or a server admin. Remove it any time; AdminHammer works without it.

## Requirements

- [AdminHammer](https://thunderstore.io/c/valheim/p/Jatify/AdminHammer/) 1.0.1 or newer, which brings BepInEx, Jotunn and SeneaL UI
- [SeneaL UI](https://thunderstore.io/c/valheim/p/seneaL/SeneaL_UI/) 1.2.5 or newer

A mod manager (r2modman, Gale, Thunderstore App) installs them for you.

## Credits

- Interface: drawn at runtime with [SeneaL UI](https://thunderstore.io/c/valheim/p/seneaL/SeneaL_UI/)'s components, theme and icons, by seneaL. None of SeneaL UI's files are included in this package; it must be installed separately.
- Icons: SeneaL UI's adaptation of [Phosphor Icons](https://phosphoricons.com) (MIT License, © 2023 Phosphor Icons).
- Mod loader: [BepInEx](https://github.com/BepInEx/BepInEx).

BuffMeDaddy is an unofficial, fan-made mod. It is not made, endorsed or supported by seneaL or Iron Gate AB. Please report BuffMeDaddy bugs here, not to SeneaL UI. Valheim is a trademark of Iron Gate AB.

Source: https://github.com/imdizmo/BuffMeDaddy (MIT; the MIT licence covers BuffMeDaddy's own code only)
