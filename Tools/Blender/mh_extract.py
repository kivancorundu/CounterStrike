"""
One-off extractor: packs the parts of the MakeHuman 1.1 system assets that the character generator uses into
data/makehuman_cc0.npz (+ the skin texture). MakeHuman's base mesh, targets and system skins are CC0 (see
data/MAKEHUMAN_CC0.md). Source: the npm package "makehuman-data" (JSON/binary conversion of MakeHuman 1.1 data).

    npm pack makehuman-data && tar xf makehuman-data-*.tgz
    python mh_extract.py package/public/data package/src/json/targets/target-list.json
"""
import json
import os
import shutil
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "data")

TARGETS = [
    "macrodetails/caucasian-male-young",
    "macrodetails/universal-male-young-averagemuscle-averageweight",
    "macrodetails/universal-male-young-maxmuscle-averageweight",
    "macrodetails/universal-male-young-averagemuscle-minweight",
    "macrodetails/universal-male-young-maxmuscle-minweight",
    "macrodetails/proportions/male-young-averagemuscle-averageweight-idealproportions",
    "macrodetails/proportions/male-young-maxmuscle-averageweight-idealproportions",
    "macrodetails/height/male-young-averagemuscle-averageweight-maxheight",
    "macrodetails/height/male-young-maxmuscle-averageweight-maxheight",
    "head/head-square", "chin/chin-width-max", "chin/chin-prominent-more", "chin/chin-jaw-drop-less",
    "neck/neck-scale-horiz-more", "neck/neck-scale-depth-more",
    "nose/nose-scale-horiz-incr", "nose/nose-width2-max",
    "torso/torso-muscle-pectoral-incr", "torso/torso-muscle-dorsi-incr", "torso/torso-scale-horiz-incr",
    "stomach/stomach-tone-incr", "stomach/stomach-pregnant-decr",
    "macrodetails/universal-male-young-averagemuscle-maxweight",
    "macrodetails/universal-male-young-maxmuscle-maxweight",
    "macrodetails/proportions/male-young-maxmuscle-maxweight-idealproportions",
    "macrodetails/height/male-young-maxmuscle-maxweight-maxheight",
    "measure/measure-shoulder-increase", "measure/measure-bust-increase", "measure/measure-neckcirc-increase",
    "measure/measure-upperarm-increase", "measure/measure-waist-increase",
    "armslegs/l-upperarm-muscle-incr", "armslegs/r-upperarm-muscle-incr",
    "armslegs/l-upperarm-shoulder-muscle-incr", "armslegs/r-upperarm-shoulder-muscle-incr",
    "armslegs/l-lowerarm-muscle-incr", "armslegs/r-lowerarm-muscle-incr",
    "armslegs/l-upperleg-muscle-incr", "armslegs/r-upperleg-muscle-incr",
    "armslegs/l-lowerleg-muscle-incr", "armslegs/r-lowerleg-muscle-incr",
]

JOINTS = ["root", "spine05", "spine04", "spine03", "spine02", "spine01", "neck01", "neck02", "neck03", "head",
          "clavicle", "shoulder01", "upperarm01", "upperarm02", "lowerarm01", "lowerarm02", "wrist",
          "upperleg01", "upperleg02", "lowerleg01", "lowerleg02", "foot", "toe1-1", "toe3-1",
          "metacarpal1", "metacarpal2", "metacarpal3", "metacarpal4",
          "finger1-1", "finger1-2", "finger1-3", "finger2-1", "finger2-2", "finger2-3", "finger3-1", "finger3-2", "finger3-3",
          "finger4-1", "finger4-2", "finger4-3", "finger5-1", "finger5-2", "finger5-3", "eye", "jaw"]


def parse_faces(d):
    f = d["faces"]
    layers = len(d["uvs"])
    i = 0
    out = []
    while i < len(f):
        t = f[i]
        i += 1
        n = 4 if t & 1 else 3
        vs = f[i:i + n]
        i += n
        mat = 0
        if t & 2:
            mat = f[i]
            i += 1
        uvs = None
        if t & 4:
            i += layers
        if t & 8:
            uvs = f[i:i + n]
            i += n * layers
        if t & 16:
            i += 1
        if t & 32:
            i += n
        if t & 64:
            i += 1
        if t & 128:
            i += n
        out.append((vs, mat, uvs))
    return out


def main(data_dir, target_list):
    d = json.load(open(os.path.join(data_dir, "models", "human_full_size.json")))
    verts = np.array(d["vertices"], dtype=np.float32).reshape(-1, 3) * d.get("scale", 1.0)
    uvs = np.array(d["uvs"][0], dtype=np.float32).reshape(-1, 2)
    mats = [m["DbgName"] for m in d["materials"]]
    faces = parse_faces(d)
    keep = {"body": 0, "helper-l-eye": 1, "helper-r-eye": 2}
    quads, quv, qmat = [], [], []
    for vs, mat, fu in faces:
        name = mats[mat]
        if name not in keep:
            continue
        if len(vs) == 3:
            vs, fu = vs + [vs[2]], (fu + [fu[2]]) if fu else None
        quads.append(vs)
        quv.append(fu if fu else [0, 0, 0, 0])
        qmat.append(keep[name])
    quads = np.array(quads, dtype=np.int32)
    quv = np.array(quv, dtype=np.int32)
    qmat = np.array(qmat, dtype=np.int8)

    joints = {}
    for key, idx in d["metadata"]["joint_pos_idxs"].items():
        bone, end = key.split("____")
        base = bone.split(".")[0]
        if base in JOINTS:
            joints[f"joint:{bone}:{end}"] = np.array(idx, dtype=np.int32)

    names = sorted(json.load(open(target_list))["targets"])
    tb = np.memmap(os.path.join(data_dir, "targets", "targets.bin"), dtype=np.int16, mode="r", shape=(len(names), verts.size))
    tdata = {}
    for t in TARGETS:
        row = names.index(f"data/targets/{t}.target")
        delta = np.array(tb[row]).reshape(-1, 3)
        nz = np.nonzero(np.any(delta != 0, axis=1))[0].astype(np.int32)
        tdata[f"target:{t}:idx"] = nz
        tdata[f"target:{t}:delta"] = delta[nz].astype(np.int16)  # units: 1e-3 base-mesh units

    os.makedirs(OUT, exist_ok=True)
    np.savez_compressed(os.path.join(OUT, "makehuman_cc0.npz"), verts=verts, uvs=uvs, quads=quads, quad_uvs=quv, quad_mat=qmat,
                        **joints, **tdata)
    src = os.path.join(data_dir, "skins", "young_caucasian_male", "textures", "young_lightskinned_male_diffuse.png")
    shutil.copy(src, os.path.join(OUT, "mh_skin_young_male.png"))
    print(len(verts), "verts", len(quads), "faces", len(tdata) // 2, "targets")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
