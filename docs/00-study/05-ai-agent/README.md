# 📘 Chapter 5: LLM 활용/응용 (Master Page)

## 🎯 학습 목표
- LLM을 실제 제품·서비스·연구에 통합하는 방법을 이해한다.
- Prompt Engineering, RAG, 에이전트 설계 등 핵심 응용 기술을 학습한다.
- 모델 성능 최적화와 비용 효율성을 고려한 운영 전략을 익힌다.
- 다양한 개발 프레임워크와 도구를 사용할 수 있다.

---

## 📂 학습 목차

### 1. LLM 응용 개요
- LLM 활용 사례 (챗봇, 분석, 생성, 자동화 등)
- LLM과 전통 SW의 차이
- 온디바이스 vs 클라우드 사용 전략

### 2. Prompt Engineering
- 프롬프트 구조 설계 (Instruction, Context, Example)
- Zero-shot, Few-shot, Chain-of-Thought
- 프롬프트 평가 및 개선 방법

### 3. Retrieval-Augmented Generation (RAG)
- RAG 개념과 필요성
- Vector DB 기반 검색
- 문서 임베딩과 검색 품질 평가
- RAG 설계 패턴 (단일 쿼리, 다단계 검색 등)

### 4. AI 에이전트 설계
- 도구 호출(Tool Calling)과 API 연동
- 오케스트레이션(Workflow) 설계
- 상태 관리와 메모리 구조
- 실패 복원 전략 (Retry, Validation)

### 5. 성능 최적화와 운영
- 컨텍스트 윈도우 관리
- 캐싱과 재사용 전략
- 비용 절감 방법 (모델 선택, 배치 요청)
- 모니터링과 로깅

### 6. 프레임워크 및 도구
- LangChain, LlamaIndex
- OpenAI Function Calling
- HuggingFace Pipelines
- Ollama, vLLM, Text Generation Inference(TGI)

---

## 📅 추천 학습 순서
1. LLM 활용 사례와 전략 이해
2. Prompt Engineering 실습
3. RAG 구조 설계 및 구현
4. AI 에이전트 설계와 개발
5. 성능 최적화/비용 관리 전략 학습
6. 다양한 프레임워크 적용 실습

---

## 🧪 실습 아이디어
- 동일 질문에 대한 프롬프트 변형 실험
- Vector DB(Qdrant, Chroma) 연결 후 RAG 구현
- 툴 호출 기반 계산기/날씨 조회 에이전트 제작
- 긴 문서 요약 시스템 개발
- 성능/비용 로그 분석으로 최적화

---

## 📚 참고 자료
- [LangChain 공식 문서](https://python.langchain.com/docs/)
- [LlamaIndex 공식 문서](https://docs.llamaindex.ai/)
- [OpenAI Function Calling 가이드](https://platform.openai.com/docs/guides/function-calling)
- [vLLM: Efficient LLM Serving](https://vllm.ai/)
- [Qdrant Vector DB](https://qdrant.tech/)

---

## 📝 학습 기록
> 이 섹션은 학습 중 내가 이해한 개념, 깨달음, 어려웠던 점을 간단히 기록하는 공간.
> 예시:
> - 2025-08-21: Few-shot 프롬프트가 Zero-shot 대비 효과가 큰 사례 발견.
> - 2025-08-22: 자체 RAG 구조에서 Vector DB 검색 품질 문제 해결.
