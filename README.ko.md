# EastwardModTool

[English](README.md) | 한국어

Eastward 에셋을 보고 수정하고 모드 패키지를 만드는 비공식 데스크톱 도구입니다.

## 빌드 방법

.NET 10, CMake 3.20 이상과 C++20 컴파일러가 필요합니다.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModTool.git
cd EastwardModTool
dotnet build EastwardModTool -c Release
dotnet run --project EastwardModTool
```

기존 체크아웃에서는 `git submodule update --init --recursive`를 실행하세요. CMake 경로는 `-p:CMakeExecutable=/path/to/cmake`로 지정할 수 있습니다.

## 사용 방법

Eastward 설치 폴더 또는 `content/game`을 엽니다. 설치 경로에서 `content_dlc` 폴더를 찾으면 그 안의 아카이브도 읽습니다.

에셋 목록은 Gii 디렉터리 구조로 표시합니다. **Asset types**로 종류별 보기로 전환할 수 있습니다.

**Asset > Export source...** 메뉴에서 소스를 내보내고 **Import file... / Import folder...** 메뉴에서 수정한 소스를 가져옵니다. **Global metadata**에서는 에셋, 스크립트, 텍스처 인덱스를 편집합니다.

**Save project**는 편집 내용을 `.ewproject`로 저장합니다. 에디터 탭을 닫아도 초안은 유지되며 **Reopen closed tab**으로 다시 열 수 있습니다.

### 모드 만들기

**Mod > Export mod...** 메뉴에서 변경한 리소스를 `.ewmod`에 저장합니다.

**Manage mods...** 창에서 로더를 설치하고 `.ewmod`를 추가한 뒤 활성화합니다.

`.g`로 저장하려면 **Build archives...** 메뉴를 사용합니다. 원본을 유지하고 별도 디렉터리에 출력합니다.

### 언어 패치

**Mod > Create language patch...** 창에서 언어 ID와 표시 이름, 기준 언어를 입력하고 번역 웹의 **Export JSON**으로 받은 파일을 선택합니다. 메뉴, 이미지, 폰트도 함께 지정할 수 있습니다.

**Create editing drafts**로 만든 내용을 확인한 뒤 모드로 내보내세요. 자세한 방법은 [언어 패치 가이드](https://github.com/VriskaSerket51/EastwardModLoader/blob/main/docs/language-patches.ko.md)를 참고하세요.

## 에셋 미리보기

| 에셋 | 보기 |
| --- | --- |
| 코드, 텍스트 | 소스 에디터 |
| 씬, proto, 직렬화 객체 | 계층, 속성, 객체 참조 |
| 테이블, 언어 팩 | 시트, 메시지와 언어 비교 |
| 그래프, 메시 | 노드 연결, 와이어프레임 |
| 텍스처, 덱, 스프라이트 | 이미지, 기하, 애니메이션 |
| 폰트, 패키지, 바이너리 | 글리프, 파일 목록, 16진수 |

## 다른 운영체제

Windows, Linux, macOS용으로 빌드할 수 있습니다. 네이티브 디컴파일러도 해당 OS에서 빌드합니다.

```sh
dotnet publish EastwardModTool -c Release -r win-x64 --self-contained true -o ./publish/win-x64
```

Linux와 macOS에서는 해당 환경에서 `linux-x64`, `osx-arm64` 등으로 런타임을 바꾸세요.
