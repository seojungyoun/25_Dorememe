
# 🎨 Dorememe: VR-based Multi-sensory Media Art

> 
> **사용자의 3D 스케치 데이터를 실시간으로 분석하여 개인화된 음악을 생성하는 공감각 인터렉티브 AI 시스템** 
> 
> 

## 1. Project Overview

비대면 실감형 콘텐츠의 시각적 편중을 해결하기 위해 시각과 청각을 유기적으로 결합한 공감각적 인터렉션 환경을 구축했습니다. 사용자가 VR 공간 내 테마 공간에서 3D 스케치를 수행하면, 그 예술적 데이터가 AI 파이프라인을 거쳐 완성형 오디오 트랙으로 변환됩니다. 

## 2. Key Features

* **Interactive 3D Sketching**: 사용자가 브러시의 색상, 크기, 투명도를 조절하며 가상 공간에 자유롭게 그림을 그리는 환경을 제공합니다. 


* **Real-time Parametric Mapping**: 스케치 데이터(색상, 명도, 스트로크 등)를 음악적 파라미터로 정밀하게 매핑합니다. 


* **AI Music Generation**: 개인화된 단일 멜로디 생성부터 화성이 포함된 완성형 오디오 구성까지 단계별 AI 생성을 수행합니다. 



## 3. Technical Stack & AI Pipeline

### 🤖 AI Models

* **Melody Generation**: **REMI 기반 Transformer 모델**을 활용하여 사용자의 스케치 성향이 반영된 멜로디를 생성합니다. 


* **Audio Extension**: **MusicGen-Melody** 모델을 통해 생성된 멜로디에 텍스트 프롬프트를 결합, 풍부한 화성과 악기 질감을 더합니다. 



### 🎼 Sound Engineering & Mapping Logic

* **Color-to-Pitch Correspondence**: 브러시 색상을 특정 음계에 대응시키는 알고리즘을 설계했습니다. 


* **Luma-based Tempo Analysis**: 스케치의 **명도(Luma)** 데이터를 분석하여 음악의 템포(Tempo)를 실시간으로 조절합니다. 



### ⚙️ System Architecture

* **Frontend**: Unity (VR 환경 구축 및 인터렉션 데이터 수집) 


* **Backend**: **Flask & Celery** 기반의 비동기 서버 아키텍처를 통해 대규모 생성 모델 연산을 저지연(Low-latency)으로 처리합니다. 


* **Communication**: REST API를 통한 시각 데이터 전송 및 오디오 파일 수신 파이프라인을 구축했습니다. 



## 4. Engineering Challenges & Solutions

* **실시간성 확보**: 생성형 AI 모델의 높은 연산량을 처리하기 위해 비동기 큐(Celery)를 도입하여 VR 경험의 몰입도를 저해하지 않는 속도를 구현했습니다. 


* **인터렉션**: 단순한 사운드 재생이 아닌, 사용자의 시각적 행위(브러시 굵기, 색상 등)가 음악의 질감에 직접적인 영향을 주도록 파라미터 매핑 규칙을 최적화했습니다. 
