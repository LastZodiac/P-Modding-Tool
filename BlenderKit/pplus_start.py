# Started by the P+ Blender Bridge ("Send to Blender"): loads the P+ Kit (no installing needed), opens the costume
# the Bridge sent, and opens the P+ Kit tab on the right of the 3D view.
# Blender runs it as:  blender --python pplus_start.py -- "<costume glb>" "<P+ Blender Bridge.exe>"
import os
import sys
import importlib.util
import bpy

here = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
glb = args[0] if len(args) > 0 else None
bridge = args[1] if len(args) > 1 else None

spec = importlib.util.spec_from_file_location("pplus_blender_kit", os.path.join(here, "pplus_blender_kit.py"))
kit = importlib.util.module_from_spec(spec)
sys.modules["pplus_blender_kit"] = kit
spec.loader.exec_module(kit)
kit.register()


def start():
    # Blender's starting cube, camera and light aren't wanted
    for name in ("Cube", "Camera", "Light"):
        o = bpy.data.objects.get(name)
        if o is not None:
            bpy.data.objects.remove(o, do_unlink=True)
    if bridge:
        bpy.context.scene["pplus_bridge"] = bridge
    if glb and os.path.exists(glb):
        try:
            bpy.ops.pplus.open_costume(filepath=glb)
        except Exception as e:
            print("P+ Kit: couldn't open the costume:", e)
    # the sidebar, on the P+ Kit tab
    try:
        for window in bpy.context.window_manager.windows:
            for area in window.screen.areas:
                if area.type == 'VIEW_3D':
                    area.spaces.active.show_region_ui = True
                    for region in area.regions:
                        if region.type == 'UI':
                            try:
                                region.active_panel_category = "P+ Kit"
                            except Exception:
                                pass
                    area.tag_redraw()
    except Exception as e:
        print("P+ Kit: sidebar:", e)
    return None


if bpy.app.background:
    start()
else:
    bpy.app.timers.register(start, first_interval=0.5)
