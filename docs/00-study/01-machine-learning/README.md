# 📘 Chapter 1: 머신러닝 기초 (Master Page)

## 🎯 학습 목표
- 머신러닝의 기본 개념과 작동 원리를 이해한다.
- 모델이 어떻게 데이터를 학습하는지, 예측을 어떻게 하는지 감을 잡는다.
- 회귀 → 분류 → 일반화까지, 기초 알고리즘의 흐름을 이해한다.
- 이후 NLP, 딥러닝, LLM 학습을 위한 기반을 마련한다.

---

## 📂 학습 목차

### 1. 머신러닝 개요
- 머신러닝이란?
- 지도학습 / 비지도학습 / 강화학습 차이
- 데이터, 피처, 레이블 개념
- 학습 과정의 기본 사이클

### 2. 회귀 분석 (Regression)
- **선형 회귀** (Linear Regression)
- **로지스틱 회귀** (Logistic Regression)
- 모델의 수식, 예측 함수 이해

### 3. 손실 함수 (Loss Function)
- 평균제곱오차(MSE)
- 로그 손실(Log Loss)
- 손실 함수가 학습에서 하는 역할

### 4. 최적화 (Optimization)
- 경사 하강법 (Gradient Descent)
- 학습률(Learning Rate) 조절
- 국소 최소값, 전역 최소값

### 5. 모델 성능과 과적합
- 과적합(Overfitting)과 일반화(Generalization)
- 학습/검증/테스트 데이터 분리
- 정규화(Regularization) 개념

### 6. 평가 지표
- 회귀 평가 지표: RMSE, MAE, R²
- 분류 평가 지표: 정확도, 정밀도, 재현율, F1-score

---

## 📅 추천 학습 순서
1. 머신러닝 전반 개념 파악
2. 선형 회귀 → 로지스틱 회귀 실습
3. 손실 함수와 경사 하강법 이해 및 코드 구현
4. 과적합 방지 기법 학습
5. 모델 평가 지표 계산 실습

---

## 🧪 실습 아이디어
- Python (NumPy, scikit-learn)로 간단한 회귀 모델 구현
- 학습률 변화에 따른 경사 하강법 시각화
- 과적합/일반화 비교 그래프 만들기
- 로지스틱 회귀로 이진 분류 실습

---

## 📚 참고 자료
- [Andrew Ng - Machine Learning (Coursera)](https://www.coursera.org/learn/machine-learning)
- [Scikit-learn 공식 문서](https://scikit-learn.org/stable/user_guide.html)
- [The Hundred-Page Machine Learning Book](http://themlbook.com/)
- [3Blue1Brown - 경사 하강법 영상](https://www.youtube.com/watch?v=IHZwWFHWa-w)

---

## 📝 학습 기록
> 이 섹션은 학습 중 내가 이해한 개념, 깨달음, 어려웠던 점을 간단히 기록하는 공간.
> 예시:
> - 2025-08-09: 경사 하강법 애니메이션을 보니 학습률이 왜 중요한지 직관적으로 이해됨.
> - 2025-08-10: 로지스틱 회귀의 시그모이드 함수 해석 완료.
