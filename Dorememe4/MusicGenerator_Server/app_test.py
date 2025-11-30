from flask import Flask, request, jsonify, send_from_directory
import os
import uuid
import shutil

from run import generate_music

app = Flask(__name__)

OUTPUT_FOLDER = "./music_out"
os.makedirs(OUTPUT_FOLDER, exist_ok=True)

JOB_STATUS = {}

@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    filename = request.headers.get('X-File-Name')
    if not filename or not request.data:
        return jsonify({'error': '파일이 누락되었습니다.'}), 400
    
    job_id = str(uuid.uuid4())

    tmp_csv_path = os.path.join(OUTPUT_FOLDER, f"{job_id}.csv")
    with open(tmp_csv_path, 'wb') as f:
        f.write(request.data)


    try:
        result = generate_music(
            csv_path=tmp_csv_path,
            target_sec=20.0,
            base_dir="./runs"
            )
    except Exception as e:
        JOB_STATUS[job_id] = {
            "status": "failed",
            "message": str(e)
        }
        return jsonify({
            "job_id": job_id,
            "status": "failed",
            "message": str(e)
            }), 500
    finally:
        try:
            os.remove(tmp_csv_path)
        except OSError:
            pass
            
    final_src = result["final_wav"]
    final_name = f"{job_id}_final.wav"
    final_dst = os.path.join(OUTPUT_FOLDER, final_name)
    shutil.copyfile(final_src, final_dst)

    music_url = f"/music/{final_name}"

    JOB_STATUS[job_id] = {
        "status": "completed",
        "music_url": music_url,
        "message": "최종 음원 생성 완료"
    }

    return jsonify({
        'job_id': job_id,
        'status': "completed",
        'music_url': music_url,
        'message': "음악 생성이 완료되었습니다.",
    })

@app.route('/api/status/<job_id>', methods=['GET'])
def get_job_status(job_id):
    info = JOB_STATUS.get(job_id)
    if info is None:
        return jsonify({'status': 'error', 'message': 'Job ID를 찾을 수 없습니다.'}), 404
    return jsonify(info)

@app.route('/music/<path:filename>', methods=['GET'])
def download_music(filename):
    return send_from_directory(OUTPUT_FOLDER, filename)

if __name__ == '__main__':
    app.run(host='0.0.0.0', debug=True, use_reloader=False)
