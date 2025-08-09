# 📘 Chapter 4: LLM 구조 (Master Page)

## 🎯 학습 목표
- LLM(대규모 언어 모델)의 기본 구조와 작동 방식을 이해한다.
- Transformer의 Decoder-only 구조를 중심으로 학습한다.
- 사전 학습(Pretraining), 미세 조정(Fine-tuning), RLHF 등의 학습 흐름을 이해한다.
- 토큰, 컨텍스트 윈도우, 임베딩, Attention 메커니즘의 역할을 명확히 파악한다.
- 다양한 LLM 아키텍처(Qwen, LLaMA, Mistral 등)의 차이를 비교할 수 있다.

---

## 📂 학습 목차

### 1. LLM 개요
- LLM이란 무엇인가?
- 기존 NLP 모델과의 차이
- 주요 응용 분야

### 2. Transformer 복습
- Self-Attention 구조
- 멀티헤드 어텐션
- 포지셔널 인코딩(Positional Encoding)
- 피드포워드 네트워크

### 3. Decoder-only 구조
- 인코더-디코더 vs 디코더 전용(Decoder-only)
- GPT 구조와 특징
- 단방향 Attention의 동작 방식

### 4. LLM 학습 과정
- **사전 학습(Pretraining)**: 방대한 텍스트 데이터에서 다음 토큰 예측
- **미세 조정(Fine-tuning)**: 특정 작업에 맞춘 조정
- **RLHF (Reinforcement Learning from Human Feedback)**: 사람의 피드백 기반 보상 학습
- **DPO (Direct Preference Optimization)**: RLHF 대안 학습법

### 5. 토큰과 컨텍스트
- 토큰화(Tokenization) 원리
- 컨텍스트 윈도우(Context Window)
- 긴 컨텍스트 모델의 처리 방식

### 6. 주요 LLM 아키텍처 비교
- GPT 계열(OpenAI)
- LLaMA 계열(Meta)
- Mistral
- Qwen
- Gemma
- Falcon

---

## 📅 추천 학습 순서
1. LLM 개념과 기존 NLP와의 차이 이해
2. Transformer 복습
3. Decoder-only 구조 이해
4. 학습 과정(Pretraining → Fine-tuning → RLHF/DPO) 학습
5. 토큰과 컨텍스트 개념 정리
6. 주요 모델 아키텍처 비교 분석

---

## 🧪 실습 아이디어
- HuggingFace Transformers로 GPT-2 모델 로드 후 토큰 예측 실험
- 동일 입력을 여러 모델에 넣고 결과 비교
- 컨텍스트 윈도우 길이에 따른 응답 품질 비교
- RLHF 적용 전후의 응답 차이 분석

---

## 📚 참고 자료
- [The Illustrated GPT-2](https://jalammar.github.io/illustrated-gpt2/)
- [HuggingFace Transformers 문서](https://huggingface.co/docs/transformers/index)
- [OpenAI Cookbook](https://github.com/openai/openai-cookbook)
- [RLHF 논문: Learning to summarize with human feedback](https://arxiv.org/abs/2009.01325)
- [DPO 논문: Direct Preference Optimization](https://arxiv.org/abs/2305.18290)

---

## 📝 학습 기록
> 이 섹션은 학습 중 내가 이해한 개념, 깨달음, 어려웠던 점을 간단히 기록하는 공간.
> 예시:
> - 2025-08-18: Decoder-only 구조가 왜 LLM에 적합한지 이해함.
> - 2025-08-19: Qwen과 Mistral의 구조적 차이 표 작성.
