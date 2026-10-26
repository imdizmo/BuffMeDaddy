# BuffMeDaddy

[![Join the Discord](https://jatify.com/badges/discord.png)](https://discord.gg/Ru9t2KdfVK) [![Buy me a coffee](https://jatify.com/badges/coffee.png)](https://buymeacoffee.com/imdizmo)

An optional add-on for [AdminHammer](https://thunderstore.io/c/valheim/p/Jatify/AdminHammer/): adds a **Buffs** tab to the admin panel.

[Thunderstore](https://thunderstore.io/c/valheim/p/Jatify/BuffMeDaddy/) · [GitHub](https://github.com/imdizmo/BuffMeDaddy) · [jatify.com](https://jatify.com)

![The Buffs tab: Rested given with no time limit](https://raw.githubusercontent.com/imdizmo/BuffMeDaddy/main/screenshots/buffs.webp)

## What it does

- Every status effect in the game, sorted into Meads, Forsaken powers, Food & rest, Set & item effects, Ailments and Other, with search.
- **Give** or **Remove** the selected one (or right-click a slot to toggle it), **Remove all** to clear.
- **No time limit**, per effect: switch it on and that effect never runs out, whether you gave it here or drank the mead. Switch it off and it gets a fresh normal duration. Your choices are remembered.
- **Profiles:** save the effects you have on, each with its no-time-limit switch, under a name. One click on a profile gives you all of them again (or gives them to the player you picked).
- ON = active, ∞ = no time limit; the selected effect shows its time left.

**Other players:** pick who in the panel's **Target** (you, or any player online). Giving works for anyone, even without mods. **No time limit** for someone else keeps the effect topped up from your game while you are both online; **Remove** stops that and lets it run out (an effect cannot be taken off another player's character).

On yourself it acts through the game's status-effect system (no console cheats). Like the rest of AdminHammer, only for the host or a server admin. Remove it any time; AdminHammer works without it.

## Requirements

- [AdminHammer](https://thunderstore.io/c/valheim/p/Jatify/AdminHammer/) 1.1.0 or newer, which brings BepInEx and Jotunn

A mod manager (r2modman, Gale, Thunderstore App) installs them for you.

## Credits

- Interface: AdminHammer's panel parts (the Jatify design).
- Mod loader: [BepInEx](https://github.com/BepInEx/BepInEx).
- Code: written together with Claude (Anthropic's AI), which shows up as a co-author in the commit history.

BuffMeDaddy is an unofficial, fan-made mod. It is not made, endorsed or supported by Iron Gate AB. Please report BuffMeDaddy bugs [on GitHub](https://github.com/imdizmo/BuffMeDaddy/issues). Valheim is a trademark of Iron Gate AB.

Source: https://github.com/imdizmo/BuffMeDaddy (MIT; the MIT licence covers BuffMeDaddy's own code only)
