"""CPU-only glTF conversion, invoked by import_models.py through Blender.

No rendering, atlas baking, network access, object joining or automatic instancing.
The importer supplies a fresh staging directory and validates it before promotion.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import sys

import bpy


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--texture-size", type=int, default=1024)
    parser.add_argument("--simplify-ratio", type=float, default=0.0)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    source, target = args.input.resolve(strict=True), args.output.resolve()
    if source == target or target.exists() or any(target.parent.iterdir()):
        raise ValueError("Conversion needs a separate, empty staging directory")
    if not 16 <= args.texture_size <= 16384 or not 0 <= args.simplify_ratio <= 1:
        raise ValueError("Invalid texture size or simplification ratio")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = sorted((obj for obj in bpy.context.scene.objects if obj.type == "MESH"), key=lambda obj: obj.name)
    if not meshes:
        raise ValueError("Input has no mesh objects")
    for obj in meshes:
        if 0 < args.simplify_ratio < 1:
            modifier = obj.modifiers.new("Import reduction", "DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = args.simplify_ratio
            modifier.use_collapse_triangulate = True
    # Resize each source image once. The exporter writes complete PNG PBR maps,
    # including alpha, rather than an atlas or a rebake of the material.
    texture_dir = target.parent / "textures"
    texture_dir.mkdir()
    for index, image in enumerate(sorted(bpy.data.images, key=lambda item: item.name)):
        # Imported glTF images can carry packed source bytes. Remove that copy
        # so export cannot prefer the original image over resized pixel data.
        if image.packed_file:
            image.unpack(method="REMOVE")
        width, height = image.size[:]
        largest = max(width, height)
        if largest > args.texture_size:
            factor = args.texture_size / largest
            image.scale(max(1, round(width * factor)), max(1, round(height * factor)))
        image.file_format = "PNG"
        image.filepath_raw = str(texture_dir / f"{index:03d}_{Path(image.name).stem}.png")
        image.save()
    bpy.ops.export_scene.gltf(
        filepath=str(target), export_format="GLTF_SEPARATE", export_image_format="AUTO", export_texture_dir="textures",
        export_keep_originals=False, export_texcoords=True, export_normals=True,
        export_tangents=True, export_materials="EXPORT", export_extras=True,
        export_animations=True, export_apply=True, export_yup=True, use_selection=False,
        export_draco_mesh_compression_enable=False,
    )
    print(f"BLENDER_CONVERSION version={bpy.app.version_string} objects={len(meshes)} output={target.name}")


if __name__ == "__main__":
    main()
