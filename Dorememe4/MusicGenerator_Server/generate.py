import re, math
import time
from typing import List, Optional
from pathlib import Path
import torch
import torch.nn as nn
import miditoolkit

NOTE_RE = re.compile(r"^NOTE_(\d+)$")
DUR_RE = re.compile(r"^DUR_(\d+)$")
VEL_RE = re.compile(r"^VEL_(\d+)$")
POS_RE = re.compile(r"^POS_(\d+)$")
BPM_RE = re.compile(r"^BPM_(\d+)$")
    
def bars_to_seconds(bars: int, bpm: int, beats_per_bar: int = 4) -> float:
    return bars * (beats_per_bar * 60.0 / bpm)

def parse_bpm(prefix_tokens, default=120):
    for t in prefix_tokens:
        m = BPM_RE.match(t)
        if m:
            try: return int(m.group(1))
            except: pass
    return default
    
# --- 토큰 생성 함수 ---

@torch.no_grad()
def generate_until_seconds(model: nn.Module,
                           dataset,
                           prefix_tokens: List[str],
                           target_sec: float,
                           temperature = 1.0,
                           top_p: float = 0.98,
                           max_steps: int = 1024, # 8000 -> 1024
                           beats_per_bar: int = 4,
                           fill_last_bar: bool = False,
                           generator: Optional[torch.Generator] = None,
                           ):
    model.eval()
    stoi = dataset.stoi
    itos = dataset.itos
    PAD_ID = dataset.PAD_ID
    EOS_ID = dataset.EOS_ID

    bpm = parse_bpm(prefix_tokens, default=120)

    # 목표 마디 수 계산
    target_bars = max(4, int(math.ceil(target_sec * bpm / (60 * beats_per_bar))))

    dev = next(model.parameters()).device

    # prefix 준비
    ids = [stoi.get(t, PAD_ID) for t in prefix_tokens]
    x = torch.tensor(ids, dtype=torch.long, device=dev).unsqueeze(0)

    prefix_bars = sum(1 for t in prefix_tokens if t == "BAR")
    bars = prefix_bars

    limit = (bars >= target_bars)
    
    # 시간 기반 안전 장치를 위한 시작 시간 기록
    start_time = time.time() 

    steps = 0
    stop = False
    in_last_bar = False
    lastbar_note_cnt = 0

    # 루프 조건: max_steps 또는 내부 break에 의존
    while True: 
        # 1. 생성 길이/시간 초과 시 무조건 종료
        if steps >= max_steps:
             print(f"[Generator] WARNING: Max steps ({max_steps}) reached. Forcing stop.")
             break 
             
        # 2. 시간 초과 조건
        if (time.time() - start_time) > (target_sec * 2): # 목표 시간의 2배를 넘으면 비정상으로 간주
             print(f"[Generator] WARNING: Time exceeded 2x target ({target_sec * 2:.1f}s). Forcing stop.")
             break

        steps += 1

        if x.size(1) > dataset.block_size:
            x = x[:, -dataset.block_size:]

        logits = model(x, pad_id=PAD_ID)[:, -1, :]
        logits = logits / max(1e-6, temperature)
        base_logits = logits.clone()

        # 목표 마디 도달 전 EOS 금지
        if EOS_ID is not None:
            # 로직 수정: target_bars에 도달하지 않았으면 무조건 EOS 금지
            forbid_eos = (not limit) 
            
            if fill_last_bar:
                # fill_last_bar 모드에서는 마지막 마디에 노트를 최소 1개는 강제함
                forbid_eos = (not limit) or (in_last_bar and lastbar_note_cnt == 0)

            if forbid_eos:
                logits[:, EOS_ID] = float("-inf")

        # Nucleus(Top-p) Sampling
        sorted_logits, sorted_idx = torch.sort(logits, descending=True)
        probs = torch.softmax(sorted_logits[0], dim=-1)
        cum = torch.cumsum(probs, dim=-1)
        cutoff_idx = (cum > top_p).nonzero(as_tuple=False)
        cutoff = (int(cutoff_idx[0].item()) + 1) if cutoff_idx.numel() > 0 else probs.size(0)
        if cutoff < 1:
            cutoff = 1

        keep = torch.zeros_like(logits, dtype=torch.bool)
        keep.scatter_(1, sorted_idx[:, :cutoff], True)
        logits = logits.masked_fill(~keep, float("-inf"))

        # 모든 로짓이 -inf가 되는 예외 상황 (Fallback)
        row = logits[0]
        if torch.isneginf(row).all():
            logits = base_logits.clone()
            if EOS_ID is not None and not limit:
                logits[:, EOS_ID] = float("-inf")

        # 최종 확률 분포
        probs = torch.softmax(logits, dim=-1)
        
        # isfinite 체크 및 Fallback
        if (not torch.isfinite(probs).all()) or (probs.sum() <= 0):
             # 텐서에 NaN/Inf가 있거나 확률 합이 0이면, 가장 높은 로짓을 강제 선택
            intnext = int(torch.argmax(logits[0]).item())
            next_id = torch.tensor([[intnext]], dtype=torch.long, device=logits.device)
        else:
            next_id = torch.multinomial(probs, 1, generator=generator)

        nid = int(next_id.item())
        tok = itos[nid]

        if tok == "BAR":
            if limit: 
                # 목표 마디 도달 후 BAR이 나오면 즉시 종료
                stop = True
                break # continue 대신 break 사용
            # 목표 마디 도달 전이면 BAR을 허용하고 다음 스텝으로 진행

        ids.append(nid)
        x = torch.cat([x, next_id], dim=1)

        if tok == "BAR":
            bars += 1
            if bars == target_bars:
                limit = True
                in_last_bar = True
                
        if in_last_bar and tok.startswith("NOTE_"):
            lastbar_note_cnt += 1

        if stop and tok == "EOS":
            break
        if tok == "EOS":
            break

    toks = [itos[i] for i in ids]
    approx = bars_to_seconds(bars, bpm, beats_per_bar)
    print(f"{approx:.1f}s  (bars={bars}, bpm={bpm})")
    print("Generated tokens:\n", " ".join(toks))

    return toks


# --- MIDI 변환 함수 ---

def vbin_to_vel(vbin: int, vel_bins: int = 8) -> int:
    if vbin is None:
        vbin = vel_bins
    vbin = max(1, min(vel_bins, int(vbin)))
    step = 127 / vel_bins
    vel = int(round((vbin - 0.5) * step))
    return max(1, min(127, vel))


def tokens_to_midi(tokens, out_midi_path: str, tpq: int = 480, grid_div: int = 4):
    bpm = 120
    for t in tokens:
        m = BPM_RE.match(t)
        if m:
            bpm = int(m.group(1))
            break

    midi = miditoolkit.MidiFile()
    midi.ticks_per_beat = tpq
    midi.tempo_changes = [miditoolkit.TempoChange(bpm, time=0)]

    inst = miditoolkit.Instrument(program=0, is_drum=False, name="melody")
    midi.instruments = [inst]
    
    total_bars = sum(1 for t in tokens if t == "BAR")
    bar_ticks = tpq * 4
    grid_ticks = tpq // grid_div
    max_tick = (total_bars + 1) * bar_ticks

    cur_bar = 0
    cur_pos = 0
    pending = None
    last_end_tick = 0

    def flush_note():
        nonlocal pending, last_end_tick
        if pending and pending.get("dur") is not None:
            start = max(0, cur_bar * bar_ticks + pending["pos"] * grid_ticks)
            dur_ticks = max(grid_ticks, pending["dur"] * grid_ticks)
            
            start = max(start, last_end_tick)
            end = min(start + dur_ticks, max_tick)

            if end <= start:
                pending = None
                return

            vel = vbin_to_vel(pending.get("velbin", 8))

            inst.notes.append(miditoolkit.Note(
                velocity=vel,
                pitch=pending["pitch"],
                start=start,
                end=end
            ))

            last_end_tick = end
            pending = None

    for t in tokens:

        if t == "BAR":
            cur_bar += 1
            cur_pos = 0
            continue

        m = POS_RE.match(t)
        if m:
            cur_pos = int(m.group(1))
            continue

        m = NOTE_RE.match(t)
        if m:
            pending = {
                "pitch": int(m.group(1)),
                "dur": None,
                "velbin": None,
                "pos": cur_pos
            }
            continue

        m = DUR_RE.match(t)
        if m and pending:
            pending["dur"] = int(m.group(1))
            if pending.get("velbin") is not None:
                flush_note()
            continue

        m = VEL_RE.match(t)
        if m and pending:
            pending["velbin"] = int(m.group(1))
            if pending.get("dur") is not None:
                flush_note()
            continue

        if t == "EOS":
            break

    flush_note()

    Path(out_midi_path).parent.mkdir(parents=True, exist_ok=True)
    midi.dump(out_midi_path)
    return out_midi_path