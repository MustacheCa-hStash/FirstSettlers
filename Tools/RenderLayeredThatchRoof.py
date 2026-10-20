"""Blender review renders of the editable source; no authoring changes saved."""
import bpy, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'ArtReferences/ThatchRoof/Redesign'
if Path(bpy.data.filepath).stem=='BakedThatchRoof':OUT=OUT/'Baked'
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=32
scene.render.resolution_x=1200;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Roof review world');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.10,.13,.15,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
for obj in list(bpy.data.objects):
    if obj.name.startswith(('Roof_Thatch_Export','Roof_Baked_Export')):obj.hide_render=True
camera_data=bpy.data.cameras.new('Roof review camera');camera=bpy.data.objects.new(camera_data.name,camera_data);scene.collection.objects.link(camera)
scene.camera=camera;camera_data.type='ORTHO';camera_data.ortho_scale=5.6
for loc,power,size in [((1,-4,7),850,5),((-3,3,5),450,4),((6,4,6),650,4)]:
    data=bpy.data.lights.new('Roof review softbox','AREA');data.energy=power;data.size=size
    obj=bpy.data.objects.new(data.name,data);scene.collection.objects.link(obj);obj.location=loc;obj.rotation_euler=(Vector((2,1,1.2))-obj.location).to_track_quat('-Z','Y').to_euler()
for tag,position,look,scale in [('broad',(6,-6,5),(2,.8,1.2),5.6),('gable',(-6,-2,3),(1.8,1,1.2),5.6),('underside',(6,-4,-2),(2,1,1.1),5.6),('course-close',(5,-4,4),(2,.6,1.1),2.4)]:
    camera.location=position;camera.rotation_euler=(Vector(look)-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.ortho_scale=scale
    scene.render.filepath=str(OUT/('blender-'+tag+'.png'));bpy.ops.render.render(write_still=True)
