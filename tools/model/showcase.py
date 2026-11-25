"""Renders a block (an OBJ from MwmTool obj) with the game's own textures, for Workshop images.

  flatpak run org.blender.Blender -b --python tools/model/showcase.py -- \\
      [--yaw=35 --pitch=22 --size=1920x1080 --samples=128 --fill=0.72] \\
      OUT.png MODEL.obj TEXTURE_DIR MATERIALS_JSON

The materials are built the way the game shades them: the colour/metal map (_cm) gives colour
and, in its alpha, metalness; the normal/gloss map (_ng) gives the surface normal and, in its
alpha, gloss (roughness is 1 - gloss); the add map (_add) gives ambient occlusion in red and
emissive strength in green; decals are cut out by their alpha mask. Paintable parts show their
unpainted colour. Lighting is a three-light studio set with a soft world, and the block stands
on a shadow catcher, so the PNG is transparent apart from the block and its contact shadow.

yaw 0 looks at the block's front; positive yaw swings the camera to the block's left.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

EMISSIVE = (0.15, 1.0, 0.35)   # the green of a working block's indicators
GLASS = ('GenericGlass', 'Glass')


def texture(tex_dir, rel, colour):
    base = os.path.splitext(os.path.basename(rel.replace('\\', '/')))[0] + '.png'
    path = os.path.join(tex_dir, base)
    if not os.path.exists(path):
        return None
    img = bpy.data.images.get(base) or bpy.data.images.load(path)
    img.colorspace_settings.name = 'sRGB' if colour else 'Non-Color'
    img.alpha_mode = 'CHANNEL_PACKED'
    return img


def build(mat, spec, tex_dir):
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])

    if mat.name.startswith(GLASS) or spec.get('technique') == 'GLASS':
        bsdf.inputs['Base Color'].default_value = (0.6, 0.8, 0.9, 1)
        bsdf.inputs['Roughness'].default_value = 0.05
        bsdf.inputs['Transmission Weight'].default_value = 1.0
        return

    tex = spec.get('textures', {})
    links = nt.links

    def node(rel, colour):
        img = texture(tex_dir, rel, colour) if rel else None
        if img is None:
            return None
        n = nt.nodes.new('ShaderNodeTexImage')
        n.image = img
        n.interpolation = 'Cubic'
        return n

    cm = node(tex.get('ColorMetalTexture'), True)
    ng = node(tex.get('NormalGlossTexture'), False)
    add = node(tex.get('AddMapsTexture'), False)
    mask = node(tex.get('AlphamaskTexture'), False)

    colour_out = None
    if cm:
        colour_out = cm.outputs['Color']
        links.new(cm.outputs['Alpha'], bsdf.inputs['Metallic'])
    else:
        bsdf.inputs['Base Color'].default_value = (0.5, 0.5, 0.5, 1)

    if add:
        sep = nt.nodes.new('ShaderNodeSeparateColor')
        links.new(add.outputs['Color'], sep.inputs['Color'])
        if colour_out is not None:
            ao = nt.nodes.new('ShaderNodeMix')
            ao.data_type = 'RGBA'
            ao.blend_type = 'MULTIPLY'
            ao.inputs['Factor'].default_value = 1.0
            links.new(colour_out, ao.inputs['A'])
            links.new(sep.outputs['Red'], ao.inputs['B'])
            colour_out = ao.outputs['Result']
        bsdf.inputs['Emission Color'].default_value = (*EMISSIVE, 1)
        strength = nt.nodes.new('ShaderNodeMath')
        strength.operation = 'MULTIPLY'
        strength.inputs[1].default_value = 6.0
        links.new(sep.outputs['Green'], strength.inputs[0])
        links.new(strength.outputs['Value'], bsdf.inputs['Emission Strength'])

    if colour_out is not None:
        links.new(colour_out, bsdf.inputs['Base Color'])

    if ng:
        # the game's normal maps are DirectX style: green points down the texture
        sep = nt.nodes.new('ShaderNodeSeparateColor')
        links.new(ng.outputs['Color'], sep.inputs['Color'])
        flip = nt.nodes.new('ShaderNodeMath')
        flip.operation = 'SUBTRACT'
        flip.inputs[0].default_value = 1.0
        links.new(sep.outputs['Green'], flip.inputs[1])
        comb = nt.nodes.new('ShaderNodeCombineColor')
        links.new(sep.outputs['Red'], comb.inputs['Red'])
        links.new(flip.outputs['Value'], comb.inputs['Green'])
        links.new(sep.outputs['Blue'], comb.inputs['Blue'])
        nmap = nt.nodes.new('ShaderNodeNormalMap')
        links.new(comb.outputs['Color'], nmap.inputs['Color'])
        links.new(nmap.outputs['Normal'], bsdf.inputs['Normal'])
        rough = nt.nodes.new('ShaderNodeMath')
        rough.operation = 'SUBTRACT'
        rough.inputs[0].default_value = 1.0
        links.new(ng.outputs['Alpha'], rough.inputs[1])
        links.new(rough.outputs['Value'], bsdf.inputs['Roughness'])
    else:
        bsdf.inputs['Roughness'].default_value = 0.5

    if mask:
        links.new(mask.outputs['Color'], bsdf.inputs['Alpha'])
    elif spec.get('technique', '').startswith('DECAL') and cm:
        links.new(cm.outputs['Alpha'], bsdf.inputs['Alpha'])


def area(name, location, target, size, energy, colour=(1, 1, 1)):
    light = bpy.data.lights.new(name, 'AREA')
    light.shape = 'DISK'
    light.size = size
    light.energy = energy
    light.color = colour
    obj = bpy.data.objects.new(name, light)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (target - location).to_track_quat('-Z', 'Y').to_euler()
    return obj


def gpu(scene):
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for kind in ('OPTIX', 'CUDA', 'HIP'):
        try:
            prefs.compute_device_type = kind
            prefs.get_devices()
        except Exception:
            continue
        devices = [d for d in prefs.devices if d.type == kind]
        if devices:
            for d in prefs.devices:
                d.use = d.type == kind
            scene.cycles.device = 'GPU'
            print('rendering on', kind, ', '.join(d.name for d in devices))
            return
    scene.cycles.device = 'CPU'
    print('rendering on the CPU')


def main(argv):
    opts = {'yaw': 35.0, 'pitch': 22.0, 'size': '1920x1080', 'samples': 128.0, 'fill': 0.8}
    while argv[0].startswith('--'):
        key, value = argv[0][2:].split('=')
        opts[key] = value if key == 'size' else float(value)
        argv = argv[1:]
    out, obj_path, tex_dir, materials = argv[:4]
    width, height = (int(v) for v in opts['size'].split('x'))
    lib = json.load(open(materials))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    bpy.ops.wm.obj_import(filepath=obj_path, forward_axis='NEGATIVE_Z', up_axis='Y')
    objs = [o for o in scene.objects if o.type == 'MESH']

    for mat in bpy.data.materials:
        spec = lib.get(mat.name) or lib.get(mat.name.split('.')[0]) or {}
        build(mat, spec, tex_dir)
    for o in objs:
        for poly in o.data.polygons:
            poly.use_smooth = True

    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = (lo + hi) / 2
    radius = (hi - lo).length / 2

    # framed on the block's own vertices, not its bounding box: a round turret base leaves the
    # corners of its box empty
    silhouette = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]

    # the camera: perspective, a long lens, framed so the block fills `fill` of the frame
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    cam.data.lens = 70
    cam.data.sensor_width = 36
    yaw, pitch = math.radians(opts['yaw']), math.radians(opts['pitch'])
    direction = Vector((math.sin(yaw) * math.cos(pitch), math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.camera = cam

    def frame(distance):
        cam.location = centre + direction * distance
        cam.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
        bpy.context.view_layer.update()
        inv = cam.matrix_world.inverted()
        corners = silhouette
        f = cam.data.lens / cam.data.sensor_width   # focal length over sensor width
        aspect = width / height
        xs, ys = [], []
        for c in corners:
            p = inv @ c
            xs.append(p.x / -p.z * f)
            ys.append(p.y / -p.z * f * aspect)
        return xs, ys

    distance = radius * 4
    for _ in range(8):
        xs, ys = frame(distance)
        extent = max(max(xs) - min(xs), max(ys) - min(ys))
        distance *= extent / opts['fill']
    xs, ys = frame(distance)
    # centre the block in the frame
    shift_x = (max(xs) + min(xs)) / 2
    shift_y = (max(ys) + min(ys)) / 2 / (width / height)
    cam.data.shift_x = shift_x
    cam.data.shift_y = shift_y
    print('frame: centre', tuple(round(v, 2) for v in centre), 'radius', round(radius, 2), 'distance', round(distance, 2),
          'x', round(min(xs), 3), round(max(xs), 3), 'y', round(min(ys), 3), round(max(ys), 3), 'shift', round(shift_x, 3), round(shift_y, 3),
          'objects', [o.name for o in objs])

    # studio lights, scaled with the block so every block is lit alike
    side = Vector((direction.y, -direction.x, 0)).normalized()
    up = Vector((0, 0, 1))
    s = radius
    area('key', centre + (direction + side * 0.9 + up * 1.1).normalized() * s * 5, centre, s * 3.0, 160 * s * s, (1.0, 0.96, 0.9))
    area('fill', centre + (direction - side * 1.2 + up * 0.2).normalized() * s * 5, centre, s * 4.0, 22 * s * s, (0.85, 0.9, 1.0))
    area('rim', centre + (-direction * 1.0 - side * 0.4 + up * 1.2).normalized() * s * 5, centre, s * 2.5, 130 * s * s, (0.9, 0.95, 1.0))

    world = bpy.data.worlds.new('studio')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.32, 0.35, 0.40, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.22

    # a floor that only catches the block's shadow
    bpy.ops.mesh.primitive_plane_add(size=radius * 30, location=(centre.x, centre.y, lo.z))
    floor = bpy.context.active_object
    floor.is_shadow_catcher = True

    scene.render.engine = 'CYCLES'
    gpu(scene)
    scene.cycles.samples = int(opts['samples'])
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    # AgX where Blender's colour management loads; the Flathub build's does not (an OCIO version
    # mismatch), leaving only Standard, which clips sooner, so it gets a little less exposure
    try:
        scene.view_settings.view_transform = 'AgX'
        scene.view_settings.look = 'AgX - Medium High Contrast'
    except TypeError:
        scene.view_settings.view_transform = 'Standard'
        scene.view_settings.exposure = opts.get('exposure', -0.6)
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print('rendered', out)


if __name__ == '__main__':
    main(sys.argv[sys.argv.index('--') + 1:])
