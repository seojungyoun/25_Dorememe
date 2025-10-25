from flask import Flask, request, jsonify, send_from_directory
import os
import pandas as pd
import torch
import pretty_midi

app = Flask(__name__)

UPLOAD_FOLDER = 'uploads'
OUTPUT_FOLDER = 'music'
os.makedirs(UPLOAD_FOLDER, exist_ok=True)
os.makedirs(OUTPUT_FOLDER, exist_ok=True)


@app.route('/api/upload_data', methods=['POST'])
def upload_data():
    if 'file' not in request.files:
        return jsonify({'error': 'No file part in request'}), 400

    file = request.files['file']
    if file.filename == '':
        return jsonify({'error': 'No selected file'}), 400

    file_path = os.path.join(UPLOAD_FOLDER, file.filename)
    file.save(file_path)

    try:
        df = pd.read_csv(file_path)
    except Exception as e:
        return jsonify({'error': f'Failed to read CSV: {str(e)}'}), 400

    # 🎵 여기에 실제 음악 생성 로직 삽입
    output_filename = file.filename.replace('.csv', '.mp3')
    output_path = os.path.join(OUTPUT_FOLDER, output_filename)

    # 지금은 테스트용으로 dummy mp3 파일 생성
    with open(output_path, 'wb') as f:
        f.write(b'\x00\x00\x00\x00')

    return jsonify({
        'status': 'completed',
        'music_url': f'http://127.0.0.1:5000/music/{output_filename}'
    })


@app.route('/music/<path:filename>')
def download_music(filename):
    return send_from_directory(OUTPUT_FOLDER, filename)


if __name__ == '__main__':
    app.run(debug=True)
