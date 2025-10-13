import os
import io
import time
import uuid
import pandas as pd
from flask import Flask, request, jsonify, send_from_directory
from celery import Celery
from celery.result import AsyncResult

# --- Celery 연결 설정 ---
def make_celery(app):
    celery = Celery(app.import_name, broker=app.config['BROKER_URL'])
    celery.conf.update(app.config)
    class ContextTask(celery.Task):
        def __call__(self, *args, **kwargs):
            with app.app_context():
                return self.run(*args, **kwargs)
    celery.Task = ContextTask
    return celery

# --- Flask 기본 설정 ---
app = Flask(__name__)
app.config.from_object('celeryconfig')
UPLOAD_FOLDER = os.path.join(os.getcwd(), 'music_files')
os.makedirs(UPLOAD_FOLDER, exist_ok=True)
celery = make_celery(app)

# --- 비동기 음악 생성 작업 ---
@celery.task(bind=True, name='app.generate_music_task')
def generate_music_task(self, csv_data_str: str, job_id: str):
    try:
        # CSV 헤더 위치 찾기
        lines = csv_data_str.splitlines()
        data_start_index = -1
        for i, line in enumerate(lines):
            if 'Stroke Index' in line:
                data_start_index = i
                break
        if data_start_index == -1:
            raise ValueError("CSV format error: Missing header")

        # CSV 파싱
        data_string = "\n".join(lines[data_start_index:])
        df = pd.read_csv(io.StringIO(data_string), encoding='utf-8', sep=',')
        print(f"[{job_id}] CSV rows: {len(df)}")

        # 음악 생성 시뮬레이션 (지연)
        time.sleep(10)

        # 결과 파일 생성
        output_filename = f"{job_id}.mp3"
        output_path = os.path.join(UPLOAD_FOLDER, output_filename)
        with open(output_path, 'w') as f:
            f.write("DUMMY_MUSIC_DATA_MP3")

        # 절대 URL 반환
        base_url = "http://127.0.0.1:5000"   # 또는 Flask가 실행 중인 IP:포트
        music_url = f"{base_url}/music/{output_filename}"
        print(f"[{job_id}] Done -> {music_url}")

        return {'status': 'completed', 'music_url': music_url}

    except Exception as e:
        print(f"[{job_id}] Error: {e}")
        return {'status': 'failed', 'error': str(e)}

# --- CSV 업로드 API ---
@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    if 'text/csv' not in request.headers.get('Content-Type', ''):
        return jsonify({"error": "Content-Type must be text/csv"}), 415
    try:
        csv_data = request.data.decode('utf-8')
        job_id = str(uuid.uuid4())
        task = celery.send_task('app.generate_music_task', args=[csv_data, job_id])
        return jsonify({"job_id": task.id}), 200
    except Exception as e:
        return jsonify({"error": str(e)}), 500

# --- 작업 상태 확인 API ---
@app.route('/api/status/<job_id>', methods=['GET'])
def get_status(job_id):
    res = AsyncResult(job_id, app=celery)
    if res.state in ['PENDING', 'STARTED']:
        return jsonify({"job_id": job_id, "status": "processing"}), 200
    if res.state == 'FAILURE':
        return jsonify({"job_id": job_id, "status": "failed"}), 200
    if res.state == 'SUCCESS':
        result = res.result
        return jsonify({"job_id": job_id, "status": "completed", "music_url": result.get('music_url')}), 200
    return jsonify({"job_id": job_id, "status": "processing"}), 200

# --- 음악 파일 제공 API ---
@app.route('/music/<filename>', methods=['GET'])
def serve_music(filename):
    return send_from_directory(UPLOAD_FOLDER, filename)
