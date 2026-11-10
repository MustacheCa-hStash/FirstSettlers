"""Build the runtime beech from the approved Blender master.

Run with CPython (numpy, scipy, Pillow and numba) and pass the Blender executable.
The first Blender process extracts the source mesh, then the second exports the LOD.
"""

import argparse
import json
import math
from pathlib import Path
import subprocess
import sys

import numpy as np


ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "Tools"))
SOURCE = ROOT / "ArtSource/HeroAmericanBeech"
NAME = "HeroAmericanBeechLOD0"
ATLAS_SIZE = 4096
TILE = 192
PADDING = 8
FANS_PER_BRANCH = 6
SOURCE_CUTOFF = .32
CONTACT_RADIUS = .14
CONTACT_SHADE_STRENGTH = .74


def extract(work):
    import bpy
    from BuildHeroBeech import Beech

    class BranchLayout(Beech):
        def __init__(self, rows):
            super().__init__(rows)
            self.count = 0
            self.branches = []
            self.tubes = []
            self.used_lateral = False

        def wood_tube(self, points, radii, sides):
            if sides == 10:
                self.branches.append(self.count)
                self.used_lateral = False
            keep_lateral = sides == 6 and not self.used_lateral and radii[0] > .012
            if sides >= 8 or keep_lateral:
                rings, width = {17: (range(0, 27, 2), 13), 8: ((0, 1, 2), 5),
                                10: ((0, 1, 3, 5), 4), 6: ((0, 2), 4)}[sides]
                self.tubes.append((self.rng.getstate(), [tuple(points[i]) for i in rings],
                                   [radii[i] for i in rings], width))
                self.used_lateral |= keep_lateral
            self.rng.uniform(0, math.tau)
            self.rng.random()
            self.rng.random()

        def leaf(self, anchor, axis, crown_normal, length):
            # Consume the same random values as Beech.leaf. The actual leaf geometry
            # and its baked sky visibility are read from the saved master below.
            for _ in range(5):
                self.rng.random()
            self.rng.randrange(len(self.rows))
            self.count += 1

    layout = BranchLayout(json.loads((SOURCE / "Maps/LeafRows.json").read_text()))
    layout.crown()
    layout.branches.append(layout.count)
    extract_master(work, layout.count, layout.branches, rows_per_leaf=8)

    from mathutils import Vector
    for state, points, radii, width in layout.tubes:
        layout.rng.setstate(state)
        Beech.wood_tube(layout, list(map(Vector, points)), radii, width)
    write_bark(work, layout.wood)


def extract_master(work, leaf_count, branches, rows_per_leaf):
    import bpy

    vertices_per_leaf = rows_per_leaf * 3
    quads_per_leaf = (rows_per_leaf - 1) * 2
    mesh = bpy.data.objects["Foliage"].data
    if leaf_count * vertices_per_leaf != len(mesh.vertices) or len(mesh.polygons) != leaf_count * quads_per_leaf:
        raise ValueError("The master no longer matches the authored branch layout.")
    xyz = np.empty(len(mesh.vertices) * 3, np.float32)
    mesh.vertices.foreach_get("co", xyz)
    source_normals = np.empty_like(xyz)
    mesh.vertices.foreach_get("normal", source_normals)
    uv = np.empty(len(mesh.loops) * 2, np.float32)
    mesh.uv_layers[0].data.foreach_get("uv", uv)
    packed = np.empty_like(uv)
    mesh.uv_layers[1].data.foreach_get("uv", packed)
    color = np.empty(len(mesh.vertices) * 4, np.float32)
    mesh.color_attributes["LeafSkyVisibility"].data.foreach_get("color", color)

    # Every source leaf uses the same grid and loop order.
    polygons = np.empty(len(mesh.loops), np.int32)
    mesh.loops.foreach_get("vertex_index", polygons)
    faces = polygons.reshape(-1, quads_per_leaf, 4)
    if not np.all(faces - (np.arange(leaf_count) * vertices_per_leaf)[:, None, None] == faces[0]):
        raise ValueError("The master leaf grids have changed.")
    np.savez(work / "Master.npz", xyz=xyz.reshape(-1, vertices_per_leaf, 3),
             normals=source_normals.reshape(-1, vertices_per_leaf, 3),
             uv=uv.reshape(-1, quads_per_leaf, 4, 2), faces=faces[0],
             packed=packed.reshape(-1, quads_per_leaf * 4, 2)[:, 0, :],
             sky=color.reshape(-1, vertices_per_leaf, 4)[:, 0, 3], branches=branches)


def write_bark(work, wood):
    (work / "Bark.json").write_text(json.dumps({
        "vertices": list(map(tuple, wood.vertices)), "faces": wood.faces, "uv": wood.uv
    }))


def unit(vector):
    return vector / np.linalg.norm(vector)


def divide_fans(indices, centers, fans_per_branch=FANS_PER_BRANCH):
    groups = [indices]
    for _ in range(fans_per_branch - 1):
        index = max(range(len(groups)), key=lambda i: len(groups[i]) * centers[groups[i]].var(axis=0).sum())
        group = groups.pop(index)
        points = centers[group]
        _, axes = np.linalg.eigh(np.cov(points.T))
        order = np.argsort(points @ axes[:, -1], kind="stable")
        groups.extend(np.array_split(group[order], 2))
    return groups


def bake(work, source=SOURCE, fans_per_branch=FANS_PER_BRANCH, coverage_weighted_surface=False,
         leaf_texture="LeafAlbedo.png", contact_radius=CONTACT_RADIUS,
         tile_size=TILE, crossed_sprays=False):
    from numba import njit
    from PIL import Image
    from scipy.ndimage import distance_transform_edt

    @njit
    def raster(vertices, texcoords, normals, source, visibility, size, triangles_per_leaf):
        pixels = np.zeros((size, size, 4), np.uint8)
        surface = np.zeros_like(pixels)
        depth = np.full((size, size), -1e9, np.float32)
        leaf_id = np.full((size, size), -1, np.int32)
        height, width, _ = source.shape
        for i in range(len(vertices)):
            (ax, ay, az), (bx, by, bz), (cx, cy, cz) = vertices[i]
            det = (bx-ax)*(cy-ay) - (by-ay)*(cx-ax)
            if abs(det) < 1e-7:
                continue
            for y in range(max(0, math.floor(min(ay, by, cy))), min(size, math.ceil(max(ay, by, cy)))):
                for x in range(max(0, math.floor(min(ax, bx, cx))), min(size, math.ceil(max(ax, bx, cx)))):
                    b = ((x+.5-ax)*(cy-ay) - (y+.5-ay)*(cx-ax)) / det
                    c = ((bx-ax)*(y+.5-ay) - (by-ay)*(x+.5-ax)) / det
                    a = 1-b-c
                    if min(a, b, c) < 0:
                        continue
                    z = a*az + b*bz + c*cz
                    if z <= depth[y, x]:
                        continue
                    uv = texcoords[i]
                    u = min(width-1, max(0, int((a*uv[0, 0]+b*uv[1, 0]+c*uv[2, 0])*width)))
                    v = min(height-1, max(0, int((1-a*uv[0, 1]-b*uv[1, 1]-c*uv[2, 1])*height)))
                    if source[v, u, 3] >= SOURCE_CUTOFF * 255:
                        depth[y, x] = z
                        leaf_id[y, x] = i//triangles_per_leaf
                        for channel in range(3):
                            pixels[y, x, channel] = np.uint8(source[v, u, channel] * visibility[i//triangles_per_leaf])
                        pixels[y, x, 3] = 255
                        n0, n1, n2 = normals[i]
                        nx = a*n0[0] + b*n1[0] + c*n2[0]
                        ny = a*n0[1] + b*n1[1] + c*n2[1]
                        nz = a*n0[2] + b*n1[2] + c*n2[2]
                        length = max(1e-6, math.sqrt(nx*nx + ny*ny + nz*nz))
                        surface[y, x, 0] = round((.5 + .5*nx/length)*255)
                        surface[y, x, 1] = round((.5 + .5*ny/length)*255)
                        surface[y, x, 2] = 255
                        surface[y, x, 3] = 255
        return pixels, surface, depth, leaf_id

    @njit
    def shade_leaf_contacts(pixels, surface, depth, leaf_id, offsets, distances):
        # Estimate a short-range ambient horizon in all directions around each
        # visible source leaf texel. Preserve blade detail and ignore leaves
        # that only overlap in projection but are far away in depth.
        height, width = depth.shape
        for y in range(height):
            for x in range(width):
                leaf = leaf_id[y, x]
                if leaf < 0:
                    continue
                horizon_sum = 0.
                for direction in range(len(offsets)):
                    horizon = 0.
                    for step in range(offsets.shape[1]):
                        dx, dy = offsets[direction, step]
                        sx, sy = x+dx, y+dy
                        if not (0 <= sx < width and 0 <= sy < height):
                            continue
                        other = leaf_id[sy, sx]
                        if other < 0 or other == leaf:
                            continue
                        rise = depth[sy, sx] - depth[y, x]
                        if rise <= .004:
                            continue
                        separation = math.sqrt(rise*rise + distances[direction, step]**2)
                        falloff = max(0., 1. - (separation/contact_radius)**2)
                        horizon = max(horizon, (rise-.004)/separation * falloff)
                    horizon_sum += horizon
                occlusion = horizon_sum / len(offsets)
                surface[y, x, 2] = round((1. - occlusion)*255)
                gain = 1. - CONTACT_SHADE_STRENGTH * occlusion
                for channel in range(3):
                    pixels[y, x, channel] = round(pixels[y, x, channel] * gain)

    data = dict(np.load(work / "Master.npz"))
    tex = np.asarray(Image.open(source / "Maps" / leaf_texture).convert("RGBA"))
    atlas = Image.new("RGBA", (ATLAS_SIZE, ATLAS_SIZE))
    base_atlas = Image.new("RGBA", atlas.size)
    surface_atlas = Image.new("RGB", atlas.size, (128,128,255))
    centers = data["xyz"].mean(axis=1)
    cards, normals, sky, texcoords = [], [], [], []
    triangular = data["faces"].shape[1] == 3
    corner_order = [0, 1, 2] if triangular else [0, 1, 2, 0, 2, 3]
    faces = data["faces"][:, corner_order].reshape(-1, 3)
    vertices_per_leaf = data["xyz"].shape[1]
    triangles_per_leaf = len(faces)
    atlas_columns = ATLAS_SIZE // tile_size
    angles = np.arange(12)*math.tau/12
    directions = np.column_stack((np.cos(angles), np.sin(angles)))
    taps = directions[:, None, :] * np.array([.014, .025, .041, .061, .087, .114])[None, :, None]
    taps *= contact_radius / CONTACT_RADIUS
    for branch, (start, end) in enumerate(zip(data["branches"][:-1], data["branches"][1:])):
        fan_count = data["fan_counts"][branch] if "fan_counts" in data else fans_per_branch
        for indices in divide_fans(np.arange(start, end), centers, fan_count):
            cloud = data["xyz"][indices].reshape(-1, 3)
            local_centers = centers[indices]
            center = local_centers.mean(axis=0)
            radial = unit(np.array([center[0]+.001, center[1], 0]))
            tangent = np.array([-radial[1], radial[0], 0])
            up = np.array([0, 0, 1])
            _, axes = np.linalg.eigh(np.cov((cloud-center).T))
            fan_normal = axes[:, 0]
            if fan_normal[2] < 0:
                fan_normal = -fan_normal
            # One view follows the leaf spray, the other closes its side profile.
            views = (unit(up*.66 + fan_normal*.34 + radial*.30),
                     unit(radial*.86 + tangent*.51 + up*.25))
            if crossed_sprays:
                # Retain fine foliage in both side profiles, even when the camera
                # looks along one of the two thin planes.
                views = (unit(up*.68 + fan_normal*.57 + radial*.16),
                         unit(radial*.89 + tangent*.43 + up*.14),
                         unit(tangent*.91 - radial*.36 + up*.25))
            for view in views:
                across = unit(np.cross(view, tangent if abs(view[2]) > .7 else up))
                vertical = np.cross(view, across)
                frame = np.column_stack((across, vertical, view))
                projected = (cloud-center) @ frame
                lower = projected[:, :2].min(axis=0) - .015
                upper = projected[:, :2].max(axis=0) + .015
                coords = projected.reshape(-1, vertices_per_leaf, 3)[:, faces, :].reshape(-1, 3, 3)
                coords[:, :, :2] = (coords[:, :, :2]-lower)/(upper-lower)
                extent = tile_size - 2*PADDING
                coords[:, :, 0] = 2*(PADDING + extent*coords[:, :, 0])
                coords[:, :, 1] = 2*(PADDING + extent*(1-coords[:, :, 1]))
                if "vertex_uv" in data:
                    uv = data["vertex_uv"][indices][:, faces, :].reshape(-1, 3, 2)
                else:
                    uv = data["uv"][indices][:, :, corner_order, :].reshape(-1, 3, 2)
                # Align each source surface to the front of the card. XY fit
                # in two channels because Unity can reconstruct the positive Z.
                triangle_normals = (data["normals"][indices][:, faces, :].reshape(-1,3,3) @ frame)
                triangle_normals[triangle_normals[:,:,2].mean(axis=1) < 0] *= -1
                # A weak, direction-independent value variation carries leaf
                # layering inside each card without baking sunlight.
                rgba, surface, depth, leaf_id = raster(
                    coords, uv, triangle_normals, tex, .84 + .15*data["sky"][indices], tile_size*2, triangles_per_leaf)
                base_tile = np.array(Image.fromarray(rgba).resize((tile_size, tile_size), Image.Resampling.BOX))
                texel_size = (upper-lower)/(2*extent)
                offsets = np.rint(taps/texel_size).astype(np.int32)
                distances = np.linalg.norm(offsets*texel_size, axis=2)
                shade_leaf_contacts(rgba, surface, depth, leaf_id, offsets, distances)
                tile = np.array(Image.fromarray(rgba).resize((tile_size, tile_size), Image.Resampling.BOX))
                surface_tile = np.asarray(Image.fromarray(surface).resize((tile_size, tile_size), Image.Resampling.BOX))
                if coverage_weighted_surface:
                    # Pillow resamples RGBA as color with transparency. Treat packed surface
                    # channels as linear data and average only covered supersamples instead.
                    covered = surface[:, :, 3].reshape(tile_size, 2, tile_size, 2) != 0
                    packed_sum = surface[:, :, :3].reshape(tile_size, 2, tile_size, 2, 3).sum(axis=(1, 3))
                    surface_tile = np.uint8(np.rint(packed_sum / np.maximum(1, covered.sum(axis=(1, 3)))[:, :, None]))
                solid = tile[:, :, 3] >= 128
                if not np.any(solid):
                    raise ValueError("Empty foliage card")
                nearest = distance_transform_edt(~solid, return_distances=False, return_indices=True)
                tile[:, :, :3] = tile[nearest[0], nearest[1], :3]
                number = len(cards)
                tx, ty = (number % atlas_columns)*tile_size, (number // atlas_columns)*tile_size
                if ty + tile_size > ATLAS_SIZE:
                    raise ValueError("Leaf atlas exceeded its allocated size")
                atlas.paste(Image.fromarray(tile), (tx, ty))
                base_tile[:, :, :3] = base_tile[nearest[0], nearest[1], :3]
                base_atlas.paste(Image.fromarray(base_tile), (tx, ty))
                surface_atlas.paste(Image.fromarray(surface_tile[nearest[0], nearest[1], :3]), (tx, ty))

                # Three contour rows reduce empty fragment work while preserving
                # the actual projected alpha silhouette. The middle row bends.
                rows, cols = np.where(tile[:, :, 3] >= 32)
                ys = [int(rows.min()), int((rows.min()+rows.max())*.5), int(rows.max())+1]
                local_xy = (local_centers-center) @ frame
                vertices, card_uv, card_normals, card_sky = [], [], [], []
                middle_left, middle_right = int(cols.min())-2, int(cols.max())+3
                for row, y in enumerate(ys):
                    left, right = middle_left, middle_right
                    if row != 1:
                        t = (rows + .5 - y) / (ys[1] - y)
                        between = (t >= 0) & (t < 1)
                        # Solve the sloped card edge against occupied texels.
                        # The middle row covers the full width of the spray.
                        x, t = cols[between], t[between]
                        left = math.floor(np.min((x - 1 - t*middle_left)/(1-t)))
                        right = math.ceil(np.max((x + 2 - t*middle_right)/(1-t)))
                    columns = (left, right) if crossed_sprays else (left, (left+right)*.5, right)
                    for x in columns:
                        u, v = (x-PADDING)/extent, 1-(y-PADDING)/extent
                        point = lower + (upper-lower)*np.array([u, v])
                        distance = np.sum((local_xy[:, :2]-point)**2, axis=1)
                        neighbors = np.argpartition(distance, min(11, len(distance)-1))[:12]
                        weights = np.exp(-distance[neighbors]/.10)+1e-8
                        sample = indices[neighbors]
                        bend = np.clip(np.average(local_xy[neighbors, 2], weights=weights)*.58, -.17, .17)
                        vertices.append(center + frame @ np.array([point[0], point[1], bend]))
                        card_uv.append([(tx+x)/ATLAS_SIZE, 1-(ty+y)/ATLAS_SIZE])
                        card_normals.append(np.average(data["packed"][sample], weights=weights, axis=0))
                        card_sky.append(np.average(data["sky"][sample], weights=weights))
                cards.append(vertices)
                texcoords.append(card_uv)
                normals.append(card_normals)
                sky.append(card_sky)
    atlas.save(work / "CrownAlbedo.png")
    base_atlas.save(work / "CrownAlbedoBase.png")
    surface_atlas.save(work / "CrownSurface.png")
    np.savez(work / "Cards.npz", vertices=cards, uv=texcoords, packed=normals, sky=sky)


def export(work, output, source=SOURCE, name=NAME, species="American beech", albedo="CrownAlbedo.png"):
    import bpy
    from BuildWoodlandTrees import Mesh, material

    bpy.ops.wm.read_factory_settings(use_empty=True)
    data = dict(np.load(work / "Cards.npz"))
    leaves, wood = Mesh(), Mesh()
    woody = json.loads((work / "Bark.json").read_text())
    wood.vertices, wood.faces, wood.uv = woody["vertices"], woody["faces"], woody["uv"]
    leaves.vertices = data["vertices"].reshape(-1, 3).tolist()
    for index, uv in enumerate(data["uv"]):
        stride = len(uv) // 3
        for row in range(2):
            for column in range(stride - 1):
                a = row * stride + column
                order = (a + stride, a + stride + 1, a + 1, a)
                leaves.add(tuple(len(uv)*index+j for j in order), tuple(uv[j] for j in order))
    bark_mat = material(f"{species} bark", source / "Maps/BarkAlbedo.png",
                        normal=source / "Maps/BarkNormal.png")
    leaf_mat = material(f"Baked {species} leaf sprays", work / albedo, foliage=True)
    bark, bark_triangles = wood.finish("Bark", bark_mat)
    crown, leaf_triangles = leaves.finish("Foliage", leaf_mat, data["packed"].reshape(-1, 2))
    color = np.ones((len(leaves.vertices), 4), dtype=np.float32)
    color[:, 3] = data["sky"].flatten()
    crown.data.color_attributes.new(name="LeafSkyVisibility", type="FLOAT_COLOR",
                                    domain="POINT").data.foreach_set("color", color.flatten())
    bpy.context.view_layer.objects.active = bark
    for part in (bark, crown):
        part.select_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output / (name+".blend")), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(work / (name+".fbx")), use_selection=True,
                             object_types={"MESH"}, axis_forward="-Z", axis_up="Y", bake_anim=False,
                             path_mode="STRIP", apply_scale_options="FBX_SCALE_UNITS")
    stats = {"cards": len(data["vertices"]), "leafTriangles": leaf_triangles,
             "barkTriangles": bark_triangles, "atlasResolution": ATLAS_SIZE,
             "skyVisibilityMinMeanMax": [round(float(value), 4) for value in
                                        (color[:, 3].min(), color[:, 3].mean(), color[:, 3].max())]}
    (output / "Geometry.json").write_text(json.dumps(stats, indent=2)+"\n")
    print(name, stats, flush=True)


def main():
    if "--" in sys.argv:
        mode, work, output = sys.argv[sys.argv.index("--")+1:]
        if mode == "extract":
            extract(Path(work))
        else:
            export(Path(work), Path(output))
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("blender", type=Path)
    parser.add_argument("--reuse-extract", action="store_true")
    parser.add_argument("--textures-only", action="store_true")
    args = parser.parse_args()
    work = ROOT / ".utmp/hero-beech-lod0"
    output = ROOT / "ArtSource" / NAME
    for path in (work, output):
        path.mkdir(parents=True, exist_ok=True)
    command = [str(args.blender), "-b", "--python-exit-code", "1"]
    script = ["--python", str(Path(__file__).resolve())]
    if not args.reuse_extract:
        subprocess.run(command + [str(SOURCE / "HeroAmericanBeech.blend")] + script +
                       ["--", "extract", str(work), str(output)], check=True)
    bake(work)
    if not args.textures_only:
        subprocess.run(command + ["--factory-startup"] + script +
                       ["--", "export", str(work), str(output)], check=True)


if __name__ == "__main__":
    main()
