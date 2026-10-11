"""
Physics props (Game/Assets/Vexa/Core/Physics/Props.cs): wooden crates, a steel drum and the red explosive barrel.
Each model's origin is the center of its collision box, sizes match PropDef.Half.

    blender --background --python vexa_props.py -- --out <Game/Assets/Vexa/Resources/Models> [--textures]
"""
import argparse
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vexa_common import (DETAIL, box, custom_mat, cyl, export_fbx, join, reset_scene, set_detail,  # noqa: E402
                         tri_count, torus)

custom_mat("WoodCrate", (0.42, 0.27, 0.13), 0.0, 0.7)
custom_mat("WoodCrateDark", (0.27, 0.16, 0.07), 0.0, 0.75)
custom_mat("SteelCorner", (0.30, 0.31, 0.32), 0.9, 0.4)
custom_mat("MetalDrum", (0.16, 0.24, 0.30), 0.6, 0.45)
custom_mat("RedDrum", (0.55, 0.07, 0.05), 0.35, 0.45)
custom_mat("HazardYellow", (0.85, 0.65, 0.05), 0.0, 0.5)
custom_mat("Polymer", (0.045, 0.047, 0.05), 0.0, 0.72)


def crate(s):
    """Wooden crate of edge s: plank panels inside a frame of boards, diagonal braces, steel corners."""
    h = s / 2
    t = s * 0.07          # frame board width
    parts = [box("Core", (0, 0, 0), (s * 0.96, s * 0.96, s * 0.96), "WoodCrateDark", bevel=False)]
    # plank grooves on the four sides and the top
    n = 4
    for i in range(1, n):
        o = -h + s * i / n
        for side in (-1, 1):
            parts.append(box(f"GrooveX{i}{side}", (side * s * 0.481, 0, o), (0.004, s * 0.9, 0.006), "Polymer", bevel=False))
            parts.append(box(f"GrooveY{i}{side}", (0, side * s * 0.481, o), (s * 0.9, 0.004, 0.006), "Polymer", bevel=False))
        parts.append(box(f"GrooveTop{i}", (o, 0, s * 0.481), (0.006, s * 0.9, 0.004), "Polymer", bevel=False))
    # frame: 12 edge boards
    for a in (-1, 1):
        for b in (-1, 1):
            parts.append(box(f"EdgeZ{a}{b}", (a * (h - t / 2), b * (h - t / 2), 0), (t, t, s), "WoodCrate"))
            parts.append(box(f"EdgeY{a}{b}", (a * (h - t / 2), 0, b * (h - t / 2)), (t, s - 2 * t, t), "WoodCrate"))
            parts.append(box(f"EdgeX{a}{b}", (0, a * (h - t / 2), b * (h - t / 2)), (s - 2 * t, t, t), "WoodCrate"))
    # a diagonal brace on each side
    L = (s - 2 * t) * math.sqrt(2)
    for side in (-1, 1):
        parts.append(box(f"BraceX{side}", (side * (h - t * 0.35), 0, 0), (t * 0.7, L * 0.98, t * 0.9), "WoodCrate",
                         rot=(math.radians(45 * side), 0, 0)))
        parts.append(box(f"BraceY{side}", (0, side * (h - t * 0.35), 0), (L * 0.98, t * 0.7, t * 0.9), "WoodCrate",
                         rot=(0, math.radians(-45 * side), 0)))
    # steel corner caps
    c = t * 1.25
    for a in (-1, 1):
        for b in (-1, 1):
            for d in (-1, 1):
                parts.append(box(f"Corner{a}{b}{d}", (a * (h - c / 2 + 0.003), b * (h - c / 2 + 0.003), d * (h - c / 2 + 0.003)),
                                 (c, c, c), "SteelCorner"))
    return join(parts, "crate")


def drum(name, paint, hazard=False):
    """Steel drum, 0.58 m wide and 0.88 m tall (collision box 0.58 x 0.88 x 0.58)."""
    r, hh = 0.285, 0.44
    parts = [cyl("Body", (0, 0, -hh + 0.012), (0, 0, hh - 0.012), r - 0.006, paint)]
    # rolling hoops and rims
    for z in (-hh * 0.33, hh * 0.33):
        parts.append(torus(f"Hoop{z:.2f}", (0, 0, z), r - 0.004, 0.009, paint))
    for z in (-hh + 0.012, hh - 0.012):
        parts.append(torus(f"Rim{z:.2f}", (0, 0, z), r - 0.008, 0.012, "SteelCorner"))
    # lid with two bungs
    parts.append(cyl("Lid", (0, 0, hh - 0.03), (0, 0, hh - 0.012), r - 0.02, paint))
    parts.append(cyl("Bung0", (0.12, 0.05, hh - 0.015), (0.12, 0.05, hh - 0.002), 0.03, "SteelCorner"))
    parts.append(cyl("Bung1", (-0.14, -0.04, hh - 0.015), (-0.14, -0.04, hh - 0.004), 0.02, "SteelCorner"))
    if hazard:
        # yellow band with black chevrons between the hoops: "explosive"
        parts.append(cyl("Band", (0, 0, -0.09), (0, 0, 0.09), r - 0.003, "HazardYellow", bevel=False))
        k = 14
        for i in range(k):
            a = 2 * math.pi * i / k
            parts.append(box(f"Stripe{i}", (math.cos(a) * (r - 0.001), math.sin(a) * (r - 0.001), 0), (0.006, 0.05, 0.2), "Polymer",
                             bevel=False, rot=(math.radians(35), 0, a)))
    return join(parts, name)


PROPS = {
    "crate": lambda: crate(0.9),
    "smallcrate": lambda: crate(0.6),
    "barrel": lambda: drum("barrel", "MetalDrum"),
    "redbarrel": lambda: drum("redbarrel", "RedDrum", hazard=True),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--textures", action="store_true")
    ap.add_argument("--only", default="")
    args = ap.parse_args([a for a in sys.argv[1:] if a != "--"])
    ids = [p for p in args.only.split(",") if p] or list(PROPS)
    for level, sub, size in (("pc", "Props", 1024), ("mobile", os.path.join("Props", "Mobile"), 256)):
        set_detail(level)
        for pid in ids:
            reset_scene()
            for name in list(PROPS_MATS):
                custom_mat(name, *PROPS_MATS[name])
            o = PROPS[pid]()
            if args.textures:
                import vexa_textures
                vexa_textures.bake_model(o, os.path.join(args.out, sub, "Textures"), pid, size, normal=level == "pc",
                                         scale_hint=1.4, samples=4, jpeg=level == "pc")
            export_fbx(os.path.join(args.out, sub, pid + ".fbx"), [o])
            print(f"{level:7} {pid:11} {tri_count(o):6} tris", flush=True)


# reset_scene() clears materials, so they are re-created per prop
PROPS_MATS = {
    "WoodCrate": ((0.42, 0.27, 0.13), 0.0, 0.7),
    "WoodCrateDark": ((0.27, 0.16, 0.07), 0.0, 0.75),
    "SteelCorner": ((0.30, 0.31, 0.32), 0.9, 0.4),
    "MetalDrum": ((0.16, 0.24, 0.30), 0.6, 0.45),
    "RedDrum": ((0.55, 0.07, 0.05), 0.35, 0.45),
    "HazardYellow": ((0.85, 0.65, 0.05), 0.0, 0.5),
}

if __name__ == "__main__":
    main()
