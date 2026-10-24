# EastwardModTool

[English](README.md) | 한국어

Eastward 에셋을 편집하고 모드를 만드는 비공식 데스크톱 도구입니다.

## 사용 방법

1. Eastward 설치 폴더 또는 `content/game`을 열기
2. 에셋 열고 수정하기
3. `Save project`로 편집 내용을 `.ewproject`에 저장하기

기본 보기는 Gii 폴더 구조입니다. `Asset types`에서 에셋을 종류별로 볼 수 있습니다.

소스 추출에는 `Asset > Export source...`를 사용합니다. 수정한 파일은 `Import file...` 또는 `Import folder...`로 가져올 수 있습니다.
설치 경로에 `content_dlc`가 있으면 DLC 아카이브도 읽습니다.

### 모드 생성

1. `Mod > Export mod...`로 변경분을 `.ewmod`에 저장하기
2. `Manage mods...`에서 로더를 설치한 뒤 모드를 추가하기
3. 사용할 모드를 체크한 뒤 `Apply enabled mods`를 선택하기

`.g`로 저장할 때는 `Build archives...`를 사용합니다. 결과는 별도 폴더에 생성됩니다.

### 언어 패치

`Mod > Create language patch...`에서 언어 ID, 표시 이름, 기준 언어를 입력합니다.
[번역 웹](https://github.com/VriskaSerket51/eastward-translate)의 Export JSON 파일을 선택하고 필요한 메뉴, 이미지, 폰트를 추가할 수 있습니다.

`Create editing drafts`로 초안을 만든 뒤 변경 내용을 확인하고 모드로 내보냅니다.
자세한 사용법은 [언어 패치 가이드](https://github.com/VriskaSerket51/EastwardModLoader/blob/main/docs/language-patches.ko.md)에 있습니다.

## 빌드 방법

.NET 10, CMake 3.20 이상, C++20 컴파일러가 필요합니다.

```sh
git clone --recurse-submodules https://github.com/VriskaSerket51/EastwardModTool.git
cd EastwardModTool
dotnet build EastwardModTool -c Release
dotnet run --project EastwardModTool
```

> 기존 레포에서는 `git submodule update --init --recursive`로 서브모듈을 받습니다.
> CMake가 PATH에 없으면 `-p:CMakeExecutable=/path/to/cmake`로 경로를 지정할 수 있습니다.

Windows, Linux, macOS 빌드는 각각 해당 환경에서 진행합니다.

```sh
dotnet publish EastwardModTool -c Release -r win-x64 --self-contained true -o ./publish/win-x64
```

Linux는 `linux-x64`, macOS는 `osx-arm64` 등으로 대상을 변경합니다. 네이티브 디컴파일러도 해당 OS에서 빌드해야 합니다.
