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
SOUNDS = {
    "ui_hover.wav": (ui_hover, 0.55),
    "ui_error.wav": (ui_error, 0.6),
    "pickup.wav": (pickup, 0.7),
    "item_drop.wav": (item_drop, 0.6),
    "action_confirm.wav": (action_confirm, 0.7),
    "level_up.wav": (level_up, 0.7),
}


def main():
    for name, (builder, peak) in SOUNDS.items():
        write(name, builder(), peak)


if __name__ == "__main__":
    main()
