# Gym Chaos
[▶ Play in browser](https://lukaazman.github.io/GymChaos/)

A 3D Unity game built in C#.

> **Note:** Not all features are available in the browser (WebGL) preview due to compatibility limitations between Unity and WebGL. For the full experience, a desktop build is recommended. See [Browser build differences](#browser-build-differences) for the details.

## Assets
The majority of the assets are self-made in Blender, with some character models created using Microsoft Copilot Labs' image-to-3D tool.

## Features
- Popular bodybuilders and fitness icons as characters.
- Main space is a fitness building where you can work out and progress your physique.
- Interact with your gym mates — and potentially fight them.

## Browser build differences
The browser build runs the same game, but a few things behave differently there.

**Missing or limited in the browser**
- **Exit to desktop:** the EXIT button on the start screen does nothing, because a browser tab can't be closed by the game. EXIT in the pause menu takes you back to the start screen instead of quitting.
- **Display settings:** browsers control the window size, so the resolution and window mode options in Settings may have little or no effect.
- **Precise collisions:** some 3D models fall back to simple box colliders in the browser, so collisions with that scenery and some loose items are less exact than on desktop.
- **Background saving:** saves are written on the main thread rather than in the background, which may cause a short hitch when the game autosaves.
- **Where saves live:** characters and settings are stored in the browser's own storage. They're tied to that browser and are lost if you clear the site's data.
- **Custom radio tracks:** the radio can't scan its music folder in the browser and plays only the built-in playlist. Tracks dropped into the folder only play on desktop.

**Different controls in the browser**
- **Mouse capture:** the browser only lets the game lock the mouse after you click it, so click the game once to start looking around.
- **Opening SoundCloud:** opening SoundCloud from the radio needs pop-ups allowed for the site.

**Browser-only**
- **SoundCloud playlists:** playing a SoundCloud playlist on the radio only works in the browser build. On desktop the link is saved, but the radio keeps playing its local playlist.
