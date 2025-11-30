from flask import Flask, request, jsonify, send_from_directory
import os
import uuid
import time

try:
    from run import generate_music
except ImportError as e:
    print(f"Error: {e}")
    def generate_music(*args, **kwargs):
        raise NotImplementedError("run.py's generate_music function is not imported.")

TEMP_CSV_FOLDER = "./temp_data" 
os.makedirs(TEMP_CSV_FOLDER, exist_ok=True)

RUNS_FOLDER = "./runs" 
os.makedirs(RUNS_FOLDER, exist_ok=True)

JOB_STATUS = {}
app = Flask(__name__)

@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    start_time = time.time()
    
    filename = request.headers.get('X-File-Name')
    if not filename or not request.data:
        return jsonify({'error': 'File or data is missing'}), 400
    
    job_id = str(uuid.uuid4())
    tmp_csv_path = os.path.join(TEMP_CSV_FOLDER, f"{job_id}.csv")
    
    try:
        with open(tmp_csv_path, 'wb') as f:
            f.write(request.data)
            
        result = generate_music(
            csv_path=tmp_csv_path,
            target_sec=30.0,
            base_dir=RUNS_FOLDER
            )
        
        final_src_path = result["final_wav"] 
        relative_path_for_url = os.path.relpath(final_src_path, RUNS_FOLDER).replace('\\', '/')

        base_url = request.host_url.rstrip('/')
        music_url = f"{base_url}/music/{relative_path_for_url}"

        JOB_STATUS[job_id] = {"status": "completed", "music_url": music_url, "message": "Completed"}

        end_time = time.time()
        print(f"[{job_id}] Processing time: {end_time - start_time:.2f}s")

        return jsonify({'job_id': job_id, 'status': "completed", 'music_url': music_url})

    except Exception as e:
        error_msg = f"Error: {str(e)}"
        JOB_STATUS[job_id] = {"status": "failed", "message": error_msg}
        print(f"[{job_id}] FAILED: {error_msg}")
        return jsonify({"job_id": job_id, "status": "failed", "message": error_msg}), 500
        
    finally:
        try:
            os.remove(tmp_csv_path)
        except OSError:
            pass

@app.route('/api/status/<job_id>', methods=['GET'])
def get_job_status(job_id):
    info = JOB_STATUS.get(job_id)
    if info is None:
        return jsonify({'status': 'error', 'message': 'Job ID not found'}), 404
    return jsonify(info)

@app.route('/music/<path:filename>', methods=['GET'])
def download_music(filename):
    return send_from_directory(RUNS_FOLDER, filename)

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000, debug=True, use_reloader=False)