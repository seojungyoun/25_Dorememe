from flask import Flask, request, jsonify, send_from_directory
import os
import pandas as pd
import torch
import pretty_midi
import random
import sys
import uuid
import traceback
import multiprocessing 
import time
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

# Deadlock 방지를 위해 CPU 사용을 강제합니다.
DEVICE = "cpu" 
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

# 전역 변수 초기 선언
model = None
dataset = None
SF2_PATH = "TimGM6mb.sf2" 

app = Flask(__name__)

# job_status_db는 초기화 함수를 통해 전역으로 할당됩니다.
job_status_db = None 


# -----------------------------------------------------
# 3. 모델 및 Job DB 로드 함수 정의
# -----------------------------------------------------
def load_generator_model():
    """Worker 프로세스에서 모델을 로드하여 전역 변수에 할당합니다."""
    global model, dataset
    
    if model is not None:
        return model, dataset

    try:
        print(f"Worker: Attempting to load model from {CKPT_PATH} to {DEVICE}...")
        model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, DEVICE)
        print("Worker: Model loaded successfully.")
        return model, dataset
    except Exception as e:
        print(f"Worker: FATAL Model Load Error: {e}")
        model = None 
        dataset = None
        raise

def initialize_job_db():
    """메인 프로세스에서 Job DB를 초기화하고 전역 변수에 할당합니다."""
    global job_status_db
    manager = multiprocessing.Manager()
    job_status_db = manager.dict() 
    print("Job status database initialized.")


# ==========================================================
# 비동기 작업자 함수
# ==========================================================
def process_music_generation(job_id, safe_filename, prefix_tokens, target_sec, shared_db):
    """모델 연산을 독립된 프로세스에서 실행하여 교착 상태를 방지합니다."""
    global model, dataset, SF2_PATH
    
    # 프로세스 내 모델 지연 로드 시도
    try:
        load_generator_model()
        if model is None or dataset is None:
             raise Exception("Model object is None after attempting load.")
    except Exception as e:
        # 이 Worker 프로세스가 실패해도 shared_db에 상태를 남깁니다.
        shared_db[job_id] = {'status': 'failed', 'error': f'Worker Model Setup Failed: {str(e)}'}
        return 

    print(f"[{job_id}] 1. Starting 1st music generation...")
    
    # ----------------------------------------------------------------------------------
    # 1. 1차 음악 생성
    # ----------------------------------------------------------------------------------
    try:
        target_sec_1st = 2.0 
        g = torch.Generator(device=DEVICE).manual_seed(SEED)
        
        print(f"[{job_id}] 1.1. Generating tokens (2s)...")
        generated_tokens = generate_until_seconds(
            model, dataset, prefix_tokens=prefix_tokens, target_sec=target_sec_1st, 
            temperature=1.0, top_p=0.95, generator=g
        )
        
        output_wav_filename_1st = f'{safe_filename}_1st.wav'
        output_midi_path_1st = os.path.join(OUTPUT_FOLDER, f'{safe_filename}_temp_1st.mid')
        output_wav_path_1st = os.path.join(OUTPUT_FOLDER, output_wav_filename_1st)
        
        tokens_to_midi(generated_tokens, output_midi_path_1st)
        midi_to_wav(output_midi_path_1st, output_wav_path_1st, SF2_PATH)
        os.remove(output_midi_path_1st)
        
        # URL 생성 시 localhost 사용
        music_url_1st = f'http://localhost:5000/music/{output_wav_filename_1st}'
        
        # 상태 업데이트: 1차 음악 완료
        shared_db[job_id]['status'] = '1st_ready'
        shared_db[job_id]['music_url_1st'] = music_url_1st
        print(f"[{job_id}] 1st music ready. Status updated.")

    except Exception as e:
        print("-------------------------------------------------------")
        print(f"[{job_id}] CRITICAL: 1ST MUSIC GENERATION FAILED")
        traceback.print_exc()
        print("-------------------------------------------------------")
        shared_db[job_id] = {'status': 'failed', 'error': f'1st Gen failed: {str(e)}'}
        return 

    # ----------------------------------------------------------------------------------
    # 2. 최종 음악 생성
    # ----------------------------------------------------------------------------------
    print(f"[{job_id}] 2. Starting final music generation (Target: {target_sec}s)...")
    try:
        # time.sleep(1) 

        g = torch.Generator(device=DEVICE).manual_seed(SEED + 1)
        generated_tokens_final = generate_until_seconds(
            model, dataset, prefix_tokens=prefix_tokens, target_sec=target_sec, 
            temperature=1.0, top_p=0.95, generator=g
        )

        output_wav_filename_final = f'{safe_filename}_final.wav'
        output_midi_path_final = os.path.join(OUTPUT_FOLDER, f'{safe_filename}_temp_final.mid')
        output_wav_path_final = os.path.join(OUTPUT_FOLDER, output_wav_filename_final)
        
        tokens_to_midi(generated_tokens_final, output_midi_path_final)
        midi_to_wav(output_midi_path_final, output_wav_path_final, SF2_PATH)
        os.remove(output_midi_path_final)

        # URL 생성 시 localhost 사용
        music_url_final = f'http://localhost:5000/music/{output_wav_filename_final}'
        
        # 상태 업데이트: 최종 음악 완료
        shared_db[job_id]['status'] = 'completed'
        shared_db[job_id]['music_url'] = music_url_final
        print(f"[{job_id}] ✅ Final music completed. Status updated.")
        
    except Exception as e:
        print("-------------------------------------------------------")
        print(f"[{job_id}] CRITICAL: FINAL MUSIC GENERATION FAILED")
        traceback.print_exc()
        print("-------------------------------------------------------")
        shared_db[job_id]['status'] = 'failed' 
        shared_db[job_id]['error'] = f'Final Gen failed: {str(e)}'


# ==========================================================
# Flask 엔드포인트
# ==========================================================

@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    global job_status_db, model, dataset
    
    # 모델 로드 상태 확인 (503 응답)
    if model is None or dataset is None:
        return jsonify({'error': 'Music generation model is not loaded.'}), 503
        
    filename = request.headers.get('X-File-Name')
    if not filename:
        return jsonify({'error': 'File name (X-File-Name header) is missing.'}), 400
    
    file_data = request.data 
    if not file_data:
        return jsonify({'error': 'No data in request body'}), 400

    job_id = str(uuid.uuid4())
    safe_filename = filename.split('.')[0].replace(' ', '_')

    # 2. 파일 저장 및 CSV 읽기 (동기적으로 처리)
    file_path = os.path.join(UPLOAD_FOLDER, f"{safe_filename}-{job_id}.csv")
    try:
        with open(file_path, 'wb') as f:
            f.write(file_data)
        df = pd.read_csv(file_path) 
        os.remove(file_path)
    except Exception as e:
        if os.path.exists(file_path):
            os.remove(file_path)
        return jsonify({'error': f'CSV or file handling failed: {str(e)}'}), 400
    
    prefix_tokens = ["KEY_7", "MODE_MAJ", "BPM_120", "REG_MID", "RHY_2", "DENS_1", "CHR_1", "BAR", "POS_0"]
    target_sec = 20.0 

    # 초기 상태 설정 (공유 딕셔너리)
    job_status_db[job_id] = {'status': 'in_progress', 'message': 'Starting generation...'}
    
    # multiprocessing.Process로 작업 시작
    process = multiprocessing.Process(
        target=process_music_generation, 
        args=(job_id, safe_filename, prefix_tokens, target_sec, job_status_db)
    )
    process.start()

    # 4. Job ID를 즉시 반환하여 클라이언트가 폴링을 시작하게 함
    return jsonify({
        'job_id': job_id, 
        'status': 'started',
        'message': 'Job started successfully. Polling required.'
    }), 200

@app.route('/api/status/<job_id>', methods=['GET'])
def get_job_status(job_id):
    global job_status_db
    status_info = job_status_db.get(job_id)

    if status_info is None:
        return jsonify({'status': 'error', 'message': 'Job ID not found.'}), 404

    return jsonify(status_info)


@app.route('/music/<path:filename>')
def download_music(filename):
    return send_from_directory(OUTPUT_FOLDER, filename)


if __name__ == '__main__':
    # 멀티프로세싱 관리자 초기화
    multiprocessing.freeze_support() 
    
    # Job DB 초기화 함수 호출
    initialize_job_db()
    
    # 메인 프로세스에서 모델 로드 시도
    try:
        load_generator_model()
    except Exception as e:
        print(f"FATAL: Main process model load failed. Server will start with status 503. Error: {e}")
        pass 
    
    # Flask 서버 실행 (reloader 비활성화)
    app.run(host='0.0.0.0', debug=True, use_reloader=False)