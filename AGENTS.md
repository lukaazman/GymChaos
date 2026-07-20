# GymChaos repository guide

This file is the persistent working context for AI agents in this repository.
Read it before broad repository scans or implementation work.

## Maintenance rule

- Keep this document current whenever a change alters the project layout, entry scene, controls, core gameplay architecture, supported tooling, or verification workflow.
- Update the relevant existing section in the same change; do not append a chronological diary.
- Record only facts verified from the repository. If a detail is uncertain, inspect the source rather than preserving a guess here.
- Keep this guide concise. Do not list every asset or serialized scene object.

## Project at a glance

- Product: `GymChaos`, a first-person Unity gym-fighting prototype.
- Unity project root: `GymChaos/` (open this directory in Unity, not the repository root).
- Editor version: Unity `6000.3.8f1`.
- Rendering: Universal Render Pipeline `17.3.0`.
- Input: Unity Input System `1.18.0`; `activeInputHandler: 2` currently permits both input backends. Gameplay scripts contain Input System and legacy-input fallbacks.
- Navigation package: AI Navigation `2.0.10`, although the current enemy movement is Rigidbody force based.
- Product settings currently use `DefaultCompany`, a `1024x768` default window, and no custom application identifier.

## Repository layout

- `GymChaos/Assets/Scenes/SampleScene.unity`: only enabled build scene and the main playable scene.
- `GymChaos/Assets/Scripts/`: current handwritten gameplay code.
- `GymChaos/Assets/Assets/`: FBX gym equipment imported into the Unity project.
- `GymChaos/Assets/Settings/`: URP renderer, pipeline, and volume assets.
- `GymChaos/Assets/_Recovery/`: Unity recovery scenes; do not treat these as the canonical scene unless explicitly recovering lost work.
- `GymChaos/Packages/`: Unity package manifest and lock file.
- `GymChaos/ProjectSettings/`: Unity editor, build, physics, input, quality, and rendering configuration.
- Repository-root `Assets/`: source/export working assets, including Blender files, FBX files, material sources, and some duplicated Unity-style content. It is outside the actual Unity project root. Do not assume edits here affect the playable project; runtime assets must be under `GymChaos/Assets/`.
- Unity-generated folders (`Library`, `Temp`, `Logs`, `Obj`, `Build`, `UserSettings`, `MemoryCaptures`) are ignored and must not be committed.

## Runtime architecture

- `PlayerMovement.cs`: main first-person controller. It owns movement, mouse look, jumping/crouching/sprinting, melee attacks, pickup/drop/throw interaction, held-equipment attacks, the simple IMGUI HUD, cursor locking, and input-backend wrappers.
- On `Start`, `PlayerMovement` locates its camera, creates a carry anchor, creates `PlayerHandRig`, ensures `GymArenaBootstrap` exists, and locks the cursor.
- `PlayerHandRig.cs`: procedurally creates and animates simple first-person hands for movement, punching, shoving, throwing, and holding.
- `GymArenaBootstrap.cs`: runtime scene setup. It adds missing static/pickup colliders, recognizes throwable weights by object-name prefixes, and configures Rigidbody/pickup behavior. Bars built into bench/Smith/preacher equipment stay fixed; pickable plates and nearby loose weights start kinematic when stored on equipment and unlock only when `PickupItem.PickUp` is called. It spawns four named fighters: Zyzz at `CableMachineDual`, CBum/Arnold in front of the mirror wall, and Ronnie in front of the lockers facing the player spawn. At startup it places the player on the visitor side of the reception desk, directly opposite and facing `manwithsuit1`.
- `BodybuilderEnemyVisual.cs`: stagger-loads the original GLB character scans from `Assets/StreamingAssets/BodyBuilders`, builds a model-specific rest skeleton and region weights, preserves their embedded textures with an unlit material, and disables expensive scan shadows. `FaceCensorSettings` fits each narrow curved black eye bar from the outermost real face or glasses vertices at that model's configured eye height, then binds it to the head bone; dead fighters retain the bar with two red eye X markers. Names and damage-revealed red health bars are projected above the live skinned bounds. Disconnected right-hand scan fragments on `manwithsuit1`, including forward-depth held-item fragments, are detected by mesh connectivity and bound as complete components to the hand bone.
- `BodybuilderEnemyAnimator.cs`: layers motion over each model's authored scan pose instead of straightening or retargeting the scans into a shared pose. Walking cadence and blend follow the opponent's real planar Rigidbody speed so the stride continues throughout pursuit instead of becoming a floor glide. Ronnie keeps walking whenever his body is still moving to follow a target, including inside attack range; he has a symmetric leg-focused profile with deliberately minimal upper-body swing.
- `ManWithSuitIdleAnimator.cs`: animates the neutral `manwithsuit1` reception NPC at exactly 1.125x its former size. The NPC is raised 0.08 m above the generated floor so the scan's shoes remain visible, stands between the reception counter and south wall, never receives `EnemyFighter` or pursuit behavior, and keeps a compact screen-space name above his head. The upper-arm transform stays in its authored pose while the forearm drives the visible stroke and the wrist, hand, glove, and held scan fragments inherit the distal chain; no additional shoulder-region skin-weight lock is applied.
- `GymInteriorBuilder.cs`: builds the enclosed gym interior at runtime around the scene content: rubber floor, ceiling and walls, accent bands, mirrors with a realtime reflection probe, ceiling grid and lights, reception, lockers, and wall panels.
- `GymExerciseStation.cs`: discovers supported equipment by hierarchy names, creates nearby interaction stations, owns rep/cardio state, and supplies corrected first-person camera poses. Bench press and preacher sessions animate the Barbell/EzBar already mounted near the machine, attach temporary Plate5/Plate10/Plate20 asset visuals for the selected total weight, and restore the original scene bar afterward. Squats start directly and move the real rack bar across the upper back behind the camera before reracking it on exit. Lat pulldowns animate the machine's hanging attachment above the first-person view together with its cable, without creating a free barbell. Flat bench stations require a barbell in or immediately around the bench rack.
- `PickupItem.cs`: throwable equipment state and physics. Supported `WeightType` values are `Barbell`, `EzBar`, `Plate`, `Plate5`, `Plate10`, and `Plate20`, each with mass/impact tuning. Plate5 preserves the established throw speed; heavier plates receive a small progressive range reduction and downward velocity so Plate10/Plate20 visibly fall sooner. A player-thrown Barbell deals 30 damage and its held push deals 15; EzBar retains the normal 5-damage throw/push behavior.
- `EnemyFighter.cs`: Rigidbody-driven fighters with identity-specific health (CBum/Arnold 60, Zyzz 45, Ronnie 100). CBum, Arnold, and Zyzz pursue the player; police-role Ronnie periodically selects the nearest player or living fighter with a minimum target lock and closer-target hysteresis, excluding the neutral reception NPC. Only zero health releases upright rotation constraints and applies a guaranteed fall angular velocity. Dead fighters never recover or walk, but remain sleeping/damped dynamic bodies that can be moved again by punches, shoves, and thrown equipment.
- The scene's serialized `PlayerMovement` component is attached to a `Player` object. Much of the combat setup is deliberately generated at runtime rather than stored as prefabs.

## Player controls and visible behavior

- `WASD`: move.
- Mouse: look.
- Left Shift: sprint.
- `C`: crouch.
- Space: jump.
- `E`: pick up or drop a supported weight.
- `F`: start the nearby prompted exercise without replacing the `E` pickup interaction.
- Left mouse button: punch with empty hands or throw a held item.
- Right mouse button: shove; a held bar or plate uses its specialized shove.
- Escape: release/restore cursor lock.
- During a strength exercise, Space starts one animated repetition and `Q` or `E` exits.
- After pressing `F` at bench, incline bench, preacher, or lat-pulldown stations, choose the total loaded weight from the mouse-driven grid. The menu states the empty-bar contribution (20 kg barbell or 10 kg EZ-bar); lat pulldown uses a 20-130 kg weight stack. Squats and bodyweight exercises start directly. `Q`, `E`, or Escape cancels a weight menu.
- During treadmill/bike sessions, `W` increases speed/effort, `S` decreases it, Space starts/stops gradually, and `Q` or `E` exits. Their periodic first-person camera shake and FOV scale smoothly with current speed; the bike uses a smaller amplitude and its drive parts rotate with cadence.
- HUD shows living opponent count, controls, and the currently held item. The normal runtime roster contains four fighters: CBum, Zyzz, Arnold, and police-role Ronnie.
- The gameplay camera keeps its serialized FOV on start so the procedural hands meet the bottom of the view. Cardio sessions temporarily widen it with speed and restore it on exit.

## Naming and scene assumptions

- Pickup recognition depends on GameObject names beginning with `Barbell`, `EzBar`, `Plate5`, `Plate10`, or `Plate20`; a plain plate is recognized as `Plate` or `Plate (...)`.
- `WeightStandFlat` plates use a larger dedicated pickup overlap buffer, whole-item closest-point scoring, and a relaxed two-sided view threshold so both faces of the rack remain pickable.
- Bodybuilder and reception-NPC source models are runtime data under `GymChaos/Assets/StreamingAssets/BodyBuilders`. Replacing a GLB with the same filename automatically reuses the runtime rigging pipeline; the model should be an upright, centered static scan with embedded textures. Ronnie uses `ronnie.glb` and the same 2.3 m runtime target height as the other non-Arnold fighters. The current CBum scan includes its own feet, so no procedural shoes or slippers are added. `manwithsuit1.glb` uses a normal 1.82 m target height, a forward-bent-arm rig with a separate right-hand bone, and is intentionally not part of `EnemyRoster`.
- Exercise discovery recognizes benches (`Bench2`/`InclineBench` as incline), cages/power racks/Smith machines, preacher benches, lat pulldowns, dip stations, treadmills, and bikes by hierarchy names. Renaming these objects requires updating `GymExerciseStation.TryClassify`.
- The lat-pulldown moving attachment is grouped at runtime from `Cylinder.028`, `Cylinder.029`, `Cube.013`, `Cylinder.024`, and `Bezier Curve`; keep those FBX child names or update `CreateLatPulldownAttachmentGroup`.
- Strength weight options are exercise-specific totals. Plates are decomposed per side largest-first using 20 kg, 10 kg, and 5 kg asset plates and are stacked symmetrically on the cloned asset bar.
- `GymArenaBootstrap` ignores renderers whose hierarchy names contain player/camera/hand/fighter markers. Renaming scene objects can therefore change automatic collider setup.
- `SampleScene.unity` is the source of truth for the playable environment. Recovery scenes and repository-root Blender scene files are not build entries.
- Preserve Unity `.meta` files and their GUIDs when moving or renaming assets. Perform asset moves through Unity when practical.

## Dependencies and generated content

- Package versions are pinned in `GymChaos/Packages/manifest.json` and `packages-lock.json`.
- Do not hand-edit `packages-lock.json` unless the package operation requires it.
- Avoid committing generated Unity caches or a local build.
- Blender/source assets at repository root can be large and may have similarly named copies under the Unity project. Confirm which copy is referenced before editing or replacing a model.

## Verification workflow

- There are currently no project-specific automated tests or `.asmdef` test assemblies. The Unity Test Framework package is installed, but only tutorial editor code was found.
- For script or scene changes, open `GymChaos/` in Unity `6000.3.8f1`, allow compilation/import to finish, and verify that the Console has no new errors.
- Play `Assets/Scenes/SampleScene.unity` and exercise the behavior changed. For gameplay work, minimally check movement, cursor toggle, attacks, pickup/drop/throw, collisions, enemy pursuit/hits, and HUD where relevant.
- For build-impacting changes, confirm `SampleScene.unity` remains enabled in Build Settings and make an appropriate local build.
- Before handing off, run `git diff --check` and inspect `git status --short`. Use `git -c safe.directory=D:/GitHub/GymChaos ...` in sandboxed environments that reject repository ownership.

## Current cautions

- The repository has two `Assets` trees; always distinguish source assets at `/Assets` from runtime Unity assets at `/GymChaos/Assets`.
- The runtime bootstrap mutates and extends the scene. A scene that looks flat or unconfigured in Edit Mode gains the enclosed interior, lighting, mirrors, colliders, Rigidbodies, pickups, exercise stations, hands, and enemies in Play Mode.
- Large `.unity`, `.fbx`, `.blend`, and `.meta` diffs can be noisy. Keep unrelated serialized/import changes out of focused code changes.
