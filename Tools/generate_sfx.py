"""Synthesise the small GymChaos sound effects (UI, movement, actions).

Writes 16-bit mono 44.1 kHz WAV files into
GymChaos/Assets/StreamingAssets/BodyBuilders/sound/sfx/. Everything is made
from oscillators and filtered noise with a fixed seed, so the files are
reproducible and carry no third-party licence.

Usage: python Tools/generate_sfx.py
"""

import math
import pathlib
import wave

import numpy as np

RATE = 44100
OUT = (pathlib.Path(__file__).resolve().parent.parent /
       "GymChaos/Assets/StreamingAssets/BodyBuilders/sound/sfx")
RNG = np.random.default_rng(20261006)


def t_axis(seconds):
    return np.arange(int(RATE * seconds)) / RATE


def lowpass(signal, cutoff):
    """One-pole low-pass; cutoff may be a scalar or a per-sample array."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=float), signal.shape)
    alpha = 1.0 - np.exp(-2.0 * math.pi * cutoff / RATE)
    out = np.empty_like(signal)
    state = 0.0
    for i, (x, a) in enumerate(zip(signal, alpha)):
        state += a * (x - state)
        out[i] = state
    return out


def highpass(signal, cutoff):
    return signal - lowpass(signal, cutoff)


def envelope(t, attack, decay_rate):
    attack_part = np.clip(t / max(attack, 1e-4), 0.0, 1.0)
    return attack_part * np.exp(-np.maximum(0.0, t - attack) * decay_rate)


def tone(t, freq, phase=0.0):
    freq = np.broadcast_to(np.asarray(freq, dtype=float), t.shape)
    return np.sin(2.0 * math.pi * np.cumsum(freq) / RATE + phase)


def fade_out(signal, seconds=0.01):
    n = min(len(signal), int(RATE * seconds))
    if n > 0:
        signal[-n:] *= np.linspace(1.0, 0.0, n)
    return signal


def write(name, signal, peak=0.8):
    signal = fade_out(np.asarray(signal, dtype=float))
    top = np.max(np.abs(signal))
    if top > 0:
        signal = signal / top * peak
    data = (np.clip(signal, -1.0, 1.0) * 32767).astype("<i2")
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / name
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(RATE)
        handle.writeframes(data.tobytes())
    print(f"wrote {path.relative_to(OUT.parents[4])} {len(data) / RATE:.3f}s")


def ui_hover():
    # Short, soft glassy tick for focus moves.
    t = t_axis(0.055)
    body = tone(t, 2350.0) * 0.6 + tone(t, 3520.0) * 0.25
    click = highpass(RNG.standard_normal(len(t)), 3000.0) * 0.25
    return (body + click * np.exp(-t * 400.0)) * envelope(t, 0.002, 95.0)


def ui_click():
    # Two quick plastic clicks with a low body: a pressed menu plate.
    t = t_axis(0.11)
    thump = tone(t, 520.0 * np.exp(-t * 18.0) + 180.0) * envelope(t, 0.001, 55.0)
    snap = highpass(RNG.standard_normal(len(t)), 2200.0) * np.exp(-t * 260.0)
    late = np.zeros_like(t)
    offset = int(RATE * 0.028)
    late[offset:] = snap[: len(t) - offset] * 0.45
    return thump * 0.9 + snap * 0.55 + late


def ui_confirm():
    # Rising two-note chime (E6 then B6) for accepting a choice.
    t = t_axis(0.36)
    first = (tone(t, 1318.5) + 0.3 * tone(t, 2637.0)) * envelope(t, 0.003, 14.0)
    second_t = np.maximum(0.0, t - 0.075)
    second = (tone(second_t, 1975.5) + 0.3 * tone(second_t, 3951.0)) * \
        envelope(second_t, 0.003, 11.0) * (t >= 0.075)
    return first * 0.7 + second * 0.8


def ui_back():
    # Falling two-note cue for closing or cancelling.
    t = t_axis(0.24)
    first = tone(t, 1174.7) * envelope(t, 0.003, 22.0)
    second_t = np.maximum(0.0, t - 0.06)
    second = tone(second_t, 880.0) * envelope(second_t, 0.003, 20.0) * (t >= 0.06)
    return first * 0.7 + second * 0.75


def ui_error():
    # Low buzz for a refused action (locked option, not enough stamina).
    t = t_axis(0.2)
    buzz = np.sign(tone(t, 155.0)) * 0.5 + tone(t, 310.0) * 0.3
    return lowpass(buzz, 1800.0) * envelope(t, 0.004, 16.0)


def jump():
    # Breath of air plus a light shoe scuff on take-off.
    t = t_axis(0.24)
    noise = RNG.standard_normal(len(t))
    sweep = lowpass(noise, 700.0 + 2600.0 * np.clip(t / 0.12, 0.0, 1.0))
    whoosh = highpass(sweep, 250.0) * envelope(t, 0.035, 16.0)
    scuff = highpass(noise, 1500.0) * np.exp(-t * 140.0) * 0.35
    push = tone(t, 140.0 - 40.0 * t) * envelope(t, 0.002, 45.0) * 0.4
    return whoosh * 0.8 + scuff + push


def land():
    # Heavy body landing: low thump, sole slap and a short rubber-floor tail.
    t = t_axis(0.32)
    thump = tone(t, 48.0 + 65.0 * np.exp(-t * 30.0)) * envelope(t, 0.002, 17.0)
    noise = RNG.standard_normal(len(t))
    slap = lowpass(highpass(noise, 300.0), 2800.0) * np.exp(-t * 70.0)
    tail = lowpass(noise, 450.0) * envelope(t, 0.01, 22.0) * 0.6
    return thump * 1.1 + slap * 0.55 + tail


def pickup():
    # Grab: quick upward swipe with a tight grip tick.
    t = t_axis(0.17)
    noise = RNG.standard_normal(len(t))
    swipe = highpass(lowpass(noise, 900.0 + 5200.0 * t / 0.17), 400.0) * envelope(t, 0.02, 28.0)
    grip = tone(t, 620.0 + 900.0 * t) * envelope(t, 0.002, 60.0) * 0.45
    return swipe * 0.75 + grip


def item_drop():
    # Released item: short downward swipe.
    t = t_axis(0.16)
    noise = RNG.standard_normal(len(t))
    swipe = highpass(lowpass(noise, 4200.0 - 3000.0 * t / 0.16), 300.0) * envelope(t, 0.01, 30.0)
    return swipe


def action_confirm():
    # Gameplay confirmation (rep counted, station entered, goal met):
    # a bright metallic plate ping, distinct from the menu chime.
    t = t_axis(0.5)
    partials = [(880.0, 1.0, 7.0), (1760.0 * 1.003, 0.55, 9.0),
                (2637.0, 0.35, 12.0), (4186.0, 0.18, 18.0)]
    ping = sum(tone(t, f) * a * envelope(t, 0.002, d) for f, a, d in partials)
    tick = highpass(RNG.standard_normal(len(t)), 3500.0) * np.exp(-t * 300.0) * 0.4
    return ping + tick


def level_up():
    # Short rising arpeggio for a level or mastery gain.
    t = t_axis(0.62)
    out = np.zeros_like(t)
    for index, freq in enumerate((783.99, 987.77, 1174.66, 1567.98)):
        start = index * 0.07
        local = np.maximum(0.0, t - start)
        out += (tone(local, freq) + 0.25 * tone(local, freq * 2.0)) * \
            envelope(local, 0.003, 7.5) * (t >= start)
    return out


# ui_confirm (also used for clicks and back/exit), jump, land and car_driving are
# user-provided recordings from Assets/BodyBuilders/sound/sfx; do not
# regenerate them here.
def car_door_open():
    # Driver door: latch release click, then a short hinge swing whoosh.
    rng = np.random.default_rng(20261021)
    t = t_axis(0.42)
    noise = rng.standard_normal(len(t))
    click = highpass(noise, 2200.0) * np.exp(-t * 260.0)
    pop = tone(t, 900.0 - 500.0 * t) * envelope(t, 0.001, 90.0) * 0.35
    swing_t = np.maximum(0.0, t - 0.06)
    swing = lowpass(highpass(noise, 300.0), 1400.0) * envelope(swing_t, 0.08, 9.0) * 0.4
    swing *= (t > 0.06)
    return click * 0.9 + pop + swing


def car_door_close():
    # Driver door shutting: heavy body thunk, latch catch and a dull rattle.
    rng = np.random.default_rng(20261022)
    t = t_axis(0.5)
    noise = rng.standard_normal(len(t))
    thunk = tone(t, 62.0 + 70.0 * np.exp(-t * 40.0)) * envelope(t, 0.002, 14.0)
    body = lowpass(noise, 520.0) * envelope(t, 0.003, 20.0) * 0.9
    latch = highpass(noise, 2600.0) * np.exp(-np.maximum(0.0, t - 0.012) * 220.0) * (t > 0.012) * 0.5
    return thunk * 1.2 + body + latch


# --- Race minigame ---------------------------------------------------------

def race_beep():
    # Countdown light: short square-ish beep.
    t = t_axis(0.32)
    wave_ = np.sign(tone(t, 660.0)) * 0.5 + tone(t, 660.0) * 0.5
    return lowpass(wave_, 5000.0) * envelope(t, 0.004, 9.0)


def race_go():
    # GO light: higher, longer beep with a fifth on top.
    t = t_axis(0.75)
    wave_ = np.sign(tone(t, 1320.0)) * 0.4 + tone(t, 1320.0) * 0.4 + tone(t, 1980.0) * 0.25
    return lowpass(wave_, 6500.0) * envelope(t, 0.004, 3.2)


def race_drift_loop():
    # Tyre squeal: band-limited noise with a wavering squeal tone; loops cleanly.
    rng = np.random.default_rng(20261023)
    t = t_axis(1.0)
    noise = rng.standard_normal(len(t))
    hiss = lowpass(highpass(noise, 1800.0), 5200.0) * 0.5
    squeal = tone(t, 1150.0 + 60.0 * np.sin(2.0 * math.pi * 3.0 * t)) * 0.35
    out = hiss + squeal
    # Cross-fade the ends so the loop has no click.
    n = int(RATE * 0.05)
    out[:n] = out[:n] * np.linspace(0.0, 1.0, n) + out[-n:] * np.linspace(1.0, 0.0, n)
    return out[:-n]


def race_mini_turbo():
    # Drift release: rising zip plus a puff of noise.
    rng = np.random.default_rng(20261024)
    t = t_axis(0.5)
    zip_ = tone(t, 400.0 + 1600.0 * t) * envelope(t, 0.01, 6.0)
    puff = lowpass(rng.standard_normal(len(t)), 900.0) * envelope(t, 0.005, 10.0)
    return zip_ * 0.6 + puff


def race_boost():
    # Boost: jet whoosh, swelling then fading over the boost.
    rng = np.random.default_rng(20261025)
    t = t_axis(1.6)
    noise = rng.standard_normal(len(t))
    cutoff = 600.0 + 2600.0 * np.exp(-t * 2.0)
    whoosh = lowpass(noise, cutoff) * envelope(t, 0.06, 1.6)
    rumble = tone(t, 70.0 + 30.0 * t) * envelope(t, 0.02, 1.8) * 0.6
    return whoosh + rumble


def race_bump():
    # Kart to kart: dull body thump with a plastic rattle.
    rng = np.random.default_rng(20261026)
    t = t_axis(0.35)
    noise = rng.standard_normal(len(t))
    thump = tone(t, 90.0 + 80.0 * np.exp(-t * 30.0)) * envelope(t, 0.002, 18.0)
    rattle = highpass(noise, 1500.0) * envelope(t, 0.001, 35.0) * 0.4
    return thump + rattle


def race_wall_hit():
    # Scrape into a concrete barrier: grinding noise with a hit at the start.
    rng = np.random.default_rng(20261027)
    t = t_axis(0.55)
    noise = rng.standard_normal(len(t))
    hit = lowpass(noise, 700.0) * envelope(t, 0.001, 25.0)
    scrape = lowpass(highpass(noise, 900.0), 3500.0) * envelope(t, 0.02, 6.0) * 0.5
    return hit + scrape


def race_lap():
    # New lap: two bright bell tones.
    t = t_axis(0.8)
    out = np.zeros_like(t)
    for index, freq in enumerate((1046.5, 1568.0)):
        start = index * 0.12
        local = np.maximum(0.0, t - start)
        out += (tone(local, freq) + 0.3 * tone(local, freq * 2.76)) * envelope(local, 0.002, 5.0) * (t >= start)
    return out


def race_final_lap():
    # Final lap fanfare: quick rising triad, repeated higher.
    t = t_axis(1.1)
    out = np.zeros_like(t)
    notes = (523.25, 659.25, 783.99, 659.25, 783.99, 1046.5)
    for index, freq in enumerate(notes):
        start = index * 0.11
        local = np.maximum(0.0, t - start)
        voice = np.sign(tone(local, freq)) * 0.3 + tone(local, freq) * 0.6
        out += lowpass(voice, 4000.0) * envelope(local, 0.004, 6.0 if index < 5 else 2.5) * (t >= start)
    return out


def race_finish():
    # Finish: major chord swell with a sparkle on top.
    t = t_axis(1.8)
    out = np.zeros_like(t)
    for freq in (523.25, 659.25, 783.99, 1046.5):
        out += (tone(t, freq) + 0.2 * tone(t, freq * 2.0)) * envelope(t, 0.03, 1.4)
    sparkle = np.zeros_like(t)
    for index, freq in enumerate((2093.0, 2637.0, 3136.0, 4186.0)):
        start = 0.1 + index * 0.08
        local = np.maximum(0.0, t - start)
        sparkle += tone(local, freq) * envelope(local, 0.002, 9.0) * (t >= start)
    return out + sparkle * 0.4


def race_horn():
    # Kart horn: two detuned square tones.
    t = t_axis(0.45)
    wave_ = np.sign(tone(t, 415.0)) + np.sign(tone(t, 523.0))
    return lowpass(wave_, 2400.0) * envelope(t, 0.01, 2.0)


def race_crumble():
    # Towers dropping away: deep rumble with gravel noise.
    rng = np.random.default_rng(20261028)
    t = t_axis(1.6)
    noise = rng.standard_normal(len(t))
    rumble = lowpass(noise, 140.0) * envelope(t, 0.05, 1.8) * 3.0
    gravel = lowpass(highpass(noise, 500.0), 2500.0) * envelope(t, 0.1, 2.4) * 0.4
    return rumble + gravel


SOUNDS = {
    "ui_hover.wav": (ui_hover, 0.55),
    "ui_error.wav": (ui_error, 0.6),
    "pickup.wav": (pickup, 0.7),
    "item_drop.wav": (item_drop, 0.6),
    "action_confirm.wav": (action_confirm, 0.7),
    "level_up.wav": (level_up, 0.7),
    "car_door_open.wav": (car_door_open, 0.6),
    "car_door_close.wav": (car_door_close, 0.75),
    "race_beep.wav": (race_beep, 0.6),
    "race_go.wav": (race_go, 0.7),
    "race_drift_loop.wav": (race_drift_loop, 0.5),
    "race_mini_turbo.wav": (race_mini_turbo, 0.65),
    "race_boost.wav": (race_boost, 0.75),
    "race_bump.wav": (race_bump, 0.75),
    "race_wall_hit.wav": (race_wall_hit, 0.65),
    "race_lap.wav": (race_lap, 0.6),
    "race_final_lap.wav": (race_final_lap, 0.6),
    "race_finish.wav": (race_finish, 0.7),
    "race_horn.wav": (race_horn, 0.6),
    "race_crumble.wav": (race_crumble, 0.8),
}


def main():
    # Optional file names limit the run (python Tools/generate_sfx.py car_door_open.wav).
    import sys
    wanted = set(sys.argv[1:])
    for name, (builder, peak) in SOUNDS.items():
        if not wanted or name in wanted:
            write(name, builder(), peak)


if __name__ == "__main__":
    main()
