"""Re-run the scan texture cleanup for every runtime asset from pristine copies.

  python Tools/clean_all_scan_textures.py <pristine_root> <npz_dir>

<pristine_root> mirrors GymChaos/Assets (Resources/..., StreamingAssets/...)
with the original, uncleaned files (Logs/agent/texture_backup_20261008).
<npz_dir> holds <name>.npz UV dumps of the FBX models (Tools/dump_mesh_uvs.py).
Results are written into GymChaos/Assets. The jolly cap back is repainted
after cleaning (Tools/fix_jolly_cap_back.py) and Jay's red skin cast is
corrected (Tools/fix_jay_skin.py).
"""

import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "GymChaos", "Assets")
TOOLS = os.path.join(ROOT, "Tools")

# name, path relative to Assets, uv dump (png only), extra options
CHARACTERS = ["arnold", "cbum", "zyzz", "ronnie", "jay", "goku", "mark", "davie", "policeman"]
# Characters and vehicles: a lower threshold also catches faint dots. Wider
# rings (--max-scale > 1) flag real fabric/print detail on most scans, so only
# the receptionist's flat suit uses them (pocket square kept by a box).
STRONG = ["--threshold", "50"]
SUIT = ["--protect-top", "0.13", "--threshold", "30", "--max-scale", "5",
        "--keep-box", "0.55,0.82,0.67,0.8,0.2,0.6"]  # keep box = pocket square
# Goku is cel-shaded: his ink fold lines are real detail, so default settings.
PNG = [(n, f"Resources/Characters/Textures/{n}.png", n,
        ["--protect-top", "0.13"] + ([] if n == "goku" else STRONG)) for n in CHARACTERS]
PNG += [
    ("player", "Resources/Characters/Textures/player_authored.png", "player", ["--protect-top", "0.13"] + STRONG),
    ("suit_fbx", "Resources/Characters/Textures/manwithsuit1.png", "suit_fbx", SUIT),
]
PNG += [(f"v_{n}", f"Resources/Vehicles/Textures/{t}.png", f"v_{n}", STRONG)
        for n, t in [("cbum", "cbum_vehicle"), ("zyzz", "zyzz_vehicle"), ("arnold", "arnold_vehicle"),
                     ("jay", "jaycutler_vehicle"), ("goku", "goku_vehicle"), ("ronnie", "ronnie_vehicle")]]
SA = "StreamingAssets/BodyBuilders/"
GLB = [("suit", SA + "manwithsuit1.glb", SUIT)]
GLB += [(n, SA + p, STRONG) for n, p in [
    ("hummer", "vehicles/Arnold_Hummer.glb"), ("bus", "vehicles/Davie_Bus.glb"), ("police", "vehicles/Policecar.glb")]]
# bullet.glb is a 256 px hand-made texture with large faces: the face pass
# turns its specular highlights into blocky smears, so it is not cleaned.
GLB += [(n, SA + p, []) for n, p in [
    ("black_bag", "items/black_bag.glb"), ("blue_ball", "items/blue_ball.glb"), ("bullet", "items/bullet.glb"),
    ("color_bag", "items/color_bag.glb"), ("foam_roller", "items/foam_roller.glb"), ("glock17", "items/glock17.glb"),
    ("paper_towel", "items/paper_towel.glb"), ("red_ball", "items/red_ball.glb"), ("sink", "items/sink.glb"),
    ("step", "items/step_platform.glb"), ("toilet", "items/toilet.glb"), ("bench", "items/wooden_bench.glb"),
    ("yoga_half", "items/yoga_roll_halfrolled.glb"), ("yoga_out", "items/yoga_roll_rolledout.glb"),
    ("bush1", "outside/bush1.glb"), ("bush2", "outside/bush2.glb"), ("streetlight", "outside/streetlight.glb"),
    ("tree1", "outside/tree1.glb"), ("tree2", "outside/tree2.glb"), ("radio", "sound/radio.glb"),
    ("baseball", "wearables/baseball_cap.glb"), ("beanie", "wearables/beanie.glb"),
    ("bucket", "wearables/bucket_hat.glb"), ("headband", "wearables/headband.glb"), ("jolly", "wearables/jolly_cap.glb"),
]]


def run(args):
    print(">", " ".join(os.path.basename(a) if os.path.sep in a else a for a in args), flush=True)
    # the open Unity Editor may hold a texture for a moment while it imports
    for attempt in range(4):
        if subprocess.run([sys.executable, "-I"] + args).returncode == 0:
            return
        time.sleep(3)
    raise RuntimeError("failed: " + " ".join(args))


def main(pristine, npz_dir):
    cleaner = os.path.join(TOOLS, "clean_scan_textures.py")
    for name, rel, npz, extra in PNG:
        run([cleaner, "png", os.path.join(npz_dir, npz + ".npz"), os.path.join(pristine, rel),
             os.path.join(ASSETS, rel)] + extra)
    for name, rel, extra in GLB:
        run([cleaner, "glb", os.path.join(pristine, rel), os.path.join(ASSETS, rel)] + extra)
    jolly = os.path.join(ASSETS, SA + "wearables/jolly_cap.glb")
    run([os.path.join(TOOLS, "fix_jolly_cap_back.py"), jolly, jolly])
    for _ in range(2):  # repainted panels: settle stray faces at panel edges
        run([cleaner, "glb", jolly, jolly])
    # wide-ring suit run: one more settle pass on its own output
    suit_glb = os.path.join(ASSETS, SA + "manwithsuit1.glb")
    run([cleaner, "glb", suit_glb, suit_glb] + SUIT)
    suit_png = os.path.join(ASSETS, "Resources/Characters/Textures/manwithsuit1.png")
    run([cleaner, "png", os.path.join(npz_dir, "suit_fbx.npz"), suit_png, suit_png] + SUIT)
    # faint grey scuffs on the raised right sleeve (towel hand excluded by z)
    sleeve = ["--threshold", "20", "--max-scale", "5", "--only-box", "0,0.32,0.5,0.85,0,0.75",
              "--protect-top", "0.13"]
    run([cleaner, "glb", suit_glb, suit_glb] + sleeve)
    run([cleaner, "png", os.path.join(npz_dir, "suit_fbx.npz"), suit_png, suit_png] + sleeve)
    # red/grey specks on the lapels and jacket front; pocket square kept
    chest = ["--threshold", "22", "--max-scale", "5", "--only-box", "0.2,0.85,0.5,0.86,0.45,1",
             "--keep-box", "0.55,0.82,0.67,0.8,0.2,0.6", "--protect-top", "0.13"]
    run([cleaner, "glb", suit_glb, suit_glb] + chest)
    run([cleaner, "png", os.path.join(npz_dir, "suit_fbx.npz"), suit_png, suit_png] + chest)
    police = os.path.join(TOOLS, "fix_police_taillights.py")
    run([police, "glb", os.path.join(ASSETS, SA + "vehicles/Policecar.glb"), os.path.join(ASSETS, SA + "vehicles/Policecar.glb")])
    ronnie_car = os.path.join(ASSETS, "Resources/Vehicles/Textures/ronnie_vehicle.png")
    run([police, "png", ronnie_car, ronnie_car])
    # last: light lint texels on dark clothing of every character (heads protected)
    for name, rel, npz, extra in PNG:
        if rel.startswith("Resources/Characters/"):
            out = os.path.join(ASSETS, rel)
            box = ["--keep-box", "0.55,0.82,0.67,0.8,0.2,0.6"] if npz == "suit_fbx" else []
            run([cleaner, "png", os.path.join(npz_dir, npz + ".npz"), out, out, "--protect-top", "0.13",
                 "--dark-lint"] + box)
    run([cleaner, "glb", suit_glb, suit_glb, "--protect-top", "0.13", "--threshold", "24",
         "--keep-box", "0.55,0.82,0.67,0.8,0.2,0.6", "--dark-lint"])
    # UV island borders one texel inward (GLBs; FBX meshes get the same at
    # runtime through ScanUvInset). Run once on pristine geometry only.
    for name, rel, extra in GLB:
        path = os.path.join(ASSETS, rel)
        run([os.path.join(TOOLS, "inset_uv_islands.py"), path, path, "--texels", "1.0"])
    textures = os.path.join(ASSETS, "Resources/Characters/Textures")
    run([os.path.join(TOOLS, "fix_jay_skin.py"), os.path.join(textures, "jay.png"),
         os.path.join(textures, "arnold.png"), os.path.join(textures, "jay.png"), "0.9"])


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
