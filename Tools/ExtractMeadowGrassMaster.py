"""Extract the Blender grass master in blade order for the runtime bake."""
from pathlib import Path

import bpy
import numpy as np

root = Path(__file__).resolve().parent.parent
mesh = bpy.data.objects["Individually shaped grass blades"].data
vertices = np.empty(len(mesh.vertices) * 3, np.float32)
colors = np.empty(len(mesh.vertices) * 4, np.float32)
mesh.vertices.foreach_get("co", vertices)
mesh.color_attributes["Blade color"].data.foreach_get("color", colors)
vertices = vertices.reshape(-1, 24, 3)
colors = colors.reshape(-1, 24, 4)[..., :3]
# Remove the studio slope before making reusable tufts and periodic tiles.
x = vertices[:, 1, 0]
y = vertices[:, 1, 1]
vertices[:, :, 2] -= (.15*y + .13*x + .30*np.sin(y*.62) + .18*np.cos(x*.91+y*.23))[:, None]
np.savez(root / ".utmp/meadow-grass-master.npz", vertices=vertices, colors=colors)
