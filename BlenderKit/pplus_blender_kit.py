# P+ Blender Kit: a new body on a P+ costume (keeping the costume's own head and hair, the way Smash 4 Zero Suit
# Samus keeps Brawl's), or new parts on a costume, rigged the way the costume itself is.
#
# The easy way: the P+ Blender Bridge's "Open it in Blender" opens Blender with the costume loaded and this panel open.
# Every step is a button on the "P+ Kit" tab on the right of the 3D view, in order, with what to do next.

bl_info = {
    "name": "P+ Blender Kit",
    "author": "Last Zodiac",
    "version": (0, 3, 0),
    "blender": (4, 0, 0),
    "location": "3D View > Sidebar (N) > P+ Kit",
    "description": "New bodies and parts for Project+ costumes, rigged like P+ (with the P+ Blender Bridge)",
    "category": "Import-Export",
}

import os
import sys
import math
import subprocess
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix
from bpy_extras.io_utils import ImportHelper

SOURCE = "PPlusSource (rigging reference)"
MODEL = "Your model"
BACKUP = "Your model (before fitting)"
ORIGINAL = "Your model (as it came in)"
HANDLES = "Shape handles"
SMOOTH_REPEAT = 10
PICTURE_SIZE = 1024
MAX_TRIANGLES = 4000   # a Brawl costume's whole body is about 2,000-4,000; more just eats the fighter's memory
FACE = "PPlusFace_"
PART = "PPlus_"
DOTS = "Joint dots"

# the joints you line up: (dot name, P+ bone, its mirror)
JOINTS = [
    ("Pelvis", "HipN", None), ("Neck", "NeckN", None),
    ("Left shoulder", "LShoulderJ", "Right shoulder"), ("Right shoulder", "RShoulderJ", "Left shoulder"),
    ("Left elbow", "LArmJ", "Right elbow"), ("Right elbow", "RArmJ", "Left elbow"),
    ("Left wrist", "LHandN", "Right wrist"), ("Right wrist", "RHandN", "Left wrist"),
    ("Left hip", "LLegJ", "Right hip"), ("Right hip", "RLegJ", "Left hip"),
    ("Left knee", "LKneeJ", "Right knee"), ("Right knee", "RKneeJ", "Left knee"),
    ("Left ankle", "LFootJ", "Right ankle"), ("Right ankle", "RFootJ", "Left ankle"),
    ("Left toes", "LToeN", "Right toes"), ("Right toes", "RToeN", "Left toes"),
]
# ----- finding things -----

def pplus_armature():
    for o in bpy.data.objects:
        if o.type == 'ARMATURE' and o.get("pplus_glb"):
            return o
    return None


def pplus_meshes(include_faces=True):
    out = []
    for o in bpy.data.objects:
        if o.type != 'MESH' or o.name.startswith("PPlusSource"):
            continue
        if o.name.startswith(PART) or (include_faces and o.name.startswith(FACE)):
            out.append(o)
    return out


def head_bones(arm):
    b = arm.data.bones.get("HeadN")
    if b is None:
        return set()
    return {b.name} | {c.name for c in b.children_recursive}


def bone_point(arm, name):
    b = arm.data.bones.get(name)
    return None if b is None else arm.matrix_world @ b.head_local


def world_box(objs):
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return lo, hi


def select_only(objs, active=None):
    bpy.context.view_layer.update()
    for o in list(bpy.context.view_layer.objects):
        if o is not None:
            o.select_set(False)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or (objs[0] if objs else None)


def apply_modifier(obj, name):
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        bpy.ops.object.modifier_apply(modifier=name)


def ensure_object_mode():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


def dots_collection(create=False):
    c = bpy.data.collections.get(DOTS)
    if c is None and create:
        c = bpy.data.collections.new(DOTS)
        bpy.context.scene.collection.children.link(c)
    return c


def dot(name):
    return bpy.data.objects.get("Dot: " + name)


def show_pplus(show):
    for o in pplus_meshes():
        o.hide_set(not show)


def open_folder(path):
    try:
        if sys.platform.startswith("win"):
            os.startfile(path)
        elif sys.platform == "darwin":
            subprocess.Popen(["open", path])
        else:
            subprocess.Popen(["xdg-open", path])
    except Exception:
        pass


# ----- rigging: weights from the costume's own points -----

def make_source(arm):
    """One hidden copy of every P+ shape in the default pose: where new points get their bones from."""
    old = bpy.data.objects.get(SOURCE)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    copies = []
    for o in pplus_meshes():
        c = o.copy(); c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        for m in list(c.modifiers):
            c.modifiers.remove(m)
        c.parent = None
        c.matrix_world = o.matrix_world.copy()
        copies.append(c)
    if not copies:
        return None
    select_only(copies, copies[0])
    if len(copies) > 1:
        bpy.ops.object.join()
    src = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    src.name = SOURCE
    src.hide_render = True
    src.hide_set(True)
    return src


def smooth_weights(o, repeat, factor=0.5):
    """Each point's bones blend a little with its neighbours' (along the surface), a few times over."""
    n = len(o.data.vertices); G = len(o.vertex_groups)
    if n == 0 or G == 0 or repeat <= 0:
        return
    W = np.zeros((n, G), dtype=np.float32)
    for v in o.data.vertices:
        for g in v.groups:
            W[v.index, g.group] = g.weight
    E = np.empty(len(o.data.edges) * 2, dtype=np.int64); o.data.edges.foreach_get("vertices", E); E = E.reshape(-1, 2)
    if len(E) == 0:
        return
    deg = np.bincount(E.ravel(), minlength=n).astype(np.float32)
    has = deg > 0; deg[~has] = 1
    for _ in range(repeat):
        S = np.zeros_like(W)
        np.add.at(S, E[:, 0], W[E[:, 1]]); np.add.at(S, E[:, 1], W[E[:, 0]])
        W = np.where(has[:, None], (1 - factor) * W + factor * S / deg[:, None], W)
    for gi, g in enumerate(o.vertex_groups):
        col = W[:, gi]
        idx = np.nonzero(col > 1e-3)[0]
        zero = np.nonzero(col <= 1e-3)[0]
        if len(zero):
            g.remove([int(i) for i in zero])
        r = np.round(col[idx], 3)
        for wv in np.unique(r):
            g.add([int(i) for i in idx[r == wv]], float(wv), 'REPLACE')


def bone_side(name):
    """'L' or 'R' for a P+ bone on one side of the body (LLegJ, RHandN, SkirtFLN2...), None for the middle ones."""
    if len(name) > 1 and name[0] in "LR" and (name[1].isupper() or name[1].isdigit()):
        return name[0]
    if name.startswith("Skirt") and len(name) > 7 and name[6] in "LR" and name[7] == "N":
        return name[6]
    return None


def mirror_bone(name):
    """The same bone on the other side (LLegJ -> RLegJ, SkirtFLN2 -> SkirtFRN2)."""
    side = bone_side(name)
    if side is None:
        return None
    other = 'R' if side == 'L' else 'L'
    if name.startswith("Skirt"):
        return name[:6] + other + name[7:]
    return other + name[1:]


def keep_sides(o, arm):
    """A point on the left of the body never follows a right-side bone (and the other way round): feet or knees that
    nearly touch would otherwise pick up the other leg's bones and tear when the legs move apart."""
    mid = bone_point(arm, "HipN") or Vector((0, 0, 0))
    lo, hi = world_box([o]); eps = 0.006 * max(hi.z - lo.z, 1e-6)
    side_of = {g.index: bone_side(g.name) for g in o.vertex_groups}
    mw = o.matrix_world
    xs = np.array([(mw @ v.co).x - mid.x for v in o.data.vertices])
    # a separate piece off to one side (a shoe, a glove) is all on that side, even its points near the middle
    bm = bmesh.new(); bm.from_mesh(o.data)
    pcs = islands(bm); bm.free()
    biggest = max(range(len(pcs)), key=lambda i: len(pcs[i])) if pcs else -1
    for i, comp in enumerate(pcs):
        if i == biggest:
            continue
        m = float(xs[comp].mean())
        if abs(m) > eps:
            xs[comp] = np.sign(m) * np.maximum(np.abs(xs[comp]), 2 * eps)
    for v in o.data.vertices:
        x = xs[v.index]
        wrong = 'R' if x > eps else ('L' if x < -eps else None)
        if wrong is None:
            continue
        bad = [(g.group, g.weight) for g in v.groups if side_of.get(g.group) == wrong]
        if not bad:
            continue
        if len(bad) == len(v.groups):
            # only the other side's bones: the same bones on this side instead
            for gi, w in bad:
                twin = mirror_bone(o.vertex_groups[gi].name)
                if twin:
                    g = o.vertex_groups.get(twin) or o.vertex_groups.new(name=twin)
                    side_of[g.index] = bone_side(twin)
                    g.add([v.index], w, 'ADD')
        for gi, _ in bad:
            o.vertex_groups[gi].remove([v.index])


def rig_like_pplus(objs, arm):
    """Each point of these shapes follows the bones the costume's nearest points follow (smoothly blended)."""
    src = bpy.data.objects.get(SOURCE) or make_source(arm)
    if src is None:
        raise RuntimeError("The P+ costume's shapes weren't found. Open the P+ costume first.")
    src.hide_set(False)
    for o in objs:
        for m in list(o.modifiers):
            if m.type == 'ARMATURE':
                o.modifiers.remove(m)
        o.vertex_groups.clear()
        for g in src.vertex_groups:
            o.vertex_groups.new(name=g.name)
        mod = o.modifiers.new("PPlus weights", 'DATA_TRANSFER')
        mod.object = src
        mod.use_object_transform = True
        mod.use_vert_data = True
        mod.data_types_verts = {'VGROUP_WEIGHTS'}
        mod.vert_mapping = 'POLYINTERP_NEAREST'
        mod.layers_vgroup_select_src = 'ALL'
        mod.layers_vgroup_select_dst = 'NAME'
        with bpy.context.temp_override(object=o, active_object=o):
            bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
        apply_modifier(o, mod.name)
        select_only([o], o)
        # smoothed over your model's own surface: where two of the costume's pieces meet (a top and a sleeve), the bones
        # blend instead of jumping, so nothing tears when the arm moves
        smooth_weights(o, SMOOTH_REPEAT)
        keep_sides(o, arm)
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
        bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
        used = set()
        for v in o.data.vertices:
            for g in v.groups:
                if g.weight > 0.001:
                    used.add(g.group)
        for g in [g for g in o.vertex_groups if g.index not in used]:
            o.vertex_groups.remove(g)
        mw = o.matrix_world.copy()
        o.parent = arm
        o.matrix_world = mw
        a = o.modifiers.new("P+ skeleton", 'ARMATURE')
        a.object = arm
    src.hide_set(True)


# ----- fitting your model to P+'s proportions -----

# the body's parts: each is moved (and stretched) so its joints land on P+'s. Points blend between the parts near them.
#   segment: between two joints, turned and stretched along its length
#   end:     past a joint (hands, the head), following the part before it without stretching
#   region:  three joints (hips, chest) moved as one piece, so both sides stay together (no pinching between the legs)
# The last item is the shape handle that moves the part afterwards.
PARTS = [
    ("Hips", "region", ("Pelvis", "Left hip", "Right hip"), "Hips"),
    ("Body", "segment", ("Pelvis", "Neck"), "Body"),
    ("Chest", "region", ("Neck", "Left shoulder", "Right shoulder"), "Body"),
    ("Head", "end", ("Neck", "Pelvis", "Neck"), "Head"),
    ("Upper arm.L", "segment", ("Left shoulder", "Left elbow"), "Upper arm.L"),
    ("Forearm.L", "segment", ("Left elbow", "Left wrist"), "Forearm.L"),
    ("Hand.L", "end", ("Left wrist", "Left elbow", "Left wrist"), "Hand.L"),
    ("Upper arm.R", "segment", ("Right shoulder", "Right elbow"), "Upper arm.R"),
    ("Forearm.R", "segment", ("Right elbow", "Right wrist"), "Forearm.R"),
    ("Hand.R", "end", ("Right wrist", "Right elbow", "Right wrist"), "Hand.R"),
    ("Thigh.L", "segment", ("Left hip", "Left knee"), "Thigh.L"),
    ("Shin.L", "segment", ("Left knee", "Left ankle"), "Shin.L"),
    ("Foot.L", "segment", ("Left ankle", "Left toes"), "Foot.L"),
    ("Thigh.R", "segment", ("Right hip", "Right knee"), "Thigh.R"),
    ("Shin.R", "segment", ("Right knee", "Right ankle"), "Shin.R"),
    ("Foot.R", "segment", ("Right ankle", "Right toes"), "Foot.R"),
]


def part_side(joints):
    names = " ".join(joints)
    return 'L' if "Left" in names else ('R' if "Right" in names else None)


def part_shape(kind, joints, pos):
    """Where a part is, from joint positions (name -> Vector): a list of points (2 for a line, 3 for a triangle)."""
    if any(j not in pos for j in joints):
        return None
    if kind == "segment":
        return [pos[joints[0]], pos[joints[1]]]
    if kind == "region":
        return [pos[j] for j in joints]
    a = pos[joints[0]]; d = pos[joints[2]] - pos[joints[1]]
    return [a, a + (d.normalized() if d.length > 1e-6 else Vector((0, 0, 1))) * d.length * 0.6]


def dist_to_line(co, a, b):
    ab = b - a; L2 = max(float(ab @ ab), 1e-12)
    t = np.clip((co - a) @ ab / L2, 0, 1)
    return np.linalg.norm(co - (a + np.outer(t, ab)), axis=1)


def dist_to_triangle(co, a, b, c):
    n = np.cross(b - a, c - a); nl = np.linalg.norm(n)
    edges = np.minimum(np.minimum(dist_to_line(co, a, b), dist_to_line(co, b, c)), dist_to_line(co, c, a))
    if nl < 1e-9:
        return edges
    n = n / nl
    h = (co - a) @ n
    p = co - np.outer(h, n)
    # inside the triangle (seen along its normal)?
    def side(u, v):
        return np.cross(v - u, p - u) @ n
    inside = (side(a, b) >= 0) & (side(b, c) >= 0) & (side(c, a) >= 0)
    return np.where(inside, np.abs(h), edges)


def part_weights(co, pos, pelvis_x, height, power=4.0):
    """How much each point follows each part (N x parts): nearer parts more, smoothly; never a part on the other side
    of the body. Parts that can't be placed (missing joints) get nothing."""
    shapes = [part_shape(kind, joints, pos) for _, kind, joints, _ in PARTS]
    D = np.full((len(co), len(PARTS)), np.inf)
    for i, s in enumerate(shapes):
        if s is None:
            continue
        s = [np.array(p) for p in s]
        D[:, i] = dist_to_line(co, s[0], s[1]) if len(s) == 2 else dist_to_triangle(co, s[0], s[1], s[2])
    eps = 0.015 * height
    x = co[:, 0] - pelvis_x
    for i, (_, _, joints, _) in enumerate(PARTS):
        sd = part_side(joints)
        if sd == 'L':
            D[x < -eps, i] = np.inf
        elif sd == 'R':
            D[x > eps, i] = np.inf
    scale = max(height * 0.01, 1e-6)
    W = np.where(np.isfinite(D), 1.0 / ((D / scale) ** power + 1e-6), 0.0)
    s = W.sum(axis=1, keepdims=True); s[s == 0] = 1
    return W / s


def part_maps(src, dst):
    """For each part, a function moving points from where it is on your model (src joints) to P+'s (dst joints)."""
    def seg_map(a, b, A, B, stretch=True):
        u = b - a; U = B - A
        lu, lU = u.length, U.length
        if lu < 1e-6 or lU < 1e-6:
            d = np.array(A - a)
            return lambda P: P + d
        R = np.array(u.normalized().rotation_difference(U.normalized()).to_matrix())
        k = lU / lu if stretch else 1.0
        an, An, un = np.array(a), np.array(A), np.array(u.normalized())
        def f(P):
            local = P - an
            along = local @ un
            perp = local - np.outer(along, un)
            return An + (perp + np.outer(along * k, un)) @ R.T
        return f

    def region_map(a, A):
        # the triangle's two sides and its (scaled) normal, mapped onto P+'s: one straight-line transform for the piece
        def frame(p):
            e1, e2 = p[1] - p[0], p[2] - p[0]
            n = e1.cross(e2)
            if n.length < 1e-9:
                return None
            n = n.normalized() * math.sqrt(n.length)
            return np.array([list(e1), list(e2), list(n)]).T
        F, G = frame(a), frame(A)
        if F is None or G is None:
            d = np.array(A[0] - a[0])
            return lambda P: P + d
        M = G @ np.linalg.inv(F)
        a0, A0 = np.array(a[0]), np.array(A[0])
        return lambda P: A0 + (P - a0) @ M.T

    maps = []
    for _, kind, joints, _ in PARTS:
        s, d = part_shape(kind, joints, src), part_shape(kind, joints, dst)
        if s is None or d is None:
            maps.append(None)
        elif kind == "region":
            maps.append(region_map(s, d))
        else:
            maps.append(seg_map(s[0], s[1], d[0], d[1], stretch=(kind == "segment")))
    return maps


def segment_points():
    """Each joint: where it is on your model (its dot) and where P+ has it."""
    arm = pplus_armature()
    src, dst = {}, {}
    for name, bone, _ in JOINTS:
        d = dot(name); p = bone_point(arm, bone)
        if d is not None and p is not None:
            src[name] = Vector(d.matrix_world.translation); dst[name] = Vector(p)
    return src, dst


def fit_points(co, src, dst, height):
    """Moves the points (N x 3, world) so every joint dot lands on P+'s joint."""
    if "Pelvis" not in src:
        return co
    W = part_weights(co, src, src["Pelvis"].x, height)
    maps = part_maps(src, dst)
    out = np.zeros_like(co); used = np.zeros(len(co))
    for i, f in enumerate(maps):
        if f is None:
            continue
        w = W[:, i]
        if not w.any():
            continue
        out += w[:, None] * f(co); used += w
    keep = used < 1e-9
    out[keep] = co[keep]
    return out


# ----- the steps -----

class PPLUS_OT_open_costume(bpy.types.Operator, ImportHelper):
    """Open the costume the P+ Blender Bridge sent (its "... - for Blender.glb" file)"""
    bl_idname = "pplus.open_costume"
    bl_label = "Open a P+ costume"
    filename_ext = ".glb"
    filter_glob: bpy.props.StringProperty(default="*.glb", options={'HIDDEN'})

    def execute(self, context):
        ensure_object_mode()
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=self.filepath)
        new = [o for o in bpy.data.objects if o not in before]
        arm = next((o for o in new if o.type == 'ARMATURE'), None)
        if arm is None:
            self.report({'ERROR'}, "That file has no skeleton: open the one the P+ Blender Bridge made.")
            return {'CANCELLED'}
        for o in bpy.data.objects:
            if o.type == 'ARMATURE' and o != arm and o.get("pplus_glb"):
                del o["pplus_glb"]
        arm["pplus_glb"] = self.filepath
        arm.name = "P+ skeleton"
        arm.show_in_front = False
        for o in new:
            if o.type == 'MESH' and not (o.name.startswith(PART) or o.name.startswith(FACE)) and o.parent is None:
                o.hide_set(True); o.hide_render = True
        make_source(arm)
        select_only([arm], arm)
        frame_all(context)
        self.report({'INFO'}, "P+ costume open.")
        return {'FINISHED'}


def frame_all(context):
    try:
        for area in context.screen.areas:
            if area.type == 'VIEW_3D':
                region = next(r for r in area.regions if r.type == 'WINDOW')
                with context.temp_override(area=area, region=region):
                    bpy.ops.view3d.view_all()
    except Exception:
        pass


class PPLUS_OT_pictures(bpy.types.Operator):
    """Save front, side and back pictures of the P+ costume, true to scale (no perspective), arms out: to make a new
    model with a 3D model maker (an AI one, or by hand) that has the costume's exact proportions"""
    bl_idname = "pplus.pictures"
    bl_label = "Save pictures for a 3D model maker"

    def execute(self, context):
        ensure_object_mode()
        arm = pplus_armature()
        if arm is None:
            self.report({'ERROR'}, "Open the P+ costume first.")
            return {'CANCELLED'}
        folder = os.path.join(os.path.dirname(arm["pplus_glb"]), "Pictures for a 3D model maker")
        os.makedirs(folder, exist_ok=True)
        shapes = pplus_meshes()
        lo, hi = world_box(shapes)
        size = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z) * 1.08
        mid = (lo + hi) / 2
        sc = context.scene
        # only the costume shows; everything is put back afterwards
        hidden = {o: o.hide_render for o in sc.objects}
        for o in sc.objects:
            o.hide_render = o not in shapes
        old = (sc.render.engine, sc.render.film_transparent, sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage, sc.camera, sc.render.filepath)
        sc.render.engine = 'BLENDER_WORKBENCH'
        sc.display.shading.light = 'FLAT'   # the pictures' own colours, no shading
        sc.display.shading.color_type = 'TEXTURE'
        sc.render.film_transparent = True
        sc.render.resolution_x = sc.render.resolution_y = 1024
        sc.render.resolution_percentage = 100
        cam_data = bpy.data.cameras.new("PPlus pictures"); cam_data.type = 'ORTHO'; cam_data.ortho_scale = size
        cam = bpy.data.objects.new("PPlus pictures", cam_data); sc.collection.objects.link(cam)
        sc.camera = cam
        far = size * 3
        base = os.path.splitext(os.path.basename(arm["pplus_glb"]))[0].replace(" - for Blender", "")
        views = [("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0))), ("back", Vector((0, 1, 0)))]
        saved = []
        try:
            for name, d in views:
                cam.location = mid + d * far
                cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
                cam_data.clip_end = far * 3
                path = os.path.join(folder, f"{base} - {name}.png")
                sc.render.filepath = path
                bpy.ops.render.render(write_still=True)
                saved.append(path)
        finally:
            (sc.render.engine, sc.render.film_transparent, sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage, sc.camera, sc.render.filepath) = old
            for o, h in hidden.items():
                o.hide_render = h
            bpy.data.objects.remove(cam, do_unlink=True)
            bpy.data.cameras.remove(cam_data)
        with open(os.path.join(folder, "How to use these.txt"), "w", encoding="utf-8") as f:
            f.write(f"{base}: the P+ costume from the front, the side and the back, with its arms out.\n"
                    "All three are the same scale and have no perspective, so its proportions are exact.\n\n"
                    "Give the front one (or all three) to a 3D model maker and ask for the same pose, arms out.\n"
                    "Then in Blender: P+ Kit > Open your model, and line up the joints.\n")
        open_folder(folder)
        self.report({'INFO'}, f"Saved {len(saved)} pictures in \"{os.path.basename(folder)}\" (next to the costume).")
        return {'FINISHED'}


class PPLUS_OT_open_model(bpy.types.Operator, ImportHelper):
    """Open the model whose body you want (GLB, glTF, FBX or OBJ). It's sized and placed like the costume"""
    bl_idname = "pplus.open_model"
    bl_label = "Open your model"
    bl_options = {'REGISTER', 'UNDO'}
    filter_glob: bpy.props.StringProperty(default="*.glb;*.gltf;*.fbx;*.obj", options={'HIDDEN'})

    def execute(self, context):
        ensure_object_mode()
        if pplus_armature() is None:
            self.report({'ERROR'}, "Open the P+ costume first.")
            return {'CANCELLED'}
        before = set(bpy.data.objects)
        ext = os.path.splitext(self.filepath)[1].lower()
        if ext in (".glb", ".gltf"):
            try:
                # pieces cut apart only by the picture's seams are joined back (so a hair piece is one piece)
                bpy.ops.import_scene.gltf(filepath=self.filepath, merge_vertices=True)
            except TypeError:
                bpy.ops.import_scene.gltf(filepath=self.filepath)
        elif ext == ".fbx":
            bpy.ops.import_scene.fbx(filepath=self.filepath)
        elif ext == ".obj":
            if hasattr(bpy.ops.wm, "obj_import"):
                bpy.ops.wm.obj_import(filepath=self.filepath)
            else:
                bpy.ops.import_scene.obj(filepath=self.filepath)
        else:
            self.report({'ERROR'}, "Pick a .glb, .gltf, .fbx or .obj file.")
            return {'CANCELLED'}
        new = [o for o in bpy.data.objects if o not in before]
        meshes = [o for o in new if o.type == 'MESH']
        if not meshes:
            self.report({'ERROR'}, "No shapes were found in that file.")
            return {'CANCELLED'}
        # one object in its current pose, without its own skeleton
        for o in meshes:
            mw = o.matrix_world.copy()
            if o.data.shape_keys:
                o.shape_key_clear()
            for m in list(o.modifiers):
                if m.type == 'ARMATURE':
                    apply_modifier(o, m.name)
            o.parent = None
            o.matrix_world = mw
            o.vertex_groups.clear()
        for o in new:
            if o.type != 'MESH':
                bpy.data.objects.remove(o, do_unlink=True)
        for n in (MODEL, BACKUP, ORIGINAL):
            old = bpy.data.objects.get(n)
            if old:
                bpy.data.objects.remove(old, do_unlink=True)
        remove_dots(); remove_handles()
        select_only(meshes, meshes[0])
        if len(meshes) > 1:
            bpy.ops.object.join()
        model = context.view_layer.objects.active
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if ext not in (".glb", ".gltf"):
            weld(model)
        model.name = MODEL
        line_up(model)
        keep_original(model)
        show_pplus(True)
        frame_all(context)
        self.report({'INFO'}, "Your model is in, sized and placed like the costume.")
        return {'FINISHED'}


def weld(model):
    bm = bmesh.new(); bm.from_mesh(model.data)
    lo, hi = world_box([model])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=max(hi.z - lo.z, 1e-6) * 1e-5)
    bm.to_mesh(model.data); bm.free()


def keep_original(model):
    """A hidden copy of your model as it is now, for "Put it all back"."""
    old = bpy.data.objects.get(ORIGINAL)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    c = model.copy(); c.data = model.data.copy(); c.name = ORIGINAL
    bpy.context.scene.collection.objects.link(c)
    c.hide_set(True); c.hide_render = True


def hand_height(objs):
    """Height of the hands of a model standing with its arms out (the points furthest out to the sides), above its
    feet; None if its arms aren't out."""
    pts = []
    for o in objs:
        mw = np.array(o.matrix_world)
        co = np.empty(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
        pts.append(co @ mw[:3, :3].T + mw[:3, 3])
    if not pts:
        return None
    P = np.concatenate(pts)
    lo, hi = P.min(axis=0), P.max(axis=0)
    width, tall = hi[0] - lo[0], hi[2] - lo[2]
    if tall <= 0 or width < 0.6 * tall:
        return None
    cx = (lo[0] + hi[0]) / 2
    outer = np.abs(P[:, 0] - cx) > 0.42 * width
    if outer.sum() < 4:
        return None
    return float(np.median(P[outer, 2]) - lo[2])


def line_up(model):
    """Your model gets the costume's size and place. With both standing arms out, the hands' height is matched (hair or
    a crown on top don't count); otherwise the whole height."""
    ref = [bpy.data.objects[SOURCE]] if bpy.data.objects.get(SOURCE) else pplus_meshes()
    lo, hi = world_box(ref)
    mlo, mhi = world_box([model])
    h, mh = hi.z - lo.z, mhi.z - mlo.z
    if mh <= 1e-6:
        return
    k = h / mh
    ph, mhh = hand_height(ref), hand_height([model])
    if ph and mhh and 0.5 < (ph / mhh) / k < 2.0:
        k = ph / mhh
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    mcentre = Vector(((mlo.x + mhi.x) / 2, (mlo.y + mhi.y) / 2, mlo.z))
    model.matrix_world = Matrix.Translation(centre) @ Matrix.Scale(k, 4) @ Matrix.Translation(-mcentre) @ model.matrix_world
    select_only([model], model)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


class PPLUS_OT_turn(bpy.types.Operator):
    """Turn your model a quarter turn (if it faces another way than the costume)"""
    bl_idname = "pplus.turn"
    bl_label = "Turn"
    bl_options = {'REGISTER', 'UNDO'}
    way: bpy.props.IntProperty(default=1)

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL)
        if model is None:
            return {'CANCELLED'}
        model.matrix_world = Matrix.Rotation(math.radians(90 * self.way), 4, 'Z') @ model.matrix_world
        line_up(model)
        keep_original(model)
        return {'FINISHED'}


class PPLUS_OT_resize(bpy.types.Operator):
    """Make your model a little bigger or smaller (it stays standing on the floor)"""
    bl_idname = "pplus.resize"
    bl_label = "Size"
    bl_options = {'REGISTER', 'UNDO'}
    factor: bpy.props.FloatProperty(default=1.02)

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL)
        if model is None:
            return {'CANCELLED'}
        lo, hi = world_box([model])
        foot = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
        model.matrix_world = Matrix.Translation(foot) @ Matrix.Scale(self.factor, 4) @ Matrix.Translation(-foot) @ model.matrix_world
        select_only([model], model)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if not model.get("pplus_fitted"):
            keep_original(model)
        return {'FINISHED'}


# ----- clean up: taking off pieces (hair, extra bits) -----

def islands(bm):
    """The separate pieces of a mesh: lists of vertex indices."""
    bm.verts.ensure_lookup_table()
    seen = set(); out = []
    for v in bm.verts:
        if v.index in seen:
            continue
        st = [v]; seen.add(v.index); comp = []
        while st:
            x = st.pop(); comp.append(x.index)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index); st.append(o)
        out.append(comp)
    return out


def body_piece(bm, pcs, mw, arm):
    """The piece that is the body: the biggest one reaching both the hips and the shoulders."""
    hip = bone_point(arm, "HipN"); neck = bone_point(arm, "NeckN")
    best, size = None, -1
    for i, comp in enumerate(pcs):
        zs = [(mw @ bm.verts[j].co).z for j in comp]
        if min(zs) <= hip.z <= max(zs) and max(zs) >= neck.z - 0.5 * (neck.z - hip.z) and len(comp) > size:
            best, size = i, len(comp)
    if best is None:
        best = max(range(len(pcs)), key=lambda i: len(pcs[i]))
    return best


def remove_loose_above_neck(model, arm):
    """Takes off the pieces that aren't part of the body and reach above the neck (hair, earrings, hats): with P+'s head
    they'd poke through it. Returns how many."""
    bm = bmesh.new(); bm.from_mesh(model.data); bm.verts.ensure_lookup_table()
    mw = model.matrix_world
    pcs = islands(bm)
    if len(pcs) < 2:
        bm.free(); return 0
    body = body_piece(bm, pcs, mw, arm)
    neck = bone_point(arm, "NeckN").z
    shoulders = (bone_point(arm, "LShoulderJ") or bone_point(arm, "NeckN")).z
    gone = []; n = 0
    for i, comp in enumerate(pcs):
        if i == body:
            continue
        zs = [(mw @ bm.verts[j].co).z for j in comp]
        if max(zs) > neck or (max(zs) > shoulders and sum(zs) / len(zs) > shoulders - 0.6 * (neck - shoulders)):
            gone += [bm.verts[j] for j in comp]; n += 1
    bmesh.ops.delete(bm, geom=gone, context='VERTS')
    bm.to_mesh(model.data); bm.free(); model.data.update()
    return n


class PPLUS_OT_remove_hair(bpy.types.Operator):
    """Take off your model's hair and other loose pieces on its head (earrings, hats), for P+'s head.
    Only pieces that aren't joined to the body. Undo with Ctrl+Z or "Put it all back\""""
    bl_idname = "pplus.remove_hair"
    bl_label = "Take off the hair (for P+'s head)"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL); arm = pplus_armature()
        if model is None or arm is None:
            return {'CANCELLED'}
        n = remove_loose_above_neck(model, arm)
        if n:
            self.report({'INFO'}, f"Took off {n} piece(s). Anything left? Use \"Pick pieces to take off\".")
        else:
            self.report({'WARNING'}, "No loose pieces on the head: the hair is joined to the body. Use \"Pick pieces to take off\" and box it.")
        return {'FINISHED'}


class PPLUS_OT_pick_pieces(bpy.types.Operator):
    """Click pieces of your model to take them off (Edit Mode, ready to click)"""
    bl_idname = "pplus.pick_pieces"
    bl_label = "Pick pieces to take off"
    bl_options = {'REGISTER', 'UNDO'}
    box: bpy.props.BoolProperty(default=False)

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL)
        if model is None:
            return {'CANCELLED'}
        show_pplus(False)
        select_only([model], model)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_mode(type='FACE')
        bpy.ops.mesh.select_all(action='DESELECT')
        try:
            for area in context.screen.areas:
                if area.type == 'VIEW_3D':
                    area.spaces.active.shading.show_xray = self.box   # a box reaches through to the back too
                    region = next(r for r in area.regions if r.type == 'WINDOW')
                    with context.temp_override(area=area, region=region):
                        bpy.ops.wm.tool_set_by_id(name="builtin.select_box" if self.box else "builtin.select")
        except Exception:
            pass
        return {'FINISHED'}


class PPLUS_OT_take_off(bpy.types.Operator):
    """Take off what's selected: the whole piece it's on, or just the selected part"""
    bl_idname = "pplus.take_off"
    bl_label = "Take off"
    bl_options = {'REGISTER', 'UNDO'}
    whole: bpy.props.BoolProperty(default=True)

    def execute(self, context):
        model = bpy.data.objects.get(MODEL)
        if model is None:
            return {'CANCELLED'}
        if model.mode != 'EDIT':
            self.report({'ERROR'}, "Press \"Pick pieces to take off\" first, then click a piece.")
            return {'CANCELLED'}
        bm = bmesh.from_edit_mesh(model.data)
        sel = [v for v in bm.verts if v.select]
        if not sel:
            self.report({'ERROR'}, "Nothing is selected: click a piece first (it turns orange).")
            return {'CANCELLED'}
        if self.whole:
            seen = set(v.index for v in sel); st = list(sel)
            while st:
                x = st.pop()
                for e in x.link_edges:
                    o = e.other_vert(x)
                    if o.index not in seen:
                        seen.add(o.index); st.append(o)
            gone = [v for v in bm.verts if v.index in seen]
        else:
            gone = sel
        if len(gone) > 0.8 * len(bm.verts):
            self.report({'ERROR'}, "That's almost the whole model (the piece is joined to the body). Use \"Box a part\" instead and take off just the selected part.")
            return {'CANCELLED'}
        bmesh.ops.delete(bm, geom=gone, context='VERTS')
        bmesh.update_edit_mesh(model.data)
        self.report({'INFO'}, f"Took off {len(gone)} points. Ctrl+Z puts them back.")
        return {'FINISHED'}


class PPLUS_OT_restore(bpy.types.Operator):
    """Put your model back as it came in (every piece back, no fitting)"""
    bl_idname = "pplus.restore"
    bl_label = "Put it all back"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL); orig = bpy.data.objects.get(ORIGINAL)
        if model is None or orig is None:
            return {'CANCELLED'}
        remove_handles(); remove_dots()
        model.data = orig.data.copy()
        for k in ("pplus_fitted", "pplus_clean"):
            if k in model:
                del model[k]
        b = bpy.data.objects.get(BACKUP)
        if b:
            bpy.data.objects.remove(b, do_unlink=True)
        show_pplus(True)
        return {'FINISHED'}


class PPLUS_OT_step(bpy.types.Operator):
    """Go to the next (or back to the last) step"""
    bl_idname = "pplus.step"
    bl_label = "Step"
    bl_options = {'REGISTER', 'UNDO'}
    clean: bpy.props.BoolProperty(default=True)

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL)
        if model is None:
            return {'CANCELLED'}
        try:
            for area in context.screen.areas:
                if area.type == 'VIEW_3D':
                    area.spaces.active.shading.show_xray = False
                    region = next(r for r in area.regions if r.type == 'WINDOW')
                    with context.temp_override(area=area, region=region):
                        bpy.ops.wm.tool_set_by_id(name="builtin.select_box")
        except Exception:
            pass
        if self.clean:
            model["pplus_clean"] = True
        elif "pplus_clean" in model:
            del model["pplus_clean"]; remove_dots(); show_pplus(True)
        return {'FINISHED'}


# ----- joint dots -----

def remove_dots():
    c = dots_collection()
    if c:
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(c)


class PPLUS_OT_place_dots(bpy.types.Operator):
    """Put a dot on every joint (shoulders, elbows, wrists, hips, knees, ankles...), where the costume has them.
    Then move each dot onto the same joint of your model"""
    bl_idname = "pplus.place_dots"
    bl_label = "Show the joint dots"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        arm = pplus_armature(); model = bpy.data.objects.get(MODEL)
        if arm is None or model is None:
            self.report({'ERROR'}, "Open the P+ costume and your model first.")
            return {'CANCELLED'}
        remove_dots()
        col = dots_collection(create=True)
        lo, hi = world_box([model])
        r = (hi.z - lo.z) * 0.018
        for name, bone, _ in JOINTS:
            p = bone_point(arm, bone)
            if p is None:
                continue
            e = bpy.data.objects.new("Dot: " + name, None)
            e.empty_display_type = 'SPHERE'
            e.empty_display_size = r
            e.show_in_front = True
            e.show_name = True
            e.location = p
            col.objects.link(e)
        show_pplus(False)
        select_only([dot("Left elbow") or model])
        set_view(context, 'FRONT')
        self.report({'INFO'}, "Click a dot, then move it onto the same joint of your model (arrows, or G). Front view, then side view.")
        return {'FINISHED'}


def set_view(context, which):
    try:
        for area in context.screen.areas:
            if area.type == 'VIEW_3D':
                region = next(r for r in area.regions if r.type == 'WINDOW')
                with context.temp_override(area=area, region=region):
                    bpy.ops.view3d.view_axis(type=which)
                    bpy.ops.view3d.view_all()
                area.spaces.active.region_3d.view_perspective = 'ORTHO'
    except Exception:
        pass


class PPLUS_OT_view(bpy.types.Operator):
    """Look at it straight from the front or the side (no perspective)"""
    bl_idname = "pplus.view"
    bl_label = "View"
    which: bpy.props.StringProperty(default='FRONT')

    def execute(self, context):
        set_view(context, self.which)
        return {'FINISHED'}


class PPLUS_OT_mirror_dots(bpy.types.Operator):
    """Copy the left side's dots to the right side (mirrored), so you only place one side"""
    bl_idname = "pplus.mirror_dots"
    bl_label = "Copy left dots to the right"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        arm = pplus_armature()
        mid = bone_point(arm, "HipN") or Vector((0, 0, 0))
        n = 0
        for name, _, mirror in JOINTS:
            if not name.startswith("Left") or mirror is None:
                continue
            a, b = dot(name), dot(mirror)
            if a and b:
                p = a.matrix_world.translation
                b.location = Vector((2 * mid.x - p.x, p.y, p.z)); n += 1
        self.report({'INFO'}, f"Copied {n} dots to the right side.")
        return {'FINISHED'}


class PPLUS_OT_fit(bpy.types.Operator):
    """Reshape your model so every dot lands on the costume's joint: arms, legs and body get P+'s lengths and
    places. You can move the dots and fit again as many times as you like"""
    bl_idname = "pplus.fit"
    bl_label = "Fit my model to P+"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL)
        if model is None or dots_collection() is None:
            self.report({'ERROR'}, "Open your model and show the joint dots first.")
            return {'CANCELLED'}
        # always from your model as it was before fitting, so fitting again starts fresh
        backup = bpy.data.objects.get(BACKUP)
        if backup is None:
            backup = model.copy(); backup.data = model.data.copy(); backup.name = BACKUP
            context.scene.collection.objects.link(backup)
        else:
            model.data = backup.data.copy()
        backup.hide_set(True); backup.hide_render = True
        src, dst = segment_points()
        me = model.data
        co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
        mw = np.array(model.matrix_world); co_w = co @ mw[:3, :3].T + mw[:3, 3]
        lo, hi = world_box([model])
        out = fit_points(co_w, src, dst, hi.z - lo.z)
        inv = np.linalg.inv(mw); out_l = out @ inv[:3, :3].T + inv[:3, 3]
        me.vertices.foreach_set("co", out_l.reshape(-1)); me.update()
        model["pplus_fitted"] = True
        dc = dots_collection()
        if dc:
            dc.hide_viewport = True
        show_pplus(False)
        select_only([model], model)
        self.report({'INFO'}, "Fitted. Not quite right somewhere? Use the shape handles, or \"Undo fit\" to move the dots again.")
        return {'FINISHED'}


class PPLUS_OT_unfit(bpy.types.Operator):
    """Back to your model before fitting, with the dots where you put them"""
    bl_idname = "pplus.unfit"
    bl_label = "Undo fit (move the dots again)"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        remove_handles()
        model = bpy.data.objects.get(MODEL); backup = bpy.data.objects.get(BACKUP)
        if model and backup:
            model.data = backup.data.copy()
            if "pplus_fitted" in model:
                del model["pplus_fitted"]
        dc = dots_collection()
        if dc:
            dc.hide_viewport = False
        return {'FINISHED'}


class PPLUS_OT_compare(bpy.types.Operator):
    """Show the P+ costume see-through over your model, to compare (click again to hide it)"""
    bl_idname = "pplus.compare"
    bl_label = "Compare with P+"

    def execute(self, context):
        shapes = pplus_meshes()
        show = any(o.hide_get() for o in shapes)
        for o in shapes:
            o.hide_set(not show)
            o.display_type = 'WIRE' if show else 'TEXTURED'
            o.show_in_front = show
        return {'FINISHED'}


# ----- shape handles: grab a part of your model and nudge, stretch or turn it -----

def bone_selected(pb):
    # Blender 5 keeps a pose bone's selection on the pose bone, Blender 4 on its bone
    return pb.select if hasattr(pb, "select") else pb.bone.select


def bone_select(pb, on):
    if hasattr(pb, "select"):
        pb.select = on
    else:
        pb.bone.select = on


def handles():
    return bpy.data.objects.get(HANDLES)


def remove_handles():
    h = handles()
    model = bpy.data.objects.get(MODEL)
    if model:
        for m in list(model.modifiers):
            if m.type == 'ARMATURE' and (m.object is None or m.object == h):
                model.modifiers.remove(m)
        names = {bone for _, _, _, bone in PARTS}
        for g in [g for g in model.vertex_groups if g.name in names]:
            model.vertex_groups.remove(g)
        if "pplus_handles" in model:
            del model["pplus_handles"]
    if h:
        data = h.data
        bpy.data.objects.remove(h, do_unlink=True)
        if data.users == 0:
            bpy.data.armatures.remove(data)


def joint_positions():
    """P+'s joints (where your fitted model's joints now are)."""
    arm = pplus_armature()
    return {name: Vector(bone_point(arm, bone)) for name, bone, _ in JOINTS if bone_point(arm, bone) is not None}


HANDLE_PARENT = {"Body": "Hips", "Head": "Body", "Upper arm.L": "Body", "Upper arm.R": "Body", "Forearm.L": "Upper arm.L",
                 "Forearm.R": "Upper arm.R", "Hand.L": "Forearm.L", "Hand.R": "Forearm.R", "Thigh.L": "Hips", "Thigh.R": "Hips",
                 "Shin.L": "Thigh.L", "Shin.R": "Thigh.R", "Foot.L": "Shin.L", "Foot.R": "Shin.R"}


def make_handles(context, model):
    """A handle on each part of your model (hips, body, head, arms, legs, feet). Your model follows them."""
    remove_handles()
    pos = joint_positions()
    if "Pelvis" not in pos:
        return None
    lo, hi = world_box([model]); height = hi.z - lo.z
    data = bpy.data.armatures.new(HANDLES)
    h = bpy.data.objects.new(HANDLES, data)
    context.scene.collection.objects.link(h)
    h.show_in_front = True
    data.display_type = 'OCTAHEDRAL'
    select_only([h], h)
    bpy.ops.object.mode_set(mode='EDIT')
    made = {}
    for name, kind, joints, bone in PARTS:
        if bone in made:
            continue
        s = part_shape(kind, joints, pos)
        if s is None:
            continue
        if kind == "region":   # hips: from the pelvis down to between the hips
            a = s[0]; b = (s[1] + s[2]) / 2
            if (b - a).length < height * 0.02:
                b = a - Vector((0, 0, height * 0.04))
        else:
            a, b = s[0], s[1]
        eb = data.edit_bones.new(bone)
        eb.head = a; eb.tail = b
        eb.inherit_scale = 'NONE'
        made[bone] = eb
    for name, eb in made.items():
        p = HANDLE_PARENT.get(name)
        if p in made:
            eb.parent = made[p]; eb.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    # your model follows the handles
    me = model.data
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    mw = np.array(model.matrix_world); co_w = co @ mw[:3, :3].T + mw[:3, 3]
    W = part_weights(co_w, pos, pos["Pelvis"].x, height)
    per_bone = {}
    for i, (_, _, _, bone) in enumerate(PARTS):
        if bone in made:
            per_bone[bone] = per_bone.get(bone, 0) + W[:, i]
    for bone, w in per_bone.items():
        g = model.vertex_groups.new(name=bone)
        idx = np.nonzero(w > 1e-3)[0]
        r = np.round(w[idx], 3)
        for wv in np.unique(r):
            g.add([int(j) for j in idx[r == wv]], float(wv), 'REPLACE')
    m = model.modifiers.new("Shape handles", 'ARMATURE')
    m.object = h
    m.use_vertex_groups = True
    model["pplus_handles"] = True
    return h


def apply_handles(model):
    """Your model keeps the shape the handles gave it; the handles go."""
    h = handles()
    if h is None or model is None:
        return
    for m in list(model.modifiers):
        if m.type == 'ARMATURE' and m.object == h:
            apply_modifier(model, m.name)
    bones = {b.name for b in h.data.bones}
    for g in [g for g in model.vertex_groups if g.name in bones]:
        model.vertex_groups.remove(g)
    data = h.data
    bpy.data.objects.remove(h, do_unlink=True)
    if data.users == 0:
        bpy.data.armatures.remove(data)
    if "pplus_handles" in model:
        del model["pplus_handles"]


class PPLUS_OT_handles(bpy.types.Operator):
    """Show the shape handles: click one (a part of your model), then use the arrow and stretch buttons"""
    bl_idname = "pplus.handles"
    bl_label = "Show the shape handles"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        model = bpy.data.objects.get(MODEL)
        if model is None or pplus_armature() is None:
            return {'CANCELLED'}
        h = handles() or make_handles(context, model)
        if h is None:
            self.report({'ERROR'}, "Show the joint dots and fit first.")
            return {'CANCELLED'}
        show_pplus(False)
        select_only([h], h)
        bpy.ops.object.mode_set(mode='POSE')
        for pb in h.pose.bones:
            bone_select(pb, False)
        first = h.pose.bones.get("Upper arm.L")
        if first:
            bone_select(first, True); h.data.bones.active = first.bone
        set_view(context, 'FRONT')
        self.report({'INFO'}, "Click a handle, then use the buttons (or G move, S stretch, R turn).")
        return {'FINISHED'}


class PPLUS_OT_handles_done(bpy.types.Operator):
    """Keep the shape the handles gave your model"""
    bl_idname = "pplus.handles_done"
    bl_label = "Keep this shape"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        apply_handles(bpy.data.objects.get(MODEL))
        return {'FINISHED'}


def mirror_name(name):
    if name.endswith(".L"):
        return name[:-2] + ".R"
    if name.endswith(".R"):
        return name[:-2] + ".L"
    if name.startswith("Dot: Left"):
        return "Dot: Right" + name[len("Dot: Left"):]
    if name.startswith("Dot: Right"):
        return "Dot: Left" + name[len("Dot: Right"):]
    return None


def picked(context):
    """What the arrow buttons move: the selected shape handles, or the selected joint dots."""
    h = handles()
    if h is not None and h.mode == 'POSE':
        return 'HANDLE', [pb for pb in h.pose.bones if bone_selected(pb)]
    dots = [o for o in context.selected_objects if o.name.startswith("Dot: ")]
    return 'DOT', dots


class PPLUS_OT_nudge(bpy.types.Operator):
    """Move, stretch, thicken or turn what's selected (a shape handle or a joint dot) a little"""
    bl_idname = "pplus.nudge"
    bl_label = "Nudge"
    bl_options = {'REGISTER', 'UNDO'}
    move: bpy.props.FloatVectorProperty(size=3, default=(0, 0, 0))
    stretch: bpy.props.FloatProperty(default=1.0)
    thick: bpy.props.FloatProperty(default=1.0)
    turn: bpy.props.FloatProperty(default=0.0)

    def execute(self, context):
        model = bpy.data.objects.get(MODEL)
        lo, hi = world_box([model]) if model else (Vector((0, 0, 0)), Vector((0, 0, 1)))
        step = (hi.z - lo.z) * 0.006
        both = getattr(context.scene, "pplus_both_sides", True)
        kind, items = picked(context)
        if not items:
            self.report({'ERROR'}, "Click a handle (or a joint dot) first.")
            return {'CANCELLED'}
        mv = Vector(self.move) * step
        if kind == 'DOT':
            done = set()
            for o in items:
                o.location += mv; done.add(o.name)
                m = mirror_name(o.name)
                if both and m and m not in done and bpy.data.objects.get(m) and bpy.data.objects[m] not in items:
                    bpy.data.objects[m].location += Vector((-mv.x, mv.y, mv.z)); done.add(m)
            return {'FINISHED'}
        h = handles()
        targets = []
        for pb in items:
            targets.append((pb, 1))
            m = mirror_name(pb.name)
            if both and m and h.pose.bones.get(m) and h.pose.bones[m] not in items:
                targets.append((h.pose.bones[m], -1))
        for pb, sign in targets:
            if self.stretch != 1.0 or self.thick != 1.0:
                pb.scale = (pb.scale[0] * self.thick, pb.scale[1] * self.stretch, pb.scale[2] * self.thick)
            if mv.length or self.turn:
                context.view_layer.update()
                M = pb.matrix.copy()
                d = Vector((mv.x * sign, mv.y, mv.z))
                if self.turn:
                    head = M.translation.copy()
                    R = Matrix.Rotation(math.radians(self.turn * sign), 4, Vector((0, -1, 0)))
                    M = Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ M
                M = Matrix.Translation(d) @ M
                pb.matrix = M
        context.view_layer.update()
        return {'FINISHED'}


class PPLUS_OT_reset_handle(bpy.types.Operator):
    """Put the selected handles (or all of them) back the way they were"""
    bl_idname = "pplus.reset_handle"
    bl_label = "Reset"
    bl_options = {'REGISTER', 'UNDO'}
    all: bpy.props.BoolProperty(default=False)

    def execute(self, context):
        h = handles()
        if h is None:
            return {'CANCELLED'}
        for pb in h.pose.bones:
            if self.all or bone_selected(pb):
                pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
        return {'FINISHED'}


# ----- lighter: models from 3D model makers have far more triangles than a Brawl costume -----

def lighten(model, most):
    """Fewer triangles (shape kept as close as possible), so the costume fits the fighter's memory with a sharper picture."""
    tris = sum(len(p.vertices) - 2 for p in model.data.polygons)
    if tris <= most:
        return tris
    m = model.modifiers.new("Lighter", 'DECIMATE')
    m.decimate_type = 'COLLAPSE'
    m.ratio = most / tris
    m.use_collapse_triangulate = True
    with bpy.context.temp_override(object=model, active_object=model):
        bpy.ops.object.modifier_move_to_index(modifier=m.name, index=0)
    apply_modifier(model, m.name)
    return sum(len(p.vertices) - 2 for p in model.data.polygons)


# ----- your model's picture, redone for the game -----

def bake_picture(model, size=PICTURE_SIZE):
    """Your model gets one new picture laid out just for what's left of it (models from 3D model makers come with a big
    picture cut into hundreds of scraps, much of it for the hair that was taken off): shrunk to fit the game's memory,
    a scrappy picture turns to noise, a tidy one stays clean. Returns False when there's no picture to redo."""
    me = model.data
    mats = [s.material for s in model.material_slots if s.material and s.material.use_nodes]
    if not me.uv_layers or not mats or not any(n.type == 'TEX_IMAGE' and n.image for m in mats for n in m.node_tree.nodes):
        return False
    old_uv = me.uv_layers.active.name
    for mat in mats:
        nt = mat.node_tree
        for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
            if not n.inputs['Vector'].is_linked:
                uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = old_uv
                nt.links.new(uvn.outputs['UV'], n.inputs['Vector'])
    new = me.uv_layers.new(name="P+ picture")
    me.uv_layers.active = new
    select_only([model], model)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')
    img = bpy.data.images.new(MODEL + " picture", size, size, alpha=False)
    for mat in mats:
        nt = mat.node_tree
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = img
        for n in nt.nodes:
            n.select = False
        t.select = True; nt.nodes.active = t
    sc = bpy.context.scene
    old_engine = sc.render.engine
    sc.render.engine = 'CYCLES'
    try:
        sc.cycles.samples = 1
        sc.cycles.device = 'CPU'
        sc.render.bake.margin = 8
        bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=8, use_clear=True)
    finally:
        sc.render.engine = old_engine
    img.pack()
    # one plain material showing the new picture on the new layout
    mat = bpy.data.materials.new(MODEL + " material"); mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
    nt.links.new(tn.outputs['Color'], bsdf.inputs['Base Color'])
    me.materials.clear(); me.materials.append(mat)
    me.uv_layers.remove(me.uv_layers[old_uv])
    me.uv_layers.active = me.uv_layers["P+ picture"]
    me.uv_layers["P+ picture"].active_render = True
    return True


# ----- finishing: P+'s head or yours, rigged like P+ -----

def finish_model(context, keep_pplus_head):
    arm = pplus_armature(); model = bpy.data.objects.get(MODEL)
    apply_handles(model)
    head = head_bones(arm)
    removed = 0
    if keep_pplus_head:
        # the costume: only the points that mostly follow the head stay (face/hair pieces stay whole)
        for o in pplus_meshes(include_faces=False):
            names = {g.index: g.name for g in o.vertex_groups}
            bm = bmesh.new(); bm.from_mesh(o.data)
            dl = bm.verts.layers.deform.active
            gone = []
            for v in bm.verts:
                total = h = 0.0
                if dl is not None:
                    for gi, w in v[dl].items():
                        total += w
                        if names.get(gi) in head:
                            h += w
                if total <= 0 or h / total < 0.5:
                    gone.append(v)
            bmesh.ops.delete(bm, geom=gone, context='VERTS')
            bm.to_mesh(o.data); bm.free()
            if len(o.data.vertices) == 0:
                bpy.data.objects.remove(o, do_unlink=True)
        # your model: its hair and loose head pieces, then its head (everything above the costume's neck, near the middle)
        removed += remove_loose_above_neck(model, arm)
        neck = bone_point(arm, "HeadN")
        lo, hi = world_box([model])
        tall = hi.z - lo.z
        cut = neck.z - 0.01 * tall
        reach = 0.22 * tall
        bm = bmesh.new(); bm.from_mesh(model.data)
        mw = model.matrix_world
        faces = [f for f in bm.faces if all((mw @ v.co).z > cut and math.hypot((mw @ v.co).x - neck.x, (mw @ v.co).y - neck.y) < reach for v in f.verts)]
        removed += len(faces)
        bmesh.ops.delete(bm, geom=faces, context='FACES_ONLY')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        bm.to_mesh(model.data); bm.free()
    else:
        # your model's own head: the costume's body and head go, except the hidden items/faces the Bridge keeps
        for o in pplus_meshes():
            o.hide_set(True)
            o.hide_render = True
    lighten(model, MAX_TRIANGLES)
    rig_like_pplus([model], arm)
    try:
        bake_picture(model)
    except Exception as e:
        print("P+ Kit: the picture wasn't redone:", e)
    remove_dots()
    for n in (BACKUP,):
        b = bpy.data.objects.get(n)
        if b:
            bpy.data.objects.remove(b, do_unlink=True)
    if keep_pplus_head:
        show_pplus(True)
    for o in pplus_meshes():
        o.display_type = 'TEXTURED'; o.show_in_front = False
    model["pplus_done"] = True
    model["pplus_own_head"] = not keep_pplus_head
    return removed


class PPLUS_OT_finish(bpy.types.Operator):
    """Put your body under the costume's head (or keep your model's own head), and rig it like the costume"""
    bl_idname = "pplus.finish"
    bl_label = "Finish"
    bl_options = {'REGISTER', 'UNDO'}
    keep_pplus_head: bpy.props.BoolProperty(default=True)

    def execute(self, context):
        ensure_object_mode()
        if pplus_armature() is None or bpy.data.objects.get(MODEL) is None:
            self.report({'ERROR'}, "Open the P+ costume and your model first.")
            return {'CANCELLED'}
        if self.keep_pplus_head and not head_bones(pplus_armature()):
            self.report({'ERROR'}, "This skeleton has no HeadN bone: use \"Keep my model's head\".")
            return {'CANCELLED'}
        finish_model(context, self.keep_pplus_head)
        select_only([bpy.data.objects[MODEL]], bpy.data.objects[MODEL])
        self.report({'INFO'}, "Done. Play the test move to see it bend, then save.")
        return {'FINISHED'}


class PPLUS_OT_rig_selected(bpy.types.Operator):
    """Rig the selected shapes like P+: each point follows the bones the costume's nearest points follow"""
    bl_idname = "pplus.rig_selected"
    bl_label = "Rig selected like P+"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        arm = pplus_armature()
        objs = [o for o in context.selected_objects if o.type == 'MESH' and not o.name.startswith("PPlusSource")]
        if arm is None or not objs:
            self.report({'ERROR'}, "Open the P+ costume first, then select the new shapes to rig.")
            return {'CANCELLED'}
        rig_like_pplus(objs, arm)
        self.report({'INFO'}, f"Rigged {len(objs)} shape(s) like P+.")
        return {'FINISHED'}


# ----- the test move: a short loop (arms down and back up, a step, a knee bend), like a pose from the game -----

TEST_MOVE = [   # bone, axis (in the world: X across, Y front-to-back, Z up), degrees at the middle of the move
    ("LShoulderJ", 'Y', 50), ("RShoulderJ", 'Y', -50), ("LArmJ", 'Z', -40), ("RArmJ", 'Z', 40),
    ("LLegJ", 'X', -30), ("RLegJ", 'X', 20), ("LKneeJ", 'X', 45), ("RKneeJ", 'X', 25),
    ("WaistN", 'Z', 12), ("HeadN", 'Z', 15),
]
MOVE_ACTION = "P+ test move"


def stop_test_move(context):
    arm = pplus_armature()
    if arm is None:
        return
    try:
        if context.screen and context.screen.is_animation_playing:
            bpy.ops.screen.animation_cancel(restore_frame=True)
    except Exception:
        pass
    if arm.animation_data:
        act = arm.animation_data.action
        arm.animation_data.action = None
        if act and act.name.startswith(MOVE_ACTION):
            bpy.data.actions.remove(act)
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
    context.scene.frame_set(1)


class PPLUS_OT_try_pose(bpy.types.Operator):
    """Play a short test move (arms, a step, a knee bend) to see your model bend with P+'s skeleton. Click again to stop"""
    bl_idname = "pplus.try_pose"
    bl_label = "Play a test move"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        ensure_object_mode()
        arm = pplus_armature()
        if arm is None:
            return {'CANCELLED'}
        if arm.animation_data and arm.animation_data.action:
            stop_test_move(context)
            return {'FINISHED'}
        stop_test_move(context)
        arm.animation_data_create()
        act = bpy.data.actions.new(MOVE_ACTION)
        arm.animation_data.action = act
        sc = context.scene
        sc.frame_start, sc.frame_end = 1, 60
        for frame, amount in ((1, 0.0), (30, 1.0), (60, 0.0)):
            for name, axis, deg in TEST_MOVE:
                pb = arm.pose.bones.get(name)
                if pb is None:
                    continue
                pb.rotation_mode = 'QUATERNION'
                rest = pb.bone.matrix_local.to_3x3()
                world_axis = Vector({'X': (1, 0, 0), 'Y': (0, 1, 0), 'Z': (0, 0, 1)}[axis])
                local_axis = (rest.inverted() @ world_axis).normalized()
                pb.rotation_quaternion = Matrix.Rotation(math.radians(deg * amount), 3, local_axis).to_quaternion()
                pb.keyframe_insert("rotation_quaternion", frame=frame)
        sc.frame_set(1)
        try:
            bpy.ops.screen.animation_play()
        except Exception:
            sc.frame_set(30)
        return {'FINISHED'}


class PPLUS_OT_rest_pose(bpy.types.Operator):
    """Stop the test move: back to the costume's default pose"""
    bl_idname = "pplus.rest_pose"
    bl_label = "Stop"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        stop_test_move(context)
        return {'FINISHED'}


class PPLUS_OT_save(bpy.types.Operator):
    """Save it and make the costume file (the P+ Blender Bridge opens and shows before and after)"""
    bl_idname = "pplus.save"
    bl_label = "Save and make the costume file"

    def execute(self, context):
        ensure_object_mode()
        arm = pplus_armature()
        if arm is None:
            self.report({'ERROR'}, "Open the P+ costume first.")
            return {'CANCELLED'}
        model = bpy.data.objects.get(MODEL)
        if model is not None and not model.get("pplus_done"):
            self.report({'ERROR'}, "Your model isn't finished yet: in step 5, press \"Use P+'s head\" or \"Keep my model's head\" first (that rigs it to P+'s bones). Then save.")
            return {'CANCELLED'}
        stop_test_move(context)
        src = arm["pplus_glb"]
        folder = os.path.dirname(src)
        base = os.path.splitext(os.path.basename(src))[0].replace(" - for Blender", "")
        path = os.path.join(folder, base + " - from Blender.glb")
        shapes = [o for o in bpy.data.objects if o.type == 'MESH' and not o.name.startswith("PPlusSource") and o.visible_get()
                  and any(m.type == 'ARMATURE' and m.object == arm for m in o.modifiers)]
        unrigged = [o.name for o in bpy.data.objects if o.type == 'MESH' and o.visible_get() and not o.name.startswith("PPlusSource")
                    and o not in shapes and o.parent is None]
        select_only([arm] + shapes, arm)
        bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_skins=True,
                                  export_animations=False, export_morph=False, export_apply=False)
        msg = f"Saved {os.path.basename(path)}."
        bridge = context.scene.get("pplus_bridge")
        if bridge and os.path.exists(bridge):
            try:
                own = bool(model and model.get("pplus_own_head"))
                subprocess.Popen([bridge, "--bring", path] + (["--own-head"] if own else []))
                msg += " The Bridge is making the costume file now."
            except Exception as e:
                msg += f" (The Bridge didn't open: {e}. Use its \"Choose what you saved from Blender\".)"
        else:
            msg += " Now use the Bridge's \"Choose what you saved from Blender\" and pick it."
        if unrigged:
            msg += f" Not saved (not rigged): {', '.join(unrigged)}."
        self.report({'INFO'}, msg)
        return {'FINISHED'}


# ----- the panel: one step at a time -----

def wrap(layout, text, width=38):
    words = text.split(); line = ""
    col = layout.column(align=True); col.scale_y = 0.8
    for w in words:
        if len(line) + len(w) + 1 > width:
            col.label(text=line); line = w
        else:
            line = (line + " " + w).strip()
    if line:
        col.label(text=line)


STEPS = ["Open the costume", "Your model", "Clean it up", "Line up the joints", "Shape it and finish", "Check and save"]


def current_step():
    arm = pplus_armature(); model = bpy.data.objects.get(MODEL)
    if arm is None:
        return 1
    if model is None:
        return 2
    if model.get("pplus_done"):
        return 6
    if model.get("pplus_fitted"):
        return 5
    if model.get("pplus_clean"):
        return 4
    return 3


def arrows(l, context, what):
    """The buttons that move what's selected (a joint dot or a shape handle), as seen from the front."""
    box = l.box()
    box.label(text=f"Move the selected {what}:")
    g = box.grid_flow(columns=3, align=True)
    g.label(text="")
    g.operator("pplus.nudge", text="Up", icon='TRIA_UP').move = (0, 0, 1)
    g.label(text="")
    g.operator("pplus.nudge", text="Left", icon='TRIA_LEFT').move = (-1, 0, 0)
    g.label(text="")
    g.operator("pplus.nudge", text="Right", icon='TRIA_RIGHT').move = (1, 0, 0)
    g.label(text="")
    g.operator("pplus.nudge", text="Down", icon='TRIA_DOWN').move = (0, 0, -1)
    g.label(text="")
    r = box.row(align=True)
    r.operator("pplus.nudge", text="Toward front").move = (0, -1, 0)
    r.operator("pplus.nudge", text="Toward back").move = (0, 1, 0)
    box.prop(context.scene, "pplus_both_sides", text="Both sides at once (left and right)")


class PPLUS_PT_panel(bpy.types.Panel):
    bl_label = "P+ Kit"
    bl_idname = "PPLUS_PT_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "P+ Kit"

    def draw(self, context):
        l = self.layout
        step = current_step()
        arm = pplus_armature()
        box = l.box(); col = box.column(align=True)
        for i, n in enumerate(STEPS, 1):
            col.label(text=f"{i}. {n}", icon='CHECKMARK' if i < step else ('RIGHTARROW' if i == step else 'DOT'))
        l.separator()

        if step == 1:
            l.label(text="Step 1: open the costume", icon='ARMATURE_DATA')
            wrap(l, "The P+ Blender Bridge's \"Open it in Blender\" does this for you. Or open the \"... - for Blender.glb\" it made:")
            r = l.row(); r.scale_y = 1.6; r.operator("pplus.open_costume", icon='FILE_FOLDER')
            return

        l.label(text="Costume: " + os.path.basename(arm["pplus_glb"]).replace(" - for Blender.glb", ""), icon='ARMATURE_DATA')
        model = bpy.data.objects.get(MODEL)

        if step == 2:
            l.label(text="Step 2: your model", icon='IMPORT')
            wrap(l, "Open the model whose body you want. It gets the costume's size and place.")
            r = l.row(); r.scale_y = 1.6; r.operator("pplus.open_model", icon='FILE_FOLDER')
            l.separator()
            wrap(l, "No model yet? Save pictures of the costume (front, side, back, exact proportions) to give to a 3D model maker:")
            l.operator("pplus.pictures", icon='RENDER_STILL')
            l.separator()
            wrap(l, "Only changing or adding parts (like Striker Peach)? Edit the costume's shapes, select the new ones, then:")
            l.operator("pplus.rig_selected", icon='GROUP_BONE')
            r = l.row(); r.scale_y = 1.3; r.operator("pplus.save", icon='EXPORT')
            return

        if step == 3:
            l.label(text="Step 3: clean it up", icon='BRUSH_DATA')
            editing = model is not None and model.mode == 'EDIT'
            if not editing:
                wrap(l, "Facing the same way and the same size as the costume? If not:")
                r = l.row(align=True)
                r.operator("pplus.turn", text="Turn left", icon='LOOP_BACK').way = 1
                r.operator("pplus.turn", text="Turn right", icon='LOOP_FORWARDS').way = -1
                r = l.row(align=True)
                r.operator("pplus.resize", text="Smaller", icon='REMOVE').factor = 0.98
                r.operator("pplus.resize", text="Bigger", icon='ADD').factor = 1.02
                l.operator("pplus.compare", icon='HIDE_OFF')
                l.separator()
                wrap(l, "Using P+'s head? Take your model's hair off (any part of it that's a separate piece):")
                l.operator("pplus.remove_hair", icon='USER')
                wrap(l, "Anything else to take off (leftover hair, bits)?")
                r = l.row(align=True)
                r.operator("pplus.pick_pieces", text="Click pieces", icon='RESTRICT_SELECT_OFF').box = False
                r.operator("pplus.pick_pieces", text="Box a part", icon='SELECT_SET').box = True
                l.operator("pplus.restore", icon='LOOP_BACK')
                l.separator()
                r = l.row(); r.scale_y = 1.6; r.operator("pplus.step", text="Done: next step", icon='FORWARD').clean = True
            else:
                wrap(l, "Click a piece (it turns orange; Shift+click adds more), or drag a box around a part. Drag the middle mouse button to look around.")
                r = l.row(); r.scale_y = 1.4; r.operator("pplus.take_off", text="Take off the whole piece", icon='TRASH').whole = True
                l.operator("pplus.take_off", text="Take off just what's selected").whole = False
                wrap(l, "Ctrl+Z puts back what you took off.")
                r = l.row(); r.scale_y = 1.3; r.operator("pplus.step", text="Done taking off pieces", icon='CHECKMARK').clean = False
            return

        if step == 4:
            l.label(text="Step 4: line up the joints", icon='BONE_DATA')
            dots = dots_collection()
            if dots is None or dots.hide_viewport:
                wrap(l, "Dots go on the joints (shoulders, elbows, wrists, hips, knees, ankles, toes):")
                r = l.row(); r.scale_y = 1.6; r.operator("pplus.place_dots", icon='SPHERE')
                wrap(l, "(Or skip the fitting if it already matches:)")
                r = l.row(align=True)
                r.operator("pplus.finish", text="Skip: P+'s head").keep_pplus_head = True
                r.operator("pplus.finish", text="Skip: my head").keep_pplus_head = False
            else:
                wrap(l, "Click a dot, then move it onto the same joint of your model with the arrows (or G and the mouse). Check from the side too.")
                r = l.row(align=True)
                r.operator("pplus.view", text="Front view").which = 'FRONT'
                r.operator("pplus.view", text="Side view").which = 'RIGHT'
                arrows(l, context, "dot")
                r = l.row(); r.scale_y = 1.6; r.operator("pplus.fit", icon='CHECKMARK')
            l.operator("pplus.step", text="Back to cleaning up", icon='BACK').clean = False
            return

        if step == 5:
            l.label(text="Step 5: shape it and finish", icon='MOD_ARMATURE')
            r = l.row(align=True)
            r.operator("pplus.compare", icon='HIDE_OFF')
            r.operator("pplus.unfit", text="Undo fit", icon='LOOP_BACK')
            h = handles()
            if h is None or h.mode != 'POSE':
                wrap(l, "Something a bit off (arms a little short, a foot pointing out)? Grab that part with a handle:")
                l.operator("pplus.handles", icon='BONE_DATA')
            else:
                wrap(l, "Click a handle (it turns blue), then:")
                arrows(l, context, "handle")
                r = l.row(align=True)
                r.operator("pplus.nudge", text="Longer").stretch = 1.03
                r.operator("pplus.nudge", text="Shorter").stretch = 1 / 1.03
                r = l.row(align=True)
                r.operator("pplus.nudge", text="Thicker").thick = 1.03
                r.operator("pplus.nudge", text="Thinner").thick = 1 / 1.03
                r = l.row(align=True)
                r.operator("pplus.nudge", text="Turn", icon='LOOP_BACK').turn = 3
                r.operator("pplus.nudge", text="Turn", icon='LOOP_FORWARDS').turn = -3
                r = l.row(align=True)
                r.operator("pplus.reset_handle", text="Reset this one").all = False
                r.operator("pplus.reset_handle", text="Reset all").all = True
                l.operator("pplus.handles_done", icon='CHECKMARK')
            l.separator()
            wrap(l, "Happy with it? Use the costume's head and hair (recommended: P+'s face, blinking and hair movement):")
            r = l.row(); r.scale_y = 1.6
            r.operator("pplus.finish", text="Use P+'s head", icon='USER').keep_pplus_head = True
            l.operator("pplus.finish", text="Keep my model's head").keep_pplus_head = False
            return

        l.label(text="Step 6: check and save", icon='EXPORT')
        playing = arm.animation_data is not None and arm.animation_data.action is not None
        r = l.row(); r.scale_y = 1.3
        if playing:
            r.operator("pplus.rest_pose", text="Stop the test move", icon='PAUSE')
        else:
            r.operator("pplus.try_pose", icon='PLAY')
        wrap(l, "Something wrong? Ctrl+Z steps back (as many times as you need).")
        r = l.row(); r.scale_y = 1.6; r.operator("pplus.save", icon='EXPORT')


CLASSES = (PPLUS_OT_open_costume, PPLUS_OT_pictures, PPLUS_OT_open_model, PPLUS_OT_turn, PPLUS_OT_resize, PPLUS_OT_remove_hair,
           PPLUS_OT_pick_pieces, PPLUS_OT_take_off, PPLUS_OT_restore, PPLUS_OT_step, PPLUS_OT_place_dots, PPLUS_OT_view,
           PPLUS_OT_mirror_dots, PPLUS_OT_fit, PPLUS_OT_unfit, PPLUS_OT_compare, PPLUS_OT_handles, PPLUS_OT_handles_done,
           PPLUS_OT_nudge, PPLUS_OT_reset_handle, PPLUS_OT_finish, PPLUS_OT_rig_selected, PPLUS_OT_try_pose,
           PPLUS_OT_rest_pose, PPLUS_OT_save, PPLUS_PT_panel)


def register():
    for c in CLASSES:
        # an older copy (installed as an add-on) gives way to this one
        old = getattr(bpy.types, c.__name__, None)
        if old is not None:
            try:
                bpy.utils.unregister_class(old)
            except Exception:
                pass
        bpy.utils.register_class(c)
    bpy.types.Scene.pplus_both_sides = bpy.props.BoolProperty(name="Both sides", default=True)


def unregister():
    for c in reversed(CLASSES):
        try:
            bpy.utils.unregister_class(c)
        except Exception:
            pass
    try:
        del bpy.types.Scene.pplus_both_sides
    except Exception:
        pass


if __name__ == "__main__":
    register()
