import subprocess
from pathlib import Path
import os

FLUIDSYNTH_EXEC = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    'executables', 
    'fluidsynth.exe'
)

def midi_to_wav(midi_path, wav_path, sf2_path, sample_rate=32000):
    midi_path = str(midi_path)
    wav_path  = str(wav_path)
    sf2_path  = str(sf2_path)

    Path(wav_path).parent.mkdir(parents=True, exist_ok=True)

    cmd = [
        FLUIDSYNTH_EXEC,
        "-ni",
        "-F", str(wav_path),
        "-r", str(sample_rate),
        str(sf2_path),
        str(midi_path),
    ]

    if not os.path.exists(FLUIDSYNTH_EXEC):
        print(f"FATAL ERROR: Fluidsynth executable not found at: {FLUIDSYNTH_EXEC}")
        raise FileNotFoundError(f"Fluidsynth executable not found at: {FLUIDSYNTH_EXEC}")

    subprocess.run(cmd, check=True)
    return wav_path