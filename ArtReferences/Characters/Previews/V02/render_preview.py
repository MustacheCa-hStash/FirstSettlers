import bpy
from pathlib import Path
from mathutils import Vector

out=Path(__file__).resolve().parent
source=Path('C:/Users/samee/OneDrive/Desktop/BlenderModelsFirstSettlers/Characters/Settler_Male_Base_v02.blend')
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
s=bpy.context.scene
studio=bpy.data.collections['STUDIO • preview only']
studio.hide_render=False
for o in bpy.data.objects:
    if o.type=='MESH':
        for m in o.data.materials:
            if m and m.use_nodes:
                p=m.node_tree.nodes.get('Principled BSDF')
                if p:
                    p.inputs['Base Color'].default_value=(.27,.27,.27,1)
                    p.inputs['Roughness'].default_value=.72

def area(name,pos,power,size):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);studio.objects.link(obj);obj.location=pos
    obj.rotation_euler=(Vector((0,0,1.0))-obj.location).to_track_quat('-Z','Y').to_euler()

area('Soft studio key',(-3,-4,4.5),450,3.0)
area('Soft studio fill',(3,-2.5,2.5),110,3.0)
area('Soft studio rim',(.5,3,3.5),250,2.5)
s.world.use_nodes=True
bg=s.world.node_tree.nodes.get('Background')
bg.inputs['Color'].default_value=(.30,.30,.30,1)
bg.inputs['Strength'].default_value=.35
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.003))
floor=bpy.context.object;floor.name='Temporary preview ground'
mat=bpy.data.materials.new('Studio ground');mat.use_nodes=True
p=mat.node_tree.nodes.get('Principled BSDF')
p.inputs['Base Color'].default_value=(.32,.32,.32,1)
p.inputs['Roughness'].default_value=.9
floor.data.materials.append(mat)
s.render.engine='CYCLES';s.cycles.samples=64;s.cycles.use_denoising=True
s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast'
s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG'
s.render.resolution_x=1200;s.render.resolution_y=1600
s.camera=bpy.data.objects['View • Three quarter']
s.render.filepath=str(out/'Settler_Male_Base_v02_FullBody.png')
bpy.ops.render.render(write_still=True)
s.camera=bpy.data.objects['View • Face']
s.render.resolution_x=1100;s.render.resolution_y=1100
s.camera.data.ortho_scale=.39
s.render.filepath=str(out/'Settler_Male_Base_v02_Face.png')
bpy.ops.render.render(write_still=True)
print('RENDERS_COMPLETE',str(out))
