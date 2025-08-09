# 📘 Chapter 2: 자연어 처리 기초 (Master Page)

## 🎯 학습 목표
- 자연어 처리가 무엇인지, 그리고 왜 어려운지를 이해한다.
- 텍스트 데이터를 수치로 변환하는 다양한 방법을 학습한다.
- 단어, 문장, 문서의 의미를 벡터로 표현하는 기법을 이해한다.
- LLM의 입력 전 단계(토큰화, 임베딩)의 기초를 확실히 한다.
- 이후 딥러닝, LLM 학습 및 활용에서 필요한 NLP 기초 체력을 갖춘다.

---

## 📂 학습 목차

### 1. 자연어 처리 개요
- NLP의 정의와 역사
- 전통적 NLP vs. 딥러닝 기반 NLP
- 주요 과제(분류, 번역, 질의응답 등)

### 2. 텍스트 전처리
- 토큰화(Tokenization)
- 정규화(Normalization)
- 불용어 제거(Stopword Removal)
- 어간 추출(Stemming)과 표제어 추출(Lemmatization)

### 3. 텍스트 표현 기법
- Bag of Words (BoW)
- 단어 빈도(TF)와 가중치(TF-IDF)
- 희소 표현(Sparse Representation)과 한계

### 4. 단어 임베딩(Word Embedding)
- 분산 표현(Distributed Representation) 개념
- Word2Vec: CBOW / Skip-gram
- GloVe: 통계 기반 임베딩
- 임베딩 시각화와 의미 공간 이해

### 5. 문장/문서 임베딩
- 평균 임베딩
- Doc2Vec
- Sentence Transformers

### 6. NLP 평가와 한계
- Cosine Similarity, Euclidean Distance
- 의미 중의성(Ambiguity) 문제
- Out-of-Vocabulary(OOV) 문제
- 대규모 언어모델(LLM) 이전의 한계

---

## 📅 추천 학습 순서
1. NLP의 전반적 개념 파악
2. 전처리 기법 실습 (토큰화, 정규화 등)
3. Bag of Words / TF-IDF 구현
4. Word2Vec, GloVe 개념 + 실습
5. 문장 임베딩과 문서 임베딩 이해
6. 평가 지표와 한계 이해

---

## 🧪 실습 아이디어
- Python NLTK/KoNLPy로 한글 텍스트 전처리 실습
- scikit-learn으로 BoW, TF-IDF 벡터화
- Gensim으로 Word2Vec 모델 훈련
- 두 단어/문장의 유사도 계산 (코사인 유사도)
- 임베딩 벡터를 PCA/T-SNE로 시각화

---

## 📚 참고 자료
- [NLTK 공식 문서](https://www.nltk.org/)
- [KoNLPy 한국어 NLP 라이브러리](https://konlpy.org/)
- [Gensim: Word2Vec 튜토리얼](https://radimrehurek.com/gensim/auto_examples/tutorials/run_word2vec.html)
- [CS224N - Stanford NLP 강의](https://web.stanford.edu/class/cs224n/)
- [scikit-learn 텍스트 피처 추출](https://scikit-learn.org/stable/modules/feature_extraction.html)

---

## 📝 학습 기록
> 이 섹션은 학습 중 내가 이해한 개념, 깨달음, 어려웠던 점을 간단히 기록하는 공간.
> 예시:
> - 2025-08-12: TF-IDF에서 IDF의 의미를 직관적으로 이해함.
> - 2025-08-13: Word2Vec이 단어 간 의미 관계를 벡터로 표현하는 방식을 그림으로 정리함.
