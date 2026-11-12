"""Build a high-detail meadow reference and render it against the pilot cards.

Run in Blender background mode with --python. The study uses the checked-in
pilot mesh and atlas under the same studio lighting and terrain as the prototype.
"""

import math
from pathlib import Path
import random
import re
import struct

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "ArtSource/MeadowGrassStudy"
WIDTH, LENGTH = 8, 10
SPACING = 128 * .3 / 144


def terrain_height(x, y):
    return .15*y + .13*x + .30*math.sin(y*.62) + .18*math.cos(x*.91 + y*.23)


def geometry(name, vertices, faces, material, collection):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    mesh.materials.append(material)
    return mesh


def noise_color(material, scale, dark, light):
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    noise = nodes.new("ShaderNodeTexNoise")
    coordinates = nodes.new("ShaderNodeTexCoord")
    links.new(coordinates.outputs["Object"], noise.inputs["Vector"])
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 3
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*dark, 1)
    ramp.color_ramp.elements[1].color = (*light, 1)
    links.new(noise.outputs["Fac"], ramp.inputs[0])
    links.new(ramp.outputs[0], nodes["Principled BSDF"].inputs["Base Color"])
    return nodes["Principled BSDF"]


def build_ground(collection):
    material = bpy.data.materials.new("Meadow soil and short ground cover")
    bsdf = noise_color(material, 12, (.026,.036,.009), (.091,.119,.036))
    bsdf.inputs["Roughness"].default_value = .92
    vertices = [(x*.15, y*.15, terrain_height(x*.15, y*.15))
                for y in range(-12, 90) for x in range(-39, 40)]
    faces = [(i, i+1, i+80, i+79) for i in range(101*79) if i % 79 != 78]
    mesh = geometry("Shared sloping ground", vertices, faces, material, collection)
    mesh.polygons.foreach_set("use_smooth", [True]*len(mesh.polygons))


def placements():
    rng = random.Random(3242)
    for row in range(int(LENGTH / SPACING)):
        for col in range(int(WIDTH / SPACING)):
            x = (col + rng.uniform(.04,.96))*SPACING - WIDTH/2
            y = (row + rng.uniform(.04,.96))*SPACING
            yield x, y, rng.uniform(.9,1.3), rng.uniform(0,math.tau)


def build_pilot(collection):
    asset = ROOT / "Assets/Models/Grass+Flowers/PilotMeadowGrass_Near.asset"
    data = asset.read_text()
    rows = list(struct.iter_unpack("<8f", bytes.fromhex(re.search(r"_typelessdata: ([0-9a-f]+)", data)[1])))
    triangles = list(struct.iter_unpack("<3H", bytes.fromhex(re.search(r"m_IndexBuffer: ([0-9a-f]+)", data)[1])))
    material = bpy.data.materials.new("Pilot meadow atlas cards")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(str(ROOT / "Assets/Textures/Grass/T_PilotMeadowBlades.png"))
    texture.image.pack()
    color = nodes.new("ShaderNodeMixRGB")
    color.blend_type = "MULTIPLY"
    color.inputs[0].default_value = 1
    color.inputs[2].default_value = (.59,.73,.68,1)
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = .82
    links.new(texture.outputs["Color"], color.inputs[1])
    links.new(color.outputs[0], bsdf.inputs["Base Color"])
    # Match the runtime's alpha-cutout silhouette in the studio comparison.
    cutoff = nodes.new("ShaderNodeMath")
    cutoff.operation = "GREATER_THAN"
    runtime_material = (ROOT / "Assets/Materials/M_Grass/M_PilotMeadowBlades.mat").read_text()
    cutoff.inputs[1].default_value = float(re.search(r"_Cutoff: ([\d.]+)", runtime_material)[1])
    links.new(texture.outputs["Alpha"], cutoff.inputs[0])
    links.new(cutoff.outputs[0], bsdf.inputs["Alpha"])
    vertices, faces = [], []
    for x, y, scale, angle in placements():
        c, s = math.cos(angle), math.sin(angle)
        base = len(vertices)
        z = terrain_height(x, y)
        vertices.extend((x + scale*(v[0]*c - v[2]*s), y + scale*(v[0]*s + v[2]*c), z + scale*v[1])
                        for v in rows)
        faces.extend(tuple(base+i for i in tri) for tri in triangles)
    mesh = geometry("Current pilot cards", vertices, faces, material, collection)
    uv = mesh.uv_layers.new()
    uv.data.foreach_set("uv", [value for loop in mesh.loops for value in rows[loop.vertex_index % len(rows)][6:]])


def build_prototype(collection):
    material = bpy.data.materials.new("Meadow blade with natural root and edge variation")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    color = nodes.new("ShaderNodeVertexColor")
    color.layer_name = "Blade color"
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = .70
    bsdf.inputs["Specular IOR Level"].default_value = .22
    links.new(color.outputs[0], bsdf.inputs["Base Color"])
    translucent = nodes.new("ShaderNodeBsdfTranslucent")
    links.new(color.outputs[0], translucent.inputs["Color"])
    mix = nodes.new("ShaderNodeMixShader")
    mix.inputs[0].default_value = .18
    links.new(bsdf.outputs[0], mix.inputs[1])
    links.new(translucent.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], nodes["Material Output"].inputs[0])

    rng = random.Random(85410)
    vertices, faces, colors = [], [], []
    blade_count = 0
    for x, y, scale, _ in placements():
        patch = .5 + .26*math.sin(x*1.12 + y*.43) + .20*math.sin(y*1.63 - x*.32)
        for blade in range(rng.randint(35, 44)):
            blade_count += 1
            is_tall = blade < 4 and rng.random() < .30 + patch*.60
            upright = is_tall and blade == 0
            root_x = x + rng.uniform(-.16,.16)
            root_y = y + rng.uniform(-.16,.16)
            root_z = terrain_height(root_x, root_y) - .012
            heading = rng.uniform(0,math.tau)
            if rng.random() < .38:
                heading = .65 + .55*math.sin(y*.8) + rng.gauss(0,.66)
            forward = (math.cos(heading), math.sin(heading))
            side = (-forward[1], forward[0])
            length = rng.uniform(.36,.61) if is_tall else rng.uniform(.14,.37)
            length *= scale * (.82 + patch*.30)
            width = rng.uniform(.006,.019) if is_tall else rng.uniform(.008,.025)
            lean = length * (rng.uniform(.09,.29) if upright else rng.uniform(.37,.99))
            drop = length * (rng.uniform(.04,.23) if upright else rng.uniform(.38,.82))
            twist = rng.uniform(-.60,.60)
            shade = rng.uniform(.66,1.32) * (.85 + patch*.26)
            warm = rng.uniform(.82,1.25)
            base = len(vertices)
            for row in range(8):
                t = row / 7
                reach = lean * t**1.45
                height = length*t - drop*t**3
                breadth = width * (1 - t**2.2) * (.68 + .32*math.sin(math.pi*t)) + .00014
                for col in (-1,0,1):
                    cross = breadth*.5*col
                    fold = breadth*.23 if col == 0 else 0
                    vertices.append((root_x + forward[0]*reach + side[0]*cross,
                                     root_y + forward[1]*reach + side[1]*cross,
                                     root_z + height + fold + cross*twist*t))
                    tone = shade * (.52 + .48*math.sin(t*math.pi*.5)) * (1.08 if col == 0 else .96)
                    colors.extend((.105*tone*warm, .177*tone, .035*tone/warm, 1))
                if row:
                    a = base + (row-1)*3
                    faces.extend(((a,a+1,a+4,a+3),(a+1,a+2,a+5,a+4)))
    mesh = geometry("Individually shaped grass blades", vertices, faces, material, collection)
    mesh.polygons.foreach_set("use_smooth", [True]*len(mesh.polygons))
    attribute = mesh.color_attributes.new(name="Blade color", type="FLOAT_COLOR", domain="POINT")
    attribute.data.foreach_set("color", colors)
    print(f"Grass master: {blade_count:,} blades, {len(faces)*2:,} triangles", flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    shared, pilot, prototype = [bpy.data.collections.new(name)
                               for name in ("Review stage", "Current pilot grass", "High-fidelity meadow")]
    for collection in (shared, pilot, prototype):
        scene.collection.children.link(collection)
    build_ground(shared)
    build_pilot(pilot)
    build_prototype(prototype)

    scene.render.engine = "CYCLES"
    preferences = bpy.context.preferences.addons["cycles"].preferences
    try:
        preferences.compute_device_type = "OPTIX"
        preferences.refresh_devices()
        if any(device.type == "OPTIX" for device in preferences.devices):
            for device in preferences.devices:
                device.use = device.type == "OPTIX"
            scene.cycles.device = "GPU"
    except RuntimeError:
        pass
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.cycles.transparent_max_bounces = 16
    scene.cycles.diffuse_bounces = 2
    scene.render.resolution_x, scene.render.resolution_y = 980, 595
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.exposure = .45
    scene.world = bpy.data.worlds.new("Meadow daylight")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes["Background"]
    background.inputs[0].default_value = (.30,.47,.72,1)
    background.inputs[1].default_value = .67

    sun = bpy.data.lights.new("Afternoon sunlight", "SUN")
    sun.energy, sun.angle = 2.7, .11
    sun.color = (1,.96,.84)
    light = bpy.data.objects.new(sun.name, sun)
    shared.objects.link(light)
    light.rotation_euler = Vector((.55,.48,-.91)).to_track_quat("-Z","Y").to_euler()
    camera = bpy.data.objects.new("Matched review camera", bpy.data.cameras.new("Meadow review"))
    shared.objects.link(camera)
    scene.camera = camera
    camera.data.lens = 36
    OUTPUT.mkdir(parents=True, exist_ok=True)
    views = (
        ("EyeLevel", (0,.42,1.48), (-.15,5.8,.97)),
        ("AcrossSlope", (-2.9,2.2,1.33), (1.6,5.3,1.01)),
        ("Above", (0,4.6,8.7), (0,5.1,.3)),
    )
    for name, position, target in views:
        camera.location = position
        camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z","Y").to_euler()
        for label, collection in (("Current",pilot),("Prototype",prototype)):
            pilot.hide_render = pilot.hide_viewport = collection != pilot
            prototype.hide_render = prototype.hide_viewport = collection != prototype
            scene.render.filepath = str(OUTPUT / f"{name}-{label}.png")
            if name == "EyeLevel" and collection == prototype:
                bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT / "MeadowGrassHighFidelity.blend"), compress=True)
            bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
