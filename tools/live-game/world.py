#!/usr/bin/env python3
"""Builds the GrappleHook live-test world from an existing save (tools/live-game/README.md).

  world.py build <source world dir> <dest world dir>   copy planet and player, drop every grid,
                                                       load GrappleHook only, add the test rig

The source is only read. The test world loads the local GrappleHook mod alone, with spectator on,
autosave off, and one static rig in front of the player carrying every grapple block, so a world
load runs each block's Init, its NetSync setup and the terminal registration.
"""
import os
import random
import re
import shutil
import sys

GRID = re.compile(r'<MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_CubeGrid">(.*?)</MyObjectBuilder_EntityBase>\s*', re.S)
CHARACTER = re.compile(r'xsi:type="MyObjectBuilder_Character">.*?<PositionAndOrientation>(.*?)</PositionAndOrientation>', re.S)
# The local mod folder the world loads: GrappleHook (this repository's deploy) by default, or
# LT.3280424480 (lt's link to the published Workshop copy) when lt is run with LT_MOD=workshop.
MOD = os.environ.get('LT_MOD_FOLDER', 'GrappleHook')  # empty: no GrappleHook (LT_MOD=none)
MODS = '''<Mods>
    <ModItem FriendlyName="GrappleHook">
      <Name>%s</Name>
      <PublishedServiceName>Steam</PublishedServiceName>
    </ModItem>
  </Mods>''' % MOD

# (xsi type, subtype, grid position, extra builder XML). Every grapple block sits on the armour floor,
# mounted by its Bottom face as the definitions require, and faces the rig's forward (-Z), where a
# wall six blocks out gives the hook something to hit. The battery starts charged so the turret works.
# A vanilla gatling turret (3x3x3) sits beside them: the mod rewires the Shoot actions of the turret
# class it registers against, and the probe checks that a turret that is not a grapple still obeys them.
RIG = [('MyObjectBuilder_CubeBlock', 'LargeBlockArmorBlock', (x, 0, z), '') for x in range(-1, 9) for z in range(-6, 1)]
RIG += [('MyObjectBuilder_CubeBlock', 'LargeBlockArmorBlock', (x, y, -6), '') for x in range(-1, 5) for y in range(1, 4)]
RIG += [
    ('MyObjectBuilder_LargeMissileTurret', 'GrappleHookTurretLarge', (0, 1, 0), ''),
    ('MyObjectBuilder_SmallMissileLauncher', 'GrappleHookFixedLarge', (2, 1, 0), ''),
    ('MyObjectBuilder_BatteryBlock', 'LargeBlockBatteryBlock', (4, 1, 0),
     '\n          <CurrentStoredPower>3</CurrentStoredPower>\n          <ProducerEnabled>true</ProducerEnabled>'),
    ('MyObjectBuilder_LargeGatlingTurret', '', (6, 1, -3), ''),
    ('MyObjectBuilder_Cockpit', 'LargeBlockCockpitSeat', (8, 1, 0), lambda: seat_toolbar()),
]

# The small grid rig: its own static grid, sitting on the large rig's floor one cell right of the
# large turret's firing line and three cells out, aimed at the same wall. The small turret is
# 5x5x5 small blocks, so its 2.5 m cube fills large cell SMALL_CELL exactly. The battery touches
# the turret's side mount point: blocks that do not connect are split into separate grids. The
# small fixed launcher lies on the battery.
SMALL_CELL = (1, 1, -3)
SMALL_RIG = [
    ('MyObjectBuilder_LargeMissileTurret', 'GrappleHookTurretSmall', (0, 0, 0), ''),
    ('MyObjectBuilder_BatteryBlock', 'SmallBlockBatteryBlock', (5, 0, 1),
     '\n          <CurrentStoredPower>1</CurrentStoredPower>\n          <ProducerEnabled>true</ProducerEnabled>'),
    # the small fixed launcher (1x1x4) lies on the battery, aimed at the wall beside the turret
    ('MyObjectBuilder_SmallMissileLauncher', 'GrappleHookFixedSmall', (6, 2, 0), ''),
]

# Minutes to move the world's clock on (LT_SUN_SHIFT): the sun turns with ElapsedGameTime, so half
# a day (SunRotationIntervalMinutes / 2) swaps night for day at the rig, for screenshots.
SUN_SHIFT = float(os.environ.get('LT_SUN_SHIFT', '0'))

# The open cockpit seat on the rig carries the grapple actions on its toolbar: slots 1-4 work the
# large turret, 5-8 the large fixed launcher (Detach, Tighten, Loosen, Reset), and 9 selects the
# large fixed launcher as a weapon group. A character's own
# toolbar cannot hold block actions - the game drops them - so sit in the seat to use them.
ACTIONS = ['Detach', 'TightenWinch', 'LoosenWinch', 'ResetWinch']
ENTITY = {}  # (grid name, subtype) -> entity id, filled as the rig is built


def seat_toolbar():
    slots = ''
    targets = [ENTITY[('GrappleRig', 'GrappleHookTurretLarge')], ENTITY[('GrappleRig', 'GrappleHookFixedLarge')]]
    for g, block in enumerate(targets):
        for i, action in enumerate(ACTIONS):
            slots += ('\n              <Slot>\n                <Index>%d</Index>\n                <Item />'
                      '\n                <Data xsi:type="MyObjectBuilder_ToolbarItemTerminalBlock">'
                      '\n                  <Action>%s</Action>\n                  <BlockEntityId>%d</BlockEntityId>'
                      '\n                </Data>\n              </Slot>') % (4 * g + i, action, block)
    # slot 9: the large fixed launcher as a weapon group - select it, then left click hooks and
    # right click lets go
    slots += ('\n              <Slot>\n                <Index>8</Index>\n                <Item />'
              '\n                <Data xsi:type="MyObjectBuilder_ToolbarItemWeapon">'
              '\n                  <DefinitionId Type="MyObjectBuilder_SmallMissileLauncher" Subtype="GrappleHookFixedLarge" />'
              '\n                </Data>\n              </Slot>')
    return ('\n          <Toolbar>\n            <ToolbarType>Character</ToolbarType>'
            '\n            <SelectedSlot xsi:nil="true" />\n            <Slots>%s\n            </Slots>'
            '\n          </Toolbar>') % slots

# More local mods to load beside GrappleHook, comma separated: lt sets GrappleProbe for LT_PROBE=1.
EXTRA_MODS = [m for m in os.environ.get('LT_EXTRA_MODS', '').split(',') if m]


def read(path):
    return open(path, encoding='utf-8-sig').read()


def setting(text, key, value):
    return re.sub(r'<%s>[^<]*</%s>' % (key, key), '<%s>%s</%s>' % (key, value, key), text)


def vector(frame, name):
    m = re.search(r'<%s x="([^"]+)" y="([^"]+)" z="([^"]+)"' % name, frame)
    return tuple(float(v) for v in m.groups())


def grid(frame, at, name, size, blocks, ids):
    """One static grid at world position at, oriented like frame."""
    frame = re.sub(r'<Position x="[^"]+" y="[^"]+" z="[^"]+" />', '<Position x="%r" y="%r" z="%r" />' % at, frame)
    numbered = []
    for t, s, p, extra in blocks:
        entity = ids.getrandbits(62)
        ENTITY[(name, s)] = entity
        numbered.append((t, s, entity, p, extra))
    blocks = ''.join(
        '\n        <MyObjectBuilder_CubeBlock xsi:type="%s">'
        '\n          <SubtypeName>%s</SubtypeName>'
        '\n          <EntityId>%d</EntityId>'
        '\n          <Min x="%d" y="%d" z="%d" />%s'
        '\n        </MyObjectBuilder_CubeBlock>' % ((t, s, entity) + p + (extra() if callable(extra) else extra,))
        for t, s, entity, p, extra in numbered)
    return ('<MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_CubeGrid">'
            '\n      <SubtypeName />'
            '\n      <EntityId>%d</EntityId>'
            '\n      <PersistentFlags>CastShadows InScene</PersistentFlags>'
            '\n      <PositionAndOrientation>%s</PositionAndOrientation>'
            '\n      <GridSizeEnum>%s</GridSizeEnum>'
            '\n      <CubeBlocks>%s\n      </CubeBlocks>'
            '\n      <DisplayName>%s</DisplayName>'
            '\n      <IsStatic>true</IsStatic>'
            '\n    </MyObjectBuilder_EntityBase>\n    ') % (ids.getrandbits(62), frame, size, blocks, name)


def rig(sector):
    """The large rig 15 m ahead of and 3 m above the player, in the player's own frame, and the
    small rig on its floor."""
    m = CHARACTER.search(sector)
    if m is None:
        sys.exit('the source world has no character to place the rig beside')
    frame = m.group(1)
    pos, fwd, up = vector(frame, 'Position'), vector(frame, 'Forward'), vector(frame, 'Up')
    right = (fwd[1] * up[2] - fwd[2] * up[1], fwd[2] * up[0] - fwd[0] * up[2], fwd[0] * up[1] - fwd[1] * up[0])
    at = tuple(p + 15 * f + 3 * u for p, f, u in zip(pos, fwd, up))
    ids = random.Random(32144)
    large = grid(frame, at, 'GrappleRig', 'Large', RIG, ids)
    # small grid origin: the cell's centre less two small blocks (the turret's centre is block 2)
    # on each axis; grid +X is right, +Y up, +Z back
    local = tuple(2.5 * c - 1.0 for c in SMALL_CELL)
    small_at = tuple(a + local[0] * r + local[1] * u - local[2] * f for a, r, u, f in zip(at, right, up, fwd))
    small = grid(frame, small_at, 'GrappleRigSmall', 'Small', SMALL_RIG, ids)
    return large + small


def build(src, dst, name):
    if os.path.exists(dst):
        shutil.rmtree(dst)
    os.makedirs(dst)
    for f in os.listdir(src):
        if f.endswith('.vx2') or f == 'thumb.jpg':
            shutil.copy2(os.path.join(src, f), dst)
    sector = read(os.path.join(src, 'SANDBOX_0_0_0_.sbs'))
    dropped = len(GRID.findall(sector))
    sector = GRID.sub('', sector)
    sector = sector.replace('<MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_Character">',
                            rig(sector) + '<MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_Character">', 1)
    open(os.path.join(dst, 'SANDBOX_0_0_0_.sbs'), 'w', encoding='utf-8').write(sector)
    for f in ('Sandbox.sbc', 'Sandbox_config.sbc'):
        text = read(os.path.join(src, f))
        mods = (MODS if MOD else '<Mods>\n  </Mods>').replace('  </Mods>', ''.join(
            '    <ModItem FriendlyName="%s">\n      <Name>%s</Name>\n      <PublishedServiceName>Steam</PublishedServiceName>\n    </ModItem>\n' % (m, m)
            for m in EXTRA_MODS) + '  </Mods>')
        text = re.sub(r'<Mods>.*?</Mods>', mods, text, count=1, flags=re.S)
        text = setting(text, 'SessionName', name)
        text = setting(text, 'EnableSpectator', 'true')
        text = setting(text, 'AutoSaveInMinutes', '0')
        if SUN_SHIFT:
            m = re.search(r'<ElapsedGameTime>(\d+)</ElapsedGameTime>', text)
            if m:  # TimeSpan ticks, 10 million a second
                text = setting(text, 'ElapsedGameTime', str(int(m.group(1)) + int(SUN_SHIFT * 60 * 10 ** 7)))
        open(os.path.join(dst, f), 'w', encoding='utf-8').write(text)
    print('built %s: %d grids dropped, GrappleRig added with %d blocks, GrappleRigSmall with %d, mods %s' % (dst, dropped, len(RIG), len(SMALL_RIG), ','.join([MOD] + EXTRA_MODS)))


def main():
    if len(sys.argv) >= 4 and sys.argv[1] == 'build':
        build(sys.argv[2], sys.argv[3], os.path.basename(sys.argv[3].rstrip('/')))
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main()
