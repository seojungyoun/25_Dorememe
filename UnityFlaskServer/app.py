# app.py

from flask import Flask, request, send_from_directory, abort
import os
from werkzeug.utils import secure_filename

# ----------------------------------------------------
# 1. 초기 설정
# ----------------------------------------------------
app = Flask(__name__)
# Unity 클라이언트가 업로드/다운로드할 파일을 저장할 폴더
UPLOAD_FOLDER = 'uploads'
app.config['UPLOAD_FOLDER'] = UPLOAD_FOLDER
# 서버 포트 설정
PORT = 5000

# uploads 폴더가 없으면 생성
if not os.path.exists(UPLOAD_FOLDER):
    os.makedirs(UPLOAD_FOLDER)

# ----------------------------------------------------
# 2. 파일 업로드 API 구현 (Unity -> 서버)
# 엔드포인트: POST /upload
# ----------------------------------------------------
@app.route('/upload', methods=['POST'])
def upload_file():
    # Unity에서 파일 데이터가 'file'이라는 이름으로 전송될 것을 기대합니다.
    if 'file' not in request.files:
        return 'No file part', 400
    
    file = request.files['file']
    
    if file.filename == '':
        return 'No selected file', 400
        
    if file:
        # 파일 이름의 보안 처리
        filename = secure_filename(file.filename)
        # 파일을 설정된 UPLOAD_FOLDER에 저장
        file.save(os.path.join(app.config['UPLOAD_FOLDER'], filename))
        print(f"파일 업로드 성공: {filename}")
        return 'File uploaded successfully', 200

# ----------------------------------------------------
# 3. 파일 다운로드 API 구현 (서버 -> Unity)
# 엔드포인트: GET /downloads/<filename>
# ----------------------------------------------------
@app.route('/downloads/<filename>', methods=['GET'])
def download_file(filename):
    try:
        # UPLOAD_FOLDER에 있는 파일을 Unity 클라이언트에게 전송
        return send_from_directory(app.config['UPLOAD_FOLDER'], 
                                   filename, 
                                   as_attachment=True) # as_attachment=True 는 보통 브라우저에서 다운로드 대화 상자를 띄우지만, Unity에서는 파일 내용을 전송합니다.
    except FileNotFoundError:
        print(f"파일을 찾을 수 없음: {filename}")
        abort(404) # 파일이 없으면 404 에러 반환

# ----------------------------------------------------
# 4. 서버 실행
# ----------------------------------------------------
if __name__ == '__main__':
    print(f"Flask 서버가 http://127.0.0.1:{PORT} 에서 실행됩니다.")
    app.run(host='0.0.0.0', port=PORT, debug=True)
    
# 참고: host='0.0.0.0'은 외부 접속을 허용합니다 (로컬 네트워크 환경에서 다른 기기가 접속 가능).
# 로컬 테스트 시 http://127.0.0.1:5000 또는 http://localhost:5000 을 사용하세요.