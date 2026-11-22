"""Small grid grapple turret: a 5x5x5 small block (2.5 m), the vanilla small turret footprint.

  flatpak run org.blender.Blender -b --python tools/model/designs/grapple_turret_small.py -- OUTDIR [elevation]

Three models, as the game's turret class expects:
  GrappleTurretSmall           fixed: low armoured base slab with a sloped skirt, the bearing,
                               the conveyor port, and the subpart_MissileTurretBase1 dummy
  GrappleTurretSmall_Base      turns (azimuth): the armoured housing, a yoke around the barrel
  GrappleTurretSmall_Barrels   tilts (elevation): the launcher, the sensor box, the muzzle and
                               camera dummies
plus build stages GrappleTurretSmall_BS1..3. The styling follows the large GrappleHook turret:
flared skirt with bolt slots, a rounded housing with a light upper band over a dark lower one,
a louvred front grille, a square launcher with a stepped round nozzle and a sensor box on top.

The launcher is the large turret's size, because it fires the same hook: 0.47 m wide, 0.45 m
tall, from 0.22 m behind its pivot to 1.265 m ahead, the nozzle stepping down to 0.105 m
radius around a bore wider than the hook's 0.045 m shaft. At rest the hook's tail sits at the
pivot and its barbs (0.22 m) open just past the nozzle, as on the large turret.

Blender frame: metres, +Z up, front +Y; the block spans -1.25..1.25 on every axis.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import semodel as se  # noqa: E402

COL = 'PaintedMetal_Colorable'
DARK = 'PaintedMetal_Darker'
VDARK = 'PaintedMetal_VeryDark'
METAL = 'Metal_Dull'
BLACK = 'BlackMetal'
YELLOW = 'PaintedMetal_Yellow'
GLASS = 'GenericGlass'

H = 1.25                  # half block
SLAB_TOP = -0.98          # top of the vertical slab wall
SKIRT_TOP = -0.84         # top of the sloped skirt
PIVOT_Z = -0.76           # azimuth pivot (top of the bearing), block frame
BARREL_PIVOT = (0.0, -0.05, 0.46)  # elevation pivot, in the housing's frame


def base(c):
    """The fixed part. Mirrors the vanilla small turret: full 2.5 m footprint, about 0.5 m tall."""
    outline = se.chamfer_rect(2 * H, 2 * H, 0.34)
    se.prism(c, 'slab_foot', DARK, outline, -H, -H + 0.07, bev=0.01)
    se.prism(c, 'slab', COL, outline, -H + 0.07, SLAB_TOP, bev=0.02)
    se.prism(c, 'skirt', COL, se.chamfer_rect(2 * H - 0.02, 2 * H - 0.02, 0.33), SLAB_TOP, SKIRT_TOP, top_inset=0.26, bev=0.015)
    # dark trim bands across the middle of each side, like the vanilla bases and the large skirt
    for sx in (-1, 1):
        se.box(c, 'band_x%d' % sx, DARK, (sx * H - 0.03 if sx > 0 else -H - 0.01, -0.28, -H + 0.07), (H + 0.01 if sx > 0 else -H + 0.03, 0.28, SLAB_TOP - 0.02), bev=0.01)
        se.box(c, 'band_y%d' % sx, DARK, (-0.28, sx * H - 0.03 if sx > 0 else -H - 0.01, -H + 0.07), (0.28, H + 0.01 if sx > 0 else -H + 0.03, SLAB_TOP - 0.02), bev=0.01)
    # bolt slots along the skirt's top edge: dark recesses with a bright bolt head
    for i, (x, y, rot) in enumerate(_ring_points(0.86, 8)):
        se.box(c, 'slot%d' % i, VDARK, (x - 0.07, y - 0.07, SKIRT_TOP - 0.035), (x + 0.07, y + 0.07, SKIRT_TOP + 0.004))
        se.cylinder(c, 'bolt%d' % i, METAL, 0.03, SKIRT_TOP - 0.03, SKIRT_TOP + 0.012, segments=8, centre=(x, y, 0))
    # the bearing the housing turns on
    se.cylinder(c, 'bearing_ring', DARK, 0.70, SKIRT_TOP, SKIRT_TOP + 0.03, segments=48, bev=0.008)
    se.cylinder(c, 'bearing', METAL, 0.62, SKIRT_TOP, PIVOT_Z, segments=48, bev=0.008)
    # conveyor port underneath, the same dummy the vanilla small turrets use
    se.dummy(c, 'detector_conveyor_large_001', (0, 0, -1.2535), scale=(1.325, 1.325, 0.16))
    se.dummy(c, 'subpart_MissileTurretBase1', (0, 0, PIVOT_Z), {'file': 'GrappleTurretSmall_Base'})


def _ring_points(r, n):
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n
        yield r * math.cos(a), r * math.sin(a), a


HW, HD, HR, HCY = 1.24, 1.06, 0.28, -0.20   # housing plan: width, depth, corner radius, centre y
SLOT = 0.53                                   # the channel the launcher sits in (it is 0.47 wide)
SLOT_BACK = -0.33                             # how far back the channel runs
LOW_TOP = 0.20                                # top of the dark lower band, and the channel floor
TOP = 0.68                                    # top of the housing, flush with the launcher


def u_outline():
    """Counter-clockwise housing outline with the launcher channel cut into the front."""
    pts = se.rounded_rect(HW, HD, HR, cy=HCY)
    front = HCY + HD / 2
    steps = (len(pts) // 4)                   # points per corner
    notch = [(SLOT / 2, front), (SLOT / 2, SLOT_BACK), (-SLOT / 2, SLOT_BACK), (-SLOT / 2, front)]
    return pts[:steps] + notch + pts[steps:]


def housing(c):
    """Turns with azimuth. Origin at the pivot. One rounded armoured shell, dark below and
    light above, with a channel down the front the launcher tilts in, like the large head.
    The channel floor sits low enough for the launcher's rounded back to swing through it."""
    se.cylinder(c, 'neck', VDARK, 0.54, 0.0, 0.08, segments=48, bev=0.01)
    se.prism(c, 'hull_low', DARK, se.rounded_rect(HW, HD, HR, cy=HCY), 0.08, LOW_TOP, bev=0.02)
    se.prism(c, 'hull_high', COL, u_outline(), LOW_TOP, TOP, bev=0.045, seg=2)
    se.box(c, 'channel_floor', VDARK, (-SLOT / 2, SLOT_BACK, LOW_TOP - 0.03), (SLOT / 2, HCY + HD / 2 - 0.02, LOW_TOP + 0.005))
    # raised armour plates and latches on the outside of each cheek
    for sx in (-1, 1):
        x = sx * HW / 2
        se.box(c, 'plate%d' % sx, COL, (min(x, x + sx * 0.02), HCY - 0.30, 0.30), (max(x, x + sx * 0.02), HCY + 0.24, 0.60), bev=0.01)
        se.box(c, 'plate_groove%d' % sx, DARK, (min(x, x + sx * 0.024), HCY - 0.04, 0.30), (max(x, x + sx * 0.024), HCY - 0.02, 0.60))
        se.box(c, 'latch%d' % sx, DARK, (min(x, x + sx * 0.04), HCY - 0.41, 0.38), (max(x, x + sx * 0.04), HCY - 0.35, 0.52), bev=0.006)
        # trunnion bosses where the launcher's axle enters each cheek
        se.cylinder(c, 'boss%d' % sx, METAL, 0.12, sx * SLOT / 2 - (0.0 if sx > 0 else 0.02), sx * SLOT / 2 + (0.02 if sx > 0 else 0.0), segments=20, axis='X', centre=(0, BARREL_PIVOT[1], BARREL_PIVOT[2]), bev=0.004)
        # yellow accent on each cheek top, either side of the channel
        se.box(c, 'hazard%d' % sx, YELLOW, (sx * SLOT / 2 + (0.04 if sx > 0 else -0.12), HCY + HD / 2 - 0.18, TOP - 0.004), (sx * SLOT / 2 + (0.12 if sx > 0 else -0.04), HCY + HD / 2 - 0.07, TOP + 0.006))
    # louvred grille across the front of the lower band
    gy = HCY + HD / 2
    se.box(c, 'grille_frame', VDARK, (-0.36, gy - 0.04, 0.095), (0.36, gy + 0.012, 0.185), bev=0.008)
    for i in range(3):
        z = 0.108 + i * 0.026
        se.box(c, 'louvre%d' % i, METAL, (-0.32, gy, z), (0.32, gy + 0.03, z + 0.012))
    # rear: a vent block and the antenna
    by = HCY - HD / 2
    se.box(c, 'rear_vent', VDARK, (-0.26, by - 0.02, 0.32), (0.26, by + 0.01, 0.56), bev=0.006)
    for i in range(4):
        z = 0.35 + i * 0.05
        se.box(c, 'rear_louvre%d' % i, METAL, (-0.23, by - 0.035, z), (0.23, by - 0.01, z + 0.018))
    se.cylinder(c, 'antenna_base', DARK, 0.03, TOP - 0.02, TOP + 0.03, segments=10, centre=(0.42, by + 0.16, 0))
    se.cylinder(c, 'antenna', BLACK, 0.007, TOP + 0.03, TOP + 0.36, segments=6, centre=(0.42, by + 0.16, 0))
    se.dummy(c, 'subpart_MissileTurretBarrels', BARREL_PIVOT, {'file': 'GrappleTurretSmall_Barrels'})


def launcher(c):
    """Tilts with elevation. Origin at the trunnion, firing along +Y. The large turret's
    launcher size; the back is a drum round the axle so it swings without reaching further."""
    se.cylinder(c, 'axle', METAL, 0.09, -SLOT / 2 - 0.02, SLOT / 2 + 0.02, segments=20, axis='X', bev=0.006)
    se.cylinder(c, 'drum', DARK, 0.215, -0.235, 0.235, segments=28, axis='X', bev=0.015)
    for sx in (-1, 1):
        se.cylinder(c, 'drum_cap%d' % sx, METAL, 0.15, sx * 0.235 - (0.0 if sx > 0 else 0.012), sx * 0.235 + (0.012 if sx > 0 else 0.0), segments=24, axis='X', bev=0.003)
    # square body: light on top, dark underneath, as on the large launcher
    se.box(c, 'body', COL, (-0.235, 0.0, -0.02), (0.235, 0.82, 0.218), bev=0.045, seg=2)
    se.box(c, 'belly', DARK, (-0.228, 0.04, -0.236), (0.228, 0.80, -0.02), bev=0.03)
    for sx in (-1, 1):
        x0, x1 = (0.235, 0.255) if sx > 0 else (-0.255, -0.235)
        se.box(c, 'rail%d' % sx, DARK, (x0, 0.18, 0.03), (x1, 0.76, 0.10), bev=0.006)
        se.box(c, 'stripe%d' % sx, YELLOW, (x0 - 0.002, 0.66, 0.105), (x1 + 0.002, 0.74, 0.14))
    # stepped round nozzle: 0.175 -> 0.14 -> 0.105 m, the large one's taper
    se.box(c, 'collar', DARK, (-0.205, 0.80, -0.205), (0.205, 0.92, 0.205), bev=0.025)
    se.cylinder(c, 'nozzle1', METAL, 0.175, 0.92, 1.06, segments=32, axis='Y', bev=0.008)
    se.cylinder(c, 'nozzle2', DARK, 0.14, 1.06, 1.17, segments=32, axis='Y', bev=0.008)
    se.cylinder(c, 'nozzle3', METAL, 0.105, 1.17, 1.265, segments=28, axis='Y', bev=0.006)
    se.cylinder(c, 'bore', BLACK, 0.072, 0.95, 1.268, segments=24, axis='Y')
    # sensor box on top, tilting with the launcher like the large one's
    se.box(c, 'sensor_mount', DARK, (-0.05, 0.10, 0.21), (0.05, 0.22, 0.26))
    se.box(c, 'sensor', COL, (-0.12, 0.04, 0.25), (0.12, 0.30, 0.38), bev=0.02)
    se.box(c, 'sensor_hood', DARK, (-0.125, 0.27, 0.365), (0.125, 0.34, 0.39))
    se.box(c, 'sensor_glass', GLASS, (-0.095, 0.299, 0.27), (0.095, 0.305, 0.355))
    se.dummy(c, 'muzzle_missile_001', (0, 1.265, 0), scale=(0.1, 0.1, 0.1))
    se.dummy(c, 'camera', (0, 0.34, 0.33), scale=(0.1, 0.1, 0.1))


def build_stages():
    """Construction models, all static. Stage 1: frame on the slab; 2: the bearing and the
    housing shell; 3: everything but the panels and the launcher detail."""
    outline = se.chamfer_rect(2 * H, 2 * H, 0.34)
    c = se.collection('GrappleTurretSmall_BS1', 'Small')
    se.prism(c, 'slab', 'Construction', outline, -H, SLAB_TOP, bev=0.02)
    for i, (x, y, _) in enumerate(_ring_points(0.62, 4)):
        se.box(c, 'post%d' % i, 'Construction', (x - 0.05, y - 0.05, SLAB_TOP), (x + 0.05, y + 0.05, -0.62))
    c = se.collection('GrappleTurretSmall_BS2', 'Small')
    se.prism(c, 'slab', 'Construction', outline, -H, SLAB_TOP, bev=0.02)
    se.prism(c, 'skirt', DARK, se.chamfer_rect(2 * H - 0.02, 2 * H - 0.02, 0.33), SLAB_TOP, SKIRT_TOP, top_inset=0.26)
    se.cylinder(c, 'bearing', METAL, 0.62, SKIRT_TOP, PIVOT_Z, segments=48)
    se.prism(c, 'hull_low', 'Construction', se.rounded_rect(HW, HD, HR, cy=HCY), PIVOT_Z + 0.08, PIVOT_Z + LOW_TOP)
    c = se.collection('GrappleTurretSmall_BS3', 'Small')
    se.prism(c, 'slab', COL, outline, -H, SLAB_TOP, bev=0.02)
    se.prism(c, 'skirt', COL, se.chamfer_rect(2 * H - 0.02, 2 * H - 0.02, 0.33), SLAB_TOP, SKIRT_TOP, top_inset=0.26)
    se.cylinder(c, 'bearing', METAL, 0.62, SKIRT_TOP, PIVOT_Z, segments=48)
    se.prism(c, 'hull_low', DARK, se.rounded_rect(HW, HD, HR, cy=HCY), PIVOT_Z + 0.08, PIVOT_Z + LOW_TOP)
    se.prism(c, 'cheeks', 'Construction', u_outline(), PIVOT_Z + LOW_TOP, PIVOT_Z + TOP - 0.12)
    se.box(c, 'barrel', 'Construction', (-0.2, -0.2, PIVOT_Z + 0.30), (0.2, 1.1, PIVOT_Z + 0.62))
    for coll in ('GrappleTurretSmall_BS1', 'GrappleTurretSmall_BS2', 'GrappleTurretSmall_BS3'):
        se.dummy(bpy.data.collections[coll], 'detector_conveyor_large_001', (0, 0, -1.2535), scale=(1.325, 1.325, 0.16))


def main(argv):
    outdir = argv[0]
    elevation = float(argv[1]) if len(argv) > 1 else 12.0
    se.reset()
    base(se.collection('GrappleTurretSmall', 'Small'))
    housing(se.collection('GrappleTurretSmall_Base', 'Small'))
    launcher(se.collection('GrappleTurretSmall_Barrels', 'Small'))
    build_stages()
    se.export(outdir)

    # previews: the block assembled, at a raised elevation, from four sides
    m_base = se.dummy_matrix('GrappleTurretSmall', 'subpart_MissileTurretBase1')
    m_bar = m_base @ se.dummy_matrix('GrappleTurretSmall_Base', 'subpart_MissileTurretBarrels') @ Matrix.Rotation(math.radians(elevation), 4, 'X')
    se.instance('GrappleTurretSmall', Matrix.Identity(4))
    se.instance('GrappleTurretSmall_Base', m_base)
    se.instance('GrappleTurretSmall_Barrels', m_bar)
    cube = ((-H, -H, -H), (H, H, H))
    for name, yaw, pitch in (('front_left', 35, 22), ('back_right', 215, 30), ('side', 90, 0), ('front', 0, 0)):
        se.render(os.path.join(outdir, 'preview_%s.png' % name), {'_preview'}, yaw, pitch, frame=cube)
    # the block icon: square, transparent, framed on the visible block rather than the cube
    se.render(os.path.join(outdir, 'icon_GrappleTurretSmall.png'), {'_preview'}, 38, 24, size=256, transparent=True, margin=1.04)
    for i in (1, 2, 3):
        se.render(os.path.join(outdir, 'preview_bs%d.png' % i), {'GrappleTurretSmall_BS%d' % i}, 35, 22, size=500, frame=cube)


if __name__ == '__main__':
    main(sys.argv[sys.argv.index('--') + 1:])
