from flask import Flask, request, jsonify, send_from_directory
import os
import pandas as pd
import torch
import random
import sys
import uuid
import traceback
import multiprocessing 
import time 
from pathlib import Path
import threading 
import io 
import soundfile as sf 
import pretty_midi

# 1. 모듈 임포트 및 초기 설정

# 음악 생성 및 피처 추출 모듈 임포트
try:
    from load_model import load_model 
    from generate import generate_until_seconds, tokens_to_midi
    from midi_to_wav import midi_to_wav
    from features_to_prefix import get_season, session_to_features, build_prefix_tokens
    from musicgen_melody import init_musicgen, prefix_to_text, stylize_melody
    
    MODEL_AVAILABLE = True
except ImportError as e:
    print(f"FATAL: 핵심 모듈 임포트 실패 (외부 파일): {e}")
    MODEL_AVAILABLE = False
    print("경고: 외부 모델 모듈이 없으므로 생성 기능은 작동하지 않습니다. 서버만 구동됩니다.")


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
GEN_DEVICE = "cpu"
MUSICGEN_DEVICE = "cuda" if torch.cuda.is_available() else "cpu"

# 랜덤 시드 설정
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
# 전역 변수
model = None
dataset = None
musicgen_model_loaded = False 
SF2_PATH = os.path.join(os.path.dirname(pretty_midi.__file__), "TimGM6mb.sf2")
app = Flask(__name__)
job_status_db = None 

# 2. 모델 로드 및 초기화 함수

def initialize_job_db():
    """멀티프로세싱을 위한 Job DB 초기화."""
    global job_status_db
    manager = multiprocessing.Manager()
    job_status_db = manager.dict() 

def load_generator_model():
    """워커 프로세스에서 멜로디 모델만 로드."""
    global model, dataset
    
    if not MODEL_AVAILABLE:
        raise ImportError("모델 로드 모듈이 없습니다.")
        
    if model is not None:
        return model, dataset

    try:
        model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, GEN_DEVICE)
        return model, dataset
    except Exception as e:
        model = None 
        dataset = None
        raise

def load_musicgen_model():
    """메인 프로세스에서 MusicGen 모델을 한 번만 로드하고 전역 플래그 설정."""
    global musicgen_model_loaded
    
    if not MODEL_AVAILABLE:
        musicgen_model_loaded = True
        return
        
    if not musicgen_model_loaded:
        try:
            print(f"메인 프로세스 MusicGen 초기화 시작: {MUSICGEN_DEVICE}")
            init_musicgen(device=MUSICGEN_DEVICE, use_fp16=(MUSICGEN_DEVICE == "cuda")) 
            musicgen_model_loaded = True
            print("MusicGen 모델 초기화 완료.")
        except Exception as e:
            print(f"MusicGen 모델 로드 실패: {e}")
            raise


# 3. 비동기 작업자 함수 (1차 작업: 멜로디 생성)

def process_music_generation_1st(job_id, safe_filename, prefix_tokens, target_sec, shared_db, season):
    """
    1차 멜로디 생성 및 WAV 파일 저장. 
    1차 완료 상태를 '1st_ready' 대신 '2nd_start_ready'로 변경하여 외부 노출을 방지.
    """
    global model, dataset, SF2_PATH
    
    try:
        if model is None or dataset is None:
            load_generator_model()
    except Exception as e:
        shared_db[job_id] = {'status': 'failed', 'error': f'워커 멜로디 모델 설정 실패: {str(e)}'}
        return 

    try:
        shared_db[job_id] = {'status': 'in_progress', 'message': f'1차 멜로디 생성 중 ({target_sec}초)...'}
        
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

        try:
            os.remove(output_midi_path_final)
        except Exception:
            pass 
        
        try:
            audio, sr = sf.read(melody_wav_path)
            # 1차 파일은 2차 작업에서 임시로 사용
            sf.write(melody_wav_path, audio, sr)
            wav_data = Path(melody_wav_path).read_bytes()
            
        except Exception as e:
            traceback.print_exc()
            shared_db[job_id] = {'status': 'failed', 'error': f'1차 WAV 안전 처리 실패: {str(e)}'}
            return
            
        time.sleep(1.0) 
        
        music_url_melody = f'http://localhost:5000/music/{melody_wav_filename}'

        current_status = {
            'status': '2nd_start_ready', # 내부 상태: 1차 완료 및 2차 시작 준비
            'music_url_1st': music_url_melody, # 1차 URL은 내부용으로만 저장
            'melody_wav_path': melody_wav_path,
            'wav_data': wav_data,
            'prefix_tokens': prefix_tokens,
            'season': season,
            'safe_filename': safe_filename,
            'target_sec': target_sec
        }
        shared_db[job_id] = current_status
        print(f"[{job_id}] 1차 멜로디 WAV 생성 및 메모리 로드 완료. 2차 작업 준비 완료.")

    except Exception as e:
        traceback.print_exc()
        shared_db[job_id] = {'status': 'failed', 'error': f'1차 멜로디 생성 실패: {str(e)}'}
        return

# 4. 비동기 작업자 함수 (2차 작업: 스타일 변환)

def process_stylization_2nd(job_id, job_data, shared_db):
    """2차 MusicGen 스타일 변환만 담당. 메모리 데이터 사용. 1차 URL 제거."""
    
    if not MODEL_AVAILABLE:
        # 모델이 없는 경우, 최종 실패 상태로 변경 (1차 URL은 남겨둠)
        status_update = job_data.copy()
        status_update.update({
            'status': 'failed', 
            'error': '모델 모듈이 없어 2차 스타일 변환을 시작할 수 없습니다.'
        })
        shared_db[job_id] = status_update
        return
        
    # 작업 데이터 추출
    melody_wav_path = job_data['melody_wav_path']
    wav_data = job_data['wav_data']
    prefix_tokens = job_data['prefix_tokens']
    season = job_data['season']
    safe_filename = job_data['safe_filename']
    target_sec = job_data['target_sec']

    try:
        prompt = prefix_to_text(prefix_tokens, include_tokens=True, season=season)
        print(f"[{job_id}] 2차 MusicGen 프롬프트 생성: {prompt}")
    except Exception as e:
        traceback.print_exc()
        # 1차 작업 상태를 유지하고 2차 실패 오류만 추가
        status_update = dict(job_data)
        status_update.update({'status': 'failed', 'error': f'2차 MusicGen 프롬프트 생성 실패: {str(e)}'})
        shared_db[job_id] = status_update
        return

    shared_db[job_id] = {'status': 'in_progress', 'message': 'MusicGen 스타일 변환 준비 중...'}
    
    temp_safe_wav_path = None
    
    try:
        melody_stream = io.BytesIO(wav_data)
        
        shared_db[job_id] = {'status': 'in_progress', 'message': 'MusicGen으로 스타일 변환 중...'}
        
        max_new_tokens = int(target_sec * 512 / 10) 
        
        output_wav_filename_styled = f'{safe_filename}_final_styled.wav'
        output_wav_path_styled = os.path.join(OUTPUT_FOLDER, output_wav_filename_styled)
        
        audio, sr = sf.read(melody_stream) 
        temp_safe_wav_path = os.path.join(OUTPUT_FOLDER, f'{safe_filename}_safe_temp.wav')
        sf.write(temp_safe_wav_path, audio, sr)
        
        stylize_melody(
            melody_wav_path=temp_safe_wav_path,
            out_wav_path=output_wav_path_styled,
            prompt=prompt,
            device=MUSICGEN_DEVICE,
            use_fp16=(MUSICGEN_DEVICE == "cuda"),
            max_new_tokens=max_new_tokens 
        )
        
        # 임시 파일 및 1차 멜로디 파일 삭제
        os.remove(temp_safe_wav_path)
        if os.path.exists(melody_wav_path):
            os.remove(melody_wav_path) 
        
        music_url_final = f'http://localhost:5000/music/{output_wav_filename_styled}'
        
        current_status = dict(job_data)
        current_status.pop('music_url_1st', None) # 1차 URL 제거
        current_status.update({'status': 'completed', 'music_url': music_url_final, 'message': '최종 음원 생성 완료.'})
        shared_db[job_id] = current_status
        print(f"[{job_id}] 최종 스타일 변환 완료.")
            
    except Exception as e:
        traceback.print_exc()
        if temp_safe_wav_path and os.path.exists(temp_safe_wav_path):
            os.remove(temp_safe_wav_path)
            
        # 2차 실패 시, 1차 URL은 그대로 두어 디버깅에 활용
        shared_db[job_id] = {
            'status': 'failed', 
            'error': f'최종 스타일 변환 실패: {str(e)}',
            'music_url_1st': job_data.get('music_url_1st') 
        }

# 5. Flask 엔드포인트

@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    """CSV 업로드 처리, 특징 추출 후 1차 멜로디 생성 시작 및 2차 작업 트리거."""
    global job_status_db
    
    if not musicgen_model_loaded and MODEL_AVAILABLE:
        return jsonify({'error': 'MusicGen 모델이 로드되지 않았습니다. 서버 로그를 확인하세요.'}), 503
    
    if not MODEL_AVAILABLE:
        return jsonify({'error': '서버에 음악 생성 모듈이 없어 요청을 처리할 수 없습니다.'}), 500
            
    filename = request.headers.get('X-File-Name')
    if not filename or not request.data:
        return jsonify({'error': '파일 이름 또는 데이터가 누락되었습니다.'}), 400

    job_id = str(uuid.uuid4())
    safe_filename = filename.split('.')[0].replace(' ', '_')

    file_path = os.path.join(UPLOAD_FOLDER, f"{safe_filename}-{job_id}.csv")
    try:
        with open(file_path, 'wb') as f:
            f.write(request.data)
        df = pd.read_csv(file_path) 
        os.remove(file_path) 
        
        season = get_season(df)
        features = session_to_features(df, season)
        prefix_tokens = build_prefix_tokens(features)
        
    except Exception as e:
        if os.path.exists(file_path): os.remove(file_path)
        traceback.print_exc()
        return jsonify({'error': f'특징 추출 실패: {str(e)}'}), 400
    
    target_sec = 20.0

    job_status_db[job_id] = {'status': 'in_progress', 'message': '1차 멜로디 생성 및 최종 스타일 변환 시작...'}
    
    process_1st = multiprocessing.Process(
        target=process_music_generation_1st, 
        args=(job_id, safe_filename, prefix_tokens, target_sec, job_status_db, season)
    )
    process_1st.start()
    
    def check_and_start_2nd(job_id):
        """1차 프로세스가 완료되면 2차 MusicGen 프로세스를 시작. '2nd_start_ready' 상태 감지."""
        process_1st.join() 
        
        time.sleep(0.5) 
        
        status_info = job_status_db.get(job_id)
        # 내부 상태 '2nd_start_ready' 감지
        if status_info and status_info.get('status') == '2nd_start_ready': 
            print(f"[{job_id}] 1차 작업 완료 감지. 2차 스타일 변환 프로세스 즉시 시작.")
            
            process_2nd = multiprocessing.Process(
                target=process_stylization_2nd,
                args=(job_id, status_info, job_status_db)
            )
            process_2nd.start()
        elif status_info and status_info.get('status') == 'failed':
            print(f"[{job_id}] 1차 작업 실패로 2차 작업 미실행.")
        else:
            print(f"[{job_id}] 1차 작업 상태 불명({status_info.get('status', 'N/A')})으로 2차 작업 미실행.")

    # 1차 작업이 완료되는 것을 감시하고 2차 작업을 시작하는 스레드
    threading.Thread(target=check_and_start_2nd, args=(job_id,)).start()

    return jsonify({
        'job_id': job_id, 
        'status': 'started',
        'message': f'음악 생성 파이프라인 시작됨. 최종 "completed" 상태를 확인하세요.'
    })


@app.route('/api/status/<job_id>', methods=['GET'])
def get_job_status(job_id):
    """작업 상태 확인 및 URL 호스트 대체. 1차 URL 노출 방지."""
    global job_status_db
    status_info = job_status_db.get(job_id)

    if status_info is None:
        return jsonify({'status': 'error', 'message': 'Job ID를 찾을 수 없습니다.'}), 404

    base_url = request.host_url.strip('/')
    status_dict = dict(status_info) 

    # 클라이언트에게 1차 URL 정보 숨기기
    status_dict.pop('music_url_1st', None)
    
    if 'music_url' in status_dict:
        status_dict['music_url'] = status_dict['music_url'].replace('http://localhost:5000', base_url)

    if 'error_2nd' in status_dict:
        status_dict['error'] = status_dict.pop('error_2nd')

    # 내부 상태 '2nd_start_ready'를 외부에는 'in_progress'로 표시하여 1차 완료 상태를 숨김
    if status_dict.get('status') == '2nd_start_ready':
        status_dict['status'] = 'in_progress'
        status_dict['message'] = '최종 스타일 변환 대기/진행 중...'
    
    # 내부 상태 정보 제거 (데이터 보호)
    status_dict.pop('wav_data', None)
    status_dict.pop('prefix_tokens', None)
    status_dict.pop('melody_wav_path', None)
    status_dict.pop('safe_filename', None)
    status_dict.pop('season', None)
    status_dict.pop('target_sec', None)

    return jsonify(status_dict)


@app.route('/music/<path:filename>')
def download_music(filename):
    """음악 파일 다운로드."""
    return send_from_directory(OUTPUT_FOLDER, filename)

# 6. 메인 실행 블록
if __name__ == '__main__':
    multiprocessing.freeze_support() 
    
    initialize_job_db()
    
    if MODEL_AVAILABLE:
        try:
            # 워커 프로세스가 모델을 로드할 수 있도록 메인 프로세스에서 한 번 로드 시도
            model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, GEN_DEVICE)
            print("메인 프로세스 멜로디 모델 로드 완료.")
        except Exception as e:
            print(f"메인 프로세스 멜로디 모델 로드 실패. 오류: {e}")
            pass
        
        try:
            load_musicgen_model() # MusicGen은 메인 프로세스에서 로드
        except Exception as e:
            print("="*50)
            print(f"FATAL: MusicGen 모델 로드에 심각한 문제가 발생했습니다.")
            print(f"오류 상세: {e}")
            print("="*50)
            print("GPU/CUDA 설정, Python 패키지 의존성을 확인하세요.")
            sys.exit(1)
    
    app.run(host='0.0.0.0', debug=True, use_reloader=False)