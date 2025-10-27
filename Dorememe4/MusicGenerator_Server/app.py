from flask import Flask, request, jsonify, send_from_directory
import os
import pandas as pd
import torch
import pretty_midi
import random
import sys
import uuid
import traceback # 오류 추적을 위해 추가
from pathlib import Path

# -----------------------------------------------------
# 1. 음악 생성 모듈 임포트
# -----------------------------------------------------
try:
    from load_model import load_model 
    from generate import generate_until_seconds, tokens_to_midi
    from midi2wav import midi_to_wav
except ImportError as e:
    print(f"FATAL: Failed to import model modules: {e}")
    sys.exit(1)


# -----------------------------------------------------
# 2. 설정 및 경로 정의
# -----------------------------------------------------
UPLOAD_FOLDER = 'uploads'
OUTPUT_FOLDER = 'music'
os.makedirs(UPLOAD_FOLDER, exist_ok=True)
os.makedirs(OUTPUT_FOLDER, exist_ok=True)

# 모델 경로 설정
DATA_JSONL = "./melody_tok.jsonl"
VOCAB_JSON = "./melody_voc.json"
CKPT_PATH = "./melModel_tf.pt" 

DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
SEED = 42

random.seed(SEED)
torch.manual_seed(SEED)

class Cfg: 
    def __init__(self):
        self.block_size = 384
        self.hidden_size = 384
        self.num_heads = 6
        self.num_layers = 8
        self.ffn_hidden_size = 4 * self.hidden_size
        self.dropout = 0.1

cfg = Cfg()

model = None
dataset = None
SF2_PATH = os.path.join(os.path.dirname(pretty_midi.__file__), "TimGM6mb.sf2")

app = Flask(__name__)

job_status_db = {} 

# -----------------------------------------------------
# 3. 서버 시작 시 모델 로드Q
# -----------------------------------------------------
def load_generator_model():
    global model, dataset
    try:
        print(f"Attempting to load model from {CKPT_PATH} to {DEVICE}...")
        model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, DEVICE)
        print("Model loaded successfully. Ready to generate music.")
    except FileNotFoundError as e:
        print(f"FATAL: Model or data file not found: {e}")
    except Exception as e:
        print(f"FATAL: An error occurred while loading the model: {e}")

@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    # 모델 로드 상태 확인 (503 에러 방지)
    if model is None:
        return jsonify({'error': 'Music generation model is not loaded.'}), 503
        
    # 1. 파일 이름 및 Raw 데이터 추출
    filename = request.headers.get('X-File-Name')
    if not filename:
        return jsonify({'error': 'File name (X-File-Name header) is missing.'}), 400
    
    file_data = request.data 
    if not file_data:
        return jsonify({'error': 'No data in request body'}), 400

    job_id = str(uuid.uuid4())
    safe_filename = filename.split('.')[0].replace(' ', '_')

    # 2. 파일 저장 및 CSV 읽기
    file_path = os.path.join(UPLOAD_FOLDER, f"{safe_filename}-{job_id}.csv")
    try:
        with open(file_path, 'wb') as f:
            f.write(file_data)
        
        df = pd.read_csv(file_path) 
        os.remove(file_path)
    except Exception as e:
        if os.path.exists(file_path):
            os.remove(file_path)
        
        # CSV 처리 실패 시 500이 아닌 400 반환 (클라이언트 문제로 간주)
        return jsonify({'error': f'CSV or file handling failed: {str(e)}'}), 400
    
    # 임시 프리픽스
    prefix_tokens = ["KEY_7", "MODE_MAJ", "BPM_120", "REG_MID", "RHY_2", "DENS_1", "CHR_1", "BAR", "POS_0"]
    target_sec = 20.0 
    
    # 3. 음악 생성 및 변환
    try:
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
        
        output_wav_filename = f'{safe_filename}_generated.wav'
    
        output_midi_path = os.path.join(OUTPUT_FOLDER, f'{safe_filename}_temp.mid')
        output_wav_path = os.path.join(OUTPUT_FOLDER, output_wav_filename)
        
        tokens_to_midi(generated_tokens, output_midi_path)
        midi_to_wav(output_midi_path, output_wav_path, SF2_PATH)
        
        os.remove(output_midi_path)
        
        music_url = f'http://127.0.0.1:5000/music/{output_wav_filename}'
        job_status_db[job_id] = {'status': 'completed', 'music_url': music_url}
        
    except Exception as e:
        # 500 에러의 가장 흔한 원인! 콘솔에 상세 추적 기록
        print("--- MUSIC GENERATION FAILED ---")
        traceback.print_exc()
        print("-------------------------------")

        error_msg = f'Music generation failed: {type(e).__name__}: {str(e)}'
        job_status_db[job_id] = {'status': 'failed', 'error': error_msg}
        
        # 음악 생성 실패 시에도 500 대신 Job ID와 실패 상태를 반환
        return jsonify({'job_id': job_id, 'status': 'failed', 'error': error_msg}), 200

    # 4. Job ID 포함 응답 반환
    return jsonify({
        'job_id': job_id, 
        'status': 'started',
        'message': 'Job is being processed.'
    }), 200 # 202 대신 200 OK를 사용하여 Unity의 기본 처리를 용이하게 함

@app.route('/api/status/<job_id>', methods=['GET'])
def get_job_status(job_id):
    status_info = job_status_db.get(job_id)

    if status_info is None:
        return jsonify({'status': 'error', 'message': 'Job ID not found.'}), 404

    # music_url이 저장되어 있으면 완료 상태로 반환
    if status_info.get('music_url'):
        # Unity 클라이언트가 예상하는 "completed" 상태를 명시적으로 추가
        return jsonify({'status': 'completed', 'music_url': status_info['music_url']}) 

    # 실패 상태 처리
    if status_info.get('status') == 'failed':
        return jsonify(status_info)

    # 작업 진행 중 상태
    return jsonify({'status': 'in_progress', 'message': 'Processing.'}), 200

@app.route('/music/<path:filename>')
def download_music(filename):
    return send_from_directory(OUTPUT_FOLDER, filename)


if __name__ == '__main__':
    load_generator_model()
    app.run(debug=True)