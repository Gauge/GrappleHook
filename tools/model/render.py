"""Renders OBJ models (from MwmTool obj, or the build script) to PNG for review.

  flatpak run org.blender.Blender -b --python tools/model/render.py -- [--yaw=35 --pitch=25] OUT.png A.obj [B.obj ...]

yaw 0 looks at the front, positive yaw swings the camera to the model's left.

Each OBJ is shown side by side, left to right, at true scale, from the same three-quarter view,
with flat colours keyed to Space Engineers material names. Decal materials (Atlas*, Paneling*,
TextAtlas, Buttons*) are hidden: they are coplanar overlays that only flicker in a plain render.
"""
import math
import sys

import bpy
from mathutils import Vector

# material name prefix -> RGBA, first match wins
PALETTE = [
    ('PaintedMetal_Colorable', (0.55, 0.58, 0.62, 1)),
    ('PaintedMetal_VeryDark', (0.07, 0.07, 0.08, 1)),
    ('PaintedMetal_Darker', (0.18, 0.19, 0.21, 1)),
    ('PaintedMetal_Yellow', (0.85, 0.65, 0.10, 1)),
    ('Metal_Dull', (0.42, 0.43, 0.45, 1)),
    ('Metal_Shiny', (0.75, 0.76, 0.78, 1)),
    ('Chrome', (0.85, 0.86, 0.88, 1)),
    ('BlackMetal', (0.05, 0.05, 0.05, 1)),
    ('MetalMilitary', (0.30, 0.32, 0.28, 1)),
    ('Rubber', (0.03, 0.03, 0.03, 1)),
    ('GenericGlass', (0.55, 0.75, 0.85, 1)),
    ('Glass', (0.55, 0.75, 0.85, 1)),
    ('Emissive', (0.10, 0.90, 0.30, 1)),
    ('Conveyor', (0.25, 0.25, 0.25, 1)),
    ('Cable', (0.08, 0.08, 0.08, 1)),
]
DECALS = ('Atlas', 'Paneling', 'TextAtlas', 'Buttons', 'Decal')


def colour(name):
    for prefix, rgba in PALETTE:
        if name.startswith(prefix):
            return rgba
    return (0.9, 0.2, 0.9, 1)  # unknown: loud magenta so it gets noticed


def main(argv):
    global YAW, PITCH
    YAW, PITCH = 35.0, 25.0
    while argv[0].startswith('--'):
        key, value = argv[0][2:].split('=')
        globals()[key.upper()] = float(value)
        argv = argv[1:]
    out, objs = argv[0], argv[1:]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    x = 0.0
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    for path in objs:
        before = set(bpy.data.objects)
        bpy.ops.wm.obj_import(filepath=path, forward_axis='NEGATIVE_Z', up_axis='Y')
        new = [o for o in bpy.data.objects if o not in before]
        pts = [o.matrix_world @ Vector(c) for o in new for c in o.bound_box]
        width = max(p.x for p in pts) - min(p.x for p in pts)
        shift = x - min(p.x for p in pts)
        for o in new:
            o.location.x += shift
            for slot in o.material_slots:
                m = slot.material
                if m is None:
                    continue
                if m.name.startswith(DECALS):
                    m.diffuse_color = (0, 0, 0, 0)
                else:
                    m.diffuse_color = colour(m.name)
            # drop decal faces outright so they cannot z-fight
            decal = {i for i, s in enumerate(o.material_slots) if s.material and s.material.name.startswith(DECALS)}
            if decal:
                import bmesh
                bm = bmesh.new()
                bm.from_mesh(o.data)
                bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in decal], context='FACES')
                bm.to_mesh(o.data)
                bm.free()
        bpy.context.view_layer.update()
        for o in new:
            for c in o.bound_box:
                p = o.matrix_world @ Vector(c)
                lo = Vector(map(min, lo, p))
                hi = Vector(map(max, hi, p))
        x += width + 0.6

    corners = [Vector((a, b, c)) for a in (lo.x, hi.x) for b in (lo.y, hi.y) for c in (lo.z, hi.z)]
    centre = (lo + hi) / 2

    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    # SE forward (-Z) lands on Blender +Y after the import, so the front of a model faces +Y.
    yaw, pitch = math.radians(YAW), math.radians(PITCH)
    direction = Vector((math.sin(yaw) * math.cos(pitch), math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = centre + direction * 50
    cam.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.view_layer.update()
    # frame what the camera actually sees: the corners in camera space
    inv = cam.matrix_world.inverted()
    pts = [inv @ c for c in corners]
    w = max(p.x for p in pts) - min(p.x for p in pts)
    h = max(p.y for p in pts) - min(p.y for p in pts)
    mid = Vector(((max(p.x for p in pts) + min(p.x for p in pts)) / 2, (max(p.y for p in pts) + min(p.y for p in pts)) / 2, 0))
    cam.location = cam.matrix_world @ mid
    aspect = min(2.6, max(1.0, w / max(h, 0.01)))
    scene.render.resolution_x = int(800 * aspect)
    scene.render.resolution_y = 800
    cam.data.ortho_scale = max(w, h * aspect) * 1.08
    scene.camera = cam

    scene.render.engine = 'BLENDER_WORKBENCH'
    shading = scene.display.shading
    shading.light = 'STUDIO'
    shading.color_type = 'MATERIAL'
    shading.show_cavity = True
    shading.cavity_type = 'BOTH'
    shading.show_object_outline = False
    shading.show_specular_highlight = True
    scene.display.render_aa = '8'
    scene.render.film_transparent = False
    world = bpy.data.worlds.new('w')
    scene.world = world
    world.color = (0.16, 0.17, 0.19)
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print('rendered', out)


if __name__ == '__main__':
    main(sys.argv[sys.argv.index('--') + 1:])
