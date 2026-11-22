"""Fixed grapple launchers: launch tubes, not turrets, sized like the vanilla fixed rocket launchers.

  flatpak run org.blender.Blender -b --python tools/model/designs/grapple_fixed.py -- OUTDIR

  GrappleFixedLarge   1x1x2 large (2.5 x 2.5 x 5 m): an armoured box with one launch tube in a
                      raised round muzzle, the hook fully inside its 0.30 m bore, a winch
                      drum at the back
  GrappleFixedSmall   1x1x4 small (0.5 x 0.5 x 2 m): a slim armoured tube; the hook's shaft is
                      in the bore and its barbed head stands out of the front, as on the turrets
plus build stages GrappleFixedLarge_BS1..3 and GrappleFixedSmall_BS1..2.

They fire the same hook as the turrets (2.28 m long, 0.045 m shaft, barbs 0.22 m). The game
rests the hook from the gun's muzzle_projectile dummy (the grapple ammo is a projectile type):
its tail at the dummy, its centre 1.1 m ahead, its tip 2.24 m ahead (WeaponControlLayer). So
each model puts that dummy where the hook's tail should sit in the tube.

Styling follows the large GrappleHook turret: paintable light panels, dark trim, yellow
accents, and a stepped round tube mouth. Blender frame: metres, +Z up, front +Y.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import semodel as se  # noqa: E402

COL = 'PaintedMetal_Colorable'
DARK = 'PaintedMetal_Darker'
VDARK = 'PaintedMetal_VeryDark'
METAL = 'Metal_Dull'
BLACK = 'BlackMetal'
YELLOW = 'PaintedMetal_Yellow'

HOOK_REST = 1.1      # hook centre ahead of the muzzle dummy
HOOK_TIP = 2.24      # hook tip ahead of the muzzle dummy
ROT_BOTTOM = (math.radians(-90), 0, 0)   # a dummy facing down instead of back


def side(s, inner, outer):
    """(low, high) of a span from inner to outer on the s (+1 or -1) side of the centre."""
    return (inner, outer) if s > 0 else (-outer, -inner)


def octagon(w, c):
    """Counter-clockwise XZ outline of a w x w square with corner chamfers of c."""
    return se.chamfer_rect(w, w, c)


# ---------------------------------------------------------------- large

LH = 1.25            # half width
LY0, LY1 = -2.5, 2.5
L_MUZZLE = 0.30      # hook tip lands 4 cm out of the mouth


def large(c):
    body = 2.44                                   # leaves 3 cm a side for the raised plates
    se.prism_y(c, 'body', COL, octagon(body, 0.42), LY0 + 0.14, 1.95, bev=0.03)
    se.prism_y(c, 'belt_rear', DARK, octagon(body + 0.03, 0.43), -0.55, -0.25, bev=0.01)
    se.prism_y(c, 'belt_front', DARK, octagon(body + 0.03, 0.43), 1.30, 1.55, bev=0.01)
    se.prism_y(c, 'rear_cap', DARK, octagon(body - 0.06, 0.40), LY0 + 0.025, LY0 + 0.15, bev=0.02)
    # rear: the conveyor face, a dark plate with a ring, all inside the block's back face
    se.box(c, 'rear_plate', VDARK, (-0.62, LY0 + 0.012, -0.62), (0.62, LY0 + 0.04, 0.62), bev=0.008)
    se.cylinder(c, 'rear_ring', METAL, 0.45, LY0, LY0 + 0.03, segments=32, axis='Y', bev=0.004)
    # front: a dark frame stepping in to a raised round muzzle boss, then the stepped mouth;
    # the bore (0.30 m) clears the hook's 0.22 m barbs, the tip lands 4 cm out
    se.prism_y(c, 'front_frame', DARK, octagon(body - 0.04, 0.40), 1.94, 2.12, bev=0.03)
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * 0.86, sz * 0.86
            se.box(c, 'hazard%d%d' % (sx, sz), YELLOW, (x - 0.14, 2.11, z - 0.035), (x + 0.14, 2.125, z + 0.035))
    se.cylinder(c, 'boss', COL, 0.82, 2.10, 2.30, segments=48, axis='Y', bev=0.03)
    for i in range(8):
        a = 2 * math.pi * (i + 0.5) / 8
        se.cylinder(c, 'boss_bolt%d' % i, DARK, 0.04, 2.29, 2.315, segments=8, axis='Y', centre=(0.75 * math.cos(a), 0, 0.75 * math.sin(a)))
    se.cylinder(c, 'ring', DARK, 0.68, 2.28, 2.40, segments=48, axis='Y', bev=0.015)
    se.cylinder(c, 'tube', METAL, 0.52, 2.38, 2.47, segments=40, axis='Y', bev=0.01)
    se.cylinder(c, 'tube_lip', DARK, 0.42, 2.45, LY1, segments=40, axis='Y', bev=0.008)
    se.cylinder(c, 'bore', BLACK, 0.30, L_MUZZLE - 0.1, LY1 + 0.003, segments=32, axis='Y')
    # winch drum ends on both sides, behind the tube: the drum stops short of the block's side
    # so its hub and bolts can stand proud without leaving the block
    for sx in (-1, 1):
        se.cylinder(c, 'drum%d' % sx, METAL, 0.78, *side(sx, body / 2 - 0.02, LH - 0.012), segments=40, axis='X', centre=(0, -1.25, 0), bev=0.008)
        se.cylinder(c, 'drum_hub%d' % sx, DARK, 0.34, *side(sx, LH - 0.016, LH), segments=32, axis='X', centre=(0, -1.25, 0), bev=0.004)
        for i in range(6):
            a = 2 * math.pi * i / 6
            y, z = -1.25 + 0.56 * math.cos(a), 0.56 * math.sin(a)
            se.cylinder(c, 'drum_bolt%d_%d' % (sx, i), DARK, 0.05, *side(sx, LH - 0.016, LH), segments=8, axis='X', centre=(0, y, z))
        # raised armour plate ahead of the drum, with a groove
        px0, px1 = side(sx, body / 2 - 0.01, LH - 0.004)
        se.box(c, 'side_plate%d' % sx, COL, (px0, 0.0, -0.62), (px1, 1.15, 0.62), bev=0.01)
        gx0, gx1 = side(sx, LH - 0.006, LH)
        se.box(c, 'side_groove%d' % sx, DARK, (gx0, 0.55, -0.62), (gx1, 0.60, 0.62))
    # top and bottom plates, so the box does not read as a plain extrusion
    for sz in (-1, 1):
        z0, z1 = side(sz, body / 2 - 0.01, LH - 0.004)
        se.box(c, 'top_plate%d' % sz, COL, (-0.62, -2.05, z0), (0.62, -0.70, z1), bev=0.01)
        se.box(c, 'top_plate_b%d' % sz, COL, (-0.62, 0.0, z0), (0.62, 1.15, z1), bev=0.01)
        sz0, sz1 = side(sz, body / 2 - 0.004, body / 2 + 0.008)
        se.box(c, 'stripe%d' % sz, YELLOW, (-0.62, 1.60, sz0), (0.62, 1.70, sz1))
    se.dummy(c, 'muzzle_projectile_001', (0, L_MUZZLE, 0), scale=(0.1, 0.1, 0.1))
    # the vanilla large rocket launcher's conveyor ports: the back face and the back cell's floor
    se.dummy(c, 'detector_conveyor_001', (0, -2.519, 0), scale=(1.279, 0.131, 1.279))
    se.dummy(c, 'detector_conveyor_002', (0, -1.25, -1.28), scale=(1.279, 0.131, 1.279), rotation=ROT_BOTTOM)


def large_stages():
    body = 2.44
    c = se.collection('GrappleFixedLarge_BS1', 'Large')
    se.prism_y(c, 'rear', 'Construction', octagon(body, 0.42), LY0, -0.3)
    for sx in (-1, 1):
        for sz in (-1, 1):
            se.box(c, 'rail%d%d' % (sx, sz), 'Construction', (sx * 0.9 - 0.08, -0.3, sz * 0.9 - 0.08), (sx * 0.9 + 0.08, LY1 - 0.1, sz * 0.9 + 0.08))
    c = se.collection('GrappleFixedLarge_BS2', 'Large')
    se.prism_y(c, 'body', 'Construction', octagon(body, 0.42), LY0, 1.95)
    se.cylinder(c, 'tube', METAL, 0.52, 1.2, 2.47, segments=32, axis='Y')
    c = se.collection('GrappleFixedLarge_BS3', 'Large')
    se.prism_y(c, 'body', COL, octagon(body, 0.42), LY0 + 0.14, 1.95)
    se.prism_y(c, 'rear_cap', DARK, octagon(body - 0.06, 0.40), LY0, LY0 + 0.15)
    se.prism_y(c, 'front_frame', 'Construction', octagon(body - 0.04, 0.40), 1.94, 2.12)
    se.cylinder(c, 'boss', 'Construction', 0.82, 2.10, 2.30, segments=40, axis='Y')
    se.cylinder(c, 'tube', METAL, 0.52, 2.28, 2.47, segments=32, axis='Y')
    for coll in ('GrappleFixedLarge_BS1', 'GrappleFixedLarge_BS2', 'GrappleFixedLarge_BS3'):
        c = bpy.data.collections[coll]
        se.dummy(c, 'detector_conveyor_001', (0, -2.519, 0), scale=(1.279, 0.131, 1.279))
        se.dummy(c, 'detector_conveyor_002', (0, -1.25, -1.28), scale=(1.279, 0.131, 1.279), rotation=ROT_BOTTOM)


# ---------------------------------------------------------------- small

SH = 0.25
SY0, SY1 = -1.0, 1.0
S_MUZZLE = -0.35     # front face 1.35 m ahead: the barbs (from 1.4 m) clear it, the shaft stays in


def small(c):
    body = 0.47
    se.prism_y(c, 'body', COL, octagon(body, 0.085), SY0 + 0.07, SY1 - 0.20, bev=0.012)
    se.prism_y(c, 'rear_cap', DARK, octagon(body - 0.02, 0.08), SY0, SY0 + 0.08, bev=0.008)
    se.prism_y(c, 'belt', DARK, octagon(body + 0.02, 0.09), -0.42, -0.30, bev=0.005)
    se.prism_y(c, 'front_shroud', DARK, octagon(body + 0.01, 0.09), SY1 - 0.22, SY1 - 0.01, bev=0.012)
    se.prism_y(c, 'front_face', VDARK, octagon(body - 0.10, 0.06), SY1 - 0.02, SY1 - 0.003)
    # stepped tube mouth round the hook's 0.045 m shaft
    se.cylinder(c, 'tube', METAL, 0.13, SY1 - 0.12, SY1 - 0.005, segments=28, axis='Y', bev=0.004)
    se.cylinder(c, 'tube_lip', DARK, 0.105, SY1 - 0.03, SY1 + 0.0, segments=28, axis='Y', bev=0.003)
    se.cylinder(c, 'bore', BLACK, 0.072, S_MUZZLE - 0.05, SY1 + 0.003, segments=20, axis='Y')
    # winch drum ends on both sides, near the back; hub proud of the drum, inside the block
    for sx in (-1, 1):
        se.cylinder(c, 'drum%d' % sx, METAL, 0.15, *side(sx, body / 2 - 0.004, SH - 0.006), segments=24, axis='X', centre=(0, -0.68, 0), bev=0.003)
        se.cylinder(c, 'drum_hub%d' % sx, DARK, 0.065, *side(sx, SH - 0.008, SH), segments=16, axis='X', centre=(0, -0.68, 0))
        # a side rail with a yellow mark near the muzzle
        rx0, rx1 = side(sx, body / 2 - 0.004, SH - 0.002)
        se.box(c, 'rail%d' % sx, DARK, (rx0, -0.22, -0.06), (rx1, 0.70, 0.06), bev=0.004)
        mx0, mx1 = side(sx, body / 2 - 0.004, SH - 0.004)
        se.box(c, 'mark%d' % sx, YELLOW, (mx0, 0.56, 0.06), (mx1, 0.66, 0.09))
    for sz in (-1, 1):
        z0, z1 = side(sz, body / 2 - 0.004, SH - 0.003)
        se.box(c, 'top_plate%d' % sz, COL, (-0.12, -0.24, z0), (0.12, 0.68, z1), bev=0.004)
        gz0, gz1 = side(sz, SH - 0.005, SH)
        se.box(c, 'top_groove%d' % sz, DARK, (-0.12, 0.20, gz0), (0.12, 0.23, gz1))
    se.dummy(c, 'muzzle_projectile_001', (0, S_MUZZLE, 0), scale=(0.06, 0.06, 0.06))


def small_stages():
    body = 0.47
    c = se.collection('GrappleFixedSmall_BS1', 'Small')
    se.prism_y(c, 'body', 'Construction', octagon(body, 0.085), SY0, 0.0)
    c = se.collection('GrappleFixedSmall_BS2', 'Small')
    se.prism_y(c, 'body', 'Construction', octagon(body, 0.085), SY0, SY1 - 0.20)
    se.prism_y(c, 'front_shroud', DARK, octagon(body + 0.01, 0.09), SY1 - 0.22, SY1 - 0.01)


# ---------------------------------------------------------------- previews

def hook(model, muzzle_y, hook_obj):
    """The real hook, placed where the game rests it, for the previews only."""
    if not os.path.exists(hook_obj):
        return
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=hook_obj, forward_axis='NEGATIVE_Z', up_axis='Y')
    coll = se.collection('_hook_' + model)
    for o in set(bpy.data.objects) - before:
        for c in o.users_collection:
            c.objects.unlink(o)
        coll.objects.link(o)
        o.location = (0, muzzle_y + HOOK_REST, 0)
        o.data.materials.clear()
        o.data.materials.append(se.material('BlackMetal'))


def main(argv):
    outdir = argv[0]
    repo = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
    hook_obj = os.path.join(repo, 'out', 'model', 'ref', 'hook.obj')
    se.reset()
    large(se.collection('GrappleFixedLarge', 'Large'))
    large_stages()
    small(se.collection('GrappleFixedSmall', 'Small'))
    small_stages()
    se.export(outdir)

    hook('GrappleFixedLarge', L_MUZZLE, hook_obj)
    hook('GrappleFixedSmall', S_MUZZLE, hook_obj)
    for name, frame in (('GrappleFixedLarge', ((-LH, LY0, -LH), (LH, LY1 + 0.1, LH))),
                        ('GrappleFixedSmall', ((-SH, SY0, -SH), (SH, S_MUZZLE + HOOK_TIP, SH)))):
        show = {name, '_hook_' + name}
        for view, yaw, pitch in (('front_left', 35, 22), ('back_right', 215, 30), ('side', 90, 0), ('front', 0, 0)):
            se.render(os.path.join(outdir, 'preview_%s_%s.png' % (name, view)), show, yaw, pitch, frame=frame)
        se.render(os.path.join(outdir, 'icon_%s.png' % name), {name}, 38, 24, size=256, transparent=True, margin=1.04)
        stages = sorted(c.name for c in bpy.data.collections if c.name.startswith(name + '_BS'))
        for s in stages:
            se.render(os.path.join(outdir, 'preview_%s.png' % s), {s}, 35, 22, size=500, frame=frame)


if __name__ == '__main__':
    main(sys.argv[sys.argv.index('--') + 1:])
