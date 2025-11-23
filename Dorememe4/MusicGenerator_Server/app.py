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
from multiprocessing import Process, Queue, Manager

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
MUSICGEN_DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
GEN_DEVICE = MUSICGEN_DEVICE 

# MusicGen 워커 수 (병렬 처리. 2개)
NUM_MUSICGEN_WORKERS = 2 

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
global_generator_model = None # 모델 로드를 메인 프로세스에서 관리
dataset = None
SF2_PATH = os.path.join(os.path.dirname(pretty_midi.__file__), "TimGM6mb.sf2")
app = Flask(__name__)
job_status_db = None 
musicgen_queue = None 
musicgen_worker_process = None 


# 2. 모델 로드 및 초기화 함수

def initialize_job_db_and_queues():
    """멀티프로세싱을 위한 Job DB 및 작업 큐 초기화."""
    global job_status_db, musicgen_queue
    manager = Manager()
    job_status_db = manager.dict() 
    musicgen_queue = Queue() 

def load_generator_model():
    """멜로디 모델 로드. VRAM 재사용을 위해 전역 변수 사용."""
    global global_generator_model, dataset
    
    if not MODEL_AVAILABLE:
        raise ImportError("모델 로드 모듈이 없습니다.")
        
    if global_generator_model is not None:
        return global_generator_model, dataset

    try:
        global_generator_model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, GEN_DEVICE)
        return global_generator_model, dataset
    except Exception as e:
        global_generator_model = None 
        dataset = None
        raise

# 3. MusicGen 전용 워커 클래스

class MusicGen_Worker(Process):
    """
    MusicGen 모델을 미리 로드하고 작업 큐에서 요청을 받아 처리하는 전용 프로세스.
    """
    def __init__(self, queue, shared_db, device, sf2_path, output_folder):
        super().__init__()
        self.queue = queue
        self.shared_db = shared_db
        self.device = device
        self.sf2_path = sf2_path
        self.musicgen_model_loaded = False 
        self.output_folder = output_folder
        
    def load_model_once(self):
        """MusicGen 모델을 프로세스 시작 시점에 한 번만 로드."""
        if not MODEL_AVAILABLE:
            raise ImportError("모델 로드 모듈이 없습니다.")
            
        try:
            print(f"[{self.name}] MusicGen 초기화 시작: {self.device}")
            use_fp16 = (self.device == "cuda") 
            init_musicgen(device=self.device, use_fp16=use_fp16) 
            self.musicgen_model_loaded = True
            print(f"[{self.name}] MusicGen 모델 초기화 완료.")
        except Exception as e:
            print(f"[{self.name}] MusicGen 모델 로드 실패: {e}")
            raise

    def run(self):
        """큐에서 작업을 받아 처리."""
        try:
            self.load_model_once() 
        except Exception as e:
            print(f"[{self.name}] 치명적 오류: MusicGen 워커가 시작할 수 없습니다.")
            return

        print(f"[{self.name}] MusicGen 워커가 작업 대기 중입니다...")
        while True:
            try:
                job_id, job_data = self.queue.get() 
                print(f"[{self.name}] 작업 수신: {job_id}")
                
                self._process_stylization_2nd_in_worker(job_id, job_data)
                
            except KeyboardInterrupt:
                break
            except Exception as e:
                print(f"[{self.name}] 작업 처리 중 예외 발생: {e}")
                traceback.print_exc()

    def _process_stylization_2nd_in_worker(self, job_id, job_data):
        """2차 MusicGen 스타일 변환만 담당. 총 소요 시간 계산 및 출력."""
        
        start_time = job_data.get('start_time', time.time())
        
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
            status_update = dict(job_data)
            status_update.update({'status': 'failed', 'error': f'2차 MusicGen 프롬프트 생성 실패: {str(e)}'})
            self.shared_db[job_id] = status_update
            return

        self.shared_db[job_id] = {'status': 'in_progress', 'message': 'MusicGen 스타일 변환 중... (추론 가속)'}
        temp_safe_wav_path = None
        
        try:
            melody_stream = io.BytesIO(wav_data)
            max_new_tokens = int(target_sec * 512 / 10) 
            
            output_wav_filename_styled = f'{safe_filename}_final_styled.wav'
            output_wav_path_styled = os.path.join(self.output_folder, output_wav_filename_styled)
            
            audio, sr = sf.read(melody_stream) 
            temp_safe_wav_path = os.path.join(self.output_folder, f'{safe_filename}_safe_temp.wav')
            sf.write(temp_safe_wav_path, audio, sr)
            
            stylize_melody(
                melody_wav_path=temp_safe_wav_path,
                out_wav_path=output_wav_path_styled,
                prompt=prompt,
                device=self.device,
                use_fp16=(self.device == "cuda"),
                max_new_tokens=max_new_tokens 
            )
            
            os.remove(temp_safe_wav_path)
            if os.path.exists(melody_wav_path):
                os.remove(melody_wav_path) 
            
            music_url_final = f'http://localhost:5000/music/{output_wav_filename_styled}'
            
            current_status = dict(job_data)
            current_status.pop('music_url_1st', None) 
            current_status.update({'status': 'completed', 'music_url': music_url_final, 'message': '최종 음원 생성 완료.'})
            self.shared_db[job_id] = current_status
            
            end_time = time.time()
            total_time = end_time - start_time
            minutes = int(total_time // 60)
            seconds = total_time % 60
            print(f"[{job_id}] 최종 스타일 변환 완료. 총 소요 시간: {minutes}분 {seconds:.2f}초") 
                
        except Exception as e:
            traceback.print_exc()
            if temp_safe_wav_path and os.path.exists(temp_safe_wav_path):
                os.remove(temp_safe_wav_path)
                
            self.shared_db[job_id] = {
                'status': 'failed', 
                'error': f'최종 스타일 변환 실패: {str(e)}',
                'music_url_1st': job_data.get('music_url_1st') 
            }

# 4. 비동기 작업자 함수

def process_music_generation_1st(job_id, safe_filename, prefix_tokens, target_sec, shared_db, season, musicgen_q):
    """
    GPU 가속으로 멜로디 생성 후, 2차 MusicGen 워커 큐에 작업을 전송.
    """
    global global_generator_model, dataset, SF2_PATH
    
    try:
        if global_generator_model is None or dataset is None:
            global_generator_model, dataset = load_generator_model()
    except Exception as e:
        shared_db[job_id] = {'status': 'failed', 'error': f'워커 멜로디 모델 설정 실패: {str(e)}'}
        return 

    try:
        shared_db[job_id] = {'status': 'in_progress', 'message': f'1차 멜로디 생성 중 ({target_sec}초)...'}
        
        g = torch.Generator(device=GEN_DEVICE).manual_seed(SEED + 1)
        generated_tokens_final = generate_until_seconds(
            global_generator_model, dataset, prefix_tokens=prefix_tokens, target_sec=target_sec, 
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
        
        audio, sr = sf.read(melody_wav_path)
        sf.write(melody_wav_path, audio, sr)
        wav_data = Path(melody_wav_path).read_bytes()
        
        music_url_melody = f'http://localhost:5000/music/{melody_wav_filename}'
        
        start_time = shared_db.get(job_id, {}).get('start_time', time.time())

        current_status = {
            'status': '2nd_start_ready', 
            'music_url_1st': music_url_melody, 
            'melody_wav_path': melody_wav_path,
            'wav_data': wav_data, 
            'prefix_tokens': prefix_tokens,
            'season': season,
            'safe_filename': safe_filename,
            'target_sec': target_sec,
            'start_time': start_time 
        }
        
        musicgen_q.put((job_id, current_status))
        shared_db[job_id] = {'status': 'in_progress', 'message': '1차 멜로디 생성 완료. MusicGen 대기열에 추가됨.', 'start_time': start_time}
        print(f"[{job_id}] 1차 멜로디 생성 완료. MusicGen 큐에 작업 전송.")

    except Exception as e:
        traceback.print_exc()
        shared_db[job_id] = {'status': 'failed', 'error': f'1차 멜로디 생성 실패: {str(e)}'}
        return


# 5. Flask 엔드포인트

@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    """CSV 업로드 처리, 특징 추출 후 1차 멜로디 생성 시작."""
    global job_status_db, musicgen_queue, musicgen_worker_process
    
    if musicgen_worker_process is None or not musicgen_worker_process.is_alive():
        return jsonify({'error': 'MusicGen 워커 프로세스가 작동하지 않습니다. 서버 로그를 확인하세요.'}), 503
    
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

    start_time = time.time() 
    
    job_status_db[job_id] = {'status': 'in_progress', 'message': '1차 멜로디 생성 시작...', 'start_time': start_time}
    
    process_1st = Process(
        target=process_music_generation_1st, 
        args=(job_id, safe_filename, prefix_tokens, target_sec, job_status_db, season, musicgen_queue)
    )
    process_1st.start()

    return jsonify({
        'job_id': job_id, 
        'status': 'started',
        'message': f'음악 생성 파이프라인 시작됨. 최종 "completed" 상태를 확인하세요.'
    })


@app.route('/api/status/<job_id>', methods=['GET'])
def get_job_status(job_id):
    """작업 상태 확인 및 URL 호스트 대체. 내부 정보 노출 방지."""
    global job_status_db
    status_info = job_status_db.get(job_id)

    if status_info is None:
        return jsonify({'status': 'error', 'message': 'Job ID를 찾을 수 없습니다.'}), 404

    base_url = request.host_url.strip('/')
    status_dict = dict(status_info) 

    status_dict.pop('music_url_1st', None)
    
    if 'music_url' in status_dict:
        status_dict['music_url'] = status_dict['music_url'].replace('http://localhost:5000', base_url)

    if status_dict.get('status') == '2nd_start_ready':
        status_dict['status'] = 'in_progress'
        status_dict['message'] = 'MusicGen 대기열에 추가되었거나 스타일 변환 중...'
    
    status_dict.pop('wav_data', None)
    status_dict.pop('prefix_tokens', None)
    status_dict.pop('melody_wav_path', None)
    status_dict.pop('safe_filename', None)
    status_dict.pop('season', None)
    status_dict.pop('target_sec', None)
    status_dict.pop('start_time', None) 

    return jsonify(status_dict)


@app.route('/music/<path:filename>')
def download_music(filename):
    """음악 파일 다운로드."""
    return send_from_directory(OUTPUT_FOLDER, filename)

# 6. 메인 실행
if __name__ == '__main__':
    multiprocessing.freeze_support() 
    
    initialize_job_db_and_queues() 
    
    if MODEL_AVAILABLE:
        try:
            global_generator_model, dataset = load_model(CKPT_PATH, DATA_JSONL, VOCAB_JSON, cfg, GEN_DEVICE)
            print("메인 프로세스 멜로디 모델 (GPU/CPU) 로드 완료.")
        except Exception as e:
            print(f"메인 프로세스 멜로디 모델 로드 실패. 오류: {e}")
            pass
        
        try:
            print(f"MusicGen 전용 워커 프로세스 {NUM_MUSICGEN_WORKERS}개 시작...")
            musicgen_worker_processes = []
            for i in range(NUM_MUSICGEN_WORKERS):
                worker = MusicGen_Worker(
                    queue=musicgen_queue, 
                    shared_db=job_status_db, 
                    device=MUSICGEN_DEVICE, 
                    sf2_path=SF2_PATH,
                    output_folder=OUTPUT_FOLDER
                )
                worker.name = f"MusicGen_Worker-{i}"
                worker.start()
                musicgen_worker_processes.append(worker)
            
            if musicgen_worker_processes:
                musicgen_worker_process = musicgen_worker_processes[0] 
            
            time.sleep(5) 
        except Exception as e:
            print("="*50)
            print(f"FATAL: MusicGen 워커 시작에 심각한 문제가 발생했습니다.")
            print(f"오류 상세: {e}")
            print("GPU/CUDA 설정, Python 패키지 의존성을 확인하세요.")
            print("="*50)
            sys.exit(1)
    
    app.run(host='0.0.0.0', debug=True, use_reloader=False)