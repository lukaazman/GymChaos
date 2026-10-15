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


def tub(img, title, sub, colour, bg=WHITE, ink=BLACK):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=bg)
    d.rectangle((0, 0, TILE, 28), fill=colour)
    brand(d, 0, 30, TILE, ink)
    bolt(d, 18, 64, 90, 120, colour)
    centered(d, (100, 78, TILE - 8, 128), title, FONT_BLACK, 34, ink)
    centered(d, (100, 132, TILE - 8, 160), sub, FONT_BOLD, 20, ink)
    d.rectangle((0, TILE - 28, TILE, TILE), fill=colour)


def bottle(img, title, sub, colour):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=WHITE)
    d.rectangle((0, 60, TILE, 196), fill=colour)
    centered(d, (8, 70, TILE - 8, 120), title, FONT_BLACK, 34, WHITE)
    centered(d, (8, 130, TILE - 8, 170), sub, FONT_BOLD, 24, WHITE)
    brand(d, 0, 18, TILE, BLACK)


def shaker(img):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, TILE, TILE), fill=BLACK)
    bolt(d, 96, 50, 64, 90, YELLOW)
    brand(d, 0, 160, TILE, YELLOW)
    d.rectangle((0, 230, TILE, TILE), fill=YELLOW)


FONT_SCRIPT = "C:/Windows/Fonts/segoescb.ttf"


def framed(width, height, paint):
    """Paints at the face's real aspect, then squeezes into the square tile, so
    the art is not stretched once it is mapped onto a long or narrow face."""
    def draw(img):
        canvas = Image.new("RGB", (width, height), WHITE)
        paint(canvas)
        img.paste(canvas.resize((TILE, TILE), Image.LANCZOS), (0, 0))
    return draw


def pouch_front(title, flavour, accent, bg=WHITE, ink=BLACK, badge=None):
    """Stand-up pouch front (256 x 400): brand, two-line title, flavour bolt,
    optional badge and flavour, all kept well inside the folded edges."""
    def paint(img):
        w, h = img.size
        d = ImageDraw.Draw(img)
        d.rectangle((0, 0, w, h), fill=bg)
        d.rectangle((0, h - 30, w, h), fill=accent)
        centered(d, (30, 30, w - 30, 62), "protein.com", FONT_BLACK, 24, YELLOW if bg == BLACK else ink)
        d.line((60, 72, w - 60, 72), fill=accent, width=2)
        first, second = title
        centered(d, (30, 86, w - 30, 128), first, FONT_BLACK, 40, ink)
        centered(d, (30, 128, w - 30, 168), second, FONT_BLACK, 40, ink)
        bolt(d, w / 2 - 48, 186, 96, 116, accent)
        if badge:
            d.rounded_rectangle((60, 318, w - 60, 350), radius=12, outline=accent, width=3)
            centered(d, (64, 320, w - 64, 348), badge, FONT_BOLD, 18, accent)
        centered(d, (30, 356, w - 30, 380), flavour, FONT_BOLD, 18, ink)
    return framed(256, 400, paint)


def pouch_side(title, accent, bg=WHITE, ink=BLACK):
    """Gusset side (96 x 400): coloured spine with the product name running
    up it and a small nutrition panel."""
    def paint(img):
        w, h = img.size
        d = ImageDraw.Draw(img)
        d.rectangle((0, 0, w, h), fill=bg)
        d.rectangle((w // 2 - 14, 0, w // 2 + 14, h), fill=accent)
        label = Image.new("RGB", (220, 28), accent)
        centered(ImageDraw.Draw(label), (0, 0, 220, 28), title, FONT_BLACK, 20, BLACK if accent != BLACK else WHITE)
        img.paste(label.rotate(90, expand=True), (w // 2 - 14, 30))
        line = (200, 200, 200) if bg != WHITE else (90, 90, 90)
        d.rectangle((10, 270, w - 10, 360), outline=ink, width=2)
        for n, y in enumerate(range(282, 352, 14)):
            d.line((16, y, w - 16 - (n % 3) * 10, y), fill=line, width=3)
    return framed(96, 400, paint)


GOLD = (212, 170, 60)


CHOCOLATE = (92, 52, 30)

# Bar flavours: name, accent colour, fruit painter (drawn in x 470-640).


def _dip(d):
    # Chocolate dip over the top of the fruit, with two drips.
    d.polygon([(492, 0), (640, 0), (640, 70), (604, 92), (572, 80), (546, 104), (516, 96), (492, 110)],
              fill=CHOCOLATE)
    for x, y, r in ((530, 94, 9), (572, 80, 7)):
        d.ellipse((x - r, y - r, x + r, y + r + 8), fill=CHOCOLATE)


def fruit_banana(d):
    d.ellipse((478, 18, 720, 300), fill=(250, 214, 40))
    d.ellipse((520, -14, 760, 262), fill=WHITE)
    _dip(d)


def fruit_coconut(d):
    # Half a coconut: brown shell ring around white flesh.
    d.ellipse((498, 84, 640, 226), fill=(110, 70, 40))
    d.ellipse((512, 98, 626, 212), fill=(246, 244, 236))
    _dip(d)


def fruit_peanut(d):
    shell = (214, 160, 90)
    d.ellipse((500, 96, 600, 186), fill=shell)
    d.ellipse((540, 150, 636, 246), fill=shell)
    d.ellipse((534, 140, 590, 196), fill=shell)
    _dip(d)


def fruit_berry(d):
    for x, y, r, c in ((528, 160, 36, (200, 30, 70)), (590, 176, 30, (130, 40, 120)), (560, 214, 28, (200, 30, 70))):
        d.ellipse((x - r, y - r, x + r, y + r), fill=c)
    d.polygon([(520, 120), (548, 112), (536, 136)], fill=(60, 150, 60))
    _dip(d)


BAR_FLAVOURS = {
    "banana": ("BANANA", (232, 176, 0), fruit_banana),
    "coconut": ("COCONUT", (40, 150, 100), fruit_coconut),
    "peanut": ("PEANUT", (176, 104, 40), fruit_peanut),
    "berry": ("BERRY", (200, 40, 90), fruit_berry),
}


def _bar_art(img, flavour_key, pack):
    """proteini.si bar look: white film, an outlined PROTEIN Bar badge, the
    flavour in colour and the fruit with a chocolate dip. 640 x 256."""
    name, accent, fruit = BAR_FLAVOURS[flavour_key]
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 640, 256), fill=WHITE)
    fruit(d)
    d.rectangle((0, 0, 470, 256), fill=WHITE)
    d.rectangle((40, 42, 268, 214), outline=BLACK, width=8)
    centered(d, (52, 54, 256, 124), "PROTEIN", FONT_BLACK, 52, BLACK)
    centered(d, (52, 118, 256, 206), "Bar", FONT_SCRIPT, 72, accent)
    centered(d, (290, 56, 470, 106), name, FONT_BLACK, 40, accent)
    centered(d, (290, 106, 470, 140), "MILK CHOCOLATE", FONT_BOLD, 22, CHOCOLATE)
    centered(d, (290, 162, 470, 190), "protein.com", FONT_BLACK, 20, BLACK)
    if pack:
        centered(d, (290, 196, 470, 226), pack, FONT_BOLD, 20, BLACK)


def bar_wrapper(flavour_key):
    """Single bar: the flat top is about 3.25 : 1, so the 2.5 : 1 art is
    centred with plain film either side instead of being stretched."""
    def paint(canvas):
        art = Image.new("RGB", (640, 256), WHITE)
        _bar_art(art, flavour_key, None)
        canvas.paste(art, ((canvas.size[0] - 640) // 2, 0))
    return framed(832, 256, paint)


def shake_label(flavour, script, splash, splash_dark, number=WHITE):
    """Ready-to-drink shake, proteini.si style: white bottle label with the
    PROTEIN Shake mark, a splash in the flavour colour and a big 25 g.
    Front half of the bottle, about 1 : 1."""
    def paint(img):
        w, h = img.size
        d = ImageDraw.Draw(img)
        d.rectangle((0, 0, w, h), fill=WHITE)
        # Splash wave across the lower half.
        d.polygon([(0, 150), (40, 128), (90, 146), (140, 120), (190, 140), (256, 118), (256, 256), (0, 256)],
                  fill=splash)
        d.polygon([(0, 196), (60, 176), (120, 192), (180, 170), (256, 186), (256, 256), (0, 256)], fill=splash_dark)
        centered(d, (40, 14, w - 40, 34), "protein.com", FONT_BLACK, 16, BLACK)
        d.rectangle((54, 44, w - 54, 118), outline=BLACK, width=5)
        centered(d, (60, 48, w - 60, 82), "PROTEIN", FONT_BLACK, 30, BLACK)
        centered(d, (60, 78, w - 60, 116), "Shake", FONT_SCRIPT, 34, script)
        centered(d, (40, 122, w - 40, 142), flavour, FONT_BOLD, 16, script)
        centered(d, (70, 180, w - 70, 236), "25g", FONT_BLACK, 48, number)
        centered(d, (50, 234, w - 50, 252), "PROTEIN PER SERVING", FONT_BOLD, 11, number)
    return framed(256, 256, paint)


def jar_label(img):
    """Peanut butter jar band, proteini.si style: white with the boxed
    PEANUT Butter mark, a peanut swirl and the weight. About 2 : 1."""
    w, h = img.size
    d = ImageDraw.Draw(img)
    tan, brown = (200, 140, 70), (120, 72, 34)
    d.rectangle((0, 0, w, h), fill=WHITE)
    centered(d, (150, 14, w - 150, 40), "protein.com", FONT_BLACK, 22, BLACK)
    d.rectangle((150, 52, w - 150, 196), outline=tan, width=8)
    centered(d, (160, 58, w - 160, 120), "PEANUT", FONT_BLACK, 50, tan)
    centered(d, (160, 112, w - 160, 190), "Butter", FONT_SCRIPT, 60, brown)
    centered(d, (150, 206, w - 150, 232), "SOURCE OF PROTEIN", FONT_BOLD, 18, brown)
    # Peanut swirl on the right, nuts on the left.
    d.ellipse((392, 120, 470, 200), fill=(214, 160, 90))
    d.ellipse((404, 100, 458, 150), fill=(224, 176, 106))
    d.ellipse((416, 84, 446, 114), fill=(234, 190, 120))
    for x, y in ((50, 120), (88, 150)):
        d.ellipse((x - 22, y - 16, x + 22, y + 16), fill=(214, 160, 90))
    centered(d, (20, 200, 140, 230), "1000 g", FONT_BLACK, 22, brown)


def claw_can(bg, claw, title, accent, ink):
    """Energy can in the white-can style: three ragged claw strokes, a
    small ZERO SUGAR line, the variant name and ENERGY underneath."""
    def paint(img):
        w, h = img.size
        d = ImageDraw.Draw(img)
        d.rectangle((0, 0, w, h), fill=bg)
        centered(d, (40, 10, w - 40, 30), "ZERO SUGAR", FONT_BOLD, 14, ink)
        for i, cx in enumerate((62, 96, 130)):
            top, bottom = 40 + (i % 2) * 6, 168 - (i == 1) * 10
            left, right = [], []
            steps = 7
            for k in range(steps + 1):
                y = top + (bottom - top) * k / steps
                width = 13 * (1 - 0.55 * k / steps)
                jag = (-6 if k % 2 else 5) * (1 - k / steps)
                left.append((cx - width + jag + (i - 1) * 4 * k / steps, y))
                right.append((cx + width + jag * 0.6 + (i - 1) * 4 * k / steps, y))
            d.polygon(left + right[::-1], fill=claw)
        centered(d, (36, 184, w - 36, 206), "ENERGY", FONT_BLACK, 18, accent)
        centered(d, (32, 206, w - 32, 236), title, FONT_BLACK, 26, ink)
        centered(d, (40, 238, w - 40, 254), "protein.com", FONT_BOLD, 11, ink)
    return framed(192, 256, paint)


def solid(img, colour):
    ImageDraw.Draw(img).rectangle((0, 0, TILE, TILE), fill=colour)


PRODUCTS = [
    ("whey_choc", pouch_front(("COMPLETE", "WHEY"), "CHOCOLATE", (110, 62, 34), badge="80% PROTEIN")),
    ("whey_vanilla", pouch_front(("COMPLETE", "WHEY"), "VANILLA", (214, 176, 100), badge="80% PROTEIN")),
    ("whey_straw", pouch_front(("COMPLETE", "WHEY"), "STRAWBERRY", (226, 64, 96), badge="80% PROTEIN")),
    ("whey_cookie", pouch_front(("COMPLETE", "WHEY"), "COOKIES & CREAM", (60, 60, 60), badge="80% PROTEIN")),
    ("whey_banana", pouch_front(("COMPLETE", "WHEY"), "BANANA", (230, 190, 20), badge="80% PROTEIN")),
    ("whey_caramel", pouch_front(("COMPLETE", "WHEY"), "SALTED CARAMEL", (214, 128, 40), badge="80% PROTEIN")),
    ("whey_side", pouch_side("COMPLETE WHEY", YELLOW)),
    ("isolate", pouch_front(("WHEY", "ISOLATE"), "DOUBLE CHOCOLATE", GOLD, BLACK, WHITE, "90% PROTEIN")),
    ("isolate_side", pouch_side("WHEY ISOLATE", GOLD, BLACK, WHITE)),
    ("tub_whey", lambda i: tub(i, "WHEY", "PROTEIN 1800 g", (214, 186, 140))),
    ("tub_whey_choc", lambda i: tub(i, "WHEY", "CHOCOLATE 1800 g", (110, 62, 34))),
    ("creatine", lambda i: tub(i, "CREATINE", "MONOHYDRATE 300 g", (240, 200, 0))),
    ("creatine_red", lambda i: tub(i, "RELOAD+", "CREATINE 400 g", (214, 40, 48))),
    ("bcaa", lambda i: tub(i, "BCAA", "2:1:1  300 g", (40, 120, 214))),
    ("pre_green", lambda i: tub(i, "PRE", "WORKOUT  LIME", (110, 230, 60), BLACK, WHITE)),
    ("pre_pink", lambda i: tub(i, "PRE", "WORKOUT  BERRY", (240, 60, 170), BLACK, WHITE)),
    ("pre_blue", lambda i: tub(i, "PRE", "WORKOUT  ICE", (40, 190, 240), BLACK, WHITE)),
    ("rebel", pouch_front(("CREATINE", "REBEL"), "WATERMELON", (240, 70, 150), (24, 34, 70), WHITE, "3 G PER SERVING")),
    ("rebel_side", pouch_side("CREATINE REBEL", (240, 70, 150), (24, 34, 70), WHITE)),
    ("bar_wrap_coconut", bar_wrapper("coconut")),
    ("bar_wrap_choc", bar_wrapper("banana")),
    ("bar_wrap_berry", bar_wrapper("berry")),
    ("bar_wrap_peanut", bar_wrapper("peanut")),
    ("magnesium", pouch_front(("MAGNESIUM", "+ B6"), "LEMON  10 x 15 G", YELLOW, badge="ZERO SUGAR")),
    ("magnesium_side", pouch_side("MAGNESIUM", YELLOW)),
    ("vit_d3", lambda i: bottle(i, "VITAMIN", "D3 + K2", (240, 170, 20))),
    ("omega3", lambda i: bottle(i, "OMEGA 3", "90 CAPS", (30, 110, 190))),
    ("zinc", lambda i: bottle(i, "ZINC", "60 TABS", (120, 120, 130))),
    ("can_lime", claw_can((244, 245, 246), (150, 154, 160), "ULTRA", (70, 170, 230), BLACK)),
    ("can_orange", claw_can(BLACK, (250, 140, 30), "MANGO", (250, 140, 30), WHITE)),
    ("can_purple", claw_can(BLACK, (150, 80, 220), "GRAPE", (150, 80, 220), WHITE)),
    ("can_red", claw_can(BLACK, (220, 40, 50), "CHERRY", (220, 40, 50), WHITE)),
    ("rtd_choc", shake_label("CHOCOLATE", (226, 60, 120), (120, 72, 46), (92, 52, 30))),
    ("rtd_vanilla", shake_label("VANILLA", (214, 168, 30), (246, 226, 150), (236, 206, 110), (120, 88, 10))),
    ("pb_jar", framed(512, 256, jar_label)),
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
    ("s_silver", (190, 192, 196)), ("s_canwhite", (244, 245, 246)), ("s_gold", (212, 170, 60)), ("s_coconut", (64, 170, 120)),
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
