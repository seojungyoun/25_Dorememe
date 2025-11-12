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

# 음악 생성 및 피처 추출 모듈 임포트
try:
    from load_model import load_model 
    from generate import generate_until_seconds, tokens_to_midi
    from midi_to_wav import midi_to_wav # MIDI를 WAV로 변환
    from features_to_prefix import get_season, session_to_features, build_prefix_tokens # CSV에서 특징 추출
    from musicgen_melody import init_musicgen, prefix_to_text, stylize_melody # MusicGen 스타일 변환
except ImportError as e:
    print(f"FATAL: 모듈 임포트 실패: {e}")
    sys.exit(1)


# 설정 및 경로 정의
UPLOAD_FOLDER = 'uploads'
OUTPUT_FOLDER = 'music'
os.makedirs(UPLOAD_FOLDER, exist_ok=True)
os.makedirs(OUTPUT_FOLDER, exist_ok=True)

# 모델 파일 경로
DATA_JSONL = "./melody_tok.jsonl"
VOCAB_JSON = "./melody_voc.json"
CKPT_PATH = "./melModel_tf.pt" 

# 장치 설정
GEN_DEVICE = "cpu" # 멜로디 모델은 CPU로 고정 (Deadlock 방지)
MUSICGEN_DEVICE = "cuda" if torch.cuda.is_available() else "cpu" # MusicGen은 GPU 선호

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
job_status_db = None 


# 모델 로드 및 초기화
def load_generator_model():
    """워커 프로세스에서 멜로디 모델과 MusicGen을 로드."""
    global model, dataset
    if model is not None:
        return model, dataset

    try:
        model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, GEN_DEVICE)
        init_musicgen(device=MUSICGEN_DEVICE, use_fp16=(MUSICGEN_DEVICE == "cuda"))
        return model, dataset
    except Exception as e:
        model = None 
        dataset = None
        raise

def initialize_job_db():
    """멀티프로세싱을 위한 Job DB 초기화."""
    global job_status_db
    manager = multiprocessing.Manager()
    job_status_db = manager.dict() 


# 비동기 작업자 함수
def process_music_generation(job_id, safe_filename, prefix_tokens, target_sec, shared_db, season):
    """별도 프로세스에서 음악 생성 및 스타일 변환 실행."""
    global model, dataset, SF2_PATH
    
    # 워커 프로세스에서 모델 로드
    try:
        load_generator_model()
    except Exception as e:
        shared_db[job_id] = {'status': 'failed', 'error': f'워커 모델 설정 실패: {str(e)}'}
        return 

    # MusicGen 프롬프트 생성
    prompt = prefix_to_text(prefix_tokens, include_tokens=True, season=season)
    
    # 1. 1차 멜로디 생성 (2초)
    try:
        shared_db[job_id] = {'status': 'in_progress', 'message': '2초 멜로디 생성 중...'}
        target_sec_1st = 2.0 
        g = torch.Generator(device=GEN_DEVICE).manual_seed(SEED)
        generated_tokens_1st = generate_until_seconds(
            model, dataset, prefix_tokens=prefix_tokens, target_sec=target_sec_1st, 
            temperature=1.0, top_p=0.95, generator=g
        )
        
        output_wav_filename_1st = f'{safe_filename}_1st_melody.wav'
        output_midi_path_1st = os.path.join(OUTPUT_FOLDER, f'{safe_filename}_temp_1st.mid')
        output_wav_path_1st = os.path.join(OUTPUT_FOLDER, output_wav_filename_1st)
        
        tokens_to_midi(generated_tokens_1st, output_midi_path_1st)
        midi_to_wav(output_midi_path_1st, output_wav_path_1st, SF2_PATH)
        os.remove(output_midi_path_1st)
        
        music_url_1st = f'http://localhost:5000/music/{output_wav_filename_1st}'
        
        current_status = dict(shared_db[job_id])
        current_status.update({'status': '1st_ready', 'music_url_1st': music_url_1st})
        shared_db[job_id] = current_status

    except Exception as e:
        shared_db[job_id] = {'status': 'failed', 'error': f'1차 생성 실패: {str(e)}'}
        return 

    # 2. 최종 멜로디 생성 (Target sec)
    try:
        shared_db[job_id] = {'status': 'in_progress', 'message': f'최종 {target_sec}초 멜로디 생성 중...'}
        g = torch.Generator(device=GEN_DEVICE).manual_seed(SEED + 1)
        generated_tokens_final = generate_until_seconds(
            model, dataset, prefix_tokens=prefix_tokens, target_sec=target_sec, 
            temperature=1.0, top_p=0.95, generator=g
        )

        melody_wav_filename = f'{safe_filename}_final_melody.wav'
        output_midi_path_final = os.path.join(OUTPUT_FOLDER, f'{safe_filename}_temp_final.mid')
        melody_wav_path = os.path.join(OUTPUT_FOLDER, melody_wav_filename)
        
        tokens_to_midi(generated_tokens_final, output_midi_path_final)
        midi_to_wav(output_midi_path_final, melody_wav_path, SF2_PATH)
        os.remove(output_midi_path_final)
        
        # 3. MusicGen 스타일 변환
        shared_db[job_id] = {'status': 'in_progress', 'message': 'MusicGen으로 스타일 변환 중...'}
        output_wav_filename_styled = f'{safe_filename}_final_styled.wav'
        output_wav_path_styled = os.path.join(OUTPUT_FOLDER, output_wav_filename_styled)
        
        stylize_melody(
            melody_wav_path=melody_wav_path,
            out_wav_path=output_wav_path_styled,
            prompt=prompt,
            device=MUSICGEN_DEVICE,
            use_fp16=(MUSICGEN_DEVICE == "cuda"),
            max_new_tokens=int(target_sec * 512 / 10) 
        )
        
        if os.path.exists(melody_wav_path):
             os.remove(melody_wav_path) # 중간 파일 정리

        music_url_final = f'http://localhost:5000/music/{output_wav_filename_styled}'
        
        current_status = dict(shared_db[job_id])
        current_status.update({'status': 'completed', 'music_url': music_url_final})
        shared_db[job_id] = current_status
        
    except Exception as e:
        traceback.print_exc()
        current_status = dict(shared_db[job_id])
        current_status.update({'status': 'failed', 'error': f'최종 생성/스타일 변환 실패: {str(e)}'})
        shared_db[job_id] = current_status


# Flask 엔드포인트
@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    """CSV 업로드 처리, 특징 추출 후 음악 생성 작업 시작."""
    global job_status_db, model, dataset
    
    if model is None or dataset is None:
        return jsonify({'error': '모델이 로드되지 않았습니다.'}), 503
        
    filename = request.headers.get('X-File-Name')
    if not filename or not request.data:
        return jsonify({'error': '파일 이름 또는 데이터가 누락되었습니다.'}), 400

    job_id = str(uuid.uuid4())
    safe_filename = filename.split('.')[0].replace(' ', '_')

    # 파일 저장 및 CSV 읽기
    file_path = os.path.join(UPLOAD_FOLDER, f"{safe_filename}-{job_id}.csv")
    try:
        with open(file_path, 'wb') as f:
            f.write(request.data)
        df = pd.read_csv(file_path) 
        os.remove(file_path) 
        
        # 특징 추출 및 Prefix 토큰 생성 (복원된 로직)
        season = get_season(df)
        features = session_to_features(df, season)
        prefix_tokens = build_prefix_tokens(features)
        
    except Exception as e:
        if os.path.exists(file_path): os.remove(file_path)
        return jsonify({'error': f'특징 추출 실패: {str(e)}'}), 400
    
    target_sec = 40.0 

    job_status_db[job_id] = {'status': 'in_progress', 'message': '작업 시작...'}
    
    # 생성 프로세스 시작
    process = multiprocessing.Process(
        target=process_music_generation, 
        args=(job_id, safe_filename, prefix_tokens, target_sec, job_status_db, season)
    )
    process.start()

    return jsonify({
        'job_id': job_id, 
        'status': 'started',
        'prefix_tokens': prefix_tokens 
    })

@app.route('/api/status/<job_id>', methods=['GET'])
def get_job_status(job_id):
    """작업 상태 확인 및 URL 호스트 대체."""
    global job_status_db
    status_info = job_status_db.get(job_id)

    if status_info is None:
        return jsonify({'status': 'error', 'message': 'Job ID를 찾을 수 없습니다.'}), 404

    base_url = request.host_url.strip('/')
    status_dict = dict(status_info) 

    # URL의 localhost를 실제 호스트로 대체
    if 'music_url_1st' in status_dict:
        status_dict['music_url_1st'] = status_dict['music_url_1st'].replace('http://localhost:5000', base_url)
    if 'music_url' in status_dict:
        status_dict['music_url'] = status_dict['music_url'].replace('http://localhost:5000', base_url)

    return jsonify(status_dict)


@app.route('/music/<path:filename>')
def download_music(filename):
    """음악 파일 다운로드."""
    return send_from_directory(OUTPUT_FOLDER, filename)


if __name__ == '__main__':
    multiprocessing.freeze_support() 
    initialize_job_db()
    
    # 메인 프로세스에서 멜로디 모델 로드 시도
    try:
        model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, GEN_DEVICE)
    except Exception as e:
        print(f"메인 프로세스 모델 로드 실패. 오류: {e}")
        pass 
    
    app.run(host='0.0.0.0', debug=True, use_reloader=False)