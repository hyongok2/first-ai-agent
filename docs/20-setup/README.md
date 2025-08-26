# 권장 아키텍처(오프라인/보안 환경)

* **노트북(Windows + Rancher Desktop, WSL 없음)**

  * 런타임: containerd(기본) 또는 Moby(Docker 호환) 중 택1
  * 작업: 이미지 **빌드/수집 → .tar 저장**
* **리눅스 서버(외부망 차단)**

  * A안) 개별 서버에 **이미지 직접 로드 후 실행**
  * B안) **내부 전용 레지스트리**(registry:2 or Harbor 오프라인) 세워서 운영

---

# 단계별 운영 플로우

## 1) Rancher Desktop 설정 (노트북)

* Rancher Desktop 열기 → **Preferences → Container Engine**

  * 간단/익숙한 Docker CLI 원하면 **Moby (dockerd)** 선택 → `docker` 사용
  * 기본(containerd)로도 OK → `nerdctl` 사용
* (선택) **Enable Docker CLI** 옵션 켜기 (버전에 따라 UI 문구 다름)

## 2) 이미지 빌드 & 수집 (노트북)

### Docker CLI 모드(Moby)일 때

```bash
# 빌드
docker build -t myapp:1.0 .

# 필요 이미지(베이스 포함) 미리 pull
docker pull ubuntu:22.04
docker pull postgres:16
# … 필요한 것들 모두 pull

# 하나의 tar 로 저장(레이어 포함)
docker save -o myapp_bundle.tar myapp:1.0 ubuntu:22.04 postgres:16
```

### containerd 모드일 때(nerdctl)

```bash
# 빌드
nerdctl build -t myapp:1.0 .

# pull
nerdctl pull ubuntu:22.04
nerdctl pull postgres:16

# 저장(여러 이미지 함께 가능)
nerdctl save -o myapp_bundle.tar myapp:1.0 ubuntu:22.04 postgres:16
```

> **포인트**: `save`로 만든 **tar 안에 베이스 이미지 레이어까지 포함**되므로, 서버가 외부망이 없어도 로드가 됩니다.

## 3) 안전 이관

* 사내 보안 정책 허용 수단(예: 승인된 USB, 사내 SFTP/NAS)으로 `myapp_bundle.tar` 전달
* 파일 해시(SHA256) 체크 권장:

```bash
# 노트북에서
certutil -hashfile myapp_bundle.tar SHA256

# 서버에서 동일 실행 후 값 일치 확인
sha256sum myapp_bundle.tar
```

## 4A) (간단) 서버에서 바로 로드해 실행

### 서버가 Docker일 때

```bash
docker load -i myapp_bundle.tar
docker images   # 로드된 이미지 확인
docker run --rm myapp:1.0
```

### 서버가 containerd일 때

* `nerdctl` 설치(없는 경우) 또는 `ctr` 사용

```bash
# nerdctl 사용 시
nerdctl load -i myapp_bundle.tar
nerdctl images
nerdctl run --rm myapp:1.0

# ctr 사용 예시(네임스페이스 주의)
ctr -n default images import myapp_bundle.tar
ctr -n default images ls
```

## 4B) (운영형) 내부 전용 레지스트리 구축 후 배포

> 프로젝트가 커지면 **내부 레지스트리**가 훨씬 편합니다.

### 최소 구성: `registry:2` (Docker Registry)

1. 레지스트리 이미지도 노트북에서 미리 준비

```bash
docker pull registry:2          # 또는 nerdctl pull
docker save -o registry2.tar registry:2
```

2. 서버로 전달 후 로드 & 기동

```bash
docker load -i registry2.tar
docker run -d --restart=always --name registry \
  -p 5000:5000 -v /data/registry:/var/lib/registry registry:2
```

3. 노트북에서 태깅 & 내부 레지스트리에 푸시(사내망 통신 가능 가정)

```bash
# 노트북에서 내부 IP:5000으로 태그
docker tag myapp:1.0 10.10.10.5:5000/myapp:1.0
docker push 10.10.10.5:5000/myapp:1.0
```

4. 서버(또는 다른 서버)에서 pull하여 사용

```bash
docker pull 10.10.10.5:5000/myapp:1.0
```

> **보안**: 운영 시엔 **TLS(사내 CA) + 계정(htpasswd)** 붙이세요. Harbor 오프라인 설치면 UI/권한/취약점 스캔 등 더 풍부합니다(필요 이미지들을 동일하게 tar → load 방식으로 선적재).

---

# WSL 없이 쓸 때의 주의점

* **볼륨 마운트 성능/권한**: Windows↔Linux 마운트가 느리거나 권한 꼬임이 날 수 있습니다.

  * 해결: 빌드 컨텍스트를 작게, `.dockerignore` 적극 활용, 컨테이너 내부에서 fetch하는 패턴 고려.
* **BuildKit/빌드 확장**: 대부분 정상이나, `buildx` 플러그인 기반 고급 기능은 Rancher Desktop의 모드/버전에 따라 차이가 납니다. 문제 생기면 **Moby 모드** 전환이 쉬운 해법.
* **네트워크**: 사내 프록시를 써야 한다면 Rancher Desktop의 프록시 설정과 Docker/nerdctl의 `--build-arg http_proxy` 등을 맞춰야 합니다.

---

# 실전 체크리스트

1. Rancher Desktop 설치 및 **Moby(Docker) 모드** 선택(문제 적고 쉬움)
2. 필요한 이미지 **모두 pull → 하나의 tar 로 save**
3. **SHA256 해시** 기록
4. **승인된 매체/경로**로 서버 이관
5. 서버에서 **load → run** (또는 **내부 레지스트리** 먼저 구축)
6. 내부 레지스트리 운영 시 **TLS/계정/보안정책** 반영

---

원하시면 위 과정을 **회사 표준 운영 가이드 1\~2쪽짜리**로 정리해 드릴게요. 사용하실 런타임(Docker vs containerd), 내부 레지스트리 여부, 사내 프록시 유무만 알려주시면 바로 템플릿 만들어 드립니다.
