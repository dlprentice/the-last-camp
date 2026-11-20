"""Generate first-party sculpted first-person right forearm & hand GLB for The Last Camp.

Mounted inside Player.hand_socket so the carried hurricane lantern, split oak firewood log,
and flat skipping stone rest naturally in an organic, flannel-cuffed hand instead of floating.
Uses a single continuous extruded quad-topology mesh (forearm -> wrist -> palm -> 4 curved
fingers + opposable thumb) smoothed with Catmull-Clark Subdivision Surface (level 2), plus
a rolled autumn plaid flannel cuff with baked weave texture, dark wool camp coat sleeve,
and a sleek flat vintage brass-and-leather field wristwatch.

100% original procedural geometry under the repository's MIT licence.

Coordinate convention (glTF Y-up, matching Godot Camera3D local space: +X right, +Y up, -Z forward):
  Grip centre sits at local origin (0, 0, 0); forearm extends back-right-down toward (+X, -Y, +Z).
  In Blender (Z-up, export_yup=True): X_bl = X_gd, Y_bl = -Z_gd, Z_bl = Y_gd.

RUN
    env -u DISPLAY -u WAYLAND_DISPLAY timeout 120 blender -b --factory-startup --python-exit-code 2 -P tools/make_player_hand.py
"""

from __future__ import annotations

import math
from pathlib import Path
import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

REPO = Path(__file__).resolve().parents[1]
OUT_PATH = REPO / "assets/models/player_hand.glb"


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def make_mat(name: str, rgba, roughness=0.78, metallic=0.0, coat=0.0):
    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = coat
    return m


def make_plaid_flannel_mat(name: str):
    """Create a procedural woven autumn pine-and-rust camp flannel texture packed into the GLB."""
    size = 256
    img = bpy.data.images.new(f"{name}_tex", width=size, height=size, alpha=False)
    px = [0.0] * (size * size * 4)
    for y in range(size):
        for x in range(size):
            u = (x % 64) / 64.0
            v = (y % 64) / 64.0
            # Twill weave micro-grain
            twill = 0.94 + 0.08 * math.sin((x + y) * 1.15)
            # Base warm cedar-rust wool
            r, g, b = 0.30, 0.14, 0.09
            # Broad dark forest-pine bands
            in_h = 0.18 < v < 0.44
            in_v = 0.18 < u < 0.44
            if in_h and in_v:
                r, g, b = 0.09, 0.13, 0.09
            elif in_h or in_v:
                r, g, b = 0.17, 0.14, 0.10
            # Thin warm ochre accent threads
            if abs(u - 0.72) < 0.032 or abs(v - 0.72) < 0.032:
                r, g, b = 0.44, 0.31, 0.14
            elif abs(u - 0.31) < 0.018 or abs(v - 0.31) < 0.018:
                r, g, b = 0.07, 0.09, 0.07
            idx = (y * size + x) * 4
            px[idx] = min(1.0, max(0.0, r * twill))
            px[idx + 1] = min(1.0, max(0.0, g * twill))
            px[idx + 2] = min(1.0, max(0.0, b * twill))
            px[idx + 3] = 1.0
    img.pixels = px
    img.pack()

    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.92
    bsdf.inputs["Metallic"].default_value = 0.0
    return m


def make_watch_dial_mat(name: str):
    """Minimal field watch dial texture with matte charcoal face, cream ticks, and hands."""
    size = 128
    img = bpy.data.images.new(f"{name}_tex", width=size, height=size, alpha=False)
    px = [0.0] * (size * size * 4)
    cx = cy = size * 0.5
    for y in range(size):
        for x in range(size):
            dx = (x - cx) / (size * 0.5)
            dy = (y - cy) / (size * 0.5)
            rr = math.hypot(dx, dy)
            ang = math.atan2(dy, dx)
            r, g, b = 0.07, 0.08, 0.075
            # Brass outer bezel ring
            if rr > 0.86:
                r, g, b = 0.42, 0.34, 0.20
            # Hour ticks around dial perimeter
            elif 0.68 < rr < 0.82:
                sector = abs(((ang / (math.pi / 6.0)) + 12.5) % 1.0 - 0.5)
                if sector < 0.065:
                    r, g, b = 0.78, 0.72, 0.56
            # Simple hour & minute hands (~8:20 evening camp hour!)
            if rr < 0.58:
                # Minute hand toward ~4 (ang ~ -0.52 rad)
                d_min = abs(dx * math.sin(-0.52) - dy * math.cos(-0.52))
                proj_min = dx * math.cos(-0.52) + dy * math.sin(-0.52)
                # Hour hand toward ~8 (ang ~ -2.6 rad)
                d_hr = abs(dx * math.sin(-2.6) - dy * math.cos(-2.6))
                proj_hr = dx * math.cos(-2.6) + dy * math.sin(-2.6)
                if (d_min < 0.032 and 0.0 <= proj_min < 0.55) or (d_hr < 0.044 and 0.0 <= proj_hr < 0.38):
                    r, g, b = 0.85, 0.79, 0.62
            if rr < 0.06:
                r, g, b = 0.62, 0.52, 0.34
            idx = (y * size + x) * 4
            px[idx], px[idx + 1], px[idx + 2], px[idx + 3] = r, g, b, 1.0
    img.pixels = px
    img.pack()

    m = bpy.data.materials.new(name=name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.22
    bsdf.inputs["Metallic"].default_value = 0.15
    return m


def add_organic_tube_limb(bm: bmesh.types.BMesh, rings: list[tuple[Vector, Vector, Vector, float, float]], cap_start=True, cap_end=True):
    """Build a clean quad-ring loft along curved anatomical cross-sections.
    Each ring is (center, u_axis, v_axis, rx, rz) with 8-sided rounded-octagon profile
    that subdivides into a smooth anatomical surface under Catmull-Clark subsurf.
    """
    n_sides = 8
    ring_verts = []
    for center, u_ax, v_ax, rx, rz in rings:
        verts = []
        for k in range(n_sides):
            a = (k + 0.5) * (2.0 * math.pi / n_sides)
            p = center + u_ax * (math.cos(a) * rx) + v_ax * (math.sin(a) * rz)
            verts.append(bm.verts.new(p))
        ring_verts.append(verts)

    for i in range(len(ring_verts) - 1):
        r0 = ring_verts[i]
        r1 = ring_verts[i + 1]
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            bm.faces.new((r0[k], r0[kn], r1[kn], r1[k]))

    if cap_start and ring_verts:
        # Smooth quad-fan end cap (2 quads + 2 tris -> clean subsurf dome)
        c0 = rings[0][0]
        r0 = ring_verts[0]
        # Inset slightly for smooth dome
        nrm = (rings[0][0] - rings[1][0]).normalized() if len(rings) > 1 else Vector((0, -1, 0))
        tip = bm.verts.new(c0 + nrm * (min(rings[0][3], rings[0][4]) * 0.45))
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            bm.faces.new((r0[kn], r0[k], tip))
    if cap_end and ring_verts:
        cn = rings[-1][0]
        rn = ring_verts[-1]
        nrm = (rings[-1][0] - rings[-2][0]).normalized() if len(rings) > 1 else Vector((0, 1, 0))
        tip = bm.verts.new(cn + nrm * (min(rings[-1][3], rings[-1][4]) * 0.55))
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            bm.faces.new((rn[k], rn[kn], tip))
    return ring_verts


def build_sculpted_hand_mesh(mat_skin):
    """Build a continuous organic right hand + wrist + curled fingers & thumb, voxel-remeshed
    smoothly so palm, knuckles, web spaces, fingers, thumb, and wrist flow as ONE seamless
    anatomical mesh with zero robotic ball-joint seams!"""
    bm = bmesh.new()

    # Coordinate frame: grip axis passes through origin (0, 0, 0) in Blender coords (+Y_bl forward, +Z_bl up, +X_bl right).
    # Forearm enters from (+0.075, -0.155, -0.042) toward palm at (+0.028, -0.018, -0.004).

    # 1. Wrist -> Palm loft (elliptical cross-section widening smoothly into the hand heel, palm & knuckle arch)
    palm_spine = [
        # (center, rx_thickness, rz_height, roll_rad, yaw_rad)
        (Vector((0.074, -0.155, -0.040)), 0.025, 0.031, -0.15, -0.34),  # mid-forearm inside cuff
        (Vector((0.056, -0.112, -0.026)), 0.022, 0.028, -0.14, -0.30),  # wrist narrowest
        (Vector((0.042, -0.072, -0.014)), 0.021, 0.032, -0.10, -0.22),  # heel of palm
        (Vector((0.032, -0.036, -0.006)), 0.020, 0.040, -0.06, -0.10),  # mid palm (with thenar bulge)
        (Vector((0.024,  0.002, -0.002)), 0.017, 0.044, -0.02,  0.05),  # knuckle ridge (MCP joints)
        (Vector((0.016,  0.022, -0.003)), 0.013, 0.040,  0.02,  0.18),  # knuckle front bevel
    ]
    palm_rings = []
    for pos, rx, rz, roll, yaw in palm_spine:
        rot = Euler((0.08, roll, yaw), 'XYZ').to_matrix()
        u = rot @ Vector((1, 0, 0))
        v = rot @ Vector((0, 0, 1))
        palm_rings.append((pos, u, v, rx, rz))
    add_organic_tube_limb(bm, palm_rings, cap_start=True, cap_end=True)

    # 2. Thenar muscle pad (fleshy base of the thumb on inner-top palm)
    thenar_rings = [
        (Vector((0.028, -0.052, 0.008)), Vector((1, 0, 0)), Vector((0, 0, 1)), 0.016, 0.016),
        (Vector((0.016, -0.022, 0.022)), Vector((1, 0, 0)), Vector((0, 0, 1)), 0.017, 0.015),
        (Vector((0.006,  0.004, 0.030)), Vector((1, 0, 0)), Vector((0, 0, 1)), 0.013, 0.012),
    ]
    add_organic_tube_limb(bm, thenar_rings, cap_start=True, cap_end=True)

    # 3. Four organically curved fingers wrapping around the grip axis (smooth 5-ring curves!)
    # (z_offset, length_scale, base_radius, curl_extra)
    fingers = [
        ( 0.030, 0.98, 0.0094, 0.00),  # Index
        ( 0.010, 1.05, 0.0098, 0.04),  # Middle
        (-0.010, 0.98, 0.0092, 0.08),  # Ring
        (-0.029, 0.80, 0.0080, 0.12),  # Pinky
    ]
    for z0, lscale, rad, curl in fingers:
        # Smooth anatomical arc: knuckle -> proximal -> PIP -> middle -> DIP -> fingertip
        pts = [
            (Vector(( 0.022,  0.005, z0)),               rad * 1.12),  # blends deep into knuckle ridge
            (Vector(( 0.018,  0.028 * lscale, z0)),      rad * 1.00),  # proximal mid
            (Vector(( 0.007,  0.046 * lscale, z0 - 0.002)), rad * 0.92),  # PIP bend
            (Vector((-0.010,  0.048 * lscale, z0 - 0.004 - curl * 0.01)), rad * 0.84),  # middle phalanx
            (Vector((-0.023,  0.036 * lscale, z0 - 0.006 - curl * 0.015)), rad * 0.74), # DIP bend
            (Vector((-0.025,  0.020 * lscale, z0 - 0.007 - curl * 0.02)), rad * 0.62),  # fingertip pad
        ]
        rings = []
        for idx, (pt, r) in enumerate(pts):
            if idx == 0:
                tang = (pts[1][0] - pt).normalized()
            elif idx == len(pts) - 1:
                tang = (pt - pts[idx - 1][0]).normalized()
            else:
                tang = (pts[idx + 1][0] - pts[idx - 1][0]).normalized()
            up_ref = Vector((0, 0, 1))
            side = tang.cross(up_ref)
            if side.length < 1e-4:
                side = Vector((1, 0, 0))
            side.normalize()
            up = side.cross(tang).normalized()
            rings.append((pt, side, up, r * 0.94, r * 1.04))
        add_organic_tube_limb(bm, rings, cap_start=True, cap_end=True)

    # 4. Opposable Thumb arching naturally over the top-front of the grip
    thumb_pts = [
        (Vector(( 0.022, -0.022, 0.020)), 0.0145),
        (Vector(( 0.012, -0.002, 0.032)), 0.0125),
        (Vector(( 0.000,  0.018, 0.039)), 0.0110),
        (Vector((-0.014,  0.032, 0.038)), 0.0098),
        (Vector((-0.026,  0.034, 0.032)), 0.0082),
    ]
    t_rings = []
    for idx, (pt, r) in enumerate(thumb_pts):
        tang = (thumb_pts[min(idx + 1, len(thumb_pts) - 1)][0] - thumb_pts[max(idx - 1, 0)][0]).normalized()
        side = tang.cross(Vector((0, 0, 1))).normalized()
        up = side.cross(tang).normalized()
        t_rings.append((pt, side, up, r, r * 0.92))
    add_organic_tube_limb(bm, t_rings, cap_start=True, cap_end=True)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("RightHand")
    bm.to_mesh(me)
    bm.free()

    obj = bpy.data.objects.new("RightHand", me)
    obj.data.materials.append(mat_skin)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    # Voxel Remesh fuses palm, thenar pad, fingers, thumb, and wrist into ONE continuous anatomical skin!
    remesh = obj.modifiers.new(name="Remesh", type='REMESH')
    remesh.mode = 'VOXEL'
    remesh.voxel_size = 0.0034
    remesh.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=remesh.name)

    # Gentle Corrective/Laplacian smoothing softens knuckle creases & web transitions organically
    smooth = obj.modifiers.new(name="Smooth", type='SMOOTH')
    smooth.factor = 0.55
    smooth.iterations = 5
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    # Decimate slightly to keep a crisp ~3,500-tri game-ready foreground hand (silky smooth silhouettes, fast draw)
    dec = obj.modifiers.new(name="Decimate", type='DECIMATE')
    dec.ratio = 0.35
    bpy.ops.object.modifier_apply(modifier=dec.name)

    for p in obj.data.polygons:
        p.use_smooth = True
    obj.select_set(False)
    return obj


def add_textured_sleeve_tube(name, rings_spec, mat, uv_scale=(2.0, 2.0), subsurf=1):
    """Create a sculpted fabric cuff or wool jacket forearm with clean cylindrical UVs."""
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    n_sides = 24
    ring_verts = []
    for ri, (pos, rx, rz, yaw, pitch, fold_amp) in enumerate(rings_spec):
        rot = Euler((pitch, -0.14, yaw), 'XYZ').to_matrix()
        u = rot @ Vector((1, 0, 0))
        v = rot @ Vector((0, 0, 1))
        verts = []
        for k in range(n_sides):
            ang = k * (2.0 * math.pi / n_sides)
            # Subtle organic fabric drape & crease folds
            w = 1.0 + fold_amp * (0.6 * math.sin(ang * 3.0 + ri * 0.7) + 0.4 * math.cos(ang * 5.0 - ri * 0.5))
            p = pos + u * (math.cos(ang) * rx * w) + v * (math.sin(ang) * rz * w)
            verts.append(bm.verts.new(p))
        ring_verts.append(verts)

    n_rings = len(ring_verts)
    for i in range(n_rings - 1):
        r0 = ring_verts[i]
        r1 = ring_verts[i + 1]
        v0 = (i / max(1, n_rings - 1)) * uv_scale[1]
        v1 = ((i + 1) / max(1, n_rings - 1)) * uv_scale[1]
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            u0 = (k / n_sides) * uv_scale[0]
            u1 = ((k + 1) / n_sides) * uv_scale[0]
            f = bm.faces.new((r0[k], r0[kn], r1[kn], r1[k]))
            uvs = ((u0, v0), (u1, v0), (u1, v1), (u0, v1))
            for loop, uv in zip(f.loops, uvs):
                loop[uv_layer].uv = uv

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()

    obj = bpy.data.objects.new(name, me)
    obj.data.materials.append(mat)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    if subsurf > 0:
        mod = obj.modifiers.new(name="Subsurf", type='SUBSURF')
        mod.levels = subsurf
        mod.render_levels = subsurf
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.select_set(False)
    return obj


def build_wristwatch(mat_leather, mat_brass, mat_dial):
    """Sleek vintage field wristwatch hugging the wrist cleanly (flat brushed brass bezel + matte dial)."""
    # Wrist centre at watch spot: (0.058, -0.116, -0.027), axis along forearm dir
    wrist_center = Vector((0.058, -0.116, -0.027))
    arm_rot = Euler((0.08, -0.14, -0.31), 'XYZ').to_matrix()

    # Leather strap hugging elliptical wrist cross-section
    bm = bmesh.new()
    n_sides = 24
    r_inner_x, r_inner_z = 0.0235, 0.0295
    r_outer_x, r_outer_z = 0.0275, 0.0332
    half_w = 0.0085
    rings = []
    for sy in (-half_w, half_w):
        for rx, rz in ((r_inner_x, r_inner_z), (r_outer_x, r_outer_z)):
            ring = []
            for k in range(n_sides):
                a = k * (2.0 * math.pi / n_sides)
                local = Vector((math.cos(a) * rx, sy, math.sin(a) * rz))
                ring.append(bm.verts.new(wrist_center + arm_rot @ local))
            rings.append(ring)
    # Connect outer band, front edge, back edge, inner band
    pairs = [(1, 3), (3, 2), (2, 0), (0, 1)]
    for ia, ib in pairs:
        ra, rb = rings[ia], rings[ib]
        for k in range(n_sides):
            kn = (k + 1) % n_sides
            bm.faces.new((ra[k], ra[kn], rb[kn], rb[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("WatchStrap")
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    strap = bpy.data.objects.new("WatchStrap", me)
    strap.data.materials.append(mat_leather)
    bpy.context.collection.objects.link(strap)

    # Low-profile brushed brass case resting flush on top-outer surface of the wrist
    case_pos = wrist_center + arm_rot @ Vector((0.004, 0.0, 0.0305))
    case_rot = arm_rot @ Euler((0.0, 0.14, 0.0), 'XYZ').to_matrix()

    bm_c = bmesh.new()
    bmesh.ops.create_cone(bm_c, cap_ends=True, cap_tris=False, segments=24, radius1=0.0155, radius2=0.0148, depth=0.0068)
    sharp = [e for e in bm_c.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > 0.6]
    if sharp:
        bmesh.ops.bevel(bm_c, geom=sharp, offset=0.0014, segments=2, affect='EDGES', clamp_overlap=True)
    bmesh.ops.triangulate(bm_c, faces=bm_c.faces[:])
    me_c = bpy.data.meshes.new("WatchCase")
    bm_c.to_mesh(me_c)
    bm_c.free()
    for p in me_c.polygons:
        p.use_smooth = True
    wcase = bpy.data.objects.new("WatchCase", me_c)
    wcase.location = case_pos
    wcase.rotation_euler = case_rot.to_euler('XYZ')
    wcase.data.materials.append(mat_brass)
    bpy.context.collection.objects.link(wcase)

    # Dial disc slightly recessed on top face of watch case, with planar UVs
    bm_d = bmesh.new()
    uv_layer = bm_d.loops.layers.uv.new("UVMap")
    r_dial = 0.0132
    center_v = bm_d.verts.new(Vector((0, 0, 0.0038)))
    perim = []
    for k in range(24):
        a = k * (2.0 * math.pi / 24)
        perim.append(bm_d.verts.new(Vector((math.cos(a) * r_dial, math.sin(a) * r_dial, 0.0038))))
    for k in range(24):
        kn = (k + 1) % 24
        f = bm_d.faces.new((center_v, perim[k], perim[kn]))
        for loop in f.loops:
            v = loop.vert.co
            loop[uv_layer].uv = (0.5 + v.x / (2.0 * r_dial), 0.5 + v.y / (2.0 * r_dial))
    me_d = bpy.data.meshes.new("WatchDial")
    bm_d.to_mesh(me_d)
    bm_d.free()
    wdial = bpy.data.objects.new("WatchDial", me_d)
    wdial.location = case_pos
    wdial.rotation_euler = case_rot.to_euler('XYZ')
    wdial.data.materials.append(mat_dial)
    bpy.context.collection.objects.link(wdial)


def build():
    reset()
    # Natural warm weathered skin (sRGB -> linear so it never blows out pale ivory under lantern light),
    # woven autumn flannel cuff, dark camp wool jacket
    def s2l(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    m_skin = make_mat("HandSkin", (s2l(0.62), s2l(0.45), s2l(0.35), 1.0), roughness=0.78, metallic=0.0)
    m_flannel = make_plaid_flannel_mat("FlannelCuff")
    m_jacket = make_mat("CampJacket", (s2l(0.24), s2l(0.22), s2l(0.19), 1.0), roughness=0.94, metallic=0.0)
    m_leather = make_mat("WatchStrap", (s2l(0.22), s2l(0.14), s2l(0.09), 1.0), roughness=0.68, metallic=0.0)
    m_brass = make_mat("WatchCase", (s2l(0.62), s2l(0.52), s2l(0.32), 1.0), roughness=0.36, metallic=0.80)
    m_dial = make_watch_dial_mat("WatchDial")

    # 1. Unified voxel-fused sculpted right hand + fingers + thumb + wrist
    build_sculpted_hand_mesh(m_skin)

    # 2. Sleek flat vintage wristwatch hugging the wrist
    build_wristwatch(m_leather, m_brass, m_dial)

    # 3. Rolled plaid flannel shirt cuff emerging naturally from under the camp coat
    cuff_rings = [
        (Vector((0.064, -0.134, -0.032)), 0.025, 0.030, -0.32, 0.08, 0.02),  # tucked inner lip
        (Vector((0.066, -0.136, -0.033)), 0.035, 0.039, -0.32, 0.08, 0.04),  # rolled cloth lip
        (Vector((0.076, -0.162, -0.041)), 0.038, 0.042, -0.33, 0.08, 0.05),  # mid cuff
        (Vector((0.088, -0.192, -0.050)), 0.036, 0.040, -0.34, 0.08, 0.03),  # tucked under jacket
    ]
    add_textured_sleeve_tube("FlannelCuff", cuff_rings, m_flannel, uv_scale=(2.4, 0.9), subsurf=1)

    # 4. Dark weathered wool camp jacket forearm extending past lower-right camera frame edge
    jacket_rings = [
        (Vector((0.082, -0.178, -0.046)), 0.037, 0.041, -0.34, 0.08, 0.02),  # inner cuff hem
        (Vector((0.086, -0.184, -0.048)), 0.046, 0.050, -0.34, 0.08, 0.05),  # thick coat cuff edge
        (Vector((0.114, -0.252, -0.070)), 0.051, 0.055, -0.35, 0.08, 0.05),  # mid forearm
        (Vector((0.148, -0.334, -0.096)), 0.056, 0.060, -0.36, 0.08, 0.04),  # upper forearm
        (Vector((0.186, -0.430, -0.126)), 0.062, 0.066, -0.36, 0.08, 0.03),  # past edge of view
    ]
    add_textured_sleeve_tube("JacketForearm", jacket_rings, m_jacket, uv_scale=(2.0, 2.0), subsurf=1)

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(
        filepath=str(OUT_PATH),
        export_format='GLB',
        use_selection=True,
        export_materials='EXPORT',
        export_texcoords=True,
        export_normals=True,
        export_tangents=True,
        export_yup=True,
    )
    print(f"Exported {OUT_PATH.name} ({OUT_PATH.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    build()
