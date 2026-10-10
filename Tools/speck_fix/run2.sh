S="C:/Users/Luka/AppData/Local/Temp/claude/C--Users-Luka-Documents-GitHub-GymChaos/1475b657-6b58-412e-8a85-db1000e1e409/scratchpad"; B="/c/Program Files/Blender Foundation/Blender 4.5/blender.exe"; A=C:/Users/Luka/Documents/GitHub/GymChaos/GymChaos/Assets/Resources
nm=$1; f=$2; t=$3; npz=$4
for it in 1 2 3; do
  UV_INSET=1024 TEX_OVERRIDE="$A/Characters/Textures/$t" RES=1000 "$B" -b --factory-startup -P "$S/views_inset.py" -- "$A/Characters/$f" "$S/fix/v_${nm}" "front:0:5,back:180:5,left:90:5,right:270:5" 2.0 "0.5,0.5,0.6" >/dev/null 2>&1
  n=$(python -I "$S/fix/lint_only_json.py" "$S/fix/${nm}_lint.json" "$S/fix/v_${nm}_front.png" "$S/fix/v_${nm}_back.png" "$S/fix/v_${nm}_left.png" "$S/fix/v_${nm}_right.png")
  echo "$nm iter $it lint $n"
  [ "$n" = "0" ] && break
  "$B" -b --factory-startup -P "$S/fix/probe_uv.py" -- "$A/Characters/$f" "$S/fix/${nm}_lint.json" 1000 2.0 0.5,0.5,0.6 "$S/fix/${nm}_uv.json" >/dev/null 2>&1
  python -I "$S/fix/fix_points.py" "$S/npz/$npz.npz" "$A/Characters/Textures/$t" "$S/fix/${nm}_uv.json"
done
