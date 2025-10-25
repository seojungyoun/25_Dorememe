from flask import Flask, request, jsonify, send_from_directory
import os
import pandas as pd
import torch
import pretty_midi
import random
import sys
from pathlib import Path

# -----------------------------------------------------
# 1. 음악 생성 모듈 임포트
# -----------------------------------------------------
try:
    # 이 모듈들이 app.py와 같은 경로에 있어야 합니다.
    from load_model import load_model 
    from generate import generate_until_seconds, tokens_to_midi
    from midi2wav import midi_to_wav
except ImportError as e:
    # 모듈 로드 실패 시 디버깅을 돕기 위해 오류 메시지를 출력합니다.
    print(f"FATAL: Failed to import necessary model modules. Please ensure all helper files (load_model.py, generate.py, midi2wav.py, etc.) are present and correct.")
    print(f"Import Error details: {e}")
    # 실제 서버 환경에서는 이 시점에서 종료하는 것이 안전할 수 있습니다.
    sys.exit(1)


# -----------------------------------------------------
# 2. 설정 및 경로 정의
# -----------------------------------------------------
UPLOAD_FOLDER = 'uploads'
OUTPUT_FOLDER = 'music'
os.makedirs(UPLOAD_FOLDER, exist_ok=True)
os.makedirs(OUTPUT_FOLDER, exist_ok=True)

# 모델 관련 경로 설정 (프로젝트 구조에 맞게 수정 필요)
# ⚠️ 수정된 부분: 파일들이 루트 디렉토리에 있으므로 './data/' 경로를 제거합니다.
DATA_JSONL = "./melody_tok.jsonl" # 경로 수정
VOCAB_JSON = "./melody_voc.json" # 경로 수정
CKPT_PATH = "./runs/melModel_tf.pt" 

DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
SEED = 42

random.seed(SEED)
torch.manual_seed(SEED)

class Cfg: 
    """모델 설정 클래스 (제공된 코드에서 가져옴)"""
    def __init__(self):
        self.block_size = 384
        self.hidden_size = 384
        self.num_heads = 6
        self.num_layers = 8
        self.ffn_hidden_size = 4 * self.hidden_size
        self.dropout = 0.1

cfg = Cfg()

# ⚠️ 글로벌 변수로 모델과 데이터셋 선언
model = None
dataset = None
# MIDI to WAV 변환에 필요한 SoundFont 경로
# pretty_midi 설치 시 기본적으로 포함된 사운드폰트를 사용합니다.
SF2_PATH = os.path.join(os.path.dirname(pretty_midi.__file__), "TimGM6mb.sf2")

app = Flask(__name__)

# -----------------------------------------------------
# 3. 서버 시작 시 모델 로드
# -----------------------------------------------------
def load_generator_model():
    """Flask 서버 시작 시 모델을 미리 로드하여 요청 처리 속도를 높입니다."""
    global model, dataset
    try:
        print(f"Attempting to load model from {CKPT_PATH} to {DEVICE}...")
        # load_model 함수가 수정된 경로(DATA_JSONL, VOCAB_JSON)를 사용하게 됩니다.
        model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, DEVICE)
        print("Model loaded successfully. Ready to generate music.")
    except FileNotFoundError as e:
        print(f"FATAL: Model or data file not found. Check paths: {e}")
        print("Music generation will be disabled until files are available.")
        # model = None 상태 유지
    except Exception as e:
        print(f"FATAL: An error occurred while loading the model: {e}")
        # model = None 상태 유지

@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    """
    CSV 파일을 업로드 받아 음악을 생성하고 WAV 파일 URL을 반환합니다.
    """
    if model is None:
        return jsonify({'error': 'Music generation model is not loaded or failed to initialize.'}), 503
        
    if 'file' not in request.files:
        return jsonify({'error': 'No file part in request'}), 400

    file = request.files['file']
    if file.filename == '':
        return jsonify({'error': 'No selected file'}), 400
    
    # 1. 파일 저장 및 이름 정리
    try:
        # 안전한 파일명 사용을 위해 확장자를 분리하고 경로를 설정합니다.
        filename_base, filename_ext = os.path.splitext(file.filename)
        safe_filename = filename_base.replace(' ', '_')
        file_path = os.path.join(UPLOAD_FOLDER, safe_filename + filename_ext)
        file.save(file_path)
    except Exception as e:
        return jsonify({'error': f'Failed to save uploaded file: {str(e)}'}), 500


    # 2. CSV 데이터 읽기 및 프리픽스 생성 (TODO)
    try:
        df = pd.read_csv(file_path)
    except Exception as e:
        return jsonify({'error': f'Failed to read CSV: {str(e)}'}), 400

    # -----------------------------------------------------------
    # 💡 TODO: CSV 데이터를 분석하여 음악 속성을 추출하고,
    # 해당 속성을 기반으로 prefix_tokens 리스트를 동적으로 생성해야 합니다.
    # 
    # 현재는 테스트를 위해 하드코딩된 기본 프리픽스를 사용합니다.
    prefix_tokens = ["KEY_7", "MODE_MAJ", "BPM_120", "REG_MID", "RHY_2", "DENS_1", "CHR_1", "BAR", "POS_0"]
    target_sec = 20.0 
    # -----------------------------------------------------------
    
    # 3. 음악 생성 및 변환
    try:
        # 모델을 사용하여 토큰 시퀀스 생성
        g = torch.Generator(device=DEVICE).manual_seed(SEED) 
        generated_tokens = generate_until_seconds(
            model,
            dataset,
            prefix_tokens=prefix_tokens,
            target_sec=target_sec,
            temperature=1.0,
            top_p=0.95,
            generator=g
        )
        
        # 출력 경로 설정 (생성된 WAV 파일 이름)
        output_wav_filename = f'{safe_filename}_generated.wav'
        output_midi_path = os.path.join(OUTPUT_FOLDER, f'{safe_filename}_temp.mid')
        output_wav_path = os.path.join(OUTPUT_FOLDER, output_wav_filename)
        
        # MIDI 파일 생성
        tokens_to_midi(generated_tokens, output_midi_path)
        
        # WAV 파일로 변환 (MP3 대신 WAV 사용)
        print(f"Converting MIDI to WAV using SoundFont: {SF2_PATH}")
        midi_to_wav(output_midi_path, output_wav_path, SF2_PATH)
        
        # 임시 MIDI 파일 삭제
        os.remove(output_midi_path)
        
    except Exception as e:
        # 생성 또는 변환 실패 시
        return jsonify({'error': f'Music generation/conversion failed. Check model files or fluidsynth installation. Details: {str(e)}'}), 500

    # 4. 결과 반환 (WAV 파일 URL)
    return jsonify({
        'status': 'completed',
        'music_url': f'http://127.0.0.1:5000/music/{output_wav_filename}' # WAV 파일 URL 반환
    })


@app.route('/music/<path:filename>')
def download_music(filename):
    """생성된 음악 파일을 다운로드하는 엔드포인트"""
    return send_from_directory(OUTPUT_FOLDER, filename)


if __name__ == '__main__':
    # 서버 실행 전에 모델 로드 함수 호출
    load_generator_model()
    # 주의: debug=True 상태에서는 load_generator_model이 두 번 호출될 수 있습니다.
    app.run(debug=True)
