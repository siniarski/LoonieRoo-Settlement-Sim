# Texture painting for the look-development characters (build_lookdev.py): colours and cloth detail as
# functions of the rest (T-pose) position, evaluated with numpy. The build bakes each texel's rest
# position into a float image, so the same functions paint the texture (per texel) and displace the
# high-poly mesh (per vertex) whose normals are baked onto the low one: what's painted as a hem is
# also a hem in the normal map.
#
# Outfits are dicts of garments by region; the zombie's is worn, torn and bloodied, the survivor's
# dirty but whole. Blender space: Z up, the character faces -Y, its left is +X.

import numpy as np

import sculpt as S

# ---------------------------------------------------------------- noise


def _hash(ix, iy, iz):
    h = (ix * 374761393 + iy * 668265263 + iz * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return (h ^ (h >> 16)).astype(np.float64) / 0xFFFFFFFF


def noise(p, scale):
    """Smooth 3D value noise in 0..1, p (N, 3) metres, features about 1/scale apart."""
    q = p * scale
    i = np.floor(q).astype(np.int64)
    f = q - i
    u = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (u[:, 0] if dx else 1 - u[:, 0]) * (u[:, 1] if dy else 1 - u[:, 1]) * (u[:, 2] if dz else 1 - u[:, 2])
                out = out + w * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz)
    return out


def fbm(p, scale, octaves=4):
    v, a, total = 0, 0.5, 0
    for k in range(octaves):
        v = v + a * noise(p + k * 17.3, scale * 2 ** k)
        total += a
        a *= 0.5
    return v / total


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    t = np.asarray(t)[:, None] if np.ndim(t) == 1 else t
    return a * (1 - t) + b * t


def rgb(*c):
    return np.array(c, np.float64)


# ---------------------------------------------------------------- outfits and regions

OUTFITS = {
    # sleeve: how far out along the arm the sleeve reaches (|x|, m: the shoulder is ~0.2, the wrist 0.74);
    # neck: the collar's height at the front; hem: the shirt's bottom; shoe: the shoe's top.
    "zombie": {"shirt": rgb(0.30, 0.36, 0.42), "sleeve": 0.34, "neck": 1.415, "hem": 0.93,
               "trousers": rgb(0.17, 0.21, 0.30), "shoes": rgb(0.55, 0.53, 0.50), "shoe": 0.10,
               "skin": rgb(0.40, 0.42, 0.37), "hair": rgb(0.16, 0.13, 0.10), "dead": True,
               "face": "feral"},  # chosen 2026-11-08 from the five candidates (sculpt.py FACE_VARIANTS)
    "survivor": {"shirt": rgb(0.30, 0.31, 0.22), "sleeve": 0.73, "neck": 1.44, "hem": 0.90,
                 "trousers": rgb(0.24, 0.22, 0.19), "shoes": rgb(0.20, 0.14, 0.09), "shoe": 0.18,
                 "skin": rgb(0.70, 0.52, 0.42), "hair": rgb(0.20, 0.15, 0.11), "dead": False},
}


# Outfits the dead also wear (build_lookdev.py --outfit): painted like the tee, other cuts and cloth.
OUTFIT_VARIANTS = {
    "longsleeve": {"shirt": rgb(0.36, 0.33, 0.27), "sleeve": 0.72, "neck": 1.44, "hem": 0.90,
                   "trousers": rgb(0.26, 0.24, 0.20), "shoes": rgb(0.22, 0.16, 0.11), "shoe": 0.17},
}


def regions(p, o):
    """Soft masks (N,) by rest position: shirt, trousers, shoes, hair, brows, lips, eye, iris, teeth,
    mouth (the inside), and cloth (any garment)."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    soft = 0.004
    m = {}
    # The eyes and teeth are their own geometry: pick them out by distance first.
    de = np.minimum(np.linalg.norm(p - np.array(S.EYE_L), axis=1), np.linalg.norm(p - np.array(S.EYE_R), axis=1))
    m["eye"] = (de < S.EYE_RADIUS * 1.08).astype(float)
    front = np.where(np.abs(p[:, 0] - S.EYE_L.x) < np.abs(p[:, 0] - S.EYE_R.x), S.EYE_L.x, S.EYE_R.x)
    m["iris"] = m["eye"] * sstep(0.0062, 0.0050, np.hypot(x - front, z - S.EYE_L.z)) * (y < S.EYE_L.y - 0.006)
    m["pupil"] = m["eye"] * sstep(0.0028, 0.0020, np.hypot(x - front, z - S.EYE_L.z)) * (y < S.EYE_L.y - 0.006)
    teeth = np.zeros(len(p), bool)
    for _row, c, size, ang in S.teeth_boxes():  # inside one of the boxes (in its own turned frame)
        d = p - np.array(c)
        u = d[:, 0] * np.cos(ang) - d[:, 1] * np.sin(ang)  # the box was turned by -ang: turn back by +ang
        v = d[:, 0] * np.sin(ang) + d[:, 1] * np.cos(ang)
        teeth |= (np.abs(u) < size[0] / 2 + 6e-4) & (np.abs(v) < size[1] / 2 + 6e-4) & (np.abs(d[:, 2]) < size[2] / 2 + 6e-4)
    m["teeth"] = teeth.astype(float)
    # The mouth's inside: the slit's floor, behind the lips' front.
    bag = np.linalg.norm((p - np.array(S.MOUTH_BAG_C)) / np.array(S.MOUTH_BAG_HALF), axis=1)
    m["mouth"] = np.clip(sstep(-0.087, -0.083, y) * sstep(0.005, 0.002, np.abs(z - S.MOUTH.z)) * sstep(S.MOUTH_HALF, S.MOUTH_HALF * 0.7, ax)
                         + (bag < 1.08), 0, 1) * (1 - m["teeth"])
    m["lips"] = sstep(0.009, 0.006, np.abs(z - S.MOUTH.z)) * sstep(S.MOUTH_HALF * 1.1, S.MOUTH_HALF * 0.8, ax) * (y < -0.07)
    head = sstep(1.50, 1.52, z) * (ax < 0.11)
    hairline = np.where(y < -0.02, S.EYE_L.z + 0.050 + 0.012 * (ax / 0.07) ** 2, 1.585 - 0.03 * sstep(0.0, 0.08, y))
    ears = sstep(0.066, 0.072, ax) * sstep(0.04, 0.02, np.abs(z - (S.EYE_L.z - 0.01)))
    m["hair"] = head * sstep(hairline - 0.002, hairline + 0.004, z) * (1 - ears)
    brow_z = S.EYE_L.z + 0.017
    arch = 0.003 * np.exp(-((ax - 0.030) / 0.012) ** 2)  # highest above the outer iris
    m["brows"] = sstep(0.0034, 0.0020, np.abs(z - brow_z - arch)) * sstep(0.048, 0.042, ax) * sstep(0.009, 0.013, ax) * (y < -0.06)
    # The lash line round each eye's opening (sculpt.eye_opening, here in numpy), heavier on top.
    q = np.full(len(p), 9.0)
    for sx, eye in ((1, S.EYE_L), (-1, S.EYE_R)):
        lat = (x - eye.x) * sx
        dz = z - eye.z - 0.0012 * lat / 0.014
        qe = np.hypot(lat / 0.014, dz / np.where(dz > 0, 0.0050, 0.0042))
        q = np.where(np.abs(x - eye.x) < 0.03, np.minimum(q, qe), q)
    m["lash"] = sstep(1.35, 1.05, q) * sstep(0.85, 1.0, q) * (1 - m["eye"]) * (y < -0.06) * np.where(z > S.EYE_L.z, 1.0, 0.45)
    # Garments.
    arm = sstep(0.19, 0.21, ax) * sstep(1.36, 1.38, z)
    torso = 1 - arm
    # The collar only cuts round the neck (a crew neck dipping at the front); out from it the cloth
    # covers the shoulders to their tops.
    neck_z = o["neck"] - 0.03 * sstep(0.0, -0.08, y) * sstep(0.08, 0.0, ax)
    neck_z = neck_z + (1.62 - neck_z) * sstep(0.062, 0.085, ax)
    shirt_t = torso * sstep(o["hem"] - soft, o["hem"] + soft, z) * sstep(neck_z + soft, neck_z - soft, z) * (ax < 0.25)
    shirt_a = arm * sstep(o["sleeve"] + soft, o["sleeve"] - soft, ax)
    m["shirt"] = np.clip(shirt_t + shirt_a, 0, 1)
    legs = sstep(1.0, 0.99, z) * sstep(o["shoe"] - soft, o["shoe"] + soft, z) * (ax < 0.2)
    m["trousers"] = legs * (1 - m["shirt"])
    m["shoes"] = sstep(o["shoe"] + soft, o["shoe"] - soft, z)
    m["cloth"] = np.clip(m["shirt"] + m["trousers"] + m["shoes"], 0, 1) * (1 - m["eye"])
    return m


def cloth_lift(p, o):
    """How far cloth stands off the skin (metres, per vertex): the hems and cuffs show in profile."""
    m = regions(p, o)
    return 0.0035 * m["shirt"] + 0.0028 * m["trousers"] + 0.006 * m["shoes"] + 0.002 * m["hair"] * (1 - m["cloth"])


def detail(p, o):
    """Fine relief for the high-poly mesh (metres along the normal): folds at the waist, elbows and
    knees, the weave's crumple, a seam at each hem; on skin a faint unevenness (deeper on the dead)."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    m = regions(p, o)
    folds_t = (fbm(p * np.array([1.0, 1.0, 3.5]), 9, 3) - 0.5) * 0.006  # horizontal creases on the torso
    folds_a = (fbm(p * np.array([3.5, 1.0, 1.0]), 9, 3) - 0.5) * 0.006  # round the arms
    bunch_e = sstep(0.06, 0.0, np.abs(ax - 0.48))                          # at the elbows
    bunch_k = sstep(0.07, 0.0, np.abs(z - 0.53)) + sstep(0.08, 0.0, np.abs(z - 0.16)) * 0.6  # knees, ankles
    rings = np.sin(z * 260 + fbm(p, 20, 2) * 6) * 0.0015
    crumple = (fbm(p, 60, 3) - 0.5) * 0.0018
    arm = sstep(0.19, 0.21, ax)
    shirt = m["shirt"] * (folds_t * (1 - arm) + folds_a * arm * (0.4 + bunch_e) + crumple)
    trousers = m["trousers"] * (rings * bunch_k + (fbm(p * np.array([1, 1, 3]), 10, 3) - 0.5) * 0.004 + crumple)
    skin = (1 - m["cloth"]) * (fbm(p, 120, 2) - 0.5) * (0.0016 if o["dead"] else 0.0006)
    hair = m["hair"] * (fbm(p * np.array([1, 1, 0.3]), 140, 2) - 0.5) * 0.0025
    return shirt + trousers + skin + hair


# ---------------------------------------------------------------- colour


def albedo(p, ao, o, style, out=None):
    """Colour per texel (N, 3), sRGB as displayed (the palette above is too). style "plus": soft AO (the normal map shades the folds);
    "pz": strong AO and flatter colour, Zomboid's painted look. With out (a dict), also the recolour mask the
    game tints a dead body's clothes by (out["top"], out["bottom"]: the clean cloth of the shirt and the
    trousers, 0..1, without the blood, dirt and holes on them)."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    m = regions(p, o)
    dead = o["dead"]
    n1, n2, n3 = fbm(p, 14, 4), fbm(p + 5.1, 40, 3), fbm(p + 9.7, 120, 2)

    # Skin: mottled; the dead's greyer, bruised, with veins and dark sockets.
    skin = o["skin"] * (0.88 + 0.24 * n2[:, None])
    face = o.get("face", "slack")
    if dead:
        if face == "gaunt":    # yellowed, waxy, the skin tight over the bone
            skin = skin * rgb(1.08, 1.04, 0.92)
        elif face == "bloated":  # marbled purple and green, livid where the blood has pooled
            marble = sstep(0.03, 0.0, np.abs(fbm(p, 30, 4) - 0.5)) * 0.6
            skin = mix(skin * rgb(0.95, 1.02, 0.85), rgb(0.30, 0.22, 0.32), marble + sstep(0.5, 0.8, n1) * 0.4)
        bruise = sstep(0.62, 0.78, n1) * 0.7
        skin = mix(skin, rgb(0.32, 0.26, 0.34), bruise)
        veins = sstep(0.025, 0.0, np.abs(fbm(p, 55, 3) - 0.5)) * 0.45
        skin = mix(skin, rgb(0.24, 0.27, 0.30), veins)
        de = np.minimum(np.linalg.norm(p - np.array(S.EYE_L), axis=1), np.linalg.norm(p - np.array(S.EYE_R), axis=1))
        skin = mix(skin, rgb(0.20, 0.14, 0.16), sstep(0.030, 0.014, de) * 0.8)
    else:
        flush = sstep(0.04, 0.0, np.hypot(ax - 0.045, z - 1.61)) * 0.25 * (y < -0.04)
        skin = mix(skin, rgb(0.72, 0.38, 0.34), flush)
    lips = rgb(0.36, 0.24, 0.26) if dead else rgb(0.55, 0.32, 0.30)
    if face == "gaunt":
        lips = rgb(0.30, 0.16, 0.15)  # shrunk back: what shows is gum
    col = mix(skin, lips, m["lips"] * 0.8)
    if face == "torn":
        # The wound: raw at the edge, dark deep in, ragged.
        d = np.hypot((x - S.TORN_C.x) / 1.3, (z - S.TORN_C.z) / 0.9)
        edge = 0.016 + 0.004 * (fbm(p, 120, 2) - 0.5)
        raw = sstep(edge + 0.003, edge - 0.002, d) * (y < -0.04)
        col = mix(col, mix(rgb(0.42, 0.08, 0.06), rgb(0.10, 0.02, 0.02), sstep(edge, 0.004, d)), raw * (1 - m["teeth"]))
    col = mix(col, rgb(0.06, 0.02, 0.02), m["mouth"])
    col = mix(col, o["hair"] * (0.7 + 0.6 * n3[:, None]), m["brows"] * 0.9)
    col = mix(col, rgb(0.10, 0.07, 0.06), m["lash"] * 0.85)
    # The dead's hair is painted, thin and patchy; a survivor's scalp is skin with a stubble shadow (their
    # hair is a mesh, build_hair.py: painted hair would show through round its edges).
    hair_amt = m["hair"] * (sstep(0.35, 0.55, fbm(p, 30, 3)) if dead else 0.22)
    col = mix(col, o["hair"] * (0.75 + 0.5 * fbm(p * np.array([1, 1, 0.25]), 160, 2)[:, None]), hair_amt)

    # Garments: weave, wear (faded on the raised parts), dirt rising from the ground.
    weave = 0.92 + 0.16 * n3[:, None]
    shirt = o["shirt"] * weave * (0.9 + 0.2 * n1[:, None])
    jeans = o["trousers"] * weave * (0.85 + 0.3 * fbm(p * np.array([1, 1, 4]), 25, 3)[:, None])
    jeans = mix(jeans, jeans * 1.6 + 0.05, sstep(0.05, 0.0, np.abs(z - 0.55)) * 0.5 * (y < 0))  # worn knees
    shoes = o["shoes"] * (0.9 + 0.2 * n2[:, None])
    shoes = mix(shoes, rgb(0.9, 0.9, 0.88) * 0.6, sstep(0.025, 0.018, z) * (1 if dead else 0))  # sneaker soles
    col = mix(col, shirt, m["shirt"])
    col = mix(col, jeans, m["trousers"])
    col = mix(col, shoes, m["shoes"])
    dirt = sstep(0.45, 0.75, fbm(p, 8, 4)) * sstep(0.9, 0.0, z) * (0.85 if dead else 0.5)
    col = mix(col, rgb(0.19, 0.15, 0.11), dirt * m["cloth"])
    stain = dirt * 0.9

    if dead:
        # Torn shirt: holes showing the skin under it, frayed round the edge.
        holes = fbm(p + 3.3, 11, 4)
        hole = sstep(0.66, 0.68, holes) * m["shirt"]
        fray = sstep(0.60, 0.66, holes) * m["shirt"] * (1 - hole)
        col = mix(col, col * 0.7 + 0.08, fray)
        col = mix(col, skin * 0.85, hole)
        stain = np.maximum(stain, np.clip(hole + fray * 0.6, 0, 1))
        # Blood: round the mouth and down the chin, the throat and the shirt front; splashes on the hands.
        drip = sstep(0.030, 0.0, ax - 0.03 * (1 - sstep(1.56, 1.2, z))) * sstep(S.MOUTH.z + 0.006, S.MOUTH.z - 0.004, z)
        drip = drip * sstep(0.55, 0.3, fbm(p * np.array([3, 3, 0.6]), 18, 3) + 0.25 * sstep(1.55, 1.1, z)) * (y < 0.0)
        mouth_ring = sstep(0.040, 0.012, np.hypot(x, z - S.MOUTH.z)) * (y < -0.05)
        splash = sstep(0.70, 0.74, fbm(p + 7.1, 22, 4)) * (sstep(0.55, 0.85, ax) + m["shirt"] * 0.6)
        blood = np.clip(drip + mouth_ring * 0.9 + splash, 0, 1) * (1 - m["teeth"]) * (1 - m["eye"])
        dried = mix(rgb(0.28, 0.03, 0.02), rgb(0.12, 0.04, 0.03), n2)
        col = mix(col, dried, blood * 0.92)
        stain = np.maximum(stain, blood)

    # Eyes and teeth.
    sclera = rgb(0.62, 0.62, 0.52) if dead else rgb(0.82, 0.80, 0.76)
    iris = rgb(0.55, 0.56, 0.50) if dead else rgb(0.25, 0.33, 0.30)  # the dead's clouded over
    if face == "feral":  # yellowed and threaded with red, the iris filmed over (dull: not a glow)
        sclera = mix(np.broadcast_to(rgb(0.56, 0.50, 0.40), (len(p), 3)), rgb(0.42, 0.12, 0.10), sstep(0.55, 0.75, n3) * 0.45)
        iris = rgb(0.50, 0.48, 0.38)
    eye = mix(sclera, iris, m["iris"])
    eye = mix(eye, rgb(0.42, 0.42, 0.38) if dead else rgb(0.02, 0.02, 0.02), m["pupil"])
    col = mix(col, eye, m["eye"])
    teeth = rgb(0.55, 0.50, 0.36) if dead else rgb(0.78, 0.74, 0.62)
    col = mix(col, teeth * (0.8 + 0.3 * n3[:, None]), m["teeth"])

    if out is not None:
        clean = np.clip(1 - stain, 0, 1) * (1 - m["eye"])
        out["top"] = m["shirt"] * clean
        out["bottom"] = m["trousers"] * clean
    shade = (0.30 + 0.70 * ao) if style == "pz" else (0.62 + 0.38 * ao)
    col = col * shade[:, None]
    if style == "pz":  # flatter, fewer tones
        col = np.round(col * 24) / 24
    return np.clip(col, 0, 1)
