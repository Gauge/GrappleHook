"""Blender helpers for building Space Engineers block models from code (tools/model/README.md).

Geometry is built in Blender's frame - metres, +Z up, the block's front facing +Y - one
collection per .mwm, named after the model file. export() writes each collection as the mesh
JSON MwmTool turns into an .mwm, converting to the game's frame (+Y up, front -Z) on the way.

Materials are Space Engineers material names (tools/model/materials.json); a face takes the
name of its material slot. UVs are box projected at export from the face normal, at each
material's texel density, which suits the game's tiling metal materials.
"""
import json
import math
import os
import re

import bmesh
import bpy
from mathutils import Matrix, Vector

# UV units per metre, from the large turret's own meshes (MwmTool probe)
DENSITY = {
    'PaintedMetal_Colorable': 1.0,
    'PaintedMetalScratched_Colorable': 1.0,
    'PaintedMetal_Darker': 0.45,
    'PaintedMetal_VeryDark': 0.45,
    'Metal_Dull': 1.0,
    'Metal_Shiny': 1.9,
    'BlackMetal': 1.9,
    'Chrome': 0.45,
    'Construction': 1.0,
}
SHARP_ANGLE = math.radians(35)

# Blender (x, y, z) -> game (x, z, -y): a rotation, so handedness and winding survive
TO_SE = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))


# ---------------------------------------------------------------- scene

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def collection(name, grid=None):
    """One model. grid ('Large' or 'Small') picks the mod folder it is written to:
    Models/Cubes/<grid>/<name>.mwm."""
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    if grid:
        c['grid'] = grid
    return c


# preview colours only; the game uses its own textures for each material name
PALETTE = {
    'PaintedMetal_Colorable': (0.55, 0.58, 0.62, 1),
    'PaintedMetalScratched_Colorable': (0.55, 0.58, 0.62, 1),
    'PaintedMetal_Darker': (0.18, 0.19, 0.21, 1),
    'PaintedMetal_VeryDark': (0.07, 0.07, 0.08, 1),
    'PaintedMetal_Yellow': (0.85, 0.65, 0.10, 1),
    'Metal_Dull': (0.42, 0.43, 0.45, 1),
    'Metal_Shiny': (0.75, 0.76, 0.78, 1),
    'Chrome': (0.85, 0.86, 0.88, 1),
    'BlackMetal': (0.04, 0.04, 0.04, 1),
    'Rubber': (0.03, 0.03, 0.03, 1),
    'GenericGlass': (0.45, 0.70, 0.85, 1),
    'Emissive': (0.10, 0.90, 0.30, 1),
    'Construction': (0.60, 0.50, 0.30, 1),
}


def material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.diffuse_color = PALETTE.get(name, (0.9, 0.2, 0.9, 1))
    return m


def _object(coll, name, bm, mat):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.data.materials.append(material(mat))
    coll.objects.link(obj)
    return obj


def bevel(obj, width, segments=1, angle=60, harden=True):
    """Chamfers every edge sharper than angle, as a modifier applied at export."""
    if width <= 0:
        return obj
    m = obj.modifiers.new('bevel', 'BEVEL')
    m.width = width
    m.segments = segments
    m.limit_method = 'ANGLE'
    m.angle_limit = math.radians(angle)
    m.harden_normals = harden
    m.miter_outer = 'MITER_ARC'
    return obj


# ---------------------------------------------------------------- primitives

def box(coll, name, mat, lo, hi, bev=0.0, seg=1):
    """Axis aligned box from corner lo to corner hi."""
    lo, hi = Vector(lo), Vector(hi)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector(((lo.x + hi.x) / 2 + v.co.x * (hi.x - lo.x),
                       (lo.y + hi.y) / 2 + v.co.y * (hi.y - lo.y),
                       (lo.z + hi.z) / 2 + v.co.z * (hi.z - lo.z)))
    return bevel(_object(coll, name, bm, mat), bev, seg)


def prism(coll, name, mat, outline, z0, z1, bev=0.0, seg=1, top_inset=0.0):
    """Extrudes a closed XY outline (counter-clockwise) from z0 to z1. top_inset shrinks the
    top towards the outline's centre, for sloped skirts."""
    bm = bmesh.new()
    c = Vector((sum(p[0] for p in outline) / len(outline), sum(p[1] for p in outline) / len(outline)))
    bottom = [bm.verts.new((p[0], p[1], z0)) for p in outline]
    top = []
    for p in outline:
        d = Vector(p) - c
        scale = max(0.0, 1 - top_inset / max(d.length, 1e-6)) if top_inset else 1.0
        q = c + d * scale
        top.append(bm.verts.new((q.x, q.y, z1)))
    n = len(outline)
    bm.faces.new(list(reversed(bottom)))
    bm.faces.new(top)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bevel(_object(coll, name, bm, mat), bev, seg)


def prism_y(coll, name, mat, outline_xz, y0, y1, bev=0.0, seg=1):
    """Extrudes a closed XZ outline (counter-clockwise seen from the front, +Y) from y0 to y1:
    a body running front to back, such as a chamfered launcher housing."""
    # built along Z from -y1 to -y0, then turned +90 degrees about X: (x, a, t) -> (x, -t, a),
    # so the outline's second coordinate lands on Z and the extrusion on y0..y1
    obj = prism(coll, name, mat, list(outline_xz), -y1, -y0, bev=0.0)
    obj.data.transform(Matrix.Rotation(math.radians(90), 4, 'X'))
    return bevel(obj, bev, seg)


def cylinder(coll, name, mat, radius, z0, z1, segments=24, axis='Z', centre=(0, 0, 0), radius_top=None, bev=0.0):
    """Cylinder (or cone frustum with radius_top) along axis, from z0 to z1 along that axis."""
    bm = bmesh.new()
    rt = radius if radius_top is None else radius_top
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=rt, depth=z1 - z0)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, (z0 + z1) / 2))
    if axis == 'X':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'Y'))
    elif axis == 'Y':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, 'X'))
    bmesh.ops.translate(bm, verts=bm.verts, vec=centre)
    return bevel(_object(coll, name, bm, mat), bev, 1)


def rounded_rect(w, d, r, cx=0.0, cy=0.0, steps=4):
    """Counter-clockwise outline of a w x d rectangle with corner radius r."""
    pts = []
    for qx, qy, start in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        ox, oy = cx + qx * (w / 2 - r), cy + qy * (d / 2 - r)
        for k in range(steps + 1):
            a = math.radians(start + 90 * k / steps)
            pts.append((ox + r * math.cos(a), oy + r * math.sin(a)))
    return pts


def chamfer_rect(w, d, c, cx=0.0, cy=0.0):
    """Counter-clockwise outline of a w x d rectangle with 45 degree corner chamfers of c."""
    x, y = w / 2, d / 2
    pts = [(x, y - c), (x - c, y), (-x + c, y), (-x, y - c), (-x, -y + c), (-x + c, -y), (x - c, -y), (x, -y + c)]
    return [(cx + p[0], cy + p[1]) for p in pts]


def dummy(coll, name, location, props=None, scale=(1, 1, 1), rotation=(0, 0, 0)):
    """An empty exported as a model dummy; props become its custom data (file=, highlight=)."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'CUBE'
    e.empty_display_size = 0.5
    e.location = location
    e.rotation_euler = rotation
    e.scale = scale
    for k, v in (props or {}).items():
        e[k] = v
    coll.objects.link(e)
    return e


# ---------------------------------------------------------------- export

def _uv(co, n, density):
    """Box projection: drop the axis the face normal leans on most."""
    a = (abs(n.x), abs(n.y), abs(n.z))
    if a[0] >= a[1] and a[0] >= a[2]:
        u, v = (co.y if n.x > 0 else -co.y), co.z
    elif a[1] >= a[2]:
        u, v = (-co.x if n.y > 0 else co.x), co.z
    else:
        u, v = co.x, (co.y if n.z > 0 else -co.y)
    return (u * density, v * density)


def mesh_json(coll):
    deps = bpy.context.evaluated_depsgraph_get()
    parts = {}
    for obj in coll.objects:
        if obj.type != 'MESH':
            continue
        ev = obj.evaluated_get(deps)
        mesh = ev.to_mesh()
        mesh.transform(obj.matrix_world)
        if not any(e.use_edge_sharp for e in mesh.edges):
            mesh.set_sharp_from_angle(angle=SHARP_ANGLE)
        mesh.calc_loop_triangles()
        normals = mesh.corner_normals
        for tri in mesh.loop_triangles:
            slot = obj.material_slots[tri.material_index] if obj.material_slots else None
            name = slot.material.name if slot and slot.material else 'PaintedMetal_Colorable'
            p = parts.setdefault(name, {'material': name, 'positions': [], 'normals': [], 'uvs': [], 'indices': [], '_seen': {}})
            fn = tri.normal
            dens = DENSITY.get(name, 1.0)
            for loop in tri.loops:
                co = mesh.vertices[mesh.loops[loop].vertex_index].co
                nr = normals[loop].vector
                uv = _uv(co, fn, dens)
                se_co = TO_SE @ co.to_4d()
                se_n = TO_SE.to_3x3() @ nr
                key = (round(se_co.x, 5), round(se_co.y, 5), round(se_co.z, 5),
                       round(se_n.x, 3), round(se_n.y, 3), round(se_n.z, 3), round(uv[0], 4), round(uv[1], 4))
                idx = p['_seen'].get(key)
                if idx is None:
                    idx = len(p['positions']) // 3
                    p['_seen'][key] = idx
                    p['positions'] += [se_co.x, se_co.y, se_co.z]
                    p['normals'] += [se_n.x, se_n.y, se_n.z]
                    p['uvs'] += list(uv)
                p['indices'].append(idx)
        ev.to_mesh_clear()
    for p in parts.values():
        del p['_seen']
    dummies = []
    for obj in coll.objects:
        if obj.type != 'EMPTY':
            continue
        m = TO_SE @ obj.matrix_world @ TO_SE.inverted()
        # XNA matrices are row-vector: rows are the images of the axes, then the translation
        flat = [m[r][c] for c in range(4) for r in range(4)]
        props = {k: str(obj[k]) for k in obj.keys() if not k.startswith('_')}
        # Blender suffixes duplicate names (.001); every model may reuse a dummy name
        dummies.append({'name': re.sub(r'\.\d{3}$', '', obj.name), 'matrix': flat, 'props': props})
    return {'parts': list(parts.values()), 'dummies': dummies}


def export(outdir, extra=None):
    """Writes <outdir>/<grid>/<collection>.json for every collection; extra adds keys per collection."""
    for coll in bpy.data.collections:
        if coll.name.startswith('_'):
            continue
        data = mesh_json(coll)
        data.update((extra or {}).get(coll.name, {}))
        folder = os.path.join(outdir, coll.get('grid', 'Small'))
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, coll.name + '.json')
        with open(path, 'w') as f:
            json.dump(data, f)
        tris = sum(len(p['indices']) // 3 for p in data['parts'])
        print('exported %s: %d triangles, %d dummies' % (path, tris, len(data['dummies'])))


# ---------------------------------------------------------------- preview

def instance(coll_name, matrix, name=None):
    """Places a whole collection (one model) at matrix, for previews of the assembled block."""
    e = bpy.data.objects.new(name or ('_' + coll_name), None)
    e.instance_type = 'COLLECTION'
    e.instance_collection = bpy.data.collections[coll_name]
    e.matrix_world = matrix
    collection('_preview').objects.link(e)
    return e


def dummy_matrix(coll_name, dummy_name):
    obj = bpy.data.collections[coll_name].objects[dummy_name]
    return Matrix.LocRotScale(obj.matrix_world.to_translation(), obj.matrix_world.to_quaternion(), None)


def render(path, show, yaw=35.0, pitch=25.0, size=900, frame=None, transparent=False, margin=1.1):
    """Workbench render of the collections in show (others hidden), front at yaw 0.
    frame: (lo, hi) corners to frame instead of the visible objects' bounds."""
    scene = bpy.context.scene
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.exclude = lc.name not in show
    bpy.context.view_layer.update()
    if frame is None:
        pts = []
        for lc in bpy.context.view_layer.layer_collection.children:
            if lc.exclude:
                continue
            for o in lc.collection.all_objects:
                if o.type == 'MESH':
                    pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
                elif o.instance_type == 'COLLECTION':
                    for so in o.instance_collection.all_objects:
                        if so.type == 'MESH':
                            pts += [o.matrix_world @ so.matrix_world @ Vector(c) for c in so.bound_box]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    else:
        lo, hi = Vector(frame[0]), Vector(frame[1])
    corners = [Vector((a, b, c)) for a in (lo.x, hi.x) for b in (lo.y, hi.y) for c in (lo.z, hi.z)]
    centre = (lo + hi) / 2
    cam = scene.camera
    if cam is None:
        cam = bpy.data.objects.new('_cam', bpy.data.cameras.new('_cam'))
        scene.collection.objects.link(cam)
        scene.camera = cam
    cam.data.type = 'ORTHO'
    y, p = math.radians(yaw), math.radians(pitch)
    d = Vector((-math.sin(y) * math.cos(p), math.cos(y) * math.cos(p), math.sin(p)))
    cam.location = centre + d * 50
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    q = [inv @ c for c in corners]
    w = max(v.x for v in q) - min(v.x for v in q)
    h = max(v.y for v in q) - min(v.y for v in q)
    mid = Vector(((max(v.x for v in q) + min(v.x for v in q)) / 2, (max(v.y for v in q) + min(v.y for v in q)) / 2, 0))
    cam.location = cam.matrix_world @ mid
    aspect = min(2.6, max(1.0, w / max(h, 0.01)))
    scene.render.resolution_x, scene.render.resolution_y = int(size * aspect), size
    cam.data.ortho_scale = max(w, h * aspect) * margin
    scene.render.engine = 'BLENDER_WORKBENCH'
    s = scene.display.shading
    s.light, s.color_type = 'STUDIO', 'MATERIAL'
    s.show_cavity, s.cavity_type = True, 'BOTH'
    s.show_specular_highlight = True
    scene.display.render_aa = '8'
    if scene.world is None:
        scene.world = bpy.data.worlds.new('w')
    scene.world.color = (0.16, 0.17, 0.19)
    scene.render.film_transparent = transparent
    scene.render.image_settings.color_mode = 'RGBA' if transparent else 'RGB'
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('rendered', path)
