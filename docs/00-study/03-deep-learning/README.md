# 📘 Chapter 3: 딥러닝 구조 (Master Page)

## 🎯 학습 목표
- 인공신경망의 기본 구조와 작동 원리를 이해한다.
- 퍼셉트론에서 다층 퍼셉트론(MLP)으로 확장되는 흐름을 학습한다.
- 순환신경망(RNN), LSTM, GRU의 필요성과 동작 방식을 이해한다.
- Attention 메커니즘과 Transformer 구조의 핵심 아이디어를 이해한다.
- 이후 LLM 구조(챕터 4)로 자연스럽게 연결되는 기반을 마련한다.

---

## 📂 학습 목차

### 1. 인공신경망 기초
- 퍼셉트론(Perceptron) 개념
- 활성화 함수(Activation Function): Sigmoid, ReLU, Tanh
- 다층 퍼셉트론(MLP) 구조
- 순전파(Forward Propagation)와 역전파(Backpropagation)

### 2. 심층 신경망과 학습
- 은닉층의 역할
- 파라미터(가중치, 편향) 업데이트
- 경사 소실/폭발(Vanishing/Exploding Gradient) 문제
- 배치 정규화(Batch Normalization)

### 3. 순환 신경망(RNN)
- 시퀀스 데이터 처리 개념
- RNN 구조와 한계
- 시퀀스 길이에 따른 학습 문제

### 4. LSTM과 GRU
- LSTM 구조(입력 게이트, 출력 게이트, 망각 게이트)
- GRU의 단순화된 구조
- 장기 의존성(Long-Term Dependency) 문제 해결

### 5. Attention 메커니즘
- Attention의 기본 아이디어
- Self-Attention과 어텐션 가중치 계산
- 시퀀스 병렬 처리의 가능성

### 6. Transformer 구조
- 인코더-디코더 구조 개요
- 포지셔널 인코딩(Positional Encoding)
- 멀티헤드 어텐션(Multi-Head Attention)
- 피드포워드 네트워크
- 레이어 정규화와 잔차 연결

---

## 📅 추천 학습 순서
1. 퍼셉트론과 MLP 기본 구조 학습
2. 순전파/역전파 개념과 경사하강법 복습
3. RNN 구조 및 한계 이해
4. LSTM/GRU로 한계 극복 방법 학습
5. Attention 개념 학습
6. Transformer 구조로 확장

---

## 🧪 실습 아이디어
- NumPy로 간단한 MLP 구현 (MNIST 손글씨 분류)
- RNN으로 간단한 문장 생성 실습
- LSTM으로 시계열 예측 모델 구현
- PyTorch로 Self-Attention 계산 구현
- Transformer의 인코더 블록 구성 실습

---

## 📚 참고 자료
- [3Blue1Brown - 신경망 시각화 시리즈](https://www.3blue1brown.com/topics/neural-networks)
- [CS231n - Stanford Convolutional Neural Networks](http://cs231n.stanford.edu/)
- [The Illustrated Transformer](https://jalammar.github.io/illustrated-transformer/)
- [PyTorch Tutorials](https://pytorch.org/tutorials/)

---

## 📝 학습 기록
> 이 섹션은 학습 중 내가 이해한 개념, 깨달음, 어려웠던 점을 간단히 기록하는 공간.
> 예시:
> - 2025-08-15: LSTM 게이트 구조가 직관적으로 이해됨.
> - 2025-08-16: Self-Attention 계산 과정을 직접 구현함.
