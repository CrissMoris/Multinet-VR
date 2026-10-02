"""Geometry helpers for the MultiTravel Valiz Challenge asset builder (Blender 5.x, background mode).

Every helper creates one Blender object carrying a custom property ``mt_mat`` (a key of
Assets/MultiTravel/Art/materials.json). ``export_asset`` evaluates all modifiers, merges the parts
into one mesh with one material slot per key, writes metre-based box-projected UVs and exports FBX.
Units: metres. Blender axes: X = right, Y = forward (away from viewer), Z = up.
"""
import json
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

PALETTE = {}


def load_palette(path):
    global PALETTE
    with open(path, encoding="utf-8") as f:
        PALETTE = json.load(f)["materials"]


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.textures, bpy.data.curves, bpy.data.images):
        for item in list(block):
            block.remove(item)
    random.seed(1234)


def _link(name, mesh, mat):
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    if mat not in PALETTE:
        raise KeyError(f"Unknown material key '{mat}'")
    obj["mt_mat"] = mat
    return obj


def from_bmesh(name, bm, mat, loc=(0, 0, 0), rot=(0, 0, 0)):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = _link(name, mesh, mat)
    obj.location = loc
    obj.rotation_euler = rot
    return obj


def add_bevel(obj, width, segments=3, angle=None):
    mod = obj.modifiers.new("bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(angle if angle is not None else 30)
    mod.miter_outer = "MITER_ARC"
    return obj


def add_subsurf(obj, levels=1):
    mod = obj.modifiers.new("subsurf", "SUBSURF")
    mod.levels = levels
    mod.render_levels = levels
    return obj


def box(name, size, loc=(0, 0, 0), rot=(0, 0, 0), mat="plastic_grey", bevel=0.0, segments=3):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    obj = from_bmesh(name, bm, mat, loc, rot)
    if bevel > 0:
        add_bevel(obj, min(bevel, min(size) * 0.49), segments)
    return obj


def cylinder(name, radius, depth, loc=(0, 0, 0), rot=(0, 0, 0), mat="plastic_grey", segments=24, radius2=None, bevel=0.0, bevel_segments=2):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=depth)
    obj = from_bmesh(name, bm, mat, loc, rot)
    if bevel > 0:
        add_bevel(obj, bevel, bevel_segments, angle=50)
    return obj


def sphere(name, radius, loc=(0, 0, 0), scale=(1, 1, 1), mat="plastic_grey", u=24, v=12):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=radius)
    for vert in bm.verts:
        vert.co = Vector((vert.co.x * scale[0], vert.co.y * scale[1], vert.co.z * scale[2]))
    return from_bmesh(name, bm, mat, loc)


def _frames(points, closed):
    """Parallel-transport frames along a polyline: returns list of (tangent, normal, binormal)."""
    n = len(points)
    tangents = []
    for i in range(n):
        if closed:
            t = points[(i + 1) % n] - points[i - 1]
        else:
            t = points[min(i + 1, n - 1)] - points[max(i - 1, 0)]
        tangents.append(t.normalized())
    ref = Vector((0, 0, 1)) if abs(tangents[0].z) < 0.9 else Vector((1, 0, 0))
    normal = tangents[0].cross(ref).normalized()
    frames = []
    for t in tangents:
        normal = (normal - t * normal.dot(t)).normalized()
        frames.append((t, normal, t.cross(normal).normalized()))
    return frames


def sweep(name, points, radius, mat, closed=False, segments=12, scale=(1, 1), cap=True, radius_fn=None):
    """Tube along ``points`` (list of 3-tuples). ``scale`` squashes the cross-section (normal, binormal)."""
    pts = [Vector(p) for p in points]
    frames = _frames(pts, closed)
    bm = bmesh.new()
    rings = []
    for i, (p, (t, nrm, bin_)) in enumerate(zip(pts, frames)):
        r = radius * (radius_fn(i / max(1, len(pts) - 1)) if radius_fn else 1.0)
        ring = []
        for s in range(segments):
            a = 2 * math.pi * s / segments
            offset = nrm * (math.cos(a) * r * scale[0]) + bin_ * (math.sin(a) * r * scale[1])
            ring.append(bm.verts.new(p + offset))
        rings.append(ring)
    count = len(rings) if closed else len(rings) - 1
    for i in range(count):
        a, b = rings[i], rings[(i + 1) % len(rings)]
        for s in range(segments):
            bm.faces.new((a[s], a[(s + 1) % segments], b[(s + 1) % segments], b[s]))
    if cap and not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return from_bmesh(name, bm, mat)


def arc_points(center, radius, start_deg, end_deg, steps, plane="xz"):
    pts = []
    for i in range(steps + 1):
        a = math.radians(start_deg + (end_deg - start_deg) * i / steps)
        c, s = math.cos(a) * radius, math.sin(a) * radius
        if plane == "xz":
            pts.append((center[0] + c, center[1], center[2] + s))
        elif plane == "xy":
            pts.append((center[0] + c, center[1] + s, center[2]))
        else:
            pts.append((center[0], center[1] + c, center[2] + s))
    return pts


def torus(name, major, minor, loc=(0, 0, 0), rot=(0, 0, 0), mat="plastic_grey", start=0, end=360, steps=48, segments=12, scale=(1, 1)):
    closed = abs(end - start) >= 360
    pts = arc_points((0, 0, 0), major, start, end if not closed else end - 360 / steps, steps if not closed else steps - 1, "xy")
    obj = sweep(name, pts, minor, mat, closed=closed, segments=segments, scale=scale)
    obj.location = loc
    obj.rotation_euler = rot
    return obj


def rounded_rect(w, h, r, steps=6):
    """Counter-clockwise rounded rectangle outline centred at the origin (2D points)."""
    r = min(r, w / 2 - 1e-4, h / 2 - 1e-4)
    pts = []
    corners = [(w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)]
    for cx, cy, a0 in corners:
        for i in range(steps + 1):
            a = math.radians(a0 + 90 * i / steps)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def extrude_outline(name, outline, thickness, mat, loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, segments=3):
    """Prism from a 2D outline (XY) extruded along +Z by ``thickness`` (bottom at z=0)."""
    bm = bmesh.new()
    verts = [bm.verts.new((x, y, 0)) for x, y in outline]
    face = bm.faces.new(verts)
    if face.normal.z < 0:
        face.normal_flip()
    res = bmesh.ops.extrude_face_region(bm, geom=[face])
    top = [e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=top, vec=(0, 0, thickness))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = from_bmesh(name, bm, mat, loc, rot)
    if bevel > 0:
        add_bevel(obj, bevel, segments, angle=40)
    return obj


def soft_body(name, parts, mat, voxel=0.004, smooth=6, wrinkle=0.0, wrinkle_scale=0.05, target_tris=4000):
    """Union of rough ``parts`` turned into one soft, watertight fabric/foam shape.

    Voxel-remeshes the union (rounded seams), relaxes it, optionally adds cloth wrinkles
    and decimates to ``target_tris``. ``parts`` are consumed.
    """
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    for p in parts:
        me = bpy.data.meshes.new_from_object(p.evaluated_get(dg))
        me.transform(p.matrix_world)
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        bpy.data.objects.remove(p)
    obj = from_bmesh(name, bm, mat)
    rem = obj.modifiers.new("remesh", "REMESH")
    rem.mode = "VOXEL"
    rem.voxel_size = voxel
    if smooth:
        sm = obj.modifiers.new("smooth", "LAPLACIANSMOOTH")
        sm.iterations = smooth
        sm.lambda_factor = 0.6
        sm.use_volume_preserve = True
    if wrinkle > 0:
        tex = bpy.data.textures.new(name + "_wrinkle", "CLOUDS")
        tex.noise_scale = wrinkle_scale
        tex.noise_depth = 2
        disp = obj.modifiers.new("wrinkle", "DISPLACE")
        disp.texture = tex
        disp.strength = wrinkle
        disp.mid_level = 0.5
    bake(obj)
    faces = len(obj.data.polygons)
    if faces > target_tris / 2:
        dec = obj.modifiers.new("decimate", "DECIMATE")
        dec.ratio = max(0.02, min(1.0, (target_tris / 2) / faces))
        bake(obj)
    return obj


def bake(obj):
    """Applies the modifier stack of ``obj`` in place."""
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    old = obj.data
    obj.modifiers.clear()
    obj.data = me
    bpy.data.meshes.remove(old)
    return obj


def text(name, body, size, loc, mat, rot=(0, 0, 0), depth=0.0004, align="CENTER"):
    curve = bpy.data.curves.new(name, "FONT")
    curve.body = body
    curve.size = size
    curve.extrude = depth / 2
    curve.align_x = align
    curve.align_y = "CENTER"
    font_obj = bpy.data.objects.new(name + "_font", curve)
    bpy.context.scene.collection.objects.link(font_obj)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(font_obj.evaluated_get(dg))
    bpy.data.objects.remove(font_obj)
    bpy.data.curves.remove(curve)
    obj = _link(name, me, mat)
    obj.location = loc
    obj.rotation_euler = rot
    return obj


def mirror_x(objs):
    """Returns mirrored copies (across X) of ``objs`` (for left/right shoe pairs etc.)."""
    bpy.context.view_layer.update()
    out = []
    for o in objs:
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        c.matrix_world = Matrix.Scale(-1, 4, (1, 0, 0)) @ o.matrix_world
        out.append(c)
    return out


def translate(objs, offset):
    for o in objs:
        o.location = Vector(o.location) + Vector(offset)
    return objs


def rotate(objs, angle_deg, axis="Z", pivot=(0, 0, 0)):
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(angle_deg), 4, axis) @ Matrix.Translation(-Vector(pivot))
    bpy.context.view_layer.update()
    for o in objs:
        o.matrix_world = m @ o.matrix_world
    return objs


def _box_uv(bm, uv_layer):
    for face in bm.faces:
        n = face.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for loop in face.loops:
            co = loop.vert.co
            if ax == 0:
                u, v = co.y * (1 if n.x > 0 else -1), co.z
            elif ax == 1:
                u, v = co.x * (-1 if n.y > 0 else 1), co.z
            else:
                u, v = co.x, co.y * (1 if n.z > 0 else -1)
            loop[uv_layer].uv = (u, v)


def export_asset(name, out_dir, origin="bottom", sharp_angle=40.0, keep=False):
    """Merges every object in the scene into ``name``, writes UVs/normals and exports ``out_dir/name.fbx``.

    origin: 'bottom' = centre of the bounding box footprint at the lowest point; 'none' = keep world origin.
    Returns a dict with triangle count and dimensions.
    """
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH" and "mt_mat" in o]
    bm = bmesh.new()
    keys = []
    for o in objs:
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        me.transform(o.matrix_world)
        if o.matrix_world.determinant() < 0:
            me.flip_normals()
        key = o["mt_mat"]
        if key not in keys:
            keys.append(key)
        idx = keys.index(key)
        for p in me.polygons:
            p.material_index = idx
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    for o in list(bpy.context.scene.objects):
        bpy.data.objects.remove(o)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.normal_update()
    if origin == "bottom":
        xs = [v.co.x for v in bm.verts]
        ys = [v.co.y for v in bm.verts]
        zs = [v.co.z for v in bm.verts]
        shift = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
        bmesh.ops.translate(bm, verts=bm.verts, vec=-shift)
    # Blender (+X right, +Y forward) -> Unity (+X right, +Z forward) through the -Z/Y FBX axis setting needs a half turn.
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi, 3, "Z"))
    uv = bm.loops.layers.uv.new("UVMap")
    _box_uv(bm, uv)
    cos_limit = math.cos(math.radians(sharp_angle))
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2:
            e.smooth = e.link_faces[0].normal.dot(e.link_faces[1].normal) > cos_limit
        else:
            e.smooth = False
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    for key in keys:
        mat = bpy.data.materials.get(key) or bpy.data.materials.new(key)
        hexv = PALETTE[key]["tint"]
        mat.diffuse_color = (int(hexv[0:2], 16) / 255, int(hexv[2:4], 16) / 255, int(hexv[4:6], 16) / 255, 1)
        mesh.materials.append(mat)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".fbx")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="OFF",
                             use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP",
                             use_custom_props=False, use_tspace=False)
    dims = [round(d, 4) for d in obj.dimensions]
    if not keep:
        bpy.data.objects.remove(obj)
    return {"name": name, "tris": tris, "dims_xyz": dims, "materials": keys}
