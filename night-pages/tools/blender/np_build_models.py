"""Night Pages' own models: the things a 1950s-70s farmhouse had that no free library does.

Run with Blender's Python (Blender 4+/5, or the `bpy` pip package on Python 3.11):

    python tools/blender/np_build_models.py [ids...] [--preview]

Each model is built from real-world measurements, given procedural materials (worn chrome, Bakelite, Formica,
vinyl, cedar, cast iron, enamel, pleated fabric) with the wear real things get (rubbed edges, scratches in the
direction of use, dust on top, grime in the crevices), unwrapped, and baked to Poly Haven-style 1k maps:

    Assets/NightPages/Resources/NPModels/<id>/<id>_1k.fbx
    Assets/NightPages/Resources/NPModels/<id>/textures/<id>_diff_1k.jpg      colour
    Assets/NightPages/Resources/NPModels/<id>/textures/<id>_nor_gl_1k.jpg    normal (OpenGL)
    Assets/NightPages/Resources/NPModels/<id>/textures/<id>_ms_1k.png        metal R, occlusion G, smoothness A

so the game loads them exactly like the downloaded ones. --preview also renders tools/blender/previews/<id>.png
(Cycles, three-point light) to check them by eye. Fronts face -Y in Blender, as Poly Haven's do.
"""
import math, os, sys
from pathlib import Path
import bpy, bmesh
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2] / "Assets" / "NightPages" / "Resources" / "NPModels"
PREV = Path(__file__).resolve().parent / "previews"
RES = 1024


# ---------------------------------------------------------------- scene helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 24
    sc.unit_settings.system = "METRIC"


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def apply(obj):
    """Bake modifiers (and curves) into a plain mesh."""
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    new = bpy.data.objects.new(obj.name, me)
    new.matrix_world = obj.matrix_world.copy()
    link(new)
    for m in obj.data.materials if hasattr(obj.data, "materials") else []:
        pass
    bpy.data.objects.remove(obj, do_unlink=True)
    return new


def box(name, size, loc, bevel=0.004, segs=3, mat=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    o = link(bpy.data.objects.new(name, me))
    o.location = loc
    if bevel > 0:
        m = o.modifiers.new("bevel", "BEVEL"); m.width = bevel; m.segments = segs; m.limit_method = "ANGLE"
    o = apply(o)
    if mat: o.data.materials.append(mat)
    return o


def cyl(name, r, h, loc, verts=48, bevel=0.002, mat=None, r2=None, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r if r2 is None else r2, depth=h)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    o = link(bpy.data.objects.new(name, me))
    o.location = loc; o.rotation_euler = rot
    if bevel > 0:
        m = o.modifiers.new("bevel", "BEVEL"); m.width = bevel; m.segments = 2; m.limit_method = "ANGLE"
    for p in o.data.polygons: p.use_smooth = True
    o = apply(o)
    if mat: o.data.materials.append(mat)
    return o


def lathe(name, profile, verts=64, mat=None, loc=(0, 0, 0)):
    """Spin a (radius, z) profile round the Z axis: lamps, percolators, canisters."""
    bm = bmesh.new()
    prev = None
    rings = []
    for i in range(verts):
        a = 2 * math.pi * i / verts
        rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z)) for r, z in profile])
    for i in range(verts):
        a, b = rings[i], rings[(i + 1) % verts]
        for j in range(len(profile) - 1):
            bm.faces.new((a[j], b[j], b[j + 1], a[j + 1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    o = link(bpy.data.objects.new(name, me))
    o.location = loc
    for p in o.data.polygons: p.use_smooth = True
    if mat: o.data.materials.append(mat)
    return o


def tube(name, points, r, mat=None, res=12):
    """Bent tubing along points (chrome chair frames, lamp stems)."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"; cu.bevel_depth = r; cu.bevel_resolution = 4; cu.resolution_u = res
    sp = cu.splines.new("BEZIER")
    sp.bezier_points.add(len(points) - 1)
    for bp, p in zip(sp.bezier_points, points):
        bp.co = p; bp.handle_left_type = bp.handle_right_type = "AUTO"
    cu.use_fill_caps = True
    o = link(bpy.data.objects.new(name, cu))
    o = apply(o)
    for p in o.data.polygons: p.use_smooth = True
    if mat: o.data.materials.append(mat)
    return o


def join(objs, name):
    for o in bpy.context.selected_objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


# ---------------------------------------------------------------- materials (procedural, with wear)

def nodes(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return nt, bsdf


def noise(nt, scale, detail=6.0, rough=0.55, stretch=None):
    tc = nt.nodes.new("ShaderNodeTexCoord")
    n = nt.nodes.new("ShaderNodeTexNoise")
    n.inputs["Scale"].default_value = scale
    n.inputs["Detail"].default_value = detail
    n.inputs["Roughness"].default_value = rough
    if stretch:
        mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = stretch
        nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
        nt.links.new(mp.outputs["Vector"], n.inputs["Vector"])
    else:
        nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    return n


def ramp(nt, src, a, b, lo=0.35, hi=0.65):
    r = nt.nodes.new("ShaderNodeValToRGB")
    r.color_ramp.elements[0].position = lo; r.color_ramp.elements[0].color = (*a, 1) if len(a) == 3 else a
    r.color_ramp.elements[1].position = hi; r.color_ramp.elements[1].color = (*b, 1) if len(b) == 3 else b
    nt.links.new(src, r.inputs["Fac"])
    return r


def edge_wear(nt):
    """1 on rubbed edges and corners (where hands and years wear things), from the geometry's pointiness."""
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    r = nt.nodes.new("ShaderNodeValToRGB")
    r.color_ramp.elements[0].position = 0.56; r.color_ramp.elements[0].color = (0, 0, 0, 1)
    r.color_ramp.elements[1].position = 0.64; r.color_ramp.elements[1].color = (1, 1, 1, 1)
    nt.links.new(geo.outputs["Pointiness"], r.inputs["Fac"])
    n = noise(nt, 60.0)
    m = nt.nodes.new("ShaderNodeMath"); m.operation = "MULTIPLY"
    nt.links.new(r.outputs["Color"], m.inputs[0]); nt.links.new(n.outputs["Fac"], m.inputs[1])
    g = nt.nodes.new("ShaderNodeMath"); g.operation = "GREATER_THAN"; g.inputs[1].default_value = 0.4
    nt.links.new(m.outputs[0], g.inputs[0])
    return g.outputs[0]


def dust_top(nt):
    """Dust settles on what faces up."""
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], sep.inputs["Vector"])
    r = nt.nodes.new("ShaderNodeMapRange")
    r.inputs["From Min"].default_value = 0.75; r.inputs["From Max"].default_value = 1.0
    nt.links.new(sep.outputs["Z"], r.inputs["Value"])
    n = noise(nt, 18.0, 4, 0.6)
    m = nt.nodes.new("ShaderNodeMath"); m.operation = "MULTIPLY"
    nt.links.new(r.outputs["Result"], m.inputs[0]); nt.links.new(n.outputs["Fac"], m.inputs[1])
    s = nt.nodes.new("ShaderNodeMath"); s.operation = "MULTIPLY"; s.inputs[1].default_value = 0.55
    nt.links.new(m.outputs[0], s.inputs[0])
    return s.outputs[0]


def mix_col(nt, fac, a_out, b_col):
    mx = nt.nodes.new("ShaderNodeMix"); mx.data_type = "RGBA"
    nt.links.new(fac, mx.inputs["Factor"])
    nt.links.new(a_out, mx.inputs[6])
    mx.inputs[7].default_value = (*b_col, 1)
    return mx.outputs[2]


def bump(nt, bsdf, height_out, strength=0.15, dist=0.002):
    b = nt.nodes.new("ShaderNodeBump")
    b.inputs["Strength"].default_value = strength
    b.inputs["Distance"].default_value = dist
    nt.links.new(height_out, b.inputs["Height"])
    nt.links.new(b.outputs["Normal"], bsdf.inputs["Normal"])


def M_paint(name, col, rough=0.45, under=(0.25, 0.22, 0.2), metal_under=False, dust=True):
    """Painted or enamelled surface: slight colour drift, chipped on the edges to what's underneath, dusty on top."""
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    n = noise(nt, 9.0)
    base = ramp(nt, n.outputs["Fac"], tuple(c * 0.9 for c in col), tuple(min(1, c * 1.06) for c in col))
    wear = edge_wear(nt)
    c = mix_col(nt, wear, base.outputs["Color"], under)
    if dust: c = mix_col(nt, dust_top(nt), c, (0.42, 0.39, 0.35))
    nt.links.new(c, b.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = rough - 0.08; rr.inputs["To Max"].default_value = rough + 0.15
    nt.links.new(noise(nt, 40, 8, 0.7, (1, 1, 6)).outputs["Fac"], rr.inputs["Value"])
    nt.links.new(rr.outputs["Result"], b.inputs["Roughness"])
    if metal_under: nt.links.new(wear, b.inputs["Metallic"])
    bump(nt, b, noise(nt, 120, 6, 0.6).outputs["Fac"], 0.06)
    return mat


def M_chrome(name="Chrome", rust=0.15):
    """Chrome plating: mirror-bright, hazed with fine scratches, pitted brown where it's worn through."""
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    b.inputs["Metallic"].default_value = 1.0
    pit = ramp(nt, noise(nt, 90, 8, 0.75).outputs["Fac"], (0, 0, 0), (1, 1, 1), 0.72 - rust * 0.3, 0.78 - rust * 0.3)
    c = mix_col(nt, pit.outputs["Color"], ramp(nt, noise(nt, 5).outputs["Fac"], (0.78, 0.79, 0.8), (0.9, 0.9, 0.92)).outputs["Color"], (0.32, 0.2, 0.12))
    nt.links.new(c, b.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.06; rr.inputs["To Max"].default_value = 0.28
    nt.links.new(noise(nt, 200, 4, 0.5, (1, 1, 25)).outputs["Fac"], rr.inputs["Value"])
    mx = nt.nodes.new("ShaderNodeMath"); mx.operation = "MAXIMUM"
    nt.links.new(rr.outputs["Result"], mx.inputs[0]); nt.links.new(pit.outputs["Color"], mx.inputs[1])
    nt.links.new(mx.outputs[0], b.inputs["Roughness"])
    bump(nt, b, pit.outputs["Color"], 0.2, 0.0005)
    return mat


def M_bakelite(name="Bakelite", col=(0.018, 0.016, 0.015)):
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    c = ramp(nt, noise(nt, 14).outputs["Fac"], col, tuple(c * 1.6 for c in col))
    nt.links.new(mix_col(nt, edge_wear(nt), c.outputs["Color"], (0.09, 0.07, 0.06)), b.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.12; rr.inputs["To Max"].default_value = 0.35
    nt.links.new(noise(nt, 70, 6, 0.6, (1, 1, 8)).outputs["Fac"], rr.inputs["Value"])
    nt.links.new(rr.outputs["Result"], b.inputs["Roughness"])
    bump(nt, b, noise(nt, 300, 4).outputs["Fac"], 0.04)
    return mat


def M_wood(name, col=(0.32, 0.17, 0.09), grain=(1, 1, 22), rough=0.42, dust=True):
    """Varnished wood: wave-band grain stretched along the boards, darker in the grain, varnish rubbed thin at edges."""
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = grain
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    w = nt.nodes.new("ShaderNodeTexWave"); w.wave_type = "RINGS"; w.inputs["Scale"].default_value = 3.0
    w.inputs["Distortion"].default_value = 9.0; w.inputs["Detail"].default_value = 3.0
    nt.links.new(mp.outputs["Vector"], w.inputs["Vector"])
    c = ramp(nt, w.outputs["Fac"], tuple(x * 0.62 for x in col), col, 0.2, 0.85)
    c2 = mix_col(nt, edge_wear(nt), c.outputs["Color"], tuple(min(1, x * 1.5) for x in col))
    if dust: c2 = mix_col(nt, dust_top(nt), c2, (0.4, 0.37, 0.33))
    nt.links.new(c2, b.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = rough - 0.1; rr.inputs["To Max"].default_value = rough + 0.2
    nt.links.new(noise(nt, 30, 6, 0.6, (1, 1, 10)).outputs["Fac"], rr.inputs["Value"])
    nt.links.new(rr.outputs["Result"], b.inputs["Roughness"])
    bump(nt, b, w.outputs["Fac"], 0.08)
    return mat


def M_formica(name="Formica", col=(0.78, 0.2, 0.18)):
    """'Cracked ice' laminate: colour with pearly flakes, glossy, worn dull where plates go."""
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    v = nt.nodes.new("ShaderNodeTexVoronoi"); v.inputs["Scale"].default_value = 28
    tc = nt.nodes.new("ShaderNodeTexCoord"); nt.links.new(tc.outputs["Object"], v.inputs["Vector"])
    fl = ramp(nt, v.outputs["Distance"], (1, 1, 1), (0, 0, 0), 0.0, 0.12)
    c = mix_col(nt, fl.outputs["Color"], ramp(nt, noise(nt, 6).outputs["Fac"], tuple(x * 0.92 for x in col), col).outputs["Color"], (0.93, 0.9, 0.86))
    nt.links.new(mix_col(nt, edge_wear(nt), c, (0.85, 0.8, 0.7)), b.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.1; rr.inputs["To Max"].default_value = 0.4
    nt.links.new(noise(nt, 3, 3, 0.5).outputs["Fac"], rr.inputs["Value"])
    nt.links.new(rr.outputs["Result"], b.inputs["Roughness"])
    return mat


def M_vinyl(name="Vinyl", col=(0.55, 0.06, 0.05)):
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    v = nt.nodes.new("ShaderNodeTexVoronoi"); v.inputs["Scale"].default_value = 900
    tc = nt.nodes.new("ShaderNodeTexCoord"); nt.links.new(tc.outputs["Object"], v.inputs["Vector"])
    c = ramp(nt, noise(nt, 8).outputs["Fac"], tuple(x * 0.85 for x in col), col)
    cracks = ramp(nt, noise(nt, 40, 10, 0.8, (1, 1, 3)).outputs["Fac"], (0, 0, 0), (1, 1, 1), 0.66, 0.7)
    nt.links.new(mix_col(nt, cracks.outputs["Color"], c.outputs["Color"], (0.8, 0.72, 0.6)), b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.38
    bump(nt, b, v.outputs["Distance"], 0.12, 0.0008)
    return mat


def M_fabric(name, col=(0.82, 0.74, 0.6), weave=500):
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    w1 = nt.nodes.new("ShaderNodeTexWave"); w1.inputs["Scale"].default_value = weave; w1.bands_direction = "X"
    w2 = nt.nodes.new("ShaderNodeTexWave"); w2.inputs["Scale"].default_value = weave; w2.bands_direction = "Y"
    nt.links.new(tc.outputs["Object"], w1.inputs["Vector"]); nt.links.new(tc.outputs["Object"], w2.inputs["Vector"])
    m = nt.nodes.new("ShaderNodeMath"); m.operation = "MULTIPLY"
    nt.links.new(w1.outputs["Fac"], m.inputs[0]); nt.links.new(w2.outputs["Fac"], m.inputs[1])
    stain = ramp(nt, noise(nt, 4, 3, 0.5).outputs["Fac"], (0, 0, 0), (1, 1, 1), 0.62, 0.8)
    c = ramp(nt, noise(nt, 20).outputs["Fac"], tuple(x * 0.9 for x in col), col)
    nt.links.new(mix_col(nt, stain.outputs["Color"], c.outputs["Color"], tuple(x * 0.75 for x in col)), b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.92
    bump(nt, b, m.outputs[0], 0.2, 0.0006)
    return mat


def M_brass(name="Brass"):
    mat = bpy.data.materials.new(name); nt, b = nodes(mat)
    b.inputs["Metallic"].default_value = 1.0
    tarn = ramp(nt, noise(nt, 12, 6, 0.6).outputs["Fac"], (0, 0, 0), (1, 1, 1), 0.45, 0.7)
    c = mix_col(nt, tarn.outputs["Color"], ramp(nt, noise(nt, 3).outputs["Fac"], (0.78, 0.6, 0.3), (0.86, 0.7, 0.38)).outputs["Color"], (0.36, 0.26, 0.14))
    nt.links.new(mix_col(nt, edge_wear(nt), c, (0.95, 0.8, 0.5)), b.inputs["Base Color"])
    rr = nt.nodes.new("ShaderNodeMapRange"); rr.inputs["To Min"].default_value = 0.22; rr.inputs["To Max"].default_value = 0.5
    nt.links.new(tarn.outputs["Color"], rr.inputs["Value"])
    nt.links.new(rr.outputs["Result"], b.inputs["Roughness"])
    return mat


def M_iron(name, paint=(0.82, 0.8, 0.72)):
    """Cast iron under many coats of paint: the paint thick, crazed, chipped to rust at the edges."""
    return M_paint(name, paint, 0.55, (0.24, 0.12, 0.07), metal_under=False)


# ---------------------------------------------------------------- the models

def rotary_phone():
    blk = M_bakelite(); ch = M_chrome(rust=0.05); paper = M_paint("Card", (0.9, 0.87, 0.78), 0.8, dust=False)
    parts = []
    # The body: a sloped wedge with soft edges (Western Electric 500 type), 23 x 13 x 20 cm.
    bm = bmesh.new()
    pts = [(-0.115, -0.1, 0), (0.115, -0.1, 0), (0.115, 0.1, 0), (-0.115, 0.1, 0),
           (-0.1, -0.08, 0.075), (0.1, -0.08, 0.075), (0.1, 0.09, 0.115), (-0.1, 0.09, 0.115)]
    v = [bm.verts.new(p) for p in pts]
    for f in ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Body"); bm.to_mesh(me); bm.free()
    body = link(bpy.data.objects.new("Body", me))
    mo = body.modifiers.new("bevel", "BEVEL"); mo.width = 0.022; mo.segments = 6
    sub = body.modifiers.new("sub", "SUBSURF"); sub.levels = 1
    body = apply(body); body.data.materials.append(blk)
    for p in body.data.polygons: p.use_smooth = True
    parts.append(body)
    # The dial: chrome-rimmed disc on the sloped face, ten finger holes, the card in the middle.
    tilt = math.atan2(0.04, 0.17)
    centre = Vector((0, -0.005, 0.097))
    d = cyl("Dial", 0.047, 0.008, centre, 64, 0.0015, blk, rot=(tilt, 0, 0))
    # Ten finger holes round the dial (a boolean of small cylinders).
    holes = []
    for i in range(10):
        a = math.radians(60 + i * 27)
        hc = cyl("Hole", 0.0075, 0.03, (0, 0, 0), 24, 0.0, None)
        hc.location = centre + Matrix.Rotation(tilt, 4, "X") @ Vector((0.033 * math.cos(a), 0.033 * math.sin(a), 0.0))
        hc.rotation_euler = (tilt, 0, 0)
        holes.append(hc)
    cutter = join(holes, "Cutter")
    bo = d.modifiers.new("holes", "BOOLEAN"); bo.object = cutter; bo.operation = "DIFFERENCE"; bo.solver = "EXACT"
    d = apply(d); bpy.data.objects.remove(cutter, do_unlink=True)
    plate = cyl("Number plate", 0.042, 0.004, centre + Vector((0, 0, -0.004)), 64, 0.0, paper, rot=(tilt, 0, 0)); parts.append(plate)
    parts.append(d)
    rim = cyl("Rim", 0.049, 0.004, centre + Vector((0, 0, -0.002)), 64, 0.001, ch, rot=(tilt, 0, 0)); parts.append(rim)
    card = cyl("Card", 0.016, 0.0095, centre, 32, 0.0005, paper, rot=(tilt, 0, 0)); parts.append(card)
    stop = box("Finger stop", (0.004, 0.02, 0.006), centre + Vector((0.044, -0.02, 0.004)), 0.001, 2, ch); parts.append(stop)
    # The cradle prongs and the handset across them.
    for x in (-0.07, 0.07):
        parts.append(box("Prong", (0.03, 0.035, 0.03), (x, 0.065, 0.122), 0.008, 4, blk))
    # The handset: a grip bowed up between the earpiece and the mouthpiece, which hang down into the cradle.
    hs = tube("Handset", [(-0.105, 0.065, 0.158), (-0.07, 0.065, 0.172), (0.0, 0.065, 0.178), (0.07, 0.065, 0.172), (0.105, 0.065, 0.158)], 0.0135, blk)
    parts.append(hs)
    for x in (-0.105, 0.105):
        cup = lathe("Cup", [(0.0, 0.0), (0.03, 0.0), (0.031, 0.006), (0.024, 0.03), (0.014, 0.042), (0.0, 0.044)], 48, blk, (x, 0.065, 0.118))
        parts.append(cup)
        parts.append(cyl("Grille", 0.022, 0.002, (x, 0.065, 0.1175), 32, 0.0, M_paint("Grille", (0.05, 0.05, 0.05), 0.6, dust=False)))
    # The cord: a fat coil off the back left.
    cord = []
    for i in range(80):
        t = i / 79.0
        a = t * 2 * math.pi * 14
        cord.append((-0.1 - 0.05 * t + 0.008 * math.cos(a), 0.1 + 0.12 * t, 0.04 - 0.035 * t + 0.008 * math.sin(a)))
    parts.append(tube("Cord", cord, 0.0025, blk, 4))
    return join(parts, "np_rotary_phone")


def dinette_table():
    ch = M_chrome(rust=0.25); top = M_formica("Formica", (0.75, 0.22, 0.2)); band = M_chrome("Band", 0.1)
    parts = []
    parts.append(box("Top", (1.2, 0.8, 0.028), (0, 0, 0.745), 0.004, 3, top))
    parts.append(box("Band", (1.214, 0.814, 0.03), (0, 0, 0.741), 0.005, 4, band))
    parts.append(box("Apron", (1.1, 0.7, 0.06), (0, 0, 0.7), 0.003, 2, M_paint("Apron", (0.6, 0.6, 0.62), 0.5)))
    # Two U-frames of 1" tube, flared feet.
    for x in (-0.48, 0.48):
        parts.append(tube("Leg", [(x, -0.33, 0.69), (x, -0.36, 0.35), (x, -0.36, 0.03), (x, -0.36, 0.012), (x, 0.0, 0.012), (x, 0.36, 0.012), (x, 0.36, 0.03), (x, 0.36, 0.35), (x, 0.33, 0.69)], 0.0125, ch))
        for y in (-0.36, 0.36):
            parts.append(cyl("Glide", 0.016, 0.01, (x, y, 0.005), 24, 0.002, M_bakelite("Glide")))
    return join(parts, "np_dinette_table")


def dinette_chair():
    ch = M_chrome(rust=0.3); vin = M_vinyl("Vinyl", (0.62, 0.08, 0.06)); pip = M_vinyl("Piping", (0.9, 0.86, 0.78))
    parts = []
    seat = box("Seat", (0.42, 0.42, 0.075), (0, 0, 0.45), 0.03, 6, vin)
    for p in seat.data.polygons: p.use_smooth = True
    parts.append(seat)
    parts.append(box("Seat piping", (0.43, 0.43, 0.012), (0, 0, 0.452), 0.006, 3, pip))
    back = box("Back", (0.4, 0.06, 0.2), (0, 0.2, 0.78), 0.025, 6, vin)
    for p in back.data.polygons: p.use_smooth = True
    parts.append(back)
    # Frame: back legs sweep up into the back posts; front legs; a loop under the seat.
    for x in (-0.19, 0.19):
        parts.append(tube("Back leg", [(x, 0.17, 0.0), (x, 0.2, 0.3), (x, 0.21, 0.6), (x, 0.24, 0.9)], 0.011, ch))
        parts.append(tube("Front leg", [(x, -0.19, 0.0), (x, -0.18, 0.22), (x, -0.17, 0.41)], 0.011, ch))
    parts.append(tube("Seat rail", [(-0.19, -0.17, 0.41), (0.0, -0.18, 0.41), (0.19, -0.17, 0.41), (0.2, 0.0, 0.41), (0.19, 0.17, 0.41)], 0.009, ch))
    return join(parts, "np_dinette_chair")


def percolator():
    ch = M_chrome(rust=0.08); blk = M_bakelite(); amber = M_paint("Knob", (0.75, 0.45, 0.12), 0.08, dust=False)
    prof = [(0.0, 0.0), (0.06, 0.0), (0.066, 0.006), (0.07, 0.03), (0.068, 0.16), (0.06, 0.22), (0.058, 0.25), (0.03, 0.255), (0.0, 0.256)]
    parts = [lathe("Pot", prof, 64, ch)]
    parts.append(lathe("Knob", [(0.0, 0.25), (0.02, 0.252), (0.022, 0.27), (0.018, 0.29), (0.0, 0.295)], 32, amber))
    parts.append(tube("Handle", [(0.066, 0, 0.21), (0.11, 0, 0.2), (0.115, 0, 0.12), (0.1, 0, 0.05), (0.07, 0, 0.04)], 0.012, blk))
    parts.append(tube("Spout", [(-0.06, 0, 0.17), (-0.085, 0, 0.21), (-0.1, 0, 0.235)], 0.009, ch))
    parts.append(lathe("Base", [(0.0, -0.012), (0.075, -0.012), (0.075, 0.0), (0.0, 0.0)], 48, blk))
    return join(parts, "np_percolator")


def toaster():
    ch = M_chrome(rust=0.06); blk = M_bakelite()
    parts = []
    b = box("Body", (0.27, 0.15, 0.19), (0, 0, 0.1), 0.045, 8, ch)
    for p in b.data.polygons: p.use_smooth = True
    parts.append(b)
    for y in (-0.03, 0.03):
        parts.append(box("Slot", (0.2, 0.012, 0.01), (0, y, 0.196), 0.003, 2, blk))
    for x in (-0.14, 0.14):
        parts.append(box("End", (0.012, 0.14, 0.17), (x, 0, 0.095), 0.005, 3, blk))
    parts.append(box("Lever", (0.02, 0.03, 0.012), (0.152, 0, 0.12), 0.004, 3, blk))
    parts.append(cyl("Knob", 0.01, 0.012, (-0.152, 0, 0.06), 24, 0.002, blk, rot=(0, math.pi / 2, 0)))
    for x in (-0.11, 0.11):
        for y in (-0.06, 0.06):
            parts.append(cyl("Foot", 0.008, 0.012, (x, y, 0.004), 16, 0.001, blk))
    return join(parts, "np_toaster")


def cedar_chest():
    wd = M_wood("Cedar", (0.42, 0.2, 0.11), (1, 1, 14), 0.38); br = M_brass(); dark = M_wood("Plinth", (0.22, 0.1, 0.05), (1, 1, 14), 0.45)
    parts = [box("Case", (1.1, 0.48, 0.38), (0, 0, 0.27), 0.006, 3, wd),
             box("Lid", (1.13, 0.51, 0.05), (0, 0, 0.485), 0.012, 4, wd),
             box("Plinth", (1.14, 0.52, 0.08), (0, 0, 0.04), 0.008, 3, dark),
             box("Lid lip", (1.12, 0.5, 0.015), (0, 0, 0.455), 0.004, 2, dark)]
    for x in (-0.36, 0.0, 0.36):
        parts.append(box("Panel", (0.32, 0.01, 0.26), (x, -0.243, 0.27), 0.004, 3, wd))
    parts.append(box("Escutcheon", (0.05, 0.006, 0.06), (0, -0.258, 0.4), 0.002, 2, br))
    for x in (-0.57, 0.57):
        parts.append(tube("Handle", [(x, -0.06, 0.32), (x * 1.04, -0.05, 0.29), (x * 1.04, 0.05, 0.29), (x, 0.06, 0.32)], 0.006, br))
    return join(parts, "np_cedar_chest")


def floor_lamp():
    br = M_brass(); shade = M_fabric("Shade", (0.86, 0.78, 0.6), 700); trim = M_fabric("Trim", (0.55, 0.42, 0.25), 900)
    parts = [lathe("Base", [(0, 0), (0.15, 0), (0.15, 0.015), (0.12, 0.04), (0.03, 0.06), (0.018, 0.08), (0, 0.08)], 64, br),
             cyl("Pole", 0.011, 1.45, (0, 0, 0.8), 24, 0.001, br),
             lathe("Collar", [(0, 0.5), (0.02, 0.5), (0.024, 0.53), (0.02, 0.56), (0, 0.56)], 32, br),
             tube("Harp", [(-0.12, 0, 1.36), (-0.12, 0, 1.5), (0, 0, 1.62), (0.12, 0, 1.5), (0.12, 0, 1.36)], 0.003, br)]
    # A pleated drum shade: the radius waves 48 times round.
    bm = bmesh.new(); N = 96; rings = []
    for z, r in ((1.33, 0.25), (1.62, 0.17)):
        ring = []
        for i in range(N):
            a = 2 * math.pi * i / N
            rr = r * (1 + 0.025 * (1 if i % 2 else -1))
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
        rings.append(ring)
    for i in range(N):
        bm.faces.new((rings[0][i], rings[0][(i + 1) % N], rings[1][(i + 1) % N], rings[1][i]))
    me = bpy.data.meshes.new("Shade"); bm.to_mesh(me); bm.free()
    sh = link(bpy.data.objects.new("Shade", me))
    so = sh.modifiers.new("thick", "SOLIDIFY"); so.thickness = 0.003
    sh = apply(sh); sh.data.materials.append(shade); parts.append(sh)
    parts.append(lathe("Bottom trim", [(0.255, 1.325), (0.262, 1.325), (0.262, 1.345), (0.255, 1.345)], 96, trim))
    parts.append(lathe("Top trim", [(0.172, 1.61), (0.18, 1.61), (0.18, 1.625), (0.172, 1.625)], 96, trim))
    parts.append(lathe("Finial", [(0, 1.6), (0.012, 1.62), (0.015, 1.65), (0, 1.67)], 24, br))
    return join(parts, "np_floor_lamp")


def radiator():
    ir = M_iron("Radiator paint", (0.8, 0.78, 0.7))
    parts = []
    n = 10
    for i in range(n):
        x = (i - (n - 1) / 2) * 0.064
        sec = box("Section", (0.052, 0.17, 0.62), (x, 0, 0.38), 0.022, 6, ir)
        for p in sec.data.polygons: p.use_smooth = True
        parts.append(sec)
        for z in (0.12, 0.64):
            parts.append(cyl("Hub", 0.028, 0.06, (x, 0, z), 24, 0.004, ir, rot=(0, math.pi / 2, 0)))
    for x in (-0.3, 0.3):
        for y in (-0.06, 0.06):
            parts.append(box("Foot", (0.05, 0.05, 0.08), (x, y, 0.04), 0.01, 3, ir))
    parts.append(cyl("Valve", 0.018, 0.08, (0.35, 0, 0.62), 24, 0.003, M_brass(), rot=(0, math.pi / 2, 0)))
    parts.append(cyl("Valve wheel", 0.03, 0.012, (0.35, 0, 0.68), 24, 0.003, M_bakelite()))
    return join(parts, "np_radiator")


def canisters():
    col = (0.82, 0.12, 0.1); cream = (0.94, 0.9, 0.8)
    red = M_paint("Red tin", col, 0.3, (0.3, 0.3, 0.32), metal_under=True)
    lid = M_paint("Cream lid", cream, 0.32, (0.3, 0.3, 0.32), metal_under=True)
    knob = M_bakelite("Knob")
    parts = []
    x = -0.24
    for r, h in ((0.075, 0.22), (0.066, 0.19), (0.058, 0.16), (0.05, 0.13)):
        x += r
        parts.append(lathe("Tin", [(0, 0), (r, 0), (r, h), (r * 0.97, h + 0.002), (0, h + 0.002)], 64, red, (x, 0, 0)))
        parts.append(lathe("Lid", [(0, h), (r * 1.03, h), (r * 1.03, h + 0.02), (r * 0.8, h + 0.03), (0, h + 0.032)], 64, lid, (x, 0, 0)))
        parts.append(lathe("Knob", [(0, h + 0.03), (0.012, h + 0.03), (0.014, h + 0.045), (0, h + 0.05)], 24, knob, (x, 0, 0)))
        x += r + 0.012
    return join(parts, "np_canisters")


def table_lamp():
    cer = M_paint("Glaze", (0.42, 0.55, 0.52), 0.18, (0.85, 0.82, 0.75)); shade = M_fabric("Shade", (0.9, 0.84, 0.7), 800); br = M_brass()
    parts = [lathe("Base", [(0, 0), (0.07, 0), (0.075, 0.01), (0.06, 0.03), (0.09, 0.12), (0.095, 0.2), (0.07, 0.28), (0.03, 0.31), (0.02, 0.33), (0, 0.33)], 64, cer),
             cyl("Stem", 0.008, 0.12, (0, 0, 0.39), 16, 0.001, br),
             lathe("Socket", [(0, 0.43), (0.018, 0.43), (0.018, 0.47), (0, 0.47)], 24, br)]
    bm = bmesh.new(); N = 80; rings = []
    for z, r in ((0.38, 0.19), (0.6, 0.12)):
        ring = [bm.verts.new((r * (1 + 0.03 * (1 if i % 2 else -1)) * math.cos(2 * math.pi * i / N), r * (1 + 0.03 * (1 if i % 2 else -1)) * math.sin(2 * math.pi * i / N), z)) for i in range(N)]
        rings.append(ring)
    for i in range(N):
        bm.faces.new((rings[0][i], rings[0][(i + 1) % N], rings[1][(i + 1) % N], rings[1][i]))
    me = bpy.data.meshes.new("Shade"); bm.to_mesh(me); bm.free()
    sh = link(bpy.data.objects.new("Shade", me)); so = sh.modifiers.new("t", "SOLIDIFY"); so.thickness = 0.003
    sh = apply(sh); sh.data.materials.append(shade); parts.append(sh)
    return join(parts, "np_table_lamp")


MODELS = {
    "np_rotary_phone": rotary_phone, "np_dinette_table": dinette_table, "np_dinette_chair": dinette_chair,
    "np_percolator": percolator, "np_toaster": toaster, "np_cedar_chest": cedar_chest, "np_floor_lamp": floor_lamp,
    "np_radiator": radiator, "np_canisters": canisters, "np_table_lamp": table_lamp,
}


# ---------------------------------------------------------------- unwrap, bake, export

def unwrap(o):
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_maps(o, mid):
    sc = bpy.context.scene
    sc.cycles.samples = 16
    sc.render.bake.margin = 6
    imgs = {}
    for key, colourspace in (("diff", "sRGB"), ("rough", "Non-Color"), ("metal", "Non-Color"), ("nor", "Non-Color"), ("ao", "Non-Color")):
        im = bpy.data.images.new(f"{mid}_{key}", RES, RES, alpha=False, float_buffer=False)
        im.colorspace_settings.name = colourspace
        imgs[key] = im

    def target(im):
        for m in o.data.materials:
            nt = m.node_tree
            n = nt.nodes.get("BakeTarget") or nt.nodes.new("ShaderNodeTexImage")
            n.name = "BakeTarget"; n.image = im
            nt.nodes.active = n

    o.select_set(True); bpy.context.view_layer.objects.active = o
    target(imgs["rough"]); bpy.ops.object.bake(type="ROUGHNESS")
    target(imgs["nor"]); bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
    sc.cycles.samples = 24
    target(imgs["ao"]); bpy.ops.object.bake(type="AO")
    sc.cycles.samples = 16
    # Colour and metal: route each material's base colour (a diffuse bake would come out black on metals), then its
    # metallic value, into emission, one bake each.
    def emit_bake(socket_name, im):
        saved = []
        for m in o.data.materials:
            nt = m.node_tree
            b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
            out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
            em = nt.nodes.new("ShaderNodeEmission")
            src = b.inputs[socket_name]
            if src.is_linked:
                nt.links.new(src.links[0].from_socket, em.inputs["Color"])
            else:
                v = src.default_value
                em.inputs["Color"].default_value = (v[0], v[1], v[2], 1) if hasattr(v, "__len__") else (v, v, v, 1)
            old = out.inputs["Surface"].links[0].from_socket
            nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
            saved.append((nt, out, old, em))
        target(im); bpy.ops.object.bake(type="EMIT")
        for nt, out, old, em in saved:
            nt.links.new(old, out.inputs["Surface"]); nt.nodes.remove(em)
    emit_bake("Base Color", imgs["diff"])
    emit_bake("Metallic", imgs["metal"])
    return imgs


def save_maps(imgs, mid):
    import numpy as np
    d = ROOT / mid / "textures"; d.mkdir(parents=True, exist_ok=True)
    def px(im):
        a = np.array(im.pixels[:], dtype=np.float32).reshape(RES, RES, 4)
        return np.flipud(a)
    from PIL import Image
    diff = (np.clip(px(imgs["diff"])[:, :, :3], 0, 1) ** (1 / 2.2) * 255).astype("uint8") if imgs["diff"].is_float else (px(imgs["diff"])[:, :, :3] * 255).astype("uint8")
    Image.fromarray(diff).save(d / f"{mid}_diff_1k.jpg", quality=92)
    nor = (px(imgs["nor"])[:, :, :3] * 255).astype("uint8")
    Image.fromarray(nor).save(d / f"{mid}_nor_gl_1k.jpg", quality=94)
    metal = (px(imgs["metal"])[:, :, 0] * 255).astype("uint8")
    ao = (px(imgs["ao"])[:, :, 0] * 255).astype("uint8")
    smooth = ((1 - px(imgs["rough"])[:, :, 0]) * 255).clip(0, 255).astype("uint8")
    zero = np.zeros_like(metal)
    Image.fromarray(np.dstack([metal, ao, zero, smooth]), "RGBA").save(d / f"{mid}_ms_1k.png", optimize=True)
    return d


def finalise(o, mid, d):
    """One plain material that points at the baked colour map, so the FBX carries it the way Poly Haven's do."""
    o.data.materials.clear()
    m = bpy.data.materials.new(mid); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(str(d / f"{mid}_diff_1k.jpg"))
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    o.data.materials.append(m)


def export(o, mid):
    for x in bpy.context.selected_objects: x.select_set(False)
    o.select_set(True); bpy.context.view_layer.objects.active = o
    path = ROOT / mid / f"{mid}_1k.fbx"
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_ALL", path_mode="RELATIVE", embed_textures=False,
                             mesh_smooth_type="FACE", use_tspace=True, axis_forward="-Z", axis_up="Y")
    return path


def preview(mid):
    """A look at it as a person would: on a grey floor, a warm key light, a cool fill, a rim."""
    sc = bpy.context.scene
    o = bpy.data.objects[mid]
    bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
    lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    size = (hi - lo).length
    centre = (hi + lo) / 2
    bpy.ops.mesh.primitive_plane_add(size=size * 8, location=(centre.x, centre.y, lo.z))
    fl = bpy.context.active_object
    fm = bpy.data.materials.new("Floor"); fm.use_nodes = True; fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.06, 0.05, 0.045, 1)
    fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.7
    fl.data.materials.append(fm)
    def light(name, kind, energy, loc, col):
        L = bpy.data.lights.new(name, kind); L.energy = energy; L.color = col
        if kind == "AREA": L.size = size * 1.2
        ob = link(bpy.data.objects.new(name, L)); ob.location = loc
        dirn = centre - Vector(loc); ob.rotation_euler = dirn.to_track_quat("-Z", "Y").to_euler()
    light("Key", "AREA", 70 * size * size + 6, centre + Vector((-size * 1.4, -size * 1.6, size * 1.5)), (1.0, 0.88, 0.72))
    light("Fill", "AREA", 18 * size * size + 2, centre + Vector((size * 1.8, -size * 0.8, size * 0.6)), (0.75, 0.82, 1.0))
    light("Rim", "AREA", 40 * size * size + 4, centre + Vector((size * 0.5, size * 1.8, size * 1.2)), (1, 1, 1))
    w = bpy.data.worlds.new("W"); w.use_nodes = True; w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.02, 0.02, 0.025, 1)
    sc.world = w
    cam = bpy.data.cameras.new("Cam"); cam.lens = 50
    co = link(bpy.data.objects.new("Cam", cam))
    co.location = centre + Vector((size * 0.9, -size * 1.9, size * 0.85))
    co.rotation_euler = (centre - co.location).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x = 800; sc.render.resolution_y = 600
    sc.cycles.samples = 48
    sc.view_settings.view_transform = "AgX" if "AgX" in [x.name for x in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
    PREV.mkdir(parents=True, exist_ok=True)
    sc.render.filepath = str(PREV / f"{mid}.png")
    bpy.ops.render.render(write_still=True)


def build(mid, want_preview):
    reset()
    o = MODELS[mid]()
    o.name = mid
    unwrap(o)
    imgs = bake_maps(o, mid)
    d = save_maps(imgs, mid)
    finalise(o, mid, d)
    path = export(o, mid)
    dims = o.dimensions
    print(f"{mid}: {len(o.data.polygons)} faces, {dims.x:.3f} x {dims.z:.3f} x {dims.y:.3f} m (w x h x d) -> {path}", flush=True)
    if want_preview:
        # Preview with the procedural materials (what the bake came from) would hide bake mistakes: use the baked look.
        m = o.data.materials[0]; nt = m.node_tree; b = nt.nodes["Principled BSDF"]
        nor = nt.nodes.new("ShaderNodeTexImage"); nor.image = bpy.data.images.load(str(d / f"{mid}_nor_gl_1k.jpg")); nor.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(nor.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
        ms = nt.nodes.new("ShaderNodeTexImage"); ms.image = bpy.data.images.load(str(d / f"{mid}_ms_1k.png")); ms.image.colorspace_settings.name = "Non-Color"
        sep = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(ms.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Red"], b.inputs["Metallic"])
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
        nt.links.new(ms.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], b.inputs["Roughness"])
        preview(mid)
    return dims


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    pv = "--preview" in sys.argv
    sizes = {}
    for mid in args or list(MODELS):
        sizes[mid] = build(mid, pv)
    print("ModelSizes:")
    for mid, d in sizes.items():
        print(f'            {{ "{mid}", new Vector3({d.x:.3f}f, {d.z:.3f}f, {d.y:.3f}f) }},')
