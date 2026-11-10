"""Build a mature sugar maple with a tall bole and a broad crown."""

import math
from pathlib import Path
import random
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from BuildHeroRedMaple import RedMaple, build_master
from BuildHeroWhiteOak import smooth_path
from BuildWoodlandTrees import UP, horizontal


class SugarMaple(RedMaple):
    def __init__(self, rows):
        super().__init__(rows)
        self.rng = random.Random(873119)

    def leaf(self, anchor, axis, crown_normal, length, width_ratio=(.83, 1.08), roll=1.07):
        # Sugar maple's broad blade and longer petiole occupy more of the shoot.
        super().leaf(anchor, axis, crown_normal, length * 1.12,
                     width_ratio=tuple(value * 1.07 for value in width_ratio), roll=roll)

    def crown(self):
        main = self.leader((
            ((0, 0, -.25), .88), ((.03, -.04, .31), .72), ((.08, -.11, .94), .57),
            ((.21, -.17, 2.28), .49), ((.22, -.24, 4.14), .45), ((.11, -.17, 6.45), .41),
            ((-.07, .13, 8.29), .38), ((-.11, .31, 10.34), .34), ((.10, .47, 12.65), .285),
            ((.39, .63, 14.89), .223), ((.73, .56, 17.21), .151),
            ((.94, .41, 19.65), .086), ((.92, .20, 21.92), .027),
        ))
        west = self.leader((
            ((-.07, .13, 8.29), .242), ((-.88, .22, 9.75), .221),
            ((-1.82, .31, 11.65), .184), ((-2.58, .72, 13.70), .137),
            ((-3.03, .90, 15.93), .087), ((-3.10, .82, 18.15), .024),
        ))
        south = self.leader((
            ((-.11, .31, 10.34), .195), ((.43, -.42, 11.74), .163),
            ((1.35, -1.42, 13.55), .119), ((2.36, -2.05, 15.45), .080),
            ((2.76, -2.66, 17.63), .025),
        ))

        for heading, length, radius in ((1, 1.33, .25), (55, 1.08, .21), (126, 1.40, .24),
                                        (185, 1.37, .29), (246, 1.22, .22), (307, 1.56, .26)):
            direction = horizontal(math.radians(heading))
            points = smooth_path([UP * .78, direction * length * .34 + UP * .29,
                                  direction * length * .71 + UP * .035, direction * length - UP * .16], 3)
            self.wood_tube(points, [radius * (1 - .92 * i / 9) ** 1.35 for i in range(10)], 12)

        # Mature limbs spread and sweep up into adjoining portions of the dome.
        # A few lower limbs sit apart, giving the bole an irregular boundary.
        branches = (
            (main[5], 224, 5.12, 3.54, -.60, .206, .84),
            (main[6], 337, 5.54, 2.90, .72, .220, .87),
            (main[6], 86, 5.34, 3.08, -.82, .213, .86),
            (main[7], 31, 5.39, 4.30, .69, .202, .86),
            (main[7], 126, 5.48, 4.05, -.73, .192, .87),
            (west[1], 193, 5.19, 4.50, .60, .185, .85),
            (west[2], 159, 4.42, 4.09, -.70, .156, .88),
            (west[2], 239, 4.36, 3.86, .61, .163, .87),
            (south[1], 281, 5.17, 3.92, -.70, .153, .87),
            (south[2], 246, 4.28, 4.06, .50, .134, .90),
            (south[2], 320, 4.65, 4.31, .58, .139, .88),
            (main[8], 59, 5.02, 4.49, .58, .151, .88),
            (main[8], 5, 5.06, 4.82, -.57, .140, .89),
            (west[3], 109, 3.70, 4.35, .47, .106, .90),
            (west[3], 206, 3.76, 4.25, -.51, .110, .88),
            (main[9], 120, 4.52, 4.51, -.55, .113, .88),
            (main[9], 345, 4.69, 4.74, .55, .115, .90),
            (south[3], 289, 3.82, 4.00, -.55, .084, .92),
            (south[4], 217, 2.63, 3.42, .39, .065, .92),
            (west[4], 164, 2.97, 4.04, -.40, .069, .92),
            (main[10], 55, 3.67, 3.95, .40, .078, .92),
            (main[10], 257, 3.57, 3.87, -.42, .074, .91),
            (main[11], 144, 2.63, 2.64, .29, .047, .91),
            (main[11], 329, 2.52, 2.70, -.34, .046, .93),
        )
        for branch in branches:
            self.scaffold(*branch)
        for bearing, tip in ((.42, main[-1]), (2.86, west[-1]), (4.58, south[-1])):
            self.fan(tip, bearing, 1.08, .67, .018, .89)


if __name__ == "__main__":
    build_master(SugarMaple, "HeroSugarMaple", "Sugar maple")
