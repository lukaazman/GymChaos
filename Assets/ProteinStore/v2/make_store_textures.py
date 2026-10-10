"""Texture atlases for the protein.com store (v2).

Run with the system Python (needs Pillow):
    python Assets/ProteinStore/v2/make_store_textures.py

Writes next to this script:
  textures/products_atlas.png  8x8 tiles of 256 px: product labels + solid swatches
  textures/signs_atlas.png     category headers, counter panel, posters
  textures/floor_tile.png      light grey floor tiles
  textures/atlas_layout.json   tile names -> pixel rects (read by the Blender build)

Packaging follows the proteini.si house look (white stand-up pouches and
tubs with a flavour-coloured lightning bolt, black type, yellow accents) but
carries the store's own "protein.com" name instead of real brand logos.
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
OUT = HERE / "textures"
FONT_BLACK = "C:/Windows/Fonts/ariblk.ttf"
FONT_BOLD = "C:/Windows/Fonts/arialbd.ttf"

YELLOW = (255, 204, 0)
BLACK = (18, 18, 18)
WHITE = (246, 246, 244)
TILE = 256
GRID = 8

BOLT = [(0.58, 0.02), (0.22, 0.56), (0.47, 0.56), (0.36, 0.98), (0.80, 0.40), (0.54, 0.40), (0.70, 0.02)]


def font(path, size):
    return ImageFont.truetype(path, size)


def fit_text(draw, text, path, max_width, start):
    size = start
    while size > 8:
        f = font(path, size)
        if draw.textlength(text, font=f) <= max_width:
            return f
        size -= 1
    return font(path, 8)


def centered(draw, box, text, path, size, fill):
    x0, y0, x1, y1 = box
    f = fit_text(draw, text, path, x1 - x0 - 8, size)
    w = draw.textlength(text, font=f)
    bbox = f.getbbox(text)
    h = bbox[3] - bbox[1]
    draw.text((x0 + (x1 - x0 - w) / 2, y0 + (y1 - y0 - h) / 2 - bbox[1]), text, font=f, fill=fill)


def bolt(draw, x, y, w, h, fill):
    draw.polygon([(x + px * w, y + py * h) for px, py in BOLT], fill=fill)


def brand(draw, x, y, w, fill):
    centered(draw, (x, y, x + w, y + 26), "protein.com", FONT_BLACK, 22, fill)


def pouch(img, flavour, colour, title="COMPLETE WHEY", bg=WHITE, ink=BLACK):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=bg)
    d.rectangle((0, 0, TILE, 34), fill=ink)
    brand(d, 0, 4, TILE, YELLOW if ink == BLACK else BLACK)
    centered(d, (10, 44, TILE - 10, 86), title, FONT_BLACK, 30, ink)
    bolt(d, 74, 92, 108, 118, colour)
    centered(d, (10, 212, TILE - 10, 236), flavour, FONT_BOLD, 20, ink)
    d.rectangle((0, TILE - 12, TILE, TILE), fill=colour)


def tub(img, title, sub, colour, bg=WHITE, ink=BLACK):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=bg)
    d.rectangle((0, 0, TILE, 28), fill=colour)
    brand(d, 0, 30, TILE, ink)
    bolt(d, 18, 64, 90, 120, colour)
    centered(d, (100, 78, TILE - 8, 128), title, FONT_BLACK, 34, ink)
    centered(d, (100, 132, TILE - 8, 160), sub, FONT_BOLD, 20, ink)
    d.rectangle((0, TILE - 28, TILE, TILE), fill=colour)


def can(img, colour, title):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=colour)
    for i in range(0, TILE, 32):
        d.rectangle((i, 0, i + 10, TILE), fill=tuple(max(0, c - 30) for c in colour))
    d.rectangle((0, 96, TILE, 168), fill=BLACK)
    centered(d, (6, 100, TILE - 6, 164), title, FONT_BLACK, 40, WHITE)
    brand(d, 0, 200, TILE, BLACK)


def bottle(img, title, sub, colour):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=WHITE)
    d.rectangle((0, 60, TILE, 196), fill=colour)
    centered(d, (8, 70, TILE - 8, 120), title, FONT_BLACK, 34, WHITE)
    centered(d, (8, 130, TILE - 8, 170), sub, FONT_BOLD, 24, WHITE)
    brand(d, 0, 18, TILE, BLACK)


def bar_box(img, flavour, colour):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=(226, 240, 250))
    d.rectangle((0, 0, TILE, 40), fill=(28, 120, 196))
    brand(d, 0, 8, TILE, WHITE)
    centered(d, (10, 56, TILE - 10, 110), "PROTEIN", FONT_BLACK, 44, (20, 70, 130))
    centered(d, (10, 108, TILE - 10, 156), "BAR", FONT_BLACK, 44, (20, 70, 130))
    d.rectangle((40, 170, TILE - 40, 214), fill=colour)
    centered(d, (40, 172, TILE - 40, 212), flavour, FONT_BOLD, 22, WHITE)
    centered(d, (10, 222, TILE - 10, 250), "24 x 55 g", FONT_BOLD, 18, (20, 70, 130))


def wrapper(img, flavour, colour):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=(232, 244, 252))
    d.rectangle((0, 0, 40, TILE), fill=colour)
    d.rectangle((TILE - 40, 0, TILE, TILE), fill=colour)
    centered(d, (44, 40, TILE - 44, 110), "PROTEIN BAR", FONT_BLACK, 30, (20, 70, 130))
    centered(d, (44, 120, TILE - 44, 170), flavour, FONT_BOLD, 24, (20, 70, 130))
    brand(d, 44, 190, TILE - 88, (28, 120, 196))


def jar(img):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=(196, 140, 72))
    d.rectangle((0, 70, TILE, 190), fill=WHITE)
    centered(d, (8, 80, TILE - 8, 128), "PEANUT", FONT_BLACK, 38, BLACK)
    centered(d, (8, 130, TILE - 8, 178), "BUTTER", FONT_BLACK, 38, BLACK)
    brand(d, 0, 20, TILE, BLACK)


def magnesium(img):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=WHITE)
    d.rectangle((0, 0, TILE, 64), fill=YELLOW)
    centered(d, (8, 8, TILE - 8, 56), "MAGNESIUM", FONT_BLACK, 34, BLACK)
    centered(d, (8, 90, TILE - 8, 130), "10 x 15 g", FONT_BOLD, 24, BLACK)
    bolt(d, 100, 140, 56, 80, YELLOW)
    brand(d, 0, 226, TILE, BLACK)


def shaker(img):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=BLACK)
    bolt(d, 96, 50, 64, 90, YELLOW)
    brand(d, 0, 160, TILE, YELLOW)
    d.rectangle((0, 230, TILE, TILE), fill=YELLOW)


def solid(img, colour):
    ImageDraw.Draw(img).rectangle((0, 0, TILE, TILE), fill=colour)


PRODUCTS = [
    ("whey_choc", lambda i: pouch(i, "CHOCOLATE", (110, 62, 34))),
    ("whey_vanilla", lambda i: pouch(i, "VANILLA", (226, 196, 128))),
    ("whey_straw", lambda i: pouch(i, "STRAWBERRY", (226, 64, 96))),
    ("whey_cookie", lambda i: pouch(i, "COOKIES & CREAM", (60, 60, 60))),
    ("whey_banana", lambda i: pouch(i, "BANANA", (240, 206, 40))),
    ("whey_caramel", lambda i: pouch(i, "SALTED CARAMEL", (214, 128, 40))),
    ("isolate", lambda i: pouch(i, "DOUBLE CHOCOLATE", (212, 170, 60), "WHEY ISOLATE", BLACK, WHITE)),
    ("tub_whey", lambda i: tub(i, "WHEY", "PROTEIN 1800 g", (214, 186, 140))),
    ("tub_whey_choc", lambda i: tub(i, "WHEY", "CHOCOLATE 1800 g", (110, 62, 34))),
    ("creatine", lambda i: tub(i, "CREATINE", "MONOHYDRATE 300 g", (240, 200, 0))),
    ("creatine_red", lambda i: tub(i, "RELOAD+", "CREATINE 400 g", (214, 40, 48))),
    ("bcaa", lambda i: tub(i, "BCAA", "2:1:1  300 g", (40, 120, 214))),
    ("pre_green", lambda i: tub(i, "PRE", "WORKOUT  LIME", (110, 230, 60), BLACK, WHITE)),
    ("pre_pink", lambda i: tub(i, "PRE", "WORKOUT  BERRY", (240, 60, 170), BLACK, WHITE)),
    ("pre_blue", lambda i: tub(i, "PRE", "WORKOUT  ICE", (40, 190, 240), BLACK, WHITE)),
    ("rebel", lambda i: pouch(i, "WATERMELON", (240, 70, 150), "CREATINE", (24, 34, 70), WHITE)),
    ("bar_box_coconut", lambda i: bar_box(i, "COCONUT", (64, 170, 120))),
    ("bar_box_choc", lambda i: bar_box(i, "CHOCOLATE", (110, 62, 34))),
    ("bar_box_peanut", lambda i: bar_box(i, "PEANUT", (196, 120, 50))),
    ("bar_wrap_coconut", lambda i: wrapper(i, "COCONUT", (64, 170, 120))),
    ("bar_wrap_choc", lambda i: wrapper(i, "CHOCOLATE", (110, 62, 34))),
    ("bar_wrap_peanut", lambda i: wrapper(i, "PEANUT", (196, 120, 50))),
    ("magnesium", magnesium),
    ("vit_d3", lambda i: bottle(i, "VITAMIN", "D3 + K2", (240, 170, 20))),
    ("omega3", lambda i: bottle(i, "OMEGA 3", "90 CAPS", (30, 110, 190))),
    ("zinc", lambda i: bottle(i, "ZINC", "60 TABS", (120, 120, 130))),
    ("can_lime", lambda i: can(i, (120, 220, 60), "ENERGY")),
    ("can_orange", lambda i: can(i, (250, 140, 30), "ENERGY")),
    ("can_purple", lambda i: can(i, (140, 70, 210), "ENERGY")),
    ("can_red", lambda i: can(i, (220, 40, 50), "ENERGY")),
    ("rtd_choc", lambda i: bottle(i, "SHAKE", "30 g PROTEIN", (110, 62, 34))),
    ("rtd_vanilla", lambda i: bottle(i, "SHAKE", "30 g PROTEIN", (214, 176, 100))),
    ("pb_jar", jar),
    ("shaker", shaker),
]
SOLIDS = [
    ("s_white", WHITE), ("s_black", BLACK), ("s_yellow", YELLOW),
    ("s_grey", (120, 120, 124)), ("s_brown", (110, 62, 34)), ("s_beige", (226, 196, 128)),
    ("s_pink", (226, 64, 96)), ("s_dark", (60, 60, 60)), ("s_banana", (240, 206, 40)),
    ("s_caramel", (214, 128, 40)), ("s_red", (214, 40, 48)), ("s_blue", (40, 120, 214)),
    ("s_green", (110, 230, 60)), ("s_magenta", (240, 60, 170)), ("s_cyan", (40, 190, 240)),
    ("s_navy", (24, 34, 70)), ("s_lightblue", (226, 240, 250)), ("s_peanut", (196, 140, 72)),
    ("s_lime", (120, 220, 60)), ("s_orange", (250, 140, 30)), ("s_purple", (140, 70, 210)),
    ("s_silver", (190, 192, 196)), ("s_gold", (212, 170, 60)), ("s_coconut", (64, 170, 120)),
]

SIGNS = [
    "PROTEIN", "PRE-WORKOUT", "CREATINE", "PROTEIN BARS", "COLD DRINKS",
    "VITAMINS & MINERALS", "SNACKS", "AMINO ACIDS", "ISOLATE", "CHECKOUT",
]


def build_products(layout):
    atlas = Image.new("RGB", (TILE * GRID, TILE * GRID), WHITE)
    entries = PRODUCTS + [(name, (lambda c: (lambda i: solid(i, c)))(colour)) for name, colour in SOLIDS]
    if len(entries) > GRID * GRID:
        raise SystemExit("too many atlas tiles")
    for index, (name, draw) in enumerate(entries):
        tile = Image.new("RGB", (TILE, TILE), WHITE)
        draw(tile)
        x = (index % GRID) * TILE
        y = (index // GRID) * TILE
        atlas.paste(tile, (x, y))
        layout["products"][name] = [x, y, TILE, TILE]
    atlas.save(OUT / "products_atlas.png")


def build_signs(layout):
    size = 2048
    atlas = Image.new("RGB", (size, size), YELLOW)
    d = ImageDraw.Draw(atlas)
    row_h = 128
    for i, text in enumerate(SIGNS):
        y = i * row_h
        d.rectangle((0, y, 1024, y + row_h - 1), fill=YELLOW)
        d.rectangle((0, y + row_h - 12, 1024, y + row_h - 1), fill=BLACK)
        centered(d, (16, y + 8, 1008, y + row_h - 20), text, FONT_BLACK, 84, BLACK)
        layout["signs"][text] = [0, y, 1024, row_h]
    # Counter front panel: black with the yellow store name.
    y = len(SIGNS) * row_h
    d.rectangle((0, y, 1024, y + 256), fill=BLACK)
    centered(d, (40, y + 40, 984, y + 216), "protein.com", FONT_BLACK, 150, YELLOW)
    layout["signs"]["counter_panel"] = [0, y, 1024, 256]
    # Posters (portrait 512 x 768) on the right half.
    posters = [
        ("poster_whey", "NEW", "COMPLETE WHEY", (110, 62, 34)),
        ("poster_creatine", "STRONGER", "CREATINE", (240, 200, 0)),
        ("poster_bar", "SNACK SMART", "PROTEIN BAR", (28, 120, 196)),
    ]
    for i, (key, head, product, colour) in enumerate(posters):
        x = 1024 + (i % 2) * 512
        y = (i // 2) * 768
        d.rectangle((x, y, x + 511, y + 767), fill=WHITE)
        d.rectangle((x, y, x + 511, y + 120), fill=BLACK)
        centered(d, (x + 20, y + 16, x + 492, y + 104), head, FONT_BLACK, 80, YELLOW)
        # Product silhouette: pouch with the flavour bolt.
        d.rectangle((x + 136, y + 190, x + 376, y + 560), fill=(236, 236, 232), outline=(200, 200, 196), width=4)
        bolt(d, x + 186, y + 260, 140, 220, colour)
        centered(d, (x + 20, y + 590, x + 492, y + 660), product, FONT_BLACK, 56, BLACK)
        d.rectangle((x, y + 700, x + 511, y + 767), fill=YELLOW)
        centered(d, (x + 20, y + 708, x + 492, y + 760), "protein.com", FONT_BLACK, 44, BLACK)
        layout["signs"][key] = [x, y, 512, 768]
    # Open-hours door decal.
    x, y = 1024, 1536
    d.rectangle((x, y, x + 511, y + 255), fill=WHITE)
    centered(d, (x + 16, y + 20, x + 496, y + 100), "OPEN", FONT_BLACK, 80, BLACK)
    centered(d, (x + 16, y + 120, x + 496, y + 230), "MON-SAT 8-20", FONT_BOLD, 54, BLACK)
    layout["signs"]["door_hours"] = [x, y, 512, 256]
    atlas.save(OUT / "signs_atlas.png")


def build_floor():
    size = 512
    img = Image.new("RGB", (size, size), (214, 214, 210))
    d = ImageDraw.Draw(img)
    # 2 x 2 large tiles per texture with thin grout lines.
    for i in range(0, size + 1, size // 2):
        d.rectangle((i - 2, 0, i + 1, size), fill=(182, 182, 178))
        d.rectangle((0, i - 2, size, i + 1), fill=(182, 182, 178))
    img.save(OUT / "floor_tile.png")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    layout = {"atlas_size": TILE * GRID, "products": {}, "signs": {}, "signs_size": 2048}
    build_products(layout)
    build_signs(layout)
    build_floor()
    (OUT / "atlas_layout.json").write_text(json.dumps(layout, indent=1))
    print("STORE_TEXTURES_OK", len(layout["products"]), "tiles", len(layout["signs"]), "signs")


if __name__ == "__main__":
    main()
