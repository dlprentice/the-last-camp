#!/usr/bin/env python3
"""Offline texture baker for *The Last Camp* (Godot 4.7).

Generates the project's foliage atlases and utility maps by default. Explicit
fallback PBR studies must use --only and a separate --out directory.
The active CC0 PBR surfaces are supplied by import_textures.py;
textures/SOURCES.md records those external assets. Output is 8-bit PNGs:

* Tileable PBR sets (``{name}_albedo.png``, ``{name}_normal.png``,
  ``{name}_orm.png`` with R = ambient occlusion, G = roughness, B = height).
* Alpha-card atlases (``{name}.png`` RGBA albedo + coverage and
  ``{name}_nt.png`` RGB = normal, A = translucency).
* Utility maps (water normals, foam, caustics, packed noise, detail normal).

Conventions
-----------
* Albedo is authored and stored in sRGB; every other map is linear data.
* Normal maps use the OpenGL (Y+) convention.  With ``h[row, col]``:
  ``nx = -dh/dcol``, ``ny = +dh/drow``, ``nz = 1`` (normalised, ``*0.5+0.5``).
* Heights are normalised to 0..1 before they are packed.
* Every generator is seeded from a fixed constant plus its own tag, so the
  bake is deterministic regardless of job ordering or parallelism.
* Texture-space ``u`` runs along columns (x), ``v`` along rows (y); ``v = 0``
  is the top of the image.  Alpha cards grow upward from bottom-centre.

Usage
-----
    python3 tools/bake_textures.py                 # generated atlases/utilities only
    python3 tools/bake_textures.py --only grass --out local-data/grass-study
    python3 tools/bake_textures.py --size 512 --out local-data/atlas-preview
    python3 tools/bake_textures.py --check-tiling  # fresh review under local-data/bakes

Only numpy and Pillow are required.
"""

from __future__ import annotations

import argparse
import colorsys
import hashlib
import itertools
import math
import os
import sys
import tempfile
import time
from concurrent.futures import ProcessPoolExecutor, as_completed
from dataclasses import dataclass, field, replace
from pathlib import Path
from typing import Callable, Sequence

import numpy as np
from PIL import Image, ImageDraw

Array = np.ndarray
Coords = tuple[Array, Array]

SEED = 20260902
BASE_SIZE = 1024
PROJECT_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUT_DIR = PROJECT_ROOT / "textures"
TWO_PI = 2.0 * math.pi
F32 = np.float32


# =============================================================================
# Configuration
# =============================================================================


@dataclass(frozen=True)
class BakeConfig:
    """Resolution and output settings shared by every maker."""

    size: int = BASE_SIZE
    out_dir: Path = DEFAULT_OUT_DIR
    supersample: int = 2

    @property
    def scale(self) -> float:
        """Ratio between the requested size and the authoring size (1024)."""
        return self.size / BASE_SIZE

    def px(self, value_at_1024: float) -> float:
        """Scales a length authored in pixels-at-1024 to the current size."""
        return value_at_1024 * self.scale

    def count(self, count_at_1024: int) -> int:
        """Scales a stamp count so coverage stays constant across sizes."""
        return max(1, int(round(count_at_1024 * self.scale * self.scale)))

    @property
    def half_size(self) -> int:
        return max(64, self.size // 2)


# =============================================================================
# Small maths / colour helpers
# =============================================================================


def rng_for(tag: str) -> np.random.Generator:
    """Deterministic generator derived from the global seed and a tag."""
    digest = hashlib.sha256(f"{SEED}:{tag}".encode("utf-8")).digest()
    return np.random.default_rng(int.from_bytes(digest[:8], "little"))


def rgb(hex_code: str) -> Array:
    """Parses ``#rrggbb`` into a float32 triple in 0..1 (sRGB)."""
    code = hex_code.lstrip("#")
    if len(code) != 6:
        raise ValueError(f"expected #rrggbb, got {hex_code!r}")
    return np.array([int(code[i : i + 2], 16) / 255.0 for i in (0, 2, 4)], dtype=F32)


def clamp01(x: Array | float) -> Array:
    return np.clip(x, 0.0, 1.0)


def lerp(a: Array | float, b: Array | float, t: Array | float) -> Array:
    return a + (b - a) * t


def mix(a: Array, b: Array, t: Array | float) -> Array:
    """Blends two colours (3-vectors or HxWx3 images) by a scalar or HxW mask."""
    a_arr = np.asarray(a, dtype=F32)
    b_arr = np.asarray(b, dtype=F32)
    t_arr = np.asarray(t, dtype=F32)
    if t_arr.ndim == 2 and (a_arr.ndim == 3 or b_arr.ndim == 3 or a_arr.ndim == 1):
        t_arr = t_arr[..., None]
    return a_arr + (b_arr - a_arr) * t_arr


def smoothstep(edge0: float | Array, edge1: float | Array, x: Array) -> Array:
    """Hermite step; ``edge0 > edge1`` is allowed and inverts the ramp."""
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def normalise01(x: Array) -> Array:
    lo = float(np.min(x))
    hi = float(np.max(x))
    if hi - lo < 1e-12:
        return np.zeros_like(x, dtype=F32)
    return ((x - lo) / (hi - lo)).astype(F32)


def quantile_threshold(x: Array, coverage: float) -> float:
    """Value above which ``coverage`` (0..1) of the pixels lie."""
    return float(np.quantile(x, 1.0 - coverage))


def hsv_jitter(colour: Array, rng: np.random.Generator, hue: float = 0.0, sat: float = 0.0, val: float = 0.0) -> Array:
    """Random HSV perturbation of one colour, uniform within +-ranges."""
    h, s, v = colorsys.rgb_to_hsv(*(float(c) for c in colour))
    h = (h + rng.uniform(-hue, hue)) % 1.0
    s = float(np.clip(s + rng.uniform(-sat, sat), 0.0, 1.0))
    v = float(np.clip(v + rng.uniform(-val, val), 0.0, 1.0))
    return np.array(colorsys.hsv_to_rgb(h, s, v), dtype=F32)


def pick(rng: np.random.Generator, options: Sequence, weights: Sequence[float] | None = None):
    """Weighted random choice from a sequence of arbitrary objects."""
    probs = None
    if weights is not None:
        w = np.asarray(weights, dtype=np.float64)
        probs = w / w.sum()
    return options[int(rng.choice(len(options), p=probs))]


def uv_grid(size: int) -> Coords:
    """Pixel-centre texture coordinates in 0..1 (u along columns, v along rows)."""
    axis = ((np.arange(size, dtype=F32) + 0.5) / size).astype(F32)
    u, v = np.meshgrid(axis, axis)
    return u, v


def hash01(values: Array, salt: int) -> Array:
    """Stateless integer hash of an int array to floats in [0, 1)."""
    x = np.asarray(values).astype(np.uint32) ^ np.uint32(salt & 0xFFFFFFFF)
    x = (x ^ (x >> np.uint32(16))) * np.uint32(0x7FEB352D)
    x = (x ^ (x >> np.uint32(15))) * np.uint32(0x846CA68B)
    x = x ^ (x >> np.uint32(16))
    return (x.astype(np.float64) / 4294967296.0).astype(F32)


def _pair(value: int | float | tuple) -> tuple:
    if isinstance(value, tuple):
        return value
    return (value, value)


# =============================================================================
# Noise toolbox
# =============================================================================


@dataclass
class VoronoiField:
    """Tileable Worley noise.  Distances are in texture units (0..1 = tile)."""

    f1: Array
    f2: Array
    cell_id: Array
    dx: Array
    dy: Array
    salt: int

    @property
    def edge(self) -> Array:
        """``F2 - F1``: zero on cell boundaries, growing toward cell centres."""
        return self.f2 - self.f1

    def random(self, channel: int = 0) -> Array:
        """Per-cell uniform random in [0, 1); ``channel`` selects a stream."""
        return hash01(self.cell_id, self.salt + 7919 * channel)


class Noise:
    """Deterministic, tileable procedural noise.

    All fields are periodic over the unit square, so any map built from them
    tiles.  Coordinates are ``(u, v)`` arrays in texture space; passing an int
    ``size`` instead builds a ``size x size`` pixel-centre grid.  Periods may
    be ``(px, py)`` tuples for anisotropic (stretched) features.
    """

    def __init__(self, tag: str):
        self.rng = rng_for(f"noise:{tag}")
        self._salt = int(self.rng.integers(1, 2**31 - 1))

    @staticmethod
    def _coords(field: int | Coords) -> Coords:
        return uv_grid(field) if isinstance(field, int) else field

    @staticmethod
    def _fade(t: Array) -> Array:
        return t * t * t * (t * (t * 6.0 - 15.0) + 10.0)

    def perlin(self, field: int | Coords, period: int | tuple[int, int]) -> Array:
        """One octave of periodic gradient noise, roughly in -1..1."""
        px, py = (max(1, int(p)) for p in _pair(period))
        u, v = self._coords(field)
        angles = self.rng.uniform(0.0, TWO_PI, size=(py, px)).astype(F32)
        gx, gy = np.cos(angles), np.sin(angles)
        x = u * px
        y = v * py
        xi = np.floor(x)
        yi = np.floor(y)
        xf = (x - xi).astype(F32)
        yf = (y - yi).astype(F32)
        x0 = xi.astype(np.int64) % px
        y0 = yi.astype(np.int64) % py
        x1 = (x0 + 1) % px
        y1 = (y0 + 1) % py
        n00 = gx[y0, x0] * xf + gy[y0, x0] * yf
        n10 = gx[y0, x1] * (xf - 1.0) + gy[y0, x1] * yf
        n01 = gx[y1, x0] * xf + gy[y1, x0] * (yf - 1.0)
        n11 = gx[y1, x1] * (xf - 1.0) + gy[y1, x1] * (yf - 1.0)
        su = self._fade(xf)
        sv = self._fade(yf)
        top = n00 + su * (n10 - n00)
        bottom = n01 + su * (n11 - n01)
        return ((top + sv * (bottom - top)) * F32(math.sqrt(2.0))).astype(F32)

    def fbm(
        self,
        field: int | Coords,
        period: int | tuple[int, int],
        octaves: int = 5,
        gain: float = 0.5,
        lacunarity: float = 2.0,
        ridged: bool = False,
        turbulence: bool = False,
        normalise: bool = True,
    ) -> Array:
        """Fractal sum of gradient-noise octaves, normalised to 0..1 by default.

        ``ridged`` squares the inverted absolute value (sharp crests);
        ``turbulence`` uses the absolute value (billowy creases).
        """
        coords = self._coords(field)
        size = coords[0].shape[0]
        px, py = (float(p) for p in _pair(period))
        total = np.zeros_like(coords[0], dtype=F32)
        amplitude = 1.0
        weight_sum = 0.0
        for _ in range(octaves):
            n = self.perlin(coords, (int(round(px)), int(round(py))))
            if ridged:
                n = (1.0 - np.abs(n)) ** 2
            elif turbulence:
                n = np.abs(n)
            total += F32(amplitude) * n
            weight_sum += amplitude
            amplitude *= gain
            px = min(size, px * lacunarity)
            py = min(size, py * lacunarity)
        total /= F32(weight_sum)
        return normalise01(total) if normalise else total

    def warp_coords(self, field: int | Coords, strength: float, period: int | tuple[int, int] = 4, octaves: int = 3) -> Coords:
        """Displaces coordinates by a tileable vector noise field."""
        u, v = self._coords(field)
        du = self.fbm((u, v), period, octaves, normalise=False)
        dv = self.fbm((u, v), period, octaves, normalise=False)
        return (u + F32(strength) * du).astype(F32), (v + F32(strength) * dv).astype(F32)

    def warp(self, noise_fn: Callable[[Coords], Array], strength: float, field: int | Coords, period: int | tuple[int, int] = 4, octaves: int = 3) -> Array:
        """Domain warping: evaluates ``noise_fn`` on displaced coordinates."""
        return noise_fn(self.warp_coords(field, strength, period, octaves))

    def voronoi(self, field: int | Coords, cells: int | tuple[int, int], jitter: float = 1.0) -> VoronoiField:
        """Tileable Worley noise with a jittered, wrapped feature-point grid."""
        u, v = self._coords(field)
        cx, cy = (max(1, int(c)) for c in _pair(cells))
        jx = self.rng.random((cy, cx)).astype(F32)
        jy = self.rng.random((cy, cx)).astype(F32)
        x = u * cx
        y = v * cy
        xi = np.floor(x).astype(np.int64)
        yi = np.floor(y).astype(np.int64)
        f1 = np.full(u.shape, np.inf, dtype=F32)
        f2 = np.full(u.shape, np.inf, dtype=F32)
        best_id = np.zeros(u.shape, dtype=np.int64)
        best_dx = np.zeros(u.shape, dtype=F32)
        best_dy = np.zeros(u.shape, dtype=F32)
        for dj, di in itertools.product((-1, 0, 1), repeat=2):
            ni = xi + di
            nj = yi + dj
            wi = ni % cx
            wj = nj % cy
            fx = ni + 0.5 + jitter * (jx[wj, wi] - 0.5)
            fy = nj + 0.5 + jitter * (jy[wj, wi] - 0.5)
            ddx = ((fx - x) / cx).astype(F32)
            ddy = ((fy - y) / cy).astype(F32)
            d = np.sqrt(ddx * ddx + ddy * ddy)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            f1 = np.where(closer, d, f1)
            best_id = np.where(closer, wj * cx + wi, best_id)
            best_dx = np.where(closer, ddx, best_dx)
            best_dy = np.where(closer, ddy, best_dy)
        salt = int(self.rng.integers(1, 2**31 - 1))
        return VoronoiField(f1, f2, best_id, best_dx, best_dy, salt)

    def voronoi_1d(self, coord: Array, cells: int, jitter: float = 1.0) -> VoronoiField:
        """Worley noise along one axis: boundaries are lines of constant ``coord``.

        Useful for furrows or planks: ``edge`` is zero halfway between two
        neighbouring feature positions and grows linearly toward them.
        """
        cells = max(1, int(cells))
        offsets = self.rng.random(cells).astype(F32)
        x = coord * cells
        xi = np.floor(x).astype(np.int64)
        f1 = np.full(coord.shape, np.inf, dtype=F32)
        f2 = np.full(coord.shape, np.inf, dtype=F32)
        best_id = np.zeros(coord.shape, dtype=np.int64)
        best_dx = np.zeros(coord.shape, dtype=F32)
        for di in (-1, 0, 1):
            ni = xi + di
            wi = ni % cells
            fx = ni + 0.5 + jitter * (offsets[wi] - 0.5)
            ddx = ((fx - x) / cells).astype(F32)
            d = np.abs(ddx)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            f1 = np.where(closer, d, f1)
            best_id = np.where(closer, wi, best_id)
            best_dx = np.where(closer, ddx, best_dx)
        salt = int(self.rng.integers(1, 2**31 - 1))
        return VoronoiField(f1, f2, best_id, best_dx, np.zeros_like(best_dx), salt)

    def white(self, size: int) -> Array:
        """Uniform per-pixel noise (for grit and speckle)."""
        return self.rng.random((size, size)).astype(F32)


# =============================================================================
# Filters and derived maps
# =============================================================================


def blur(arr: Array, sigma: float | tuple[float, float]) -> Array:
    """Periodic Gaussian blur via the FFT (exactly tileable, any radius)."""
    sx, sy = (float(s) for s in _pair(sigma))
    if sx <= 0.0 and sy <= 0.0:
        return arr.astype(F32)
    if arr.ndim == 3:
        return np.stack([blur(arr[..., c], sigma) for c in range(arr.shape[2])], axis=-1)
    h, w = arr.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    transfer = np.exp(-2.0 * math.pi**2 * (sx * sx * fx * fx + sy * sy * fy * fy))
    spectrum = np.fft.rfft2(arr.astype(np.float64))
    return np.fft.irfft2(spectrum * transfer, s=arr.shape).astype(F32)


def resample(arr: Array, size: int) -> Array:
    """LANCZOS resampling of a float image (HxW or HxWxC) to ``size`` square."""
    if arr.ndim == 3:
        return np.stack([resample(arr[..., c], size) for c in range(arr.shape[2])], axis=-1)
    image = Image.fromarray(np.ascontiguousarray(arr, dtype=F32))
    return np.asarray(image.resize((size, size), Image.LANCZOS), dtype=F32)


def height_gradient(h: Array, periodic: bool = True) -> tuple[Array, Array]:
    """Central differences ``(dh/dcol, dh/drow)``; wraps at the border if periodic."""
    if periodic:
        dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
        dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
        return dx.astype(F32), dy.astype(F32)
    dy, dx = np.gradient(h.astype(F32))
    return dx, dy


def normal_vectors(h: Array, strength: float, periodic: bool = True) -> Array:
    """Unit normals (HxWx3) from a height field, OpenGL Y+ convention."""
    dx, dy = height_gradient(h, periodic)
    nx = -dx * strength
    ny = dy * strength
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + 1.0)
    return np.stack([nx * inv, ny * inv, inv], axis=-1).astype(F32)


def encode_normal(n: Array) -> Array:
    return clamp01(n * 0.5 + 0.5).astype(F32)


def normal_from_height(h: Array, strength: float, periodic: bool = True) -> Array:
    """Encoded OpenGL normal map (RGB in 0..1) from a height field."""
    return encode_normal(normal_vectors(h, strength, periodic))


def flat_normal(shape: tuple[int, int]) -> Array:
    n = np.zeros(shape + (3,), dtype=F32)
    n[..., 2] = 1.0
    return n


def ao_from_height(h: Array, radii: Sequence[float] = (2.0, 5.0, 12.0, 28.0), strength: float = 3.0) -> Array:
    """Cheap cavity/AO: how far the pixel sits below its blurred surroundings."""
    occlusion = np.zeros_like(h, dtype=F32)
    for radius in radii:
        occlusion += np.clip(blur(h, radius) - h, 0.0, None)
    occlusion /= F32(len(radii))
    return clamp01(1.0 - occlusion * strength).astype(F32)


def _box3_sum(arr: Array) -> Array:
    total = np.zeros_like(arr)
    for dy, dx in itertools.product((-1, 0, 1), repeat=2):
        total += np.roll(np.roll(arr, dy, axis=0), dx, axis=1)
    return total


def dilate_colour_into_alpha(colour: Array, alpha: Array, passes: int = 8, threshold: float = 0.02) -> Array:
    """Fills transparent pixels with nearby opaque colour so mips never fringe.

    ``colour`` may carry any number of channels (albedo, or normal + translucency).
    """
    known = alpha > threshold
    out = colour.astype(F32) * known[..., None]
    weight = known.astype(F32)
    for _ in range(passes):
        weight_sum = _box3_sum(weight)
        colour_sum = _box3_sum(out)
        fill = (weight == 0.0) & (weight_sum > 0.0)
        if not fill.any():
            break
        out[fill] = colour_sum[fill] / weight_sum[fill][:, None]
        weight[fill] = 1.0
    remaining = weight == 0.0
    if remaining.any():
        far_weight = blur(weight, 24.0)
        far_colour = blur(out, 24.0)
        mean_colour = out[weight > 0.0].mean(axis=0) if (weight > 0.0).any() else np.zeros(colour.shape[-1], F32)
        safe = far_weight > 1e-4
        far = np.where(safe[..., None], far_colour / np.maximum(far_weight, 1e-4)[..., None], mean_colour)
        out[remaining] = far[remaining]
    return clamp01(out)


def make_orm(ao: Array, roughness: Array, height: Array) -> Array:
    """Packs R = AO, G = roughness, B = height (all linear 0..1)."""
    return np.stack([clamp01(ao), clamp01(roughness), clamp01(height)], axis=-1).astype(F32)


# =============================================================================
# Output helpers
# =============================================================================


@dataclass
class Output:
    """One PNG to write: float pixels in 0..1, HxW / HxWx3 / HxWx4."""

    filename: str
    pixels: Array


@dataclass
class PBRSet:
    """A tileable material: albedo (sRGB), normal (OpenGL) and packed ORM."""

    name: str
    albedo: Array
    normal: Array
    orm: Array

    def outputs(self) -> list[Output]:
        return [
            Output(f"{self.name}_albedo.png", self.albedo),
            Output(f"{self.name}_normal.png", self.normal),
            Output(f"{self.name}_orm.png", self.orm),
        ]


BakeResult = PBRSet | Output
Maker = Callable[[BakeConfig], list[BakeResult]]


def to_uint8(arr: Array) -> Array:
    return (np.clip(arr, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def save_png(path: Path, pixels: Array) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(to_uint8(pixels)).save(path, compress_level=6)
    return path


def save_output(output: Output, out_dir: Path) -> Path:
    return save_png(out_dir / output.filename, output.pixels)


def save_set(pbr: PBRSet, out_dir: Path) -> list[Path]:
    """Writes the three files of a PBR set and returns their paths."""
    return [save_output(o, out_dir) for o in pbr.outputs()]


def grey_rgb(value: Array) -> Array:
    return np.repeat(clamp01(value)[..., None], 3, axis=-1).astype(F32)


# =============================================================================
# Stroke / stamp rasteriser
# =============================================================================


def bezier_points(p0: Sequence[float], p1: Sequence[float], p2: Sequence[float], steps: int) -> Array:
    """Quadratic Bezier sampled at ``steps`` points, shape (steps, 2)."""
    t = np.linspace(0.0, 1.0, steps, dtype=F32)[:, None]
    a = np.asarray(p0, F32)[None, :]
    b = np.asarray(p1, F32)[None, :]
    c = np.asarray(p2, F32)[None, :]
    return (1 - t) ** 2 * a + 2 * (1 - t) * t * b + t * t * c


def curve_from(base: Sequence[float], angle: float, length: float, curvature: float, steps: int) -> Array:
    """Bezier leaving ``base`` at ``angle`` (radians, y down) with lateral bow."""
    direction = np.array([math.cos(angle), math.sin(angle)], F32)
    perp = np.array([-direction[1], direction[0]], F32)
    start = np.asarray(base, F32)
    end = start + direction * length + perp * (curvature * length)
    control = start + direction * (length * 0.55)
    return bezier_points(start, control, end, steps)


def polyline_tangents(points: Array) -> Array:
    d = np.gradient(points, axis=0)
    norms = np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-6)
    return d / norms


def ellipse_polygon(cx: float, cy: float, rx: float, ry: float, angle: float, segments: int = 12) -> list[tuple[float, float]]:
    t = np.linspace(0.0, TWO_PI, segments, endpoint=False)
    x = np.cos(t) * rx
    y = np.sin(t) * ry
    ca, sa = math.cos(angle), math.sin(angle)
    return [(cx + px * ca - py * sa, cy + px * sa + py * ca) for px, py in zip(x, y)]


def _colour8(colour: Array | Sequence[float]) -> tuple[int, int, int]:
    c = np.clip(np.asarray(colour, dtype=np.float64), 0.0, 1.0)
    return tuple(int(v * 255.0 + 0.5) for v in c)


@dataclass
class Stamp:
    """A pre-rasterised patch (all arrays HxW[x3]) composited by ``alpha``."""

    rgb: Array
    alpha: Array
    height: Array
    aux: Array
    normal: Array | None = None


class StrokeBatch:
    """Vector strokes drawn with PIL into colour, coverage, height and aux layers.

    Shapes whose bounding box crosses the border are drawn again shifted by
    the canvas size so tileable canvases stay seamless.  Heights and aux are
    float layers; colour is 8-bit.
    """

    def __init__(self, res: int, wrap: bool):
        self.res = res
        self.wrap = wrap
        self.rgb = Image.new("RGB", (res, res), (0, 0, 0))
        self.mask = Image.new("L", (res, res), 0)
        self.height = Image.new("F", (res, res), 0.0)
        self.aux = Image.new("F", (res, res), 0.0)
        self._draw = [ImageDraw.Draw(im) for im in (self.rgb, self.mask, self.height, self.aux)]

    def _offsets(self, points: Sequence[tuple[float, float]], pad: float) -> list[tuple[int, int]]:
        if not self.wrap:
            return [(0, 0)]
        xs = [p[0] for p in points]
        ys = [p[1] for p in points]
        ox = [0]
        oy = [0]
        if min(xs) - pad < 0:
            ox.append(self.res)
        if max(xs) + pad >= self.res:
            ox.append(-self.res)
        if min(ys) - pad < 0:
            oy.append(self.res)
        if max(ys) + pad >= self.res:
            oy.append(-self.res)
        return list(itertools.product(ox, oy))

    def polygon(self, points: Sequence[tuple[float, float]], colour: Array, height: float, aux: float) -> None:
        """Filled polygon with constant colour, height and aux."""
        if len(points) < 3:
            return
        fills = (_colour8(colour), 255, float(height), float(aux))
        for ox, oy in self._offsets(points, 0.0):
            shifted = [(x + ox, y + oy) for x, y in points]
            for draw, fill in zip(self._draw, fills):
                draw.polygon(shifted, fill=fill)

    def line(self, points: Sequence[tuple[float, float]], width: float, colour: Array, height: float, aux: float) -> None:
        """Polyline of integer pixel ``width`` with rounded joints."""
        if len(points) < 2:
            return
        w = max(1, int(round(width)))
        fills = (_colour8(colour), 255, float(height), float(aux))
        for ox, oy in self._offsets(points, w):
            shifted = [(x + ox, y + oy) for x, y in points]
            for draw, fill in zip(self._draw, fills):
                draw.line(shifted, fill=fill, width=w, joint="curve")

    def ellipse(self, cx: float, cy: float, rx: float, ry: float, angle: float, colour: Array, height: float, aux: float) -> None:
        """Rotated ellipse (12-gon) with constant colour, height and aux."""
        self.polygon(ellipse_polygon(cx, cy, rx, ry, angle), colour, height, aux)

    def ribbon(self, points: Array, half_widths: Array, colours: Array, heights: Array | float, aux: float) -> None:
        """Tapered stroke along a polyline; per-segment colours and heights."""
        n = len(points)
        if n < 2:
            return
        tangents = polyline_tangents(points)
        perp = np.stack([-tangents[:, 1], tangents[:, 0]], axis=1)
        hw = np.asarray(half_widths, F32)[:, None]
        left = points + perp * hw
        right = points - perp * hw
        colours = np.asarray(colours, F32)
        if colours.ndim == 1:
            colours = np.repeat(colours[None, :], n - 1, axis=0)
        heights = np.broadcast_to(np.asarray(heights, F32), (n - 1,))
        for i in range(n - 1):
            quad = [tuple(left[i]), tuple(left[i + 1]), tuple(right[i + 1]), tuple(right[i])]
            self.polygon(quad, colours[i], float(heights[i]), aux)


class Canvas:
    """Supersampled paint surface with wrap-around stamping.

    Layers (float32, at ``size * supersample``): ``rgb``, ``alpha`` (coverage),
    ``height``, ``aux`` (roughness for grounds, translucency for cards) and an
    optional ``normal`` layer for alpha cards.  ``finish()`` downsamples with
    LANCZOS (premultiplied by coverage) to the target size.
    """

    def __init__(self, size: int, supersample: int = 2, wrap: bool = True, with_normal: bool = False, tag: str = "canvas"):
        self.size = size
        self.ss = supersample
        self.res = size * supersample
        self.wrap = wrap
        shape = (self.res, self.res)
        self.rgb = np.zeros(shape + (3,), F32)
        self.alpha = np.zeros(shape, F32)
        self.height = np.zeros(shape, F32)
        self.aux = np.zeros(shape, F32)
        self.normal = flat_normal(shape) if with_normal else None
        self._blotch_noise = Noise(f"{tag}-blotch")
        self._blotch: dict[int, Array] = {}

    def blotch(self, period: int) -> Array:
        """A 256x256 tileable fbm tile used for per-pixel colour variation."""
        if period not in self._blotch:
            self._blotch[period] = self._blotch_noise.fbm(256, period, 4)
        return self._blotch[period]

    def begin(self) -> StrokeBatch:
        return StrokeBatch(self.res, self.wrap)

    def commit(self, batch: StrokeBatch, cylinder_sigma: float | None = None) -> None:
        """Composites a stroke batch; optional rounded normals from its height."""
        a = np.asarray(batch.mask, dtype=F32) / 255.0
        colour = np.asarray(batch.rgb, dtype=F32) / 255.0
        height = np.asarray(batch.height, dtype=F32)
        aux = np.asarray(batch.aux, dtype=F32)
        self.rgb = self.rgb * (1.0 - a)[..., None] + colour * a[..., None]
        self.height = self.height * (1.0 - a) + height * a
        self.aux = self.aux * (1.0 - a) + aux * a
        self.alpha = self.alpha + a * (1.0 - self.alpha)
        if self.normal is not None:
            rounded = normal_vectors(blur(height, cylinder_sigma or 1.0), 1.0, periodic=True)
            self.normal = self._blend_normals(self.normal, rounded, a)

    @staticmethod
    def _blend_normals(base: Array, top: Array, a: Array) -> Array:
        n = base * (1.0 - a)[..., None] + top * a[..., None]
        n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
        return n.astype(F32)

    def stamp(self, st: Stamp, x0: int, y0: int) -> None:
        """Composites a stamp whose top-left lands on canvas pixel ``(x0, y0)``."""
        h, w = st.alpha.shape
        if self.wrap:
            if h >= self.res or w >= self.res:
                raise ValueError("stamp larger than canvas")
            rows = np.arange(y0, y0 + h) % self.res
            cols = np.arange(x0, x0 + w) % self.res
            idx = np.ix_(rows, cols)
            sub = st
        else:
            ys0, xs0 = max(0, -y0), max(0, -x0)
            ys1, xs1 = min(h, self.res - y0), min(w, self.res - x0)
            if ys1 <= ys0 or xs1 <= xs0:
                return
            idx = (slice(y0 + ys0, y0 + ys1), slice(x0 + xs0, x0 + xs1))
            crop = (slice(ys0, ys1), slice(xs0, xs1))
            sub = Stamp(
                st.rgb[crop], st.alpha[crop], st.height[crop], st.aux[crop],
                None if st.normal is None else st.normal[crop],
            )
        a = sub.alpha
        a3 = a[..., None]
        self.rgb[idx] = self.rgb[idx] * (1.0 - a3) + sub.rgb * a3
        self.height[idx] = self.height[idx] * (1.0 - a) + sub.height * a
        self.aux[idx] = self.aux[idx] * (1.0 - a) + sub.aux * a
        self.alpha[idx] = self.alpha[idx] + a * (1.0 - self.alpha[idx])
        if self.normal is not None and sub.normal is not None:
            self.normal[idx] = self._blend_normals(self.normal[idx], sub.normal, a)

    def finish(self) -> "CanvasResult":
        """Downsamples every layer to the target size (coverage-premultiplied)."""
        s = self.size
        alpha = resample(self.alpha, s)
        safe = np.maximum(alpha, 1e-3)
        present = alpha > 1e-3

        def unpremultiply(layer: Array) -> Array:
            pm = resample(layer * (self.alpha[..., None] if layer.ndim == 3 else self.alpha), s)
            out = pm / (safe[..., None] if layer.ndim == 3 else safe)
            return np.where((present[..., None] if layer.ndim == 3 else present), out, 0.0).astype(F32)

        colour = clamp01(unpremultiply(self.rgb))
        height = unpremultiply(self.height)
        aux = unpremultiply(self.aux)
        normal = None
        if self.normal is not None:
            n = unpremultiply(self.normal)
            n[..., 2] = np.where(present, n[..., 2], 1.0)
            n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
            normal = n.astype(F32)
        return CanvasResult(colour, clamp01(alpha).astype(F32), height, aux, normal)


@dataclass
class CanvasResult:
    """Downsampled canvas layers at target size (``normal`` only for cards)."""

    rgb: Array
    alpha: Array
    height: Array
    aux: Array
    normal: Array | None

    def over(self, base_rgb: Array, base_height: Array, base_aux: Array) -> tuple[Array, Array, Array]:
        """Composites the stamps over a procedural base (ground textures)."""
        a = self.alpha
        colour = base_rgb * (1.0 - a)[..., None] + self.rgb * a[..., None]
        height = base_height * (1.0 - a) + self.height * a
        aux = base_aux * (1.0 - a) + self.aux * a
        return clamp01(colour).astype(F32), height.astype(F32), clamp01(aux).astype(F32)


# =============================================================================
# Analytic leaf stamps
# =============================================================================


@dataclass(frozen=True)
class LeafShape:
    """Leaf silhouette: half-width profile ``t^p (1-t)^q`` plus margin detail."""

    base_power: float
    tip_power: float
    aspect: float
    teeth: int = 0
    tooth_depth: float = 0.0
    fine_teeth: int = 0
    fine_depth: float = 0.0
    lobes: int = 0
    lobe_depth: float = 0.0

    def half_width(self, t: Array, side: Array) -> tuple[Array, Array]:
        """Smooth and margin half-widths (fractions of length) at ``t`` in 0..1."""
        tc = np.clip(t, 0.0, 1.0)
        peak_t = self.base_power / (self.base_power + self.tip_power)
        peak = peak_t**self.base_power * (1.0 - peak_t) ** self.tip_power
        smooth = self.aspect * (tc**self.base_power * (1.0 - tc) ** self.tip_power) / peak
        margin = smooth
        if self.lobes:
            phase = np.where(side > 0, 0.0, math.pi / self.lobes * 0.7)
            sinus = (0.5 - 0.5 * np.cos(TWO_PI * self.lobes * tc + phase)) ** 2.2
            margin = margin * (1.0 - self.lobe_depth * sinus)
        if self.teeth:
            margin = margin * (1.0 - self.tooth_depth * (1.0 - np.modf(self.teeth * tc)[0]))
        if self.fine_teeth:
            margin = margin * (1.0 - self.fine_depth * (1.0 - np.modf(self.fine_teeth * tc)[0]))
        return smooth.astype(F32), margin.astype(F32)


OVATE = LeafShape(0.75, 1.5, 0.30, teeth=14, tooth_depth=0.10)
ELLIPTIC = LeafShape(1.1, 1.25, 0.27, teeth=16, tooth_depth=0.06)
OAK = LeafShape(1.3, 0.9, 0.32, lobes=4, lobe_depth=0.5)
BIRCH = LeafShape(0.55, 1.7, 0.36, teeth=9, tooth_depth=0.13, fine_teeth=27, fine_depth=0.05)
LANCEOLATE = LeafShape(0.8, 1.8, 0.15)
CLOVER = LeafShape(1.6, 0.55, 0.42)
PETAL = LeafShape(1.4, 0.5, 0.5)


@dataclass
class LeafStyle:
    """Everything that shapes and shades one leaf stamp."""

    shape: LeafShape
    colour: Array
    aux: float
    edge_darken: float = 0.25
    tip_tint: float = 0.10
    midrib: float = 0.012
    rib_lighten: float = 0.22
    rib_depth: float = 0.15
    veins: int = 5
    vein_darken: float = 0.12
    vein_depth: float = 0.25
    vein_aux_scale: float = 0.5  # how much veins reduce ``aux`` (translucency on cards)
    rib_aux_scale: float = 0.6  # same for the midrib; zero for ground leaves (aux = roughness)
    bend: float = 0.0
    curl: float = 0.0
    dome: float = 1.0
    dome_px_factor: float = 0.35
    blotch: float = 0.12
    blotch_period: int = 8
    chevron: float = 0.0
    petiole: float = 0.12
    petiole_width_px: float = 2.0
    petiole_colour: Array = field(default_factory=lambda: rgb("#4a4a24"))
    petiole_aux: float = 0.1
    height_base: float = 0.5
    height_thickness: float = 0.15


def leaf_stamp(style: LeafStyle, base: tuple[float, float], angle: float, length_px: float, canvas: Canvas) -> tuple[Stamp, int, int]:
    """Rasterises one leaf analytically; returns the stamp and its top-left.

    ``base`` is where the petiole leaves the twig (canvas pixels), ``angle``
    the direction of the midrib (radians, y down) and ``length_px`` the blade
    length at canvas resolution; the blade starts one petiole length from
    ``base``.  Edges are anti-aliased analytically and the dome/vein height is
    evaluated unmasked so normals stay smooth right up to the edge.
    """
    shape = style.shape
    L = float(length_px)
    petiole_len = style.petiole * L
    half = int(math.ceil(max((L + petiole_len) * 0.5, (shape.aspect + abs(style.bend)) * L))) + 3
    ca, sa = math.cos(angle), math.sin(angle)
    centre_x = base[0] + ca * (L * 0.5 + petiole_len * 0.5)
    centre_y = base[1] + sa * (L * 0.5 + petiole_len * 0.5)
    x0 = int(round(centre_x - half))
    y0 = int(round(centre_y - half))
    ys, xs = np.mgrid[0 : 2 * half, 0 : 2 * half].astype(F32)
    X = (x0 + xs + 0.5) - base[0]
    Y = (y0 + ys + 0.5) - base[1]
    t_px = X * ca + Y * sa - petiole_len
    s_px = -X * sa + Y * ca
    t = t_px / L
    s = s_px / L - style.bend * t * t

    smooth, margin = shape.half_width(t, s)
    inside_t = (t >= 0.0) & (t <= 1.0)
    sd = np.where(inside_t, (margin - np.abs(s)) * L, -1.0)
    coverage = clamp01(sd + 0.5)
    sd_petiole = np.minimum(style.petiole_width_px * 0.5 - np.abs(s_px), np.minimum(-t_px, t_px + petiole_len))
    cov_petiole = clamp01(sd_petiole + 0.5) if petiole_len > 0 else np.zeros_like(coverage)
    alpha = np.maximum(coverage, cov_petiole)

    smooth_safe = np.maximum(smooth, 0.004)
    rel = np.clip(s / smooth_safe, -2.0, 2.0)
    rel_abs = np.abs(rel)
    tc = np.clip(t, 0.0, 1.0)

    colour = np.broadcast_to(style.colour, X.shape + (3,)).astype(F32)
    colour = colour * (1.0 + style.tip_tint * (tc - 0.5))[..., None]
    edge = smoothstep(0.6, 1.0, rel_abs) * style.edge_darken
    colour = colour * (1.0 - edge)[..., None]

    rib_px = style.midrib * L
    rib = smoothstep(rib_px + 0.8, max(rib_px - 0.6, 0.0), np.abs(s) * L) * smoothstep(0.0, 0.03, tc) * smoothstep(0.95, 0.85, tc)
    rib_colour = colour * np.array([1.15, 1.2, 0.85], F32)
    colour = mix(colour, rib_colour, rib * style.rib_lighten)

    vein = np.zeros_like(t)
    if style.veins > 0:
        slope = 0.84
        vein_w = 0.9
        for k in range(style.veins):
            tk = 0.10 + 0.72 * k / max(style.veins - 1, 1)
            curve = 0.6 * np.abs(s) * np.abs(s) / max(shape.aspect, 0.05)
            d = np.abs(t - (tk + np.abs(s) * slope + curve)) * L / math.sqrt(1.0 + slope * slope)
            vk = smoothstep(vein_w + 0.6, max(vein_w - 0.4, 0.0), d) * (t > tk) * (rel_abs < 0.92)
            vein = np.maximum(vein, vk)
        vein *= 1.0 - 0.35 * np.clip(rel_abs, 0.0, 1.0)
        colour = colour * (1.0 - style.vein_darken * vein)[..., None]

    if style.chevron > 0.0:
        band = np.exp(-(((tc - (0.40 + 0.22 * rel_abs)) / 0.05) ** 2))
        colour = mix(colour, np.array([0.86, 0.9, 0.78], F32), band * style.chevron * (rel_abs < 0.9))

    if style.blotch > 0.0:
        tile = canvas.blotch(style.blotch_period)
        sample = tile[(ys.astype(np.int64) + y0) % 256, (xs.astype(np.int64) + x0) % 256]
        colour = colour * (1.0 + style.blotch * (sample - 0.5) * 2.0)[..., None]

    is_petiole = cov_petiole > coverage
    colour = np.where(is_petiole[..., None], style.petiole_colour, colour)

    along = 1.0 - 0.25 * (2.0 * tc - 1.0) ** 2
    dome = (1.0 - rel * rel) * along
    h_local = style.dome * dome + style.curl * np.clip(rel, -1.5, 1.5) ** 2 - style.vein_depth * vein - style.rib_depth * rib
    amplitude = style.dome_px_factor * shape.aspect * L
    normal = normal_vectors(h_local * amplitude, 1.0, periodic=False)

    aux = style.aux * (1.0 - style.vein_aux_scale * vein) * (1.0 - style.rib_aux_scale * rib)
    aux = np.where(is_petiole, style.petiole_aux, aux)
    height = style.height_base + style.height_thickness * np.clip(h_local, -0.5, 1.5)
    height = np.where(is_petiole, style.height_base, height)
    return Stamp(clamp01(colour).astype(F32), alpha.astype(F32), height.astype(F32), aux.astype(F32), normal), x0, y0


def place_leaf(canvas: Canvas, style: LeafStyle, base: tuple[float, float], angle: float, length_px: float) -> None:
    st, x0, y0 = leaf_stamp(style, base, angle, length_px, canvas)
    canvas.stamp(st, x0, y0)


# =============================================================================
# Stroke families shared by several makers
# =============================================================================


def draw_blade(batch: StrokeBatch, rng: np.random.Generator, base: tuple[float, float], angle: float, length: float, half_width: float, colour: Array, heights: tuple[float, float], aux: float, curvature: float = 0.3, highlight: float = 0.14) -> None:
    """A tapered, gently curved grass blade with a lengthwise highlight."""
    pts = curve_from(base, angle, length, rng.uniform(-curvature, curvature), 9)
    u = np.linspace(0.0, 1.0, len(pts), dtype=F32)
    hw = np.maximum(half_width * (1.0 - u) ** 0.9, 0.55)
    um = (u[:-1] + u[1:]) * 0.5
    colours = colour[None, :] * lerp(0.72, 1.05, um)[:, None]
    seg_heights = lerp(heights[0], heights[1], um)
    batch.ribbon(pts, hw, colours, seg_heights, aux)
    if highlight > 0.0:
        tangents = polyline_tangents(pts)
        perp = np.stack([-tangents[:, 1], tangents[:, 0]], axis=1)
        side = 1.0 if rng.random() < 0.5 else -1.0
        core = pts + perp * (hw * 0.35 * side)[:, None]
        keep = slice(1, len(pts) - 1)
        batch.ribbon(core[keep], hw[keep] * 0.3, colours[keep.start : keep.stop - 1] * (1.0 + highlight), seg_heights[keep.start : keep.stop - 1], aux)


def draw_wisp(batch: StrokeBatch, rng: np.random.Generator, base: tuple[float, float], angle: float, length: float, width: float, colour: Array, height: float, aux: float) -> None:
    """A thin dead-grass strand lying flat (both ends taper)."""
    pts = curve_from(base, angle, length, rng.uniform(-0.25, 0.25), 8)
    u = np.linspace(0.0, 1.0, len(pts), dtype=F32)
    hw = np.maximum(width * 0.5 * np.sqrt(np.clip(np.sin(math.pi * u), 0.0, 1.0)), 0.5)
    batch.ribbon(pts, hw, colour, height, aux)


def draw_twig(batch: StrokeBatch, rng: np.random.Generator, base: tuple[float, float], angle: float, length: float, width: float, colour: Array, height: float, aux: float, segments: int = 4, wobble: float = 0.35, outline: bool = True) -> Array:
    """A slightly kinked woody twig with a dark outline and a lit core line."""
    pts = [np.asarray(base, F32)]
    a = angle
    for _ in range(segments):
        a += rng.uniform(-wobble, wobble)
        step = length / segments
        pts.append(pts[-1] + np.array([math.cos(a), math.sin(a)], F32) * step)
    poly = [tuple(p) for p in pts]
    if outline:
        batch.line(poly, width + 2.0, colour * 0.55, height * 0.97, aux)
    batch.line(poly, width, colour, height, aux)
    shifted = [(x - 0.35 * width, y - 0.35 * width) for x, y in poly]
    batch.line(shifted, max(1.0, width * 0.35), colour * 1.25, height, aux)
    return np.asarray(pts, F32)


def draw_needle_stroke(batch: StrokeBatch, base: tuple[float, float], angle: float, length: float, width: float, colour: Array, tip_colour: Array, height: float, aux: float, curvature: float) -> None:
    """A short conifer needle: slightly curved, lighter toward the tip."""
    pts = curve_from(base, angle, length, curvature, 4)
    poly = [tuple(p) for p in pts]
    batch.line(poly[:3], width, colour, height, aux)
    batch.line(poly[2:], max(1.0, width * 0.8), tip_colour, height, aux)


# =============================================================================
# Ground sets
# =============================================================================

GRASS_GREENS = ("#3d6b22", "#7fa23a")
GRASS_DRY = ("#8b8a3c", "#a58f3a")
GRASS_BLUE = "#37613f"


def random_blade_colour(rng: np.random.Generator) -> Array:
    """Mostly mid greens, 15% dry yellow blades, a few dark blue-greens."""
    roll = rng.random()
    if roll < 0.15:
        colour = rgb(pick(rng, GRASS_DRY))
        return hsv_jitter(colour, rng, hue=0.01, sat=0.08, val=0.08)
    if roll < 0.19:
        return hsv_jitter(rgb(GRASS_BLUE), rng, hue=0.02, sat=0.1, val=0.08)
    colour = mix(rgb(GRASS_GREENS[0]), rgb(GRASS_GREENS[1]), rng.random() ** 0.8)
    return hsv_jitter(colour, rng, hue=0.015, sat=0.08, val=0.06)


def draw_blade_field(batch: StrokeBatch, rng: np.random.Generator, cfg: BakeConfig, count: int, ss: int, shade: tuple[float, float], clumps: Array) -> None:
    """Blades in draw order; ``shade`` darkens early (buried) blades to late ones.

    ``clumps`` is a tileable field sampled at each blade's root so patches of
    turf read lighter or darker (lawns are never uniformly green).
    """
    res = cfg.size * ss
    n = clumps.shape[0]
    for i in range(count):
        base = (rng.uniform(0, res), rng.uniform(0, res))
        length = cfg.px(rng.uniform(40, 140)) * ss
        half_width = cfg.px(rng.uniform(3.0, 6.0)) * ss * 0.5
        depth = i / max(count - 1, 1)
        heights = (lerp(0.36, 0.5, depth) + rng.uniform(0.0, 0.06), lerp(0.7, 0.9, depth) + rng.uniform(0.0, 0.1))
        aux = rng.uniform(0.65, 0.80)
        clump = float(clumps[int(base[1] / res * n) % n, int(base[0] / res * n) % n])
        colour = random_blade_colour(rng) * lerp(shade[0], shade[1], depth) * lerp(0.82, 1.12, clump)
        draw_blade(batch, rng, base, rng.uniform(0, TWO_PI), length, half_width, colour, heights, aux)


def place_clover(canvas: Canvas, rng: np.random.Generator, centre: tuple[float, float], size_px: float, height: float) -> None:
    """Three chevron-marked leaflets around ``centre``."""
    colour = hsv_jitter(rgb("#4a7a3a"), rng, hue=0.02, sat=0.08, val=0.08)
    style = LeafStyle(
        shape=CLOVER, colour=colour, aux=0.7, edge_darken=0.2, veins=0, midrib=0.01, rib_lighten=0.3,
        rib_aux_scale=0.0, chevron=0.55, petiole=0.0, blotch=0.08, height_base=height, height_thickness=0.06, dome=0.6,
    )
    start = rng.uniform(0, TWO_PI)
    for k in range(3):
        angle = start + k * TWO_PI / 3.0 + rng.uniform(-0.15, 0.15)
        place_leaf(canvas, style, centre, angle, size_px * rng.uniform(0.9, 1.1))


def make_grass(cfg: BakeConfig) -> list[BakeResult]:
    """Dense turf from above: thousands of blades over dark soil and thatch."""
    size = cfg.size
    noise = Noise("grass")
    rng = rng_for("grass-stamps")
    soil = noise.fbm(size, 6, 5)
    grain = noise.fbm(size, 160, 2)
    base_rgb = mix(rgb("#1e1a10"), rgb("#33291b"), soil) * (0.8 + 0.4 * grain)[..., None]
    base_height = 0.12 * soil + 0.06 * grain
    base_rough = 0.85 + 0.05 * grain
    clumps = noise.fbm(256, 3, 3)

    canvas = Canvas(size, cfg.supersample, wrap=True, tag="grass")
    ss = canvas.ss
    res = canvas.res

    thatch = canvas.begin()
    for _ in range(cfg.count(320)):
        colour = hsv_jitter(mix(rgb("#4d452c"), rgb("#6b6244"), rng.random()), rng, hue=0.01, sat=0.1, val=0.06)
        draw_wisp(thatch, rng, (rng.uniform(0, res), rng.uniform(0, res)), rng.uniform(0, TWO_PI), cfg.px(rng.uniform(60, 160)) * ss, cfg.px(rng.uniform(1.5, 3.0)) * ss, colour, rng.uniform(0.26, 0.38), 0.85)
    canvas.commit(thatch)

    total = cfg.count(9000)
    first = int(total * 0.7)
    blades = canvas.begin()
    draw_blade_field(blades, rng, cfg, first, ss, (0.62, 0.9), clumps)
    canvas.commit(blades)
    for _ in range(cfg.count(60)):
        place_clover(canvas, rng, (rng.uniform(0, res), rng.uniform(0, res)), cfg.px(rng.uniform(18, 26)) * ss, rng.uniform(0.55, 0.65))
    blades = canvas.begin()
    draw_blade_field(blades, rng, cfg, total - first, ss, (0.9, 1.0), clumps)
    canvas.commit(blades)

    albedo, height, rough = canvas.finish().over(base_rgb, base_height, base_rough)
    height = normalise01(height)
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 5, 12, 28)], strength=2.5)
    normal = normal_from_height(blur(height, 0.6 * cfg.scale), 3.0)
    return [PBRSet("grass", albedo, normal, make_orm(ao, rough, height))]


LITTER_LEAF_COLOURS = ("#6b4a2a", "#8a5a2b", "#a97a3c", "#4a3520")


def litter_leaf_style(rng: np.random.Generator, order: float, ss: int) -> LeafStyle:
    """A fallen leaf: rust/ochre palette, curled edges; ``order`` 0..1 raises later leaves."""
    shape = pick(rng, (OVATE, ELLIPTIC, OAK), (0.55, 0.15, 0.30))
    if rng.random() < 0.10:
        colour = rgb("#b8952f")
    else:
        colour = rgb(pick(rng, LITTER_LEAF_COLOURS))
    colour = hsv_jitter(colour, rng, hue=0.02, sat=0.12, val=0.10)
    return LeafStyle(
        shape=shape, colour=colour, aux=rng.uniform(0.78, 0.9), edge_darken=0.38, tip_tint=0.05,
        midrib=0.012, rib_lighten=0.18, veins=int(rng.integers(4, 7)), vein_darken=0.16, vein_depth=0.15,
        vein_aux_scale=0.03, rib_aux_scale=0.03, bend=rng.uniform(-0.15, 0.15), curl=rng.uniform(0.15, 0.5), dome=0.35, blotch=0.28, blotch_period=6,
        petiole=rng.uniform(0.08, 0.16), petiole_width_px=1.6 * ss, petiole_colour=rgb("#3a2a18"), petiole_aux=0.85,
        height_base=0.30 + 0.32 * order, height_thickness=0.14,
    )


def draw_needle_litter(batch: StrokeBatch, rng: np.random.Generator, cfg: BakeConfig, count: int, ss: int) -> None:
    """Scattered fallen pine needles (30% as attached pairs)."""
    res = cfg.size * ss
    for _ in range(count):
        base = (rng.uniform(0, res), rng.uniform(0, res))
        angle = rng.uniform(0, TWO_PI)
        colour = hsv_jitter(mix(rgb("#6e5832"), rgb("#8c7848"), rng.random()), rng, hue=0.01, sat=0.1, val=0.08)
        height = rng.uniform(0.55, 0.75)
        pair = rng.random() < 0.3
        for k in range(2 if pair else 1):
            a = angle + (k - 0.5) * rng.uniform(0.15, 0.3) if pair else angle
            pts = curve_from(base, a, cfg.px(rng.uniform(30, 70)) * ss, rng.uniform(-0.1, 0.1), 4)
            batch.line([tuple(p) for p in pts], cfg.px(rng.uniform(1.0, 2.0)) * ss, colour, height, 0.8)


def make_litter(cfg: BakeConfig) -> list[BakeResult]:
    """Forest floor: humus, moss, fallen leaves, twigs and pine needles."""
    size = cfg.size
    noise = Noise("litter")
    rng = rng_for("litter-stamps")
    humus = noise.fbm(size, 5, 6)
    fine = noise.fbm(size, 140, 2)
    base_rgb = mix(rgb("#22190f"), rgb("#3a2c1c"), humus) * (0.85 + 0.3 * fine)[..., None]
    base_height = 0.08 * humus + 0.04 * fine
    base_rough = 0.95 - 0.03 * fine
    moss_field = noise.warp(lambda c: noise.fbm(c, 5, 5), 0.06, size)
    thr = quantile_threshold(moss_field, 0.08)
    moss = smoothstep(thr - 0.03, thr + 0.03, moss_field)
    moss_tex = noise.fbm(size, 200, 2)
    moss_bumps = noise.fbm(size, 90, 3, turbulence=True)
    moss_rgb = mix(rgb("#2a3f1c"), rgb("#465f2a"), 0.6 * moss_tex + 0.4 * moss_bumps) * (0.85 + 0.3 * moss_bumps)[..., None]
    base_rgb = mix(base_rgb, moss_rgb, moss)
    base_height = base_height + moss * (0.08 + 0.05 * moss_tex + 0.04 * moss_bumps)
    base_rough = lerp(base_rough, 0.95, moss)

    canvas = Canvas(size, cfg.supersample, wrap=True, tag="litter")
    ss = canvas.ss
    res = canvas.res

    under = canvas.begin()
    draw_needle_litter(under, rng, cfg, cfg.count(160), ss)
    canvas.commit(under)

    leaf_count = cfg.count(330)
    for i in range(leaf_count):
        style = litter_leaf_style(rng, i / leaf_count, ss)
        place_leaf(canvas, style, (rng.uniform(0, res), rng.uniform(0, res)), rng.uniform(0, TWO_PI), cfg.px(rng.uniform(40, 110)) * ss)

    top = canvas.begin()
    draw_needle_litter(top, rng, cfg, cfg.count(260), ss)
    for _ in range(cfg.count(55)):
        colour = hsv_jitter(mix(rgb("#4a3a28"), rgb("#6a5540"), rng.random()), rng, hue=0.01, sat=0.1, val=0.06)
        width = cfg.px(rng.uniform(2.0, 4.0)) * ss
        base = (rng.uniform(0, res), rng.uniform(0, res))
        pts = draw_twig(top, rng, base, rng.uniform(0, TWO_PI), cfg.px(rng.uniform(60, 200)) * ss, width, colour, rng.uniform(0.86, 1.0), 0.85)
        if rng.random() < 0.4:
            k = int(rng.integers(1, len(pts) - 1))
            a = math.atan2(pts[k + 1][1] - pts[k][1], pts[k + 1][0] - pts[k][0]) + rng.choice([-1, 1]) * rng.uniform(0.5, 0.9)
            draw_twig(top, rng, tuple(pts[k]), a, cfg.px(rng.uniform(20, 50)) * ss, width * 0.6, colour, 0.9, 0.85, segments=2)
    canvas.commit(top)

    albedo, height, rough = canvas.finish().over(base_rgb, base_height, base_rough)
    height = normalise01(height)
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 5, 12, 28)], strength=2.5)
    return [PBRSet("litter", albedo, normal_from_height(height, 3.0), make_orm(ao, rough, height))]


@dataclass
class PebbleField:
    """Domed, elliptical pebbles derived from a Voronoi field."""

    mask: Array
    dome: Array
    radius_px: Array
    cell_random: Array


def pebbles(vor: VoronoiField, size: int, radius_px: tuple[float, float], density: float, density_map: Array | None = None) -> PebbleField:
    """Pebble presence, radius, aspect and orientation are per Voronoi cell.

    ``density_map`` (0..1+, low frequency) makes pebbles cluster in patches.
    """
    radius = lerp(radius_px[0], radius_px[1], vor.random(1) ** 1.6)
    gate = density if density_map is None else density * density_map
    present = vor.random(2) < gate
    aspect = lerp(0.65, 1.0, vor.random(3))
    angle = vor.random(4) * math.pi
    dx = vor.dx * size
    dy = vor.dy * size
    ca, sa = np.cos(angle), np.sin(angle)
    lx = dx * ca + dy * sa
    ly = (-dx * sa + dy * ca) / aspect
    d = np.sqrt(lx * lx + ly * ly)
    mask = smoothstep(radius + 0.7, radius - 0.7, d) * present
    dome = np.sqrt(np.clip(1.0 - (d / np.maximum(radius, 1e-3)) ** 2, 0.0, 1.0)) * present
    return PebbleField(mask.astype(F32), dome.astype(F32), radius.astype(F32), vor.random(5))


def pebble_colour(field: PebbleField, palette: Sequence[str], speckle: Array, mottle: Array) -> Array:
    """Per-pebble palette pick, mottled, speckled and shaded darker toward the rim."""
    colours = np.stack([rgb(c) for c in palette])
    idx = np.minimum((field.cell_random * len(palette)).astype(np.int64), len(palette) - 1)
    colour = colours[idx] * (0.82 + 0.36 * speckle)[..., None] * (0.9 + 0.2 * mottle)[..., None]
    colour = colour * (0.68 + 0.32 * field.dome)[..., None]
    return colour.astype(F32)


def speckle_masks(noise: Noise, size: int, scale: float, light: float, dark: float) -> tuple[Array, Array]:
    """Irregular 1-3 px grains: fractions ``light``/``dark`` of blurred white noise."""
    w = blur(noise.white(size), 0.7 * scale)
    hi = quantile_threshold(w, light)
    lo = float(np.quantile(w, dark))
    spread = float(np.std(w)) * 0.35
    return smoothstep(hi - spread, hi + spread, w), smoothstep(lo + spread, lo - spread, w)


def make_mud(cfg: BakeConfig) -> list[BakeResult]:
    """Wet pond-shore mud with embedded pebbles and drying cracks."""
    size = cfg.size
    noise = Noise("mud")
    macro = noise.fbm(size, 3, 4)
    clods = noise.fbm(size, 40, 3, turbulence=True)
    grain = noise.fbm(size, 120, 2)
    grit = blur(noise.white(size), 0.6 * cfg.scale)
    sand, dark_grit = speckle_masks(noise, size, cfg.scale, 0.05, 0.05)
    coords = noise.warp_coords(size, 0.03, 6, 2)
    vor = noise.voronoi(size, 44, 0.95)
    peb = pebbles(vor, size, (cfg.px(4.0), cfg.px(15.0)), 0.55, lerp(0.3, 1.5, noise.fbm(size, 4, 3)))
    cracks_v = noise.voronoi(coords, 12, 0.9)
    dry = smoothstep(0.45, 0.65, macro)
    crack_w = 0.0025 + 0.0025 * noise.fbm(size, 20, 2)
    crack = smoothstep(crack_w, crack_w * 0.3, cracks_v.edge) * dry
    wet = smoothstep(0.55, 0.25, macro)

    soil = mix(rgb("#2b2420"), rgb("#4a3d33"), 0.35 * grain + 0.35 * macro + 0.3 * clods)
    soil = soil * (0.88 + 0.24 * grit)[..., None]
    soil = mix(soil, rgb("#6a5c48"), sand * 0.6)
    soil = soil * (1.0 - 0.4 * dark_grit)[..., None]
    soil = soil * (1.0 - 0.35 * wet)[..., None]
    soil = soil * (1.0 - 0.5 * crack)[..., None]
    contact = clamp01(blur(peb.mask, 3.0 * cfg.scale) * 1.8 - peb.mask)
    soil = soil * (1.0 - 0.35 * contact)[..., None]
    stone = pebble_colour(peb, ("#6a6660", "#8a7a62", "#9c948a", "#4e4a46", "#7d7268"), noise.fbm(size, 300, 2), noise.fbm(size, 60, 2))
    stone = stone * (1.0 - 0.25 * wet)[..., None]
    albedo = mix(soil, stone, peb.mask)

    sink = np.clip(peb.dome - 0.25, 0.0, 1.0) * (peb.radius_px / cfg.px(15.0))
    height = macro * 0.45 + clods * 0.06 + grain * 0.03 + grit * 0.02 + sink * 0.55 * peb.mask - crack * 0.06
    height = normalise01(height)
    rough_soil = lerp(0.55, 0.30, wet) + 0.06 * grain
    rough = lerp(rough_soil, lerp(0.62, 0.5, wet), peb.mask)
    rough = np.maximum(rough, crack * 0.6)
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 5, 12, 28)], strength=3.0)
    return [PBRSet("mud", clamp01(albedo), normal_from_height(height, 4.0), make_orm(ao, rough, height))]


def make_path(cfg: BakeConfig) -> list[BakeResult]:
    """Compacted trodden dirt with grit and dry wisps (no ruts: the texture tiles
    in world space, so straight ruts would cut across the trail's bends)."""
    size = cfg.size
    noise = Noise("path")
    rng = rng_for("path-stamps")
    u, v = uv_grid(size)
    soil_var = noise.fbm(size, 5, 5)
    mid = noise.fbm(size, 24, 3)
    clods = noise.fbm(size, 36, 3, turbulence=True)
    grit = blur(noise.white(size), 0.5 * cfg.scale)
    light_grit, dark_grit = speckle_masks(noise, size, cfg.scale, 0.06, 0.05)
    wear = noise.fbm(size, (12, 2), 3)
    wobble = (noise.fbm(size, (1, 3), 2) - 0.5) * 0.05
    uw = u + wobble
    ruts = np.zeros_like(uw)
    vor = noise.voronoi(size, 60, 0.95)
    peb = pebbles(vor, size, (cfg.px(3.0), cfg.px(9.0)), 0.25, lerp(0.4, 1.5, noise.fbm(size, 5, 3)))

    soil = mix(rgb("#5a4a38"), rgb("#7a6a55"), 0.45 * soil_var + 0.3 * mid + 0.25 * clods)
    soil = soil * (0.88 + 0.24 * grit)[..., None]
    soil = soil * (0.92 + 0.08 * wear)[..., None]
    soil = mix(soil, rgb("#9a8a70"), light_grit * 0.7)
    soil = soil * (1.0 - 0.35 * dark_grit)[..., None]
    soil = soil * (1.0 - 0.15 * ruts)[..., None]
    stone = pebble_colour(peb, ("#7a7068", "#8f8578", "#6a625a", "#9a9086"), noise.fbm(size, 300, 2), noise.fbm(size, 60, 2))
    stone = mix(stone, soil, 0.25)
    contact = clamp01(blur(peb.mask, 2.5 * cfg.scale) * 1.8 - peb.mask)
    soil = soil * (1.0 - 0.2 * contact)[..., None]
    base_rgb = mix(soil, stone, peb.mask)
    sink = np.clip(peb.dome - 0.4, 0.0, 1.0) * (peb.radius_px / cfg.px(9.0))
    base_height = soil_var * 0.22 + mid * 0.06 + clods * 0.06 + grit * 0.03 - ruts * 0.28 + sink * 0.3 * peb.mask + 0.3
    base_rough = lerp(0.88 + 0.07 * grit, 0.82, ruts)
    base_rough = lerp(base_rough, 0.8, peb.mask)

    canvas = Canvas(size, cfg.supersample, wrap=True, tag="path")
    ss = canvas.ss
    res = canvas.res
    wisps = canvas.begin()
    for i in range(cfg.count(130)):
        x = (rng.normal(0.0, 0.07) % 1.0) * res if i < cfg.count(110) else rng.uniform(0, res)
        colour = hsv_jitter(mix(rgb("#7a6a48"), rgb("#9a8c62"), rng.random()), rng, hue=0.01, sat=0.08, val=0.06)
        draw_wisp(wisps, rng, (x, rng.uniform(0, res)), rng.uniform(0, TWO_PI), cfg.px(rng.uniform(50, 130)) * ss, cfg.px(rng.uniform(1.5, 2.6)) * ss, colour, 0.68, 0.85)
    canvas.commit(wisps)

    albedo, height, rough = canvas.finish().over(base_rgb, base_height, base_rough)
    height = normalise01(height)
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 5, 12, 28)], strength=3.0)
    return [PBRSet("path", albedo, normal_from_height(height, 4.0), make_orm(ao, rough, height))]


# =============================================================================
# Prop sets
# =============================================================================


def make_bark_oak(cfg: BakeConfig) -> list[BakeResult]:
    """Deeply furrowed oak bark: tall ridge plates split by horizontal cracks."""
    size = cfg.size
    noise = Noise("bark_oak")
    # Furrows are boundaries of 1-D Voronoi partitions of u (ridge centres as
    # feature positions), displaced along u by low-frequency noise so they
    # wander sideways while staying continuous over the tile.  A second,
    # shallower layer with a different count merges in, giving Y-junctions.
    u, v = uv_grid(size)
    sway_a = noise.fbm(size, (2, 3), 3, normalise=False) * 0.05
    sway_b = noise.fbm(size, (2, 2), 3, normalise=False) * 0.06
    deep = noise.voronoi_1d(u + sway_a, 10, 0.6)
    shallow = noise.voronoi_1d(u + sway_b, 6, 0.6)
    width_var = noise.fbm(size, (6, 3), 3)
    width_a = 0.02 + 0.02 * width_var
    width_b = 0.011 + 0.013 * width_var
    furrow_a = smoothstep(width_a, width_a * 0.15, deep.edge)
    furrow_b = smoothstep(width_b, width_b * 0.15, shallow.edge)
    shoulder = np.maximum(smoothstep(width_a * 3.0, width_a, deep.edge), 0.6 * smoothstep(width_b * 3.0, width_b, shallow.edge))
    furrow = clamp01(furrow_a + 0.7 * furrow_b)
    ridge = 1.0 - furrow
    # Horizontal breaks: the near-horizontal boundaries of a warped, tall
    # Voronoi plate grid (orientation from the gradient of F2-F1).
    coords = noise.warp_coords(size, 0.02, (6, 2), 2)
    plates = noise.voronoi((coords[0] + sway_a, coords[1]), (16, 3), 0.8)
    plate_tone = plates.random(0)
    pgx, pgy = height_gradient(plates.edge)
    horizontal = smoothstep(0.55, 0.3, np.abs(pgx) / (np.abs(pgx) + np.abs(pgy) + 1e-6))
    break_gate = smoothstep(0.4, 0.55, noise.fbm(size, 8, 3))
    breaks = smoothstep(0.008, 0.002, plates.edge) * horizontal * ridge * break_gate
    fine_v = smoothstep(0.86, 0.92, noise.fbm(size, (70, 4), 3)) * ridge
    fibre = noise.fbm(size, (60, 6), 3, ridged=True)
    tone = noise.fbm(size, (20, 5), 3)
    micro = noise.fbm(size, 140, 3)
    lichen_clumps = smoothstep(0.35, 0.75, noise.fbm(size, 3, 3))
    lichen_field = noise.warp(lambda c: noise.fbm(c, 14, 5), 0.04, size) * smoothstep(0.5, 0.9, ridge) * lichen_clumps
    lichen_thr = quantile_threshold(lichen_field, 0.045)
    lichen = smoothstep(lichen_thr - 0.01, lichen_thr + 0.01, lichen_field)
    lichen_tex = noise.fbm(size, 160, 2)

    height = ridge * (0.55 + 0.1 * fibre + 0.08 * plate_tone + 0.04 * tone) - 0.25 * shoulder * ridge - 0.25 * breaks - 0.12 * fine_v + 0.04 * micro + 0.03 * lichen
    height = normalise01(height)

    furrow_rgb = rgb("#2a2119") * (0.8 + 0.4 * micro)[..., None]
    plate_rgb = mix(rgb("#5a4a3a"), rgb("#7a6a58"), 0.5 * tone + 0.3 * plate_tone + 0.2 * fibre)
    plate_rgb = plate_rgb * (0.9 + 0.2 * fibre)[..., None]
    plate_rgb = mix(plate_rgb, rgb("#8a8070"), smoothstep(0.8, 1.0, height) * 0.45)
    albedo = mix(furrow_rgb, plate_rgb, smoothstep(0.08, 0.45, height))
    albedo = albedo * (0.86 + 0.28 * micro)[..., None]
    albedo = albedo * (1.0 - 0.5 * breaks - 0.3 * fine_v)[..., None]
    albedo = mix(albedo, mix(rgb("#7a8a6a"), rgb("#9aa88a"), lichen_tex), lichen * 0.85)

    rough = 0.93 - 0.08 * ridge + 0.02 * micro
    rough = lerp(rough, 0.9, lichen)
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 6, 14, 32)], strength=2.2)
    return [PBRSet("bark_oak", clamp01(albedo), normal_from_height(height, 4.0), make_orm(ao, rough, height))]


def make_bark_pine(cfg: BakeConfig) -> list[BakeResult]:
    """Scaly pine bark: bevelled Voronoi plates with deep cracks between them."""
    size = cfg.size
    noise = Noise("bark_pine")
    coords = noise.warp_coords(size, 0.03, 12, 3)
    coords = noise.warp_coords(coords, 0.006, 60, 2)
    vor = noise.voronoi(coords, (26, 12), 0.8)
    edge = vor.edge
    bevel = smoothstep(0.0, 0.003, edge)
    crack = smoothstep(0.0022, 0.0, edge)
    inner = smoothstep(0.005, 0.02, edge)
    plate_h = 0.5 + 0.5 * vor.random(0)
    tilt_angle = vor.random(3) * TWO_PI
    tilt = (vor.dx * np.cos(tilt_angle) + vor.dy * np.sin(tilt_angle)) * 6.0
    flaky = smoothstep(0.35, 0.6, vor.random(4))
    flakes = noise.voronoi(coords, (60, 36), 0.9)
    flake_inner = smoothstep(0.001, 0.004, flakes.edge)
    flake_h = (flakes.random(0) - 0.5) * 0.14 * flaky
    flake_crack = smoothstep(0.002, 0.0, flakes.edge) * inner * flaky
    fine = noise.fbm(size, (40, 80), 3)
    micro = noise.fbm(size, 160, 2)
    scale_tex = noise.fbm(size, (30, 90), 3, ridged=True)
    grey_field = noise.fbm(size, 9, 3)

    height = plate_h * bevel + tilt * bevel + flake_h * inner + 0.05 * fine + 0.03 * scale_tex - 0.03 * micro - 0.08 * flake_crack
    height = normalise01(height)

    tone = vor.random(2)
    plate_rgb = mix(rgb("#4a2a1a"), rgb("#6a3a22"), np.clip(tone * 2.0, 0.0, 1.0))
    plate_rgb = mix(plate_rgb, rgb("#8a5a3a"), np.clip(tone * 2.0 - 1.0, 0.0, 1.0))
    plate_rgb = mix(plate_rgb, rgb("#5a4a3e"), 0.3 * fine)
    plate_rgb = plate_rgb * (0.9 + 0.2 * flakes.random(1) * flaky)[..., None]
    grey_mask = (flakes.random(2) < 0.35).astype(F32) * flake_inner * smoothstep(0.5, 0.7, plate_h) * smoothstep(0.42, 0.55, grey_field) * inner
    albedo = mix(plate_rgb, mix(rgb("#7a7068"), rgb("#8a8078"), micro), grey_mask * 0.85)
    albedo = albedo * (0.9 + 0.2 * fine)[..., None]
    albedo = albedo * (0.92 + 0.16 * scale_tex)[..., None]
    albedo = albedo * (1.0 - 0.35 * flake_crack)[..., None]
    albedo = mix(albedo, rgb("#1a120e"), crack)
    albedo = albedo * (0.86 + 0.14 * bevel)[..., None]

    rough = 0.8 + 0.1 * crack - 0.08 * grey_mask + 0.04 * micro
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 5, 12, 28)], strength=2.5)
    return [PBRSet("bark_pine", clamp01(albedo), normal_from_height(height, 4.0), make_orm(ao, rough, height))]


def make_rock(cfg: BakeConfig) -> list[BakeResult]:
    """Weathered granite: warped fbm, cracks, mineral speckle and lichen."""
    size = cfg.size
    noise = Noise("rock")
    coords = noise.warp_coords(size, 0.12, 3, 3)
    base = noise.fbm(coords, 5, 7, gain=0.55)
    mid = noise.fbm(size, 20, 4)
    mottle = noise.fbm(size, 30, 3, ridged=True)
    fine = noise.fbm(size, 90, 3)
    crack_coords = noise.warp_coords(size, 0.05, 5, 2)
    crack_w = 0.003 + 0.006 * noise.fbm(size, 20, 2)
    crack_gate = smoothstep(0.45, 0.6, noise.fbm(size, 5, 3))
    crack1 = smoothstep(crack_w, crack_w * 0.3, noise.voronoi(crack_coords, 7, 1.0).edge) * crack_gate
    crack2 = smoothstep(0.004, 0.001, noise.voronoi(crack_coords, 16, 1.0).edge) * smoothstep(0.55, 0.7, noise.fbm(size, 6, 3))
    cracks = clamp01(crack1 + 0.6 * crack2)

    quartz, mica = speckle_masks(noise, size, cfg.scale, 0.10, 0.08)
    density = smoothstep(0.25, 0.75, noise.fbm(size, 10, 3))
    quartz = quartz * lerp(0.4, 1.0, density)
    mica = mica * lerp(1.0, 0.5, density)

    lichen_field = noise.warp(lambda c: noise.fbm(c, 9, 5), 0.05, size)
    thr_a = quantile_threshold(lichen_field, 0.06)
    lichen_a = smoothstep(thr_a - 0.008, thr_a + 0.008, lichen_field)
    rim_a = lichen_a * smoothstep(thr_a + 0.05, thr_a + 0.01, lichen_field)
    lichen_field_b = noise.warp(lambda c: noise.fbm(c, 7, 5), 0.05, size)
    thr_b = quantile_threshold(lichen_field_b, 0.045)
    lichen_b = smoothstep(thr_b - 0.008, thr_b + 0.008, lichen_field_b) * (1.0 - lichen_a)
    rim_b = lichen_b * smoothstep(thr_b + 0.05, thr_b + 0.01, lichen_field_b)
    lichen_tex = noise.fbm(size, 120, 3, turbulence=True)
    streak = smoothstep(0.62, 0.85, noise.fbm(size, (14, 1.5), 3)) * (1.0 - lichen_a - lichen_b)

    height = base * 0.55 + mid * 0.2 + mottle * 0.12 + fine * 0.13 - 0.14 * cracks + 0.02 * (lichen_a + lichen_b) + 0.015 * quartz - 0.01 * mica
    height = normalise01(height)

    albedo = mix(rgb("#5e5e5c"), rgb("#8f8d8a"), 0.4 * base + 0.35 * mottle + 0.25 * mid)
    albedo = mix(albedo, rgb("#8a807a"), smoothstep(0.6, 0.8, noise.fbm(size, 7, 3)) * 0.35)
    albedo = mix(albedo, rgb("#4e4c4a"), smoothstep(0.62, 0.8, noise.fbm(size, 9, 3)) * 0.3)
    albedo = albedo * (0.9 + 0.2 * fine)[..., None]
    albedo = mix(albedo, rgb("#a8a49c"), quartz)
    albedo = mix(albedo, rgb("#3a3838"), mica)
    albedo = albedo * (1.0 - 0.55 * cracks)[..., None]
    albedo = albedo * (1.0 - 0.16 * streak)[..., None]
    albedo = mix(albedo, mix(rgb("#9aa040"), rgb("#7d8248"), 0.5 * lichen_tex) * (0.85 + 0.3 * lichen_tex)[..., None], lichen_a)
    albedo = mix(albedo, rgb("#b0b0a0") * (0.85 + 0.3 * lichen_tex)[..., None], lichen_b)
    albedo = albedo * (1.0 - 0.25 * (rim_a + rim_b))[..., None]

    rough = 0.66 + 0.16 * fine + 0.05 * mid
    rough = np.maximum(rough, cracks * 0.85)
    rough = rough - 0.06 * streak
    rough = lerp(rough, 0.9, clamp01(lichen_a + lichen_b))
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 5, 12, 28)], strength=2.8)
    return [PBRSet("rock", clamp01(albedo), normal_from_height(height, 5.0), make_orm(ao, rough, height))]


def make_wood(cfg: BakeConfig) -> list[BakeResult]:
    """Weathered planks: four boards, wavy grain, knots, nails and splits."""
    size = cfg.size
    noise = Noise("wood")
    u, v = uv_grid(size)
    planks = 4
    plank_u = np.modf(u * planks)[0]
    plank_id = np.floor(u * planks).astype(np.int64)
    plank_rand = hash01(plank_id, 1234)
    plank_rand2 = hash01(plank_id, 999)
    edge_dist = np.minimum(plank_u, 1.0 - plank_u)
    gap = smoothstep(0.022, 0.008, edge_dist)
    end_dist = np.minimum(v, 1.0 - v)
    end_gap = smoothstep(0.012, 0.004, end_dist)

    # Grain: wavy latewood lines (periodic wobble) that flow around knots
    # like streamlines past a cylinder; each plank has its own line density.
    wave = noise.fbm(size, (2, 1), 3, normalise=False) * 0.09
    wiggle = noise.fbm(size, (8, 2), 3, normalise=False) * 0.02
    knots = noise.voronoi(size, (4, 2), 0.7)
    knot_present = (knots.random(0) < 0.6).astype(F32)
    knot_r = lerp(0.08, 0.14, knots.random(1))
    kx = -knots.dx * planks
    ky = -knots.dy * planks / 1.8
    kr = np.sqrt(kx * kx + ky * ky) + 1e-4
    falloff = np.exp(-((kr / (2.2 * knot_r)) ** 4))
    flow = kx * (1.0 - falloff * (knot_r**2) / np.maximum(kr * kr, knot_r**2))
    knot_u = plank_u - kx
    grain_u = np.where(knot_present > 0, knot_u + flow, plank_u)
    lines = lerp(14.0, 22.0, plank_rand2)
    phase = (grain_u + wave + wiggle) * lines
    line_width = 0.4 + 0.5 * noise.fbm(size, (6, 3), 3)
    grain = smoothstep(line_width, 1.0, 0.5 + 0.5 * np.cos(TWO_PI * phase))
    grain = grain * (0.5 + 0.5 * noise.fbm(size, (3, 12), 3))
    knot_core = smoothstep(knot_r, knot_r * 0.7, kr) * knot_present
    ring_r = kr * (1.0 + 0.08 * (noise.fbm(size, 40, 2) - 0.5))
    rings = (0.5 + 0.5 * np.cos(TWO_PI * ring_r * 18.0)) * smoothstep(knot_r * 1.6, knot_r * 0.6, kr) * knot_present
    knot_zone = smoothstep(knot_r * 2.2, knot_r, kr) * knot_present
    macro = noise.fbm(size, 5, 3)
    micro = noise.fbm(size, (200, 20), 2)
    split_field = noise.fbm(size, (60, 1.5), 2)
    split = smoothstep(0.984, 0.992, split_field) * (plank_rand > 0.5) * (1.0 - gap)

    nail_x = np.minimum(np.abs(plank_u - 0.28), np.abs(plank_u - 0.72))
    nail_v = np.minimum(np.abs(v - 0.035), np.abs(v - 0.965))
    nail_d = np.sqrt((nail_x / planks) ** 2 + nail_v**2) * size
    nail_r = cfg.px(4.5)
    nail = smoothstep(nail_r + 0.8, nail_r - 0.8, nail_d)
    nail_ring = smoothstep(nail_r + 3.0 * cfg.scale, nail_r + 0.5, nail_d) * (1.0 - nail)

    height = 0.55 + 0.06 * (plank_rand - 0.5) + 0.12 * grain + 0.06 * macro + 0.03 * micro - 0.1 * knot_core + 0.03 * rings * knot_zone - 0.12 * split - 0.05 * nail
    height = np.where(gap > 0.001, lerp(height, 0.0, gap), height)
    height = lerp(height, 0.05, end_gap)
    height = normalise01(height)

    weather = smoothstep(0.15, 0.6, 0.45 * grain + 0.4 * macro + 0.3 * plank_rand + 0.05)
    brown = mix(rgb("#5a4630"), rgb("#6e5838"), micro)
    grey = mix(rgb("#7a7060"), rgb("#9a9080"), 0.5 * macro + 0.3 * plank_rand2 + 0.2 * grain)
    albedo = mix(brown, grey, weather)
    albedo = albedo * (0.9 + 0.2 * micro)[..., None]
    albedo = albedo * (1.0 - 0.3 * knot_zone * (1.0 - knot_core))[..., None]
    albedo = mix(albedo, rgb("#3a2a1c"), knot_core * 0.9)
    albedo = albedo * (1.0 - 0.18 * rings * knot_zone)[..., None]
    albedo = albedo * (1.0 - 0.55 * split)[..., None]
    albedo = mix(albedo, rgb("#1c1815"), gap)
    albedo = mix(albedo, rgb("#221d18"), end_gap * 0.85)
    albedo = mix(albedo, rgb("#5a3a28"), nail_ring * 0.6)
    albedo = mix(albedo, rgb("#2a2624"), nail)

    rough = 0.78 + 0.08 * (1.0 - weather) + 0.04 * micro - 0.03 * nail
    rough = np.maximum(rough, gap * 0.9)
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (2, 5, 12, 28)], strength=2.5)
    return [PBRSet("wood", clamp01(albedo), normal_from_height(height, 4.0), make_orm(ao, rough, height))]


def make_canvas_fabric(cfg: BakeConfig) -> list[BakeResult]:
    """Woven tent canvas: plain weave, water stains and stitched seams."""
    size = cfg.size
    noise = Noise("canvas")
    u, v = uv_grid(size)
    threads = 48
    wave_u = (noise.fbm(size, (6, 3), 2, normalise=False)) * 0.0025
    wave_v = (noise.fbm(size, (3, 6), 2, normalise=False)) * 0.0025
    uw = u + wave_u
    vw = v + wave_v
    i = np.floor(uw * threads).astype(np.int64)
    j = np.floor(vw * threads).astype(np.int64)
    fu = np.modf(uw * threads)[0]
    fv = np.modf(vw * threads)[0]
    prof_u = np.sqrt(np.clip(1.0 - (2.0 * fu - 1.0) ** 2, 0.0, 1.0))
    prof_v = np.sqrt(np.clip(1.0 - (2.0 * fv - 1.0) ** 2, 0.0, 1.0))
    over = ((i + j) % 2 == 0).astype(F32)
    weave = np.maximum(prof_u * lerp(0.85, 1.0, over), prof_v * lerp(1.0, 0.85, over))
    fibre_u = noise.fbm(size, (320, 14), 3)
    fibre_v = noise.fbm(size, (14, 320), 3)
    fibre = lerp(fibre_v, fibre_u, over)
    thread_tone = lerp(hash01(j, 77), hash01(i, 55), over)

    stain_field = noise.warp(lambda c: noise.fbm(c, 3, 5), 0.1, size)
    stain = smoothstep(0.54, 0.66, stain_field)
    tide = smoothstep(0.53, 0.57, stain_field) * smoothstep(0.62, 0.58, stain_field)
    seam_h = np.exp(-(((v - 0.5) / 0.006) ** 2))
    seam_v = np.exp(-(((u - 0.5) / 0.006) ** 2))
    stitch_rows = np.exp(-(((np.abs(v - 0.5) - 0.014) / 0.0022) ** 2)) * (np.modf(u * threads * 1.5)[0] < 0.55)
    stitch_cols = np.exp(-(((np.abs(u - 0.5) - 0.014) / 0.0022) ** 2)) * (np.modf(v * threads * 1.5)[0] < 0.55)
    stitches = clamp01(stitch_rows + stitch_cols)

    height = 0.4 * weave + 0.12 * fibre + 0.12 * (seam_h + seam_v) + 0.08 * stitches
    height = normalise01(height)

    segment_tone = hash01(i * 131 + j, 4242)
    albedo = mix(rgb("#6f6a4a"), rgb("#8a8460"), 0.25 * thread_tone + 0.75 * noise.fbm(size, 4, 3))
    albedo = albedo * (0.93 + 0.14 * fibre)[..., None]
    albedo = albedo * (0.96 + 0.08 * segment_tone)[..., None]
    albedo = albedo * (0.88 + 0.12 * weave)[..., None]
    albedo = albedo * (1.0 - 0.14 * stain)[..., None]
    albedo = albedo * (1.0 - 0.14 * tide)[..., None]
    albedo = blur(albedo, 0.6 * cfg.scale)
    albedo = mix(albedo, rgb("#4a4630"), stitches * 0.85)

    rough = 0.9 - 0.05 * stain + 0.03 * fibre
    ao = ao_from_height(height, radii=[r * cfg.scale for r in (1.5, 4, 10)], strength=1.5)
    return [PBRSet("canvas", clamp01(albedo), normal_from_height(height, 2.5), make_orm(ao, rough, height))]


# =============================================================================
# Alpha-card atlases
# =============================================================================


@dataclass
class Atlas:
    """Grid of independent card cells assembled into RGBA albedo and normal/translucency."""

    cell: int
    cols: int
    rows: int

    @property
    def width(self) -> int:
        return self.cell * self.cols

    @property
    def height(self) -> int:
        return self.cell * self.rows

    def assemble(self, cells: Sequence[CanvasResult]) -> tuple[Array, Array]:
        """Packs cells row-major into (RGBA albedo, RGBA normal + translucency)."""
        albedo = np.zeros((self.height, self.width, 4), F32)
        normal_t = np.zeros((self.height, self.width, 4), F32)
        for index, result in enumerate(cells):
            r, c = divmod(index, self.cols)
            ys = slice(r * self.cell, (r + 1) * self.cell)
            xs = slice(c * self.cell, (c + 1) * self.cell)
            colour = dilate_colour_into_alpha(result.rgb, result.alpha)
            normal = result.normal if result.normal is not None else flat_normal(result.alpha.shape)
            # Translucency is stored un-premultiplied (pure thickness value) and
            # dilated together with the normal so mips never bleed toward zero.
            packed = np.concatenate([encode_normal(normal), clamp01(result.aux)[..., None]], axis=-1)
            albedo[ys, xs, :3] = colour
            albedo[ys, xs, 3] = result.alpha
            normal_t[ys, xs] = dilate_colour_into_alpha(packed, result.alpha)
        return albedo, normal_t


def card_outputs(name: str, atlas: Atlas, cells: Sequence[CanvasResult]) -> list[BakeResult]:
    """The two files of an alpha-card atlas: ``{name}.png`` and ``{name}_nt.png``."""
    albedo, normal_t = atlas.assemble(cells)
    return [Output(f"{name}.png", albedo), Output(f"{name}_nt.png", normal_t)]


def fit_leaf_in_box(rng: np.random.Generator, base: tuple[float, float], angle: float, length: float, aspect: float, box: tuple[float, float, float, float]) -> tuple[float, float]:
    """Nudges a leaf's angle/length so its extent stays inside ``box``."""
    x0, y0, x1, y1 = box
    for attempt in range(12):
        tip = (base[0] + math.cos(angle) * length, base[1] + math.sin(angle) * length)
        pad = aspect * length
        if x0 + pad <= tip[0] <= x1 - pad and y0 + pad <= tip[1] <= y1 - pad:
            return angle, length
        if attempt < 6:
            centre = ((x0 + x1) * 0.5, (y0 + y1) * 0.5)
            toward = math.atan2(centre[1] - base[1], centre[0] - base[0])
            angle = angle + 0.35 * math.sin(toward - angle) + rng.uniform(-0.2, 0.2)
        else:
            length *= 0.85
    return angle, length * 0.7


@dataclass
class ClusterSpec:
    """Parameters of a leafy twig cluster card."""

    leaf_count: tuple[int, int]
    leaf_length: tuple[float, float]
    shapes: Sequence[tuple[LeafShape, float]]
    palette: Sequence[str]
    autumn: str | None
    autumn_chance: float
    twig_colour: str
    twig_width: tuple[float, float]
    side_twigs: tuple[int, int]
    main_length: tuple[float, float]
    translucency: tuple[float, float]
    veins: tuple[int, int]
    edge_darken: float = 0.22
    leaf_angle: tuple[float, float] = (0.6, 1.25)


OAK_CLUSTER = ClusterSpec((12, 15), (100, 170), ((OVATE, 0.4), (ELLIPTIC, 0.25), (OAK, 0.35)), ("#3f7a2b", "#5a8f34", "#7aa83c", "#a9b843"), "#b58a2f", 0.1, "#3a2a1c", (8.0, 4.0), (3, 4), (0.62, 0.68), (0.7, 0.9), (4, 6))
BIRCH_CLUSTER = ClusterSpec((20, 26), (60, 95), ((BIRCH, 1.0),), ("#6fa03a", "#8bb646", "#a7c24d"), "#d0b53a", 0.12, "#2a2018", (5.0, 2.5), (3, 5), (0.62, 0.68), (0.7, 0.9), (4, 6), edge_darken=0.18)


def twig_curve(base: tuple[float, float], angle: float, length: float, rng: np.random.Generator, steps: int = 14) -> Array:
    """A gently bowed twig polyline (random bow direction)."""
    return curve_from(base, angle, length, rng.uniform(-0.18, 0.18), steps)


def point_on(points: Array, t: float) -> tuple[tuple[float, float], float]:
    """Position and tangent angle at parameter ``t`` (0..1) along a polyline."""
    idx = min(int(t * (len(points) - 1)), len(points) - 2)
    frac = t * (len(points) - 1) - idx
    p = points[idx] * (1.0 - frac) + points[idx + 1] * frac
    d = points[idx + 1] - points[idx]
    return (float(p[0]), float(p[1])), math.atan2(float(d[1]), float(d[0]))


def draw_twig_polyline(batch: StrokeBatch, points: Array, width: tuple[float, float], colour: Array, aux: float) -> None:
    """Woody twig along ``points`` with a tapering width and cylinder height."""
    n = len(points)
    for k in range(n - 1):
        w = lerp(width[0], width[1], k / max(n - 2, 1))
        seg = [tuple(points[k]), tuple(points[k + 1])]
        batch.line(seg, w + 2.0, colour * 0.6, w * 0.45, aux)
    for k in range(n - 1):
        w = lerp(width[0], width[1], k / max(n - 2, 1))
        seg = [tuple(points[k]), tuple(points[k + 1])]
        batch.line(seg, w, colour * (0.9 + 0.2 * (k % 2)), w * 0.5, aux)


def draw_leaf_cluster(canvas: Canvas, rng: np.random.Generator, spec: ClusterSpec, cfg: BakeConfig) -> None:
    """A leafy twig: main stem from bottom-centre, side twigs, leaves lowest-first."""
    ss = canvas.ss
    res = canvas.res
    margin = 0.045 * res
    box = (margin, margin, res - margin, res - margin)
    base = (res * 0.5, res * 0.995)
    main_len = rng.uniform(*spec.main_length) * res
    main = twig_curve(base, -math.pi / 2 + rng.uniform(-0.12, 0.12), main_len, rng)
    twigs = [main]
    side_count = int(rng.integers(spec.side_twigs[0], spec.side_twigs[1] + 1))
    for k in range(side_count):
        t = lerp(0.15, 0.85, (k + rng.uniform(0.2, 0.8)) / side_count)
        origin, tangent = point_on(main, t)
        side = 1.0 if k % 2 == 0 else -1.0
        angle = tangent + side * rng.uniform(0.55, 0.95)
        length = main_len * rng.uniform(0.35, 0.55) * (1.0 - 0.4 * t)
        twigs.append(twig_curve(origin, angle, length, rng))

    batch = canvas.begin()
    twig_rgb = rgb(spec.twig_colour)
    for index, pts in enumerate(twigs):
        w0, w1 = (cfg.px(spec.twig_width[0]) * ss, cfg.px(spec.twig_width[1]) * ss)
        if index > 0:
            w0, w1 = w0 * 0.65, w1 * 0.7
        draw_twig_polyline(batch, pts, (w0, w1), twig_rgb, 0.0)
    canvas.commit(batch, cylinder_sigma=1.5 * ss)

    count = int(rng.integers(spec.leaf_count[0], spec.leaf_count[1] + 1))
    leaves = []
    for k in range(count):
        twig = twigs[int(rng.integers(0, len(twigs)))] if k % 3 else main
        t = rng.uniform(0.12 if twig is main else 0.15, 1.0)
        origin, tangent = point_on(twig, t)
        side = 1.0 if k % 2 == 0 else -1.0
        angle = tangent + side * rng.uniform(*spec.leaf_angle) if t < 0.93 else tangent + rng.uniform(-0.3, 0.3)
        length = cfg.px(rng.uniform(*spec.leaf_length)) * ss
        shape = pick(rng, [s for s, _ in spec.shapes], [w for _, w in spec.shapes])
        angle, length = fit_leaf_in_box(rng, origin, angle, length, shape.aspect, box)
        leaves.append((origin, angle, length, shape))
    leaves.sort(key=lambda item: -item[0][1])
    for origin, angle, length, shape in leaves:
        if spec.autumn and rng.random() < spec.autumn_chance:
            colour = rgb(spec.autumn)
        else:
            colour = rgb(pick(rng, spec.palette))
        colour = hsv_jitter(colour, rng, hue=0.015, sat=0.08, val=0.07)
        style = LeafStyle(
            shape=shape, colour=colour, aux=rng.uniform(*spec.translucency), edge_darken=spec.edge_darken,
            veins=int(rng.integers(spec.veins[0], spec.veins[1] + 1)), vein_darken=0.12, bend=rng.uniform(-0.12, 0.12),
            dome=1.0, dome_px_factor=0.3, blotch=0.10, petiole=rng.uniform(0.1, 0.18), petiole_width_px=1.4 * ss,
            petiole_colour=twig_rgb * 1.2, petiole_aux=0.15,
        )
        place_leaf(canvas, style, origin, angle, length)


def make_leaf_atlas(name: str, spec: ClusterSpec, cfg: BakeConfig) -> list[BakeResult]:
    """Four cluster variants of ``spec`` packed into a 2x2 card atlas."""
    atlas = Atlas(cfg.half_size, 2, 2)
    rng = rng_for(f"{name}-cards")
    cells = []
    for index in range(4):
        canvas = Canvas(atlas.cell, cfg.supersample, wrap=False, with_normal=True, tag=f"{name}-{index}")
        draw_leaf_cluster(canvas, rng, spec, cfg)
        cells.append(canvas.finish())
    return card_outputs(name, atlas, cells)


def make_leaves_oak(cfg: BakeConfig) -> list[BakeResult]:
    """Leafy oak twig clusters (2x2 atlas)."""
    return make_leaf_atlas("leaves_oak", OAK_CLUSTER, cfg)


def make_leaves_birch(cfg: BakeConfig) -> list[BakeResult]:
    """Birch twig clusters with small doubly-serrate leaves (2x2 atlas)."""
    return make_leaf_atlas("leaves_birch", BIRCH_CLUSTER, cfg)


def draw_spruce_spray(canvas: Canvas, rng: np.random.Generator, cfg: BakeConfig) -> None:
    """Conifer spray: main twig, 3-4 side twigs, forward-angled needles, buds."""
    ss = canvas.ss
    res = canvas.res
    base = (res * 0.5, res * 0.995)
    main_len = rng.uniform(0.76, 0.82) * res
    main = twig_curve(base, -math.pi / 2 + rng.uniform(-0.06, 0.06), main_len, rng, steps=24)
    twigs = [main]
    side_count = int(rng.integers(3, 5))
    for k in range(side_count):
        t = lerp(0.12, 0.7, (k + rng.uniform(0.2, 0.8)) / side_count)
        origin, tangent = point_on(main, t)
        side = 1.0 if k % 2 == 0 else -1.0
        angle = tangent + side * rng.uniform(0.7, 1.0)
        length = min(main_len * rng.uniform(0.42, 0.55), res * 0.42 / max(abs(math.sin(angle)), 0.5))
        twigs.append(twig_curve(origin, angle, length, rng, steps=16))

    batch = canvas.begin()
    twig_rgb = rgb("#4a3a28")
    for index, pts in enumerate(twigs):
        w0, w1 = cfg.px(5.0) * ss, cfg.px(2.5) * ss
        if index > 0:
            w0, w1 = w0 * 0.7, w1 * 0.8
        draw_twig_polyline(batch, pts, (w0, w1), twig_rgb, 0.05)
    canvas.commit(batch, cylinder_sigma=1.2 * ss)

    needles = canvas.begin()
    step = cfg.px(3.2) * ss
    for pts in twigs:
        length_total = float(np.sum(np.linalg.norm(np.diff(pts, axis=0), axis=1)))
        n_steps = max(2, int(length_total / step))
        for k in range(n_steps):
            t = 0.05 + 0.93 * k / n_steps
            origin, tangent = point_on(pts, t)
            for side in (-1.0, 1.0):
                angle = tangent + side * math.radians(rng.uniform(32, 48))
                length = cfg.px(rng.uniform(22, 40)) * ss
                width = cfg.px(rng.uniform(2.0, 3.0)) * ss
                colour = hsv_jitter(mix(rgb("#2f4e2a"), rgb("#4a6e35"), rng.random()), rng, hue=0.01, sat=0.06, val=0.05)
                tip = colour * np.array([1.2, 1.25, 1.1], F32)
                draw_needle_stroke(needles, (origin[0] + rng.uniform(-1, 1), origin[1] + rng.uniform(-1, 1)), angle, length, width, colour, tip, width * 0.5, 0.5, rng.uniform(-0.08, 0.08) - side * 0.06)
    for pts in twigs:
        tip, tangent = point_on(pts, 1.0)
        bud_rgb = rgb("#6a4a2a")
        needles.ellipse(tip[0], tip[1], cfg.px(3.5) * ss, cfg.px(5.0) * ss, tangent + math.pi / 2, bud_rgb, cfg.px(3.0) * ss, 0.1)
        needles.ellipse(tip[0] + math.cos(tangent) * cfg.px(2.0) * ss, tip[1] + math.sin(tangent) * cfg.px(2.0) * ss, cfg.px(2.0) * ss, cfg.px(3.0) * ss, tangent + math.pi / 2, bud_rgb * 1.3, cfg.px(3.5) * ss, 0.1)
    canvas.commit(needles, cylinder_sigma=0.8 * ss)


def make_needles_spruce(cfg: BakeConfig) -> list[BakeResult]:
    """Spruce sprays: central twig, side twigs, hundreds of needles (2x2 atlas)."""
    atlas = Atlas(cfg.half_size, 2, 2)
    rng = rng_for("needles_spruce-cards")
    cells = []
    for index in range(4):
        canvas = Canvas(atlas.cell, cfg.supersample, wrap=False, with_normal=True, tag=f"spruce-{index}")
        draw_spruce_spray(canvas, rng, cfg)
        cells.append(canvas.finish())
    return card_outputs("needles_spruce", atlas, cells)


def draw_fern_frond(canvas: Canvas, rng: np.random.Generator, cfg: BakeConfig) -> None:
    """Pinnate frond: bowed rachis with pairs of pinnae shrinking toward the tip."""
    ss = canvas.ss
    res = canvas.res
    base = (res * 0.5, res * 0.995)
    lean = rng.uniform(-0.2, 0.2)
    bow = rng.uniform(0.12, 0.26) * (1.0 if rng.random() < 0.5 else -1.0)
    length = rng.uniform(0.84, 0.9) * res
    rachis = curve_from(base, -math.pi / 2 + lean, length, bow, 48)
    dark = rgb("#4a5a2a")
    tip_green = rgb("#7fb04a")
    base_green = rgb("#3f7f2e")
    batch = canvas.begin()
    pairs = int(rng.integers(14, 19))
    max_pinna = 0.27 * res
    spacing = 0.85 * length / (pairs - 1)
    for k in range(pairs):
        t = 0.12 + 0.85 * k / (pairs - 1)
        origin, tangent = point_on(rachis, t)
        envelope = (1.0 - t) ** 0.7 * (0.3 + 0.7 * math.sin(math.pi * min(1.0, t**0.8)))
        pinna_len = max_pinna * envelope * rng.uniform(0.92, 1.08)
        tone = mix(base_green, tip_green, t)
        for side in (-1.0, 1.0):
            offset_t = 0.0 if side < 0 else 0.5 / pairs
            origin_s, tangent_s = point_on(rachis, min(1.0, t + offset_t))
            angle = tangent_s + side * rng.uniform(1.2, 1.4)
            pinna = curve_from(origin_s, angle, pinna_len, -side * 0.08, 10)
            n_sub = max(5, int(pinna_len / (cfg.px(4.0) * ss)))
            for m in range(n_sub):
                tt = 0.06 + 0.92 * m / n_sub
                p, tang = point_on(pinna, tt)
                sub_len = min(pinna_len * 0.24, spacing * 0.75) * (1.0 - 0.6 * tt) * rng.uniform(0.9, 1.1)
                sub_w = sub_len * 0.5
                sub_side = -1.0 if m % 2 == 0 else 1.0
                a = tang + sub_side * math.radians(rng.uniform(52, 66))
                cx = p[0] + math.cos(a) * sub_len * 0.45
                cy = p[1] + math.sin(a) * sub_len * 0.45
                colour = hsv_jitter(tone, rng, hue=0.01, sat=0.05, val=0.06) * (0.88 + 0.3 * tt)
                batch.ellipse(cx, cy, sub_len * 0.5, sub_w * 0.5, a, colour, cfg.px(1.6) * ss, 0.85)
            batch.line([tuple(p) for p in pinna], cfg.px(1.4) * ss, tone * 0.7, cfg.px(2.4) * ss, 0.5)
    batch.line([tuple(p) for p in rachis], cfg.px(3.2) * ss, dark, cfg.px(3.2) * ss, 0.1)
    batch.line([tuple(p) for p in rachis], cfg.px(1.2) * ss, dark * 1.3, cfg.px(3.4) * ss, 0.1)
    canvas.commit(batch, cylinder_sigma=1.2 * ss)


def make_fern(cfg: BakeConfig) -> list[BakeResult]:
    """Single pinnate fern fronds (2x2 atlas)."""
    atlas = Atlas(cfg.half_size, 2, 2)
    rng = rng_for("fern-cards")
    cells = []
    for index in range(4):
        canvas = Canvas(atlas.cell, cfg.supersample, wrap=False, with_normal=True, tag=f"fern-{index}")
        draw_fern_frond(canvas, rng, cfg)
        cells.append(canvas.finish())
    return card_outputs("fern", atlas, cells)


def draw_flower(canvas: Canvas, rng: np.random.Generator, cfg: BakeConfig, variant: int) -> None:
    """One wildflower from the side; ``variant`` 0..3 = yarrow, buttercup, clover, cornflower."""
    ss = canvas.ss
    res = canvas.res
    unit = res / 256.0 * 1.8
    base = (res * 0.5, res * 0.995)
    head_y = rng.uniform(0.26, 0.36) * res
    stem_len = base[1] - head_y
    stem = curve_from(base, -math.pi / 2 + rng.uniform(-0.1, 0.1), stem_len, rng.uniform(-0.12, 0.12), 12)
    stem_rgb = hsv_jitter(rgb("#4f7a2e"), rng, hue=0.01, sat=0.08, val=0.06)
    batch = canvas.begin()
    stem_w = 1.8 * unit
    batch.line([tuple(p) for p in stem], stem_w, stem_rgb, stem_w * 0.5, 0.1)
    canvas.commit(batch, cylinder_sigma=0.8 * ss)
    for k in range(int(rng.integers(1, 4))):
        origin, tangent = point_on(stem, rng.uniform(0.15, 0.6))
        side = 1.0 if k % 2 == 0 else -1.0
        style = LeafStyle(shape=LANCEOLATE, colour=hsv_jitter(rgb("#4f8a30"), rng, hue=0.02, sat=0.08, val=0.08), aux=0.6, veins=0, midrib=0.02, rib_lighten=0.25, blotch=0.06, petiole=0.05, petiole_width_px=1.2 * unit, petiole_colour=stem_rgb, dome_px_factor=0.4)
        place_leaf(canvas, style, origin, tangent + side * rng.uniform(0.7, 1.1), rng.uniform(28, 46) * unit)
    head, _ = point_on(stem, 1.0)
    heads = canvas.begin()
    if variant == 0:
        white = rgb("#f2f0e6")
        cream = rgb("#e8dcb0")
        for _ in range(int(rng.integers(5, 8))):
            a = -math.pi / 2 + rng.uniform(-0.9, 0.9)
            l = rng.uniform(10, 22) * unit
            stalk_end = (head[0] + math.cos(a) * l, head[1] + math.sin(a) * l * 0.6)
            heads.line([head, stalk_end], 1.2 * unit, stem_rgb, 1.0 * unit, 0.2)
            for _ in range(int(rng.integers(5, 9))):
                cx = stalk_end[0] + rng.uniform(-7, 7) * unit
                cy = stalk_end[1] + rng.uniform(-4, 4) * unit
                r = rng.uniform(2.4, 3.4) * unit
                heads.ellipse(cx, cy, r, r, 0.0, white * rng.uniform(0.92, 1.0), 1.5 * unit, 0.6)
                heads.ellipse(cx, cy, r * 0.35, r * 0.35, 0.0, cream, 1.7 * unit, 0.6)
        canvas.commit(heads, cylinder_sigma=0.7 * ss)
    elif variant == 1:
        canvas.commit(heads)
        yellow = hsv_jitter(rgb("#e8c630"), rng, hue=0.01, sat=0.08, val=0.05)
        petal_len = rng.uniform(14, 18) * unit
        start = rng.uniform(0, TWO_PI)
        for k in range(5):
            a = start + k * TWO_PI / 5.0 + rng.uniform(-0.1, 0.1)
            style = LeafStyle(shape=PETAL, colour=yellow, aux=0.6, veins=0, midrib=0.0, edge_darken=0.08, tip_tint=0.05, blotch=0.04, petiole=0.0, dome=1.0, dome_px_factor=0.35)
            place_leaf(canvas, style, head, a, petal_len)
        centre = canvas.begin()
        centre.ellipse(head[0], head[1], 4.5 * unit, 4.5 * unit, 0.0, rgb("#a48a1e"), 2.5 * unit, 0.5)
        for _ in range(9):
            a = rng.uniform(0, TWO_PI)
            r = rng.uniform(1.0, 3.5) * unit
            centre.ellipse(head[0] + math.cos(a) * r, head[1] + math.sin(a) * r, 0.9 * unit, 0.9 * unit, 0.0, rgb("#d9a51e"), 3.0 * unit, 0.5)
        canvas.commit(centre, cylinder_sigma=0.7 * ss)
    elif variant == 2:
        heads.ellipse(head[0], head[1] + 3 * unit, 6.5 * unit, 4.0 * unit, 0.0, rgb("#3f6a28"), 2.0 * unit, 0.3)
        dark = hsv_jitter(rgb("#7a3f8f"), rng, hue=0.02, sat=0.1, val=0.06)
        light = hsv_jitter(rgb("#c98bc4"), rng, hue=0.02, sat=0.1, val=0.06)
        radius = rng.uniform(13, 17) * unit
        for _ in range(int(rng.integers(70, 100))):
            a = rng.uniform(-math.pi, 0.0) + rng.uniform(-0.3, 0.3)
            l = radius * rng.uniform(0.55, 1.0)
            start = (head[0] + math.cos(a) * l * 0.2, head[1] - 2 * unit + math.sin(a) * l * 0.2)
            end = (head[0] + math.cos(a) * l, head[1] - 2 * unit + math.sin(a) * l)
            mid = ((start[0] + end[0]) * 0.5, (start[1] + end[1]) * 0.5)
            heads.line([start, mid], 1.6 * unit, dark, 1.2 * unit, 0.6)
            heads.line([mid, end], 1.4 * unit, light * rng.uniform(0.85, 1.05), 1.4 * unit, 0.6)
        canvas.commit(heads, cylinder_sigma=0.7 * ss)
    else:
        blue = hsv_jitter(rgb("#4a6ad0"), rng, hue=0.02, sat=0.1, val=0.06)
        pale = hsv_jitter(rgb("#7590ea"), rng, hue=0.02, sat=0.1, val=0.06)
        petals = int(rng.integers(10, 15))
        start = rng.uniform(0, TWO_PI)
        for k in range(petals):
            a = start + k * TWO_PI / petals + rng.uniform(-0.08, 0.08)
            l = rng.uniform(12, 17) * unit
            w = rng.uniform(2.2, 3.2) * unit
            ca, sa = math.cos(a), math.sin(a)
            pts = [
                (head[0] + ca * 2 * unit, head[1] + sa * 2 * unit),
                (head[0] + ca * l * 0.75 - sa * w, head[1] + sa * l * 0.75 + ca * w),
                (head[0] + ca * l * 0.92 - sa * w * 0.5, head[1] + sa * l * 0.92 + ca * w * 0.5),
                (head[0] + ca * l, head[1] + sa * l),
                (head[0] + ca * l * 0.92 + sa * w * 0.5, head[1] + sa * l * 0.92 - ca * w * 0.5),
                (head[0] + ca * l * 0.75 + sa * w, head[1] + sa * l * 0.75 - ca * w),
            ]
            heads.polygon(pts, mix(blue, pale, rng.random()), 1.5 * unit, 0.6)
        heads.ellipse(head[0], head[1], 4.0 * unit, 4.0 * unit, 0.0, rgb("#3a2a5a"), 2.5 * unit, 0.4)
        for _ in range(6):
            a = rng.uniform(0, TWO_PI)
            r = rng.uniform(0.5, 2.5) * unit
            heads.ellipse(head[0] + math.cos(a) * r, head[1] + math.sin(a) * r, 0.8 * unit, 0.8 * unit, 0.0, rgb("#7a5aa8"), 3.0 * unit, 0.4)
        canvas.commit(heads, cylinder_sigma=0.7 * ss)


def make_flowers(cfg: BakeConfig) -> list[BakeResult]:
    """Wildflowers from the side: yarrow, buttercup, clover, cornflower (4x4)."""
    atlas = Atlas(max(32, cfg.half_size // 2), 4, 4)
    rng = rng_for("flowers-cards")
    cells = []
    for index in range(16):
        canvas = Canvas(atlas.cell, cfg.supersample, wrap=False, with_normal=True, tag=f"flower-{index}")
        draw_flower(canvas, rng, cfg, variant=index % 4)
        cells.append(canvas.finish())
    return card_outputs("flowers", atlas, cells)


BUSH_TWIG_RGB = rgb("#3a2c1e")


def bush_leaf_layer(canvas: Canvas, rng: np.random.Generator, cfg: BakeConfig, twigs: Sequence[Array], count: int, box: tuple[float, float, float, float], shade: float, translucency: tuple[float, float]) -> None:
    """Places ``count`` leaves along random points of ``twigs``, lowest first.

    ``shade`` < 1 darkens the layer (leaves deep inside the shrub receive
    less light), which is how the back layer gains depth behind the front one.
    """
    ss = canvas.ss
    leaves = []
    for k in range(count):
        twig = twigs[k % len(twigs)]
        origin, tangent = point_on(twig, rng.uniform(0.1, 1.0))
        angle = tangent + rng.uniform(-1.5, 1.5)
        length = cfg.px(rng.uniform(80, 110)) * ss
        shape = pick(rng, (OVATE, ELLIPTIC), (0.6, 0.4))
        angle, length = fit_leaf_in_box(rng, origin, angle, length, shape.aspect, box)
        leaves.append((origin, angle, length, shape))
    leaves.sort(key=lambda item: -item[0][1])
    for origin, angle, length, shape in leaves:
        colour = hsv_jitter(mix(rgb("#2f5a25"), rgb("#5f8a35"), rng.random()), rng, hue=0.015, sat=0.08, val=0.06) * shade
        style = LeafStyle(
            shape=shape, colour=colour, aux=rng.uniform(*translucency), edge_darken=0.25, veins=int(rng.integers(4, 6)),
            bend=rng.uniform(-0.12, 0.12), dome_px_factor=0.3, blotch=0.1, petiole=rng.uniform(0.1, 0.16),
            petiole_width_px=1.3 * ss, petiole_colour=BUSH_TWIG_RGB * 1.3 * shade, petiole_aux=0.15,
        )
        place_leaf(canvas, style, origin, angle, length)


def draw_bush(canvas: Canvas, rng: np.random.Generator, cfg: BakeConfig) -> None:
    """A shrub mass: shadowed back leaves, fanned twigs, then 28-30 lit front leaves."""
    ss = canvas.ss
    res = canvas.res
    margin = 0.045 * res
    box = (margin, margin, res - margin, res - margin)
    base = (res * 0.5, res * 0.995)
    twigs = []
    twig_count = int(rng.integers(5, 8))
    for k in range(twig_count):
        angle = -math.pi / 2 + lerp(-0.85, 0.85, (k + rng.uniform(0.2, 0.8)) / twig_count)
        length = res * rng.uniform(0.58, 0.72) * (1.0 - 0.2 * abs(math.sin(angle + math.pi / 2)))
        twigs.append(twig_curve(base, angle, length, rng))
    # Interior of the shrub: darker leaves behind the twigs fill the gaps so the
    # card reads as a solid mass rather than a see-through spray.
    bush_leaf_layer(canvas, rng, cfg, twigs, int(rng.integers(14, 18)), box, shade=0.5, translucency=(0.35, 0.5))
    batch = canvas.begin()
    for pts in twigs:
        draw_twig_polyline(batch, pts, (cfg.px(5.0) * ss, cfg.px(2.5) * ss), BUSH_TWIG_RGB, 0.0)
    canvas.commit(batch, cylinder_sigma=1.2 * ss)
    bush_leaf_layer(canvas, rng, cfg, twigs, int(rng.integers(28, 31)), box, shade=1.0, translucency=(0.65, 0.85))


def make_bush(cfg: BakeConfig) -> list[BakeResult]:
    """Dense shrub leaf masses (2x2 atlas)."""
    atlas = Atlas(cfg.half_size, 2, 2)
    rng = rng_for("bush-cards")
    cells = []
    for index in range(4):
        canvas = Canvas(atlas.cell, cfg.supersample, wrap=False, with_normal=True, tag=f"bush-{index}")
        draw_bush(canvas, rng, cfg)
        cells.append(canvas.finish())
    return card_outputs("bush", atlas, cells)


# =============================================================================
# Utility textures
# =============================================================================


def gentle_normal(h: Array, max_slope: float) -> Array:
    """Normal map whose encoded XY stays within +-``max_slope``."""
    dx, dy = height_gradient(h, periodic=True)
    peak = max(float(np.max(np.abs(dx))), float(np.max(np.abs(dy))), 1e-6)
    return normal_from_height(h, max_slope / peak)


def make_water_normals(cfg: BakeConfig) -> list[BakeResult]:
    """Two low-amplitude tileable wave normal maps at different scales."""
    size = cfg.size
    noise = Noise("water")
    a = noise.fbm(size, 5, 4) * 0.6 + noise.fbm(size, 13, 3) * 0.4
    b = noise.fbm(size, (6, 10), 4) * 0.55 + noise.fbm(size, (18, 24), 3) * 0.45
    return [Output("water_normal_a.png", gentle_normal(a, 0.25)), Output("water_normal_b.png", gentle_normal(b, 0.25))]


def make_foam(cfg: BakeConfig) -> list[BakeResult]:
    """Streaky, bubbly foam mask (greyscale stored as RGB)."""
    size = cfg.half_size
    noise = Noise("foam")
    vor = noise.voronoi(noise.warp_coords(size, 0.03, 6, 2), 22, 1.0)
    bubbles = smoothstep(0.008, 0.03, vor.edge) * lerp(0.4, 1.0, vor.random(0)) * (vor.random(1) < 0.6)
    small = noise.voronoi(noise.warp_coords(size, 0.02, 10, 2), 48, 1.0)
    bubbles = np.maximum(bubbles, 0.7 * smoothstep(0.004, 0.014, small.edge) * (small.random(0) < 0.45))
    streaks = smoothstep(0.5, 0.9, noise.fbm(size, (5, 18), 4))
    soft = noise.fbm(size, 6, 4)
    raw = 0.55 * bubbles * (0.6 + 0.4 * soft) + 0.6 * streaks + 0.3 * soft
    thr = quantile_threshold(raw, 0.25)
    foam = smoothstep(thr - 0.12, thr + 0.12, raw)
    return [Output("foam.png", grey_rgb(foam))]


def make_caustics(cfg: BakeConfig) -> list[BakeResult]:
    """Thin bright cellular filaments from two overlaid Voronoi layers."""
    size = cfg.half_size
    noise = Noise("caustics")
    coords_a = noise.warp_coords(size, 0.03, 4, 2)
    coords_b = noise.warp_coords(size, 0.03, 5, 2)
    a = 1.0 - clamp01(noise.voronoi(coords_a, 8, 1.0).edge / 0.05)
    b = 1.0 - clamp01(noise.voronoi(coords_b, 13, 1.0).edge / 0.035)
    flicker = 0.6 + 0.4 * noise.fbm(size, 5, 3)
    caustic = clamp01((a**10 * 1.0 + b**10 * 0.7) * flicker)
    caustic = np.maximum(caustic, blur(caustic, 2.0) * 0.6)
    return [Output("caustics.png", grey_rgb(normalise01(caustic) ** 1.1))]


def centre_mean(x: Array, target: float = 0.5, iterations: int = 3) -> Array:
    """Gamma-adjusts a 0..1 field so its mean lands on ``target``."""
    out = normalise01(x)
    for _ in range(iterations):
        mean = float(np.mean(out))
        if mean <= 0.0 or mean >= 1.0:
            break
        out = out ** (math.log(target) / math.log(mean))
    return out.astype(F32)


def make_noise_rgba(cfg: BakeConfig) -> list[BakeResult]:
    """Packed utility noise: R fbm p4, G fbm p8, B ridged p16, A smooth p32."""
    size = cfg.half_size
    noise = Noise("noise_rgba")
    r = centre_mean(noise.fbm(size, 4, 5))
    g = centre_mean(noise.fbm(size, 8, 5))
    b = centre_mean(noise.fbm(size, 16, 4, ridged=True))
    a = centre_mean(noise.fbm(size, 32, 2))
    return [Output("noise_rgba.png", np.stack([r, g, b, a], axis=-1))]


def make_detail_normal(cfg: BakeConfig) -> list[BakeResult]:
    """Micro-surface detail normal from very fine fbm."""
    size = cfg.half_size
    noise = Noise("detail")
    h = noise.fbm(size, 96, 3, gain=0.6)
    return [Output("detail_normal.png", gentle_normal(h, 0.18))]


# =============================================================================
# Registry, contact sheet and CLI
# =============================================================================

MAKERS: dict[str, Maker] = {
    "grass": make_grass,
    "litter": make_litter,
    "mud": make_mud,
    "path": make_path,
    "bark_oak": make_bark_oak,
    "bark_pine": make_bark_pine,
    "rock": make_rock,
    "wood": make_wood,
    "canvas": make_canvas_fabric,
    "leaves_oak": make_leaves_oak,
    "leaves_birch": make_leaves_birch,
    "needles_spruce": make_needles_spruce,
    "fern": make_fern,
    "flowers": make_flowers,
    "bush": make_bush,
    "water": make_water_normals,
    "foam": make_foam,
    "caustics": make_caustics,
    "noise_rgba": make_noise_rgba,
    "detail_normal": make_detail_normal,
}

FALLBACK_PBR_NAMES = frozenset({"grass", "litter", "mud", "path", "bark_oak", "bark_pine", "rock", "wood", "canvas"})

TILEABLE_PREVIEWS = ("_albedo.png", "water_normal_a.png", "water_normal_b.png", "foam.png", "caustics.png", "detail_normal.png")
DATA_ALPHA_FILES = frozenset({"noise_rgba.png"})  # alpha is a data channel, not coverage
NON_PREVIEW_SUFFIXES = ("_normal.png", "_orm.png", "_nt.png")


def bake_job(name: str, cfg: BakeConfig) -> tuple[str, list[Path], float]:
    """Runs one maker and writes its files; returns (name, paths, seconds)."""
    started = time.perf_counter()
    paths: list[Path] = []
    for result in MAKERS[name](cfg):
        if isinstance(result, PBRSet):
            paths.extend(save_set(result, cfg.out_dir))
        else:
            paths.append(save_output(result, cfg.out_dir))
    return name, paths, time.perf_counter() - started


def checkerboard(width: int, height: int, cell: int = 16) -> Array:
    ys, xs = np.mgrid[0:height, 0:width]
    tiles = ((ys // cell + xs // cell) % 2).astype(F32)
    return grey_rgb(0.32 + 0.22 * tiles)


def is_preview(path: Path) -> bool:
    """Albedos, atlases and utility maps are shown on the sheet; packed maps are not."""
    return path.suffix == ".png" and not path.name.endswith(NON_PREVIEW_SUFFIXES)


def contact_sheet(paths: Sequence[Path], out_path: Path, thumb: int = 192, columns: int = 6) -> Path:
    """Thumbnails of every albedo/atlas (RGBA over a checkerboard) with labels."""
    previews = sorted(set(p for p in paths if is_preview(p)))
    rows = max(1, math.ceil(len(previews) / columns))
    label_h = 14
    sheet = Image.new("RGB", (columns * thumb, rows * (thumb + label_h)), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    for index, path in enumerate(previews):
        image = Image.open(path)
        if path.name in DATA_ALPHA_FILES:
            image = image.convert("RGB")
        image = image.convert("RGBA").resize((thumb, thumb), Image.LANCZOS)
        board = Image.fromarray(to_uint8(checkerboard(thumb, thumb))).convert("RGBA")
        board.alpha_composite(image)
        r, c = divmod(index, columns)
        x, y = c * thumb, r * (thumb + label_h)
        sheet.paste(board.convert("RGB"), (x, y))
        draw.text((x + 3, y + thumb + 1), path.name.replace(".png", ""), fill=(230, 230, 230))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path)
    return out_path


def write_tiling_checks(paths: Sequence[Path], out_dir: Path) -> list[Path]:
    """Writes each tileable preview rolled by half its size (seam at centre)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for path in paths:
        if not path.name.endswith(TILEABLE_PREVIEWS):
            continue
        pixels = np.asarray(Image.open(path))
        rolled = np.roll(pixels, (pixels.shape[0] // 2, pixels.shape[1] // 2), axis=(0, 1))
        target = out_dir / path.name.replace(".png", "_rolled.png")
        Image.fromarray(rolled).save(target)
        written.append(target)
    return written


def parse_only(values: Sequence[str] | None) -> list[str]:
    """Expands repeated / comma-separated ``--only`` values; exits on unknown names."""
    if not values:
        return [name for name in MAKERS if name not in FALLBACK_PBR_NAMES]
    names = [n.strip() for v in values for n in v.split(",") if n.strip()]
    unknown = [n for n in names if n not in MAKERS]
    if unknown:
        raise SystemExit(f"unknown maker(s): {', '.join(unknown)}; available: {', '.join(MAKERS)}")
    return names


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--only", action="append", metavar="NAME", help="bake only these makers (repeatable / comma-separated)")
    parser.add_argument("--size", type=int, default=BASE_SIZE, help="resolution of the tileable sets (default 1024)")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT_DIR, help="output directory (default textures/)")
    parser.add_argument("--jobs", type=int, default=os.cpu_count() or 1, help="parallel worker processes")
    parser.add_argument("--check-tiling", action="store_true", help="also write rolled copies to the review directory")
    parser.add_argument("--review-out", type=Path, help="fresh review directory (default: unique local-data/bakes directory)")
    parser.add_argument("--no-sheet", action="store_true", help="skip the contact sheet")
    parser.add_argument("--list", action="store_true", help="list maker names and exit")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Bakes the selected makers in parallel, then the contact sheet / tiling checks."""
    args = build_parser().parse_args(argv)
    if args.list:
        print("\n".join(MAKERS))
        return 0
    if args.size < 64 or args.size % 8:
        raise SystemExit("--size must be a multiple of 8 and at least 64")
    cfg = BakeConfig(size=args.size, out_dir=args.out)
    names = parse_only(args.only)
    if set(names) & FALLBACK_PBR_NAMES and cfg.out_dir.resolve() == DEFAULT_OUT_DIR.resolve():
        raise SystemExit("fallback PBR makers require a separate --out; production photoscans and their credits must stay intact")
    review_dir = None
    if not args.no_sheet or args.check_tiling:
        if args.review_out:
            review_dir = args.review_out.resolve()
            review_dir.mkdir(parents=True, exist_ok=False)
        else:
            review_root = PROJECT_ROOT / "local-data" / "bakes"
            review_root.mkdir(parents=True, exist_ok=True)
            (review_root.parent / ".gdignore").touch()
            review_dir = Path(tempfile.mkdtemp(prefix="review-", dir=review_root))
    cfg.out_dir.mkdir(parents=True, exist_ok=True)
    print(f"baking {len(names)} maker(s) at {cfg.size}px -> {cfg.out_dir}")
    started = time.perf_counter()
    written: list[Path] = []
    jobs = max(1, min(args.jobs, len(names)))
    if jobs == 1:
        results = [bake_job(name, cfg) for name in names]
        for name, paths, seconds in results:
            written.extend(paths)
            print(f"  {name:<16} {seconds:6.1f}s  {', '.join(p.name for p in paths)}")
    else:
        with ProcessPoolExecutor(max_workers=jobs) as pool:
            futures = [pool.submit(bake_job, name, cfg) for name in names]
            for future in as_completed(futures):
                name, paths, seconds = future.result()
                written.extend(paths)
                print(f"  {name:<16} {seconds:6.1f}s  {', '.join(p.name for p in paths)}")
    total_bytes = sum(p.stat().st_size for p in written)
    print(f"wrote {len(written)} files, {total_bytes / 1e6:.1f} MB in {time.perf_counter() - started:.1f}s")
    if not args.no_sheet:
        sheet_sources = sorted(cfg.out_dir.glob("*.png")) if args.only is None else written
        print(f"contact sheet -> {contact_sheet(sheet_sources, review_dir / 'texture_sheet.png')}")
    if args.check_tiling:
        rolled = write_tiling_checks(written, review_dir / "tiling")
        print(f"tiling checks -> {review_dir / 'tiling'} ({len(rolled)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
