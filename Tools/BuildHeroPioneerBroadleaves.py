"""Author Paper Birch and Quaking Aspen high-poly masters with alternate leaves."""

import math
from pathlib import Path
import random
import sys

from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroBeech import Beech
from BuildHeroRedMaple import build_master, point_at
from BuildHeroWhiteOak import smooth_path
from BuildWoodlandTrees import UP, horizontal


GOLDEN_ANGLE = math.pi * (3 - math.sqrt(5))


class PioneerBroadleaf(Beech):
    def leader(self, knots, sides):
        points = [Vector(position) for position, _ in knots]
        path = smooth_path(points, 4)
        radii = [a + (b - a) * i / 4 for (_, a), (_, b) in zip(knots, knots[1:]) for i in range(4)]
        self.wood_tube(path, radii + [knots[-1][1]], sides)
        return path

    def trunk_roots(self, reach, radius):
        for index in range(6):
            direction = horizontal(index * math.tau / 6 + self.rng.uniform(-.26, .26))
            length = reach * self.rng.uniform(.63, 1.12)
            points = smooth_path((UP * .48, direction * length * .32 + UP * .19,
                                  direction * length * .72, direction * length - UP * .12), 3)
            self.wood_tube(points, [radius * (1 - .95 * i / 9) ** 1.34 for i in range(10)], 10)

    def scaffold(self, anchor, heading, reach, rise, sweep, radius, fullness):
        rng = self.rng
        direction = horizontal(heading)
        sideways = horizontal(heading + math.pi / 2)
        shape = ((0, 0), (.19, .32), (.42, .63), (.65, .91), (.84, 1), (1, self.tip_rise))
        path = smooth_path([anchor + direction * (reach * t) + UP * (rise * z)
                            + sideways * (reach * sweep * t * t) for t, z in shape], 4)
        self.wood_tube(path, [max(.003, radius * (1 - .97 * i / 20) ** 1.25)
                             for i in range(21)], 17 if radius >= .12 else 12)
        laterals = max(4, round((8 + reach * 1.42) * fullness))
        for i in range(laterals):
            f = .24 + .73 * (i + rng.uniform(.08, .93)) / laterals
            start = point_at(path, f)
            sign = 1 if i % 2 else -1
            angle = heading + sign * rng.uniform(.46, 1.38) + sweep * f
            length = (.43 + reach * rng.uniform(.19, .32)) * (1 - .32 * f)
            r = max(.003, radius * rng.uniform(.21, .35) * (1 - .67 * f))
            self.spray(start, angle, length, r, fullness)
            if i % 3 != 0:
                self.spray(start, heading - sign * rng.uniform(.50, 1.11),
                           length * rng.uniform(.56, .85), r * .71, fullness * .94)
        self.spray(path[-1], heading + sweep, max(.3, reach * .21), max(.003, radius * .1), fullness)

    def spray(self, anchor, heading, length, radius, fullness):
        rng = self.rng
        forward = horizontal(heading)
        sideways = horizontal(heading + math.pi / 2)
        rise = length * rng.uniform(*self.spray_rise)
        bow = length * rng.uniform(-.22, .20)
        path = [anchor + forward * (length * t) + UP * (rise * t + length * .16 * math.sin(math.pi * t))
                + sideways * bow * t * t for t in (0, .22, .5, .74, 1)]
        self.wood_tube(path, [radius * v for v in (1, .74, .51, .26, .08)], 6)
        center = path[2] + forward * .18
        count = max(4, round((6.7 + length * 1.7) * fullness))
        for i in range(count):
            f = .17 + .82 * (i + rng.uniform(.1, .9)) / count
            start = point_at(path, f)
            sign = -1 if i % 2 else 1
            direction = horizontal(heading + sign * rng.uniform(.47, 1.44))
            size = (.20 + min(.43, length * .34)) * rng.uniform(.65, 1.19) * (1 - .25 * f)
            self.twig(start, direction, size, size * rng.uniform(*self.twig_rise),
                      rng.randrange(10, 15), center)
        self.twig(path[-1], forward, min(.46, length * .48), rise * .21, 9, center)

    def leaf(self, anchor, axis, crown_normal, length):
        super().leaf(anchor, axis, crown_normal, length * self.leaf_size, self.leaf_width, self.leaf_roll)


class PaperBirch(PioneerBroadleaf):
    tip_rise = .86
    spray_rise = (-.63, .42)
    twig_rise = (-1.0, .40)
    leaf_size, leaf_width, leaf_roll = .94, (.62, .83), .99

    def crown(self):
        rng = self.rng = random.Random(227190)
        main = self.leader((
            ((0, 0, -.17), .43), ((.04, -.02, .29), .35), ((.13, -.08, 1.30), .295),
            ((.24, -.09, 3.05), .27), ((.37, -.03, 5.20), .238),
            ((.20, .15, 7.04), .195), ((.14, .36, 9.16), .151),
            ((.36, .60, 11.46), .108), ((.68, .69, 13.60), .072),
            ((.95, .56, 15.53), .045), ((1.22, .35, 17.63), .015),
        ), 29)
        second = self.leader((
            ((.31, -.01, 4.85), .164), ((-.22, -.25, 6.16), .137),
            ((-.97, -.38, 7.86), .111), ((-1.43, -.36, 9.86), .084),
            ((-1.63, -.28, 11.83), .055), ((-1.95, -.18, 14.06), .023),
        ), 19)
        self.trunk_roots(.91, .155)
        for leader, start, end, count, reach, rotation in (
            (main, .38, .992, 20, 4.34, .21),
            (second, .19, .995, 10, 3.08, 2.40),
        ):
            for i in range(count):
                t = (i + rng.uniform(.12, .86)) / count
                anchor = point_at(leader, start + (end - start) * t)
                heading = rotation + i * GOLDEN_ANGLE + rng.uniform(-.43, .43)
                profile = (1 - t) ** .68 * (.64 + .40 * math.sin(math.pi * t ** .65))
                length = (.39 + reach * profile) * rng.uniform(.72, 1.18)
                rise = (.38 + length * .55) * rng.uniform(.69, 1.27)
                radius = max(.015, .112 * (1 - t) ** .82 * rng.uniform(.71, 1.34))
                self.scaffold(anchor, heading, length, rise, rng.uniform(-.27, .29), radius, rng.uniform(.81, 1.16))
        # Short branches under the leading shoot keep the top connected to the crown.
        for t, heading, reach, lift in (
            (.753, 2.28, 2.13, 1.24), (.805, 4.73, 1.78, 1.06),
            (.843, .46, 1.56, .81), (.885, 2.87, 1.41, .73), (.933, 5.35, .96, .55),
        ):
            self.scaffold(point_at(main, t), heading, reach, lift, rng.uniform(-.24, .24), .033, 1.02)


class QuakingAspen(PioneerBroadleaf):
    tip_rise = 1.0
    spray_rise = (-.30, .66)
    twig_rise = (-.46, .88)
    leaf_size, leaf_width, leaf_roll = .99, (.76, .90), 1.40

    def crown(self):
        rng = self.rng = random.Random(710943)
        main = self.leader((
            ((0, 0, -.14), .344), ((-.02, -.02, .36), .29), ((.04, -.03, 1.76), .247),
            ((.10, -.02, 3.68), .225), ((.15, .03, 5.63), .205),
            ((.15, .11, 7.69), .183), ((.19, .19, 9.50), .151),
            ((.32, .23, 11.35), .115), ((.60, .19, 13.22), .083),
            ((.83, .06, 15.12), .050), ((.91, -.11, 17.23), .014),
        ), 26)
        fork = self.leader((
            ((.17, .15, 8.64), .119), ((-.36, .37, 9.92), .104),
            ((-1.00, .57, 11.46), .074), ((-1.26, .59, 13.15), .045),
            ((-1.22, .47, 15.06), .015),
        ), 16)
        self.trunk_roots(.61, .107)
        for height, heading, length in ((3.49, 3.55, .23), (4.83, .63, .30), (5.69, 5.52, .34)):
            p = point_at(main, height / 17.23)
            d = horizontal(heading) * length
            self.wood_tube((p, p + d * .61 + UP * .07, p + d + UP * .14), (.047, .028, .013), 9)
        for leader, start, end, count, reach, rotation in (
            (main, .49, .99, 21, 3.33, 1.41),
            (fork, .16, .992, 9, 2.30, 2.98),
        ):
            for i in range(count):
                t = (i + rng.uniform(.16, .82)) / count
                anchor = point_at(leader, start + (end - start) * t)
                heading = rotation + i * GOLDEN_ANGLE + rng.uniform(-.29, .29)
                profile = math.sin(math.pi * (.25 + .74 * t)) ** .61
                length = (.33 + reach * profile) * rng.uniform(.68, 1.19)
                rise = (.38 + length * .81) * rng.uniform(.75, 1.26)
                radius = max(.013, .095 * (1 - t) ** .9 * rng.uniform(.72, 1.29))
                self.scaffold(anchor, heading, length, rise, rng.uniform(-.21, .21), radius, rng.uniform(.90, 1.18))


if __name__ == "__main__":
    source = Path(sys.argv[sys.argv.index("--") + 1]).name
    species, label = {
        "HeroPaperBirch": (PaperBirch, "Paper birch"),
        "HeroQuakingAspen": (QuakingAspen, "Quaking aspen"),
    }[source]
    build_master(species, source, label)
